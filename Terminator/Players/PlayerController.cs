using System;
using UnityEngine;
using UnityEngine.Scripting;

public enum PlayerType
{
    Local,
    Remote,
    
    Total
}

public class PlayerController : MonoBehaviour
{
    [Flags]
    private enum Status
    {
        Dead = 0x01, 
        Respawn = 0x02 | Dead,
    }

    private enum RespawnStatus
    {
        None, 
        RightNow, 
        Waiting
    }

    private static readonly int RespawnStatusHash = Animator.StringToHash("RespawnStatus");

    [SerializeField] 
    internal float _smoothTime = 0.1f;

    private bool __isLocal;
    private Status __status;
    private int __instanceID;
    
    private Vector3 __position;
    private Vector3 __velocity;
    
    private AttributeEventReceiver __attributeEventReceiver;

    private Animator __animator;
    
    public static bool isWaitingToRespawn
    {
        get
        {
            if (!EffectShared.keepRecoveryTime)
                return true;
            
            var levelData = ILevelData.instance;
            if (levelData != null && !levelData.canRecoveryExtra && LevelManager.instance.hasBeenRecovered)
                return true;

            return false;
        }
    }

    public Animator animator
    {
        get
        {
            if (__animator == null)
                __animator = GetComponentInChildren<Animator>();

            return __animator;
        }
    }

    [Preserve]
    public void Play(AnimatorParameters parameters)
    {
        switch (parameters.name)
        {
            case "Hit":
                //LevelManager.instance.dataFlag |= (int)ILevelData.Flag.HasBeenDamaged;
                
                if(__isLocal)
                    VibrateUtility.Apply(VibrationType.Peek);
                
                break;
            case "Die":
                if(__attributeEventReceiver != null)
                    __attributeEventReceiver.Clear();
                
                __SetStatus(Status.Dead);
                break;
            case "Respawn":
                if(__attributeEventReceiver != null)
                    __attributeEventReceiver.Clear();
                
                __SetStatus(Status.Respawn);
                break;
        }
        
        parameters.Apply(animator);
    }

    [Preserve]
    public void Respawn()
    {
        PlayerEvents.isActive = true;
        
        (IAnalytics.instance as IAnalyticsEx)?.RespawnPlayer();
        
        animator.SetInteger(RespawnStatusHash, (int)RespawnStatus.RightNow);
    }

    private void __OnChanged(int id, int value)
    {
        switch ((EffectAttributeID)id)
        {
            case EffectAttributeID.HPMax:
                if (__isLocal)
                {
                    (IAnalytics.instance as IAnalyticsEx)?.SetPlayerHPMax(value);

                    LevelManager.instance.hpPercentage =
                        __attributeEventReceiver[(int)EffectAttributeID.HP] * 100 / value;
                }

                break;
            case EffectAttributeID.HP:
                if(value > 0)
                    __SetStatus(0);

                if (__isLocal)
                {
                    (IAnalytics.instance as IAnalyticsEx)?.SetPlayerHP(value);

                    int max = __attributeEventReceiver[(int)EffectAttributeID.HPMax];
                    LevelManager.instance.hpPercentage = max > 0 ? value * 100 / max : 100;
                }

                break;
            case EffectAttributeID.Rage:
                if (__isLocal)
                    LevelManager.instance.rage = value;
                break;
        }
    }

    private void __SetStatus(Status value)
    {
        if (__isLocal && ((value ^ __status) & Status.Dead) == Status.Dead)
        {
            var levelManager = LevelManager.instance;
            var analytics = IAnalytics.instance as IAnalyticsEx;
            if ((value & Status.Dead) == Status.Dead)
            {
                //if(__timeScaleIndex == -1)
                //    __timeScaleIndex = TimeScaleUtility.Add(0.0f);

                if ((value & Status.Respawn) == Status.Respawn)
                {
                    bool isWaitingToRespawn = PlayerController.isWaitingToRespawn;
                    animator.SetInteger(RespawnStatusHash, isWaitingToRespawn ? (int)RespawnStatus.Waiting : 0);

                    if (levelManager == null)
                        __Respawn(false);
                    else
                        levelManager.ScheduleRecovery(__Respawn);
                }

                analytics?.DisablePlayer();
            }
            else
            {
                PlayerEvents.RespawnEnd();
                
                if(levelManager != null)
                    levelManager.ConfirmRecovery();
            
                analytics?.EnablePlayer();
            }
        }
        
        __status = value;
    }
    
    private static void __Respawn(bool result)
    {
        if (result)
        {
            PlayerEvents.RespawnStart();

            (IAnalytics.instance as IAnalyticsEx)?.RespawnPlayer();
        }
        else
            PlayerEvents.isActive = false;
    }
    
    void OnEnable()
    {
        __instanceID = 0;
        
        if (__attributeEventReceiver == null)
            __attributeEventReceiver = GetComponentInChildren<AttributeEventReceiver>();
        
        if (__attributeEventReceiver != null)
            __attributeEventReceiver.onChanged += __OnChanged;

        ++PlayerEvents.survivingCount;

        __SetStatus(0);
    }

    void OnDisable()
    {
        if (__isLocal)
        {
            __isLocal = false;

            if ((__status & Status.Dead) == Status.Dead)
                PlayerEvents.isActive = false;
        }
        
        --PlayerEvents.survivingCount;

        if ((object)__attributeEventReceiver != null)
            __attributeEventReceiver.onChanged -= __OnChanged;
    }

    void Update()
    {
        int instanceID = LocalPlayer.instanceID;
        if (instanceID == 0)
            return;

        var transform = this.transform;
        var position = transform.position;

        if (__instanceID == 0)
        {
            __instanceID = transform.GetInstanceID();
            
            __position = position;
        }

        if (!__isLocal && __instanceID == instanceID)
        {
            __isLocal = true;

            PlayerEvents.Restart();

            PlayerEvents.isActive = true;
        }

        if (__isLocal || PlayerEvents.isFocusRemotePlayer)
        {
            var playerPosition = PlayerPosition.instances == null ? null : PlayerPosition.instances[(int)PlayerType.Local];
            if (playerPosition != null)
                playerPosition.SetPosition(position);

            var rotationInstance = PlayerRotation.instances == null ? null : PlayerRotation.instances[(int)PlayerType.Local];
            if (rotationInstance != null)
                rotationInstance.SetRotation(transform.rotation);
        }
        else
        {
            var playerPosition = PlayerPosition.instances == null ? null : PlayerPosition.instances[(int)PlayerType.Remote];
            if (playerPosition != null)
                playerPosition.SetPosition(position);
            
            var rotationInstance = PlayerRotation.instances == null ? null : PlayerRotation.instances[(int)PlayerType.Remote];
            if (rotationInstance != null)
                rotationInstance.SetRotation(transform.rotation);
        }

        if(__isLocal)
            JoystickAnimatorController.Update(animator);
        else
            JoystickAnimatorController.Update(animator, ref __position, ref __velocity, _smoothTime);
    }
}
