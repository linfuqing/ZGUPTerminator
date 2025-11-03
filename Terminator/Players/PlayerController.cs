using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Flags]
    private enum Status
    {
        Dead = 0x01, 
        Respawn = 0x02 | Dead,
    }

    private Status __status;
    private int __timeScaleIndex = -1;
    
    private AttributeEventReceiver __attributeEventReceiver;

    private Animator __animator;
    public Animator animator
    {
        get
        {
            if (__animator == null)
                __animator = GetComponentInChildren<Animator>();

            return __animator;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public void Play(AnimatorParameters parameters)
    {
        switch (parameters.name)
        {
            case "Hit":
                //LevelManager.instance.dataFlag |= (int)ILevelData.Flag.HasBeenDamaged;
                
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

    private void __OnChanged(int id, int value)
    {
        switch ((EffectAttributeID)id)
        {
            case EffectAttributeID.HPMax:
                (IAnalytics.instance as IAnalyticsEx)?.SetPlayerHPMax(value);

                LevelManager.instance.hpPercentage = __attributeEventReceiver[(int)EffectAttributeID.HP] * 100 / value;
                break;
            case EffectAttributeID.HP:
                (IAnalytics.instance as IAnalyticsEx)?.SetPlayerHP(value);

                if(value > 0)
                    __SetStatus(0);

                int max = __attributeEventReceiver[(int)EffectAttributeID.HPMax];
                LevelManager.instance.hpPercentage = max > 0 ? value * 100 / max : 100;
                
                break;
            case EffectAttributeID.Rage:
                LevelManager.instance.rage = value;
                break;
        }
    }

    private void __SetStatus(Status value)
    {
        if (((value ^ __status) & Status.Dead) == Status.Dead)
        {
            var analytics = IAnalytics.instance as IAnalyticsEx;
            if ((value & Status.Dead) == Status.Dead)
            {
                if ((value & Status.Respawn) == Status.Respawn)
                {
                    if(__timeScaleIndex == -1)
                        __timeScaleIndex = TimeScaleUtility.Add(0.0f);

                    PlayerEvents.Respawn();
                    
                    analytics?.RespawnPlayer();
                }

                analytics?.DisablePlayer();
            }
            else
            {
                TimeScaleUtility.Remove(__timeScaleIndex);

                __timeScaleIndex = -1;
            
                analytics?.EnablePlayer();
            }
        }
        
        __status = value;
    }

    void Awake()
    {
        if (__attributeEventReceiver == null)
            __attributeEventReceiver = GetComponentInChildren<AttributeEventReceiver>();

        if (__attributeEventReceiver != null)
            __attributeEventReceiver.onChanged += __OnChanged;
    }

    private void OnEnable()
    {
        __SetStatus(0);
        
        PlayerEvents.isActive = true;
    }

    private void OnDisable()
    {
        if ((__status & Status.Dead) == Status.Dead)
        {
            if(__timeScaleIndex == -1)
                __timeScaleIndex = TimeScaleUtility.Add(0.0f);
            
            PlayerEvents.isActive = false;
        }
    }

    void OnDestroy()
    {
        if ((object)__attributeEventReceiver != null)
            __attributeEventReceiver.onChanged -= __OnChanged;
        
        TimeScaleUtility.Remove(__timeScaleIndex);

        __timeScaleIndex = -1;
    }

    void Update()
    {
        var transform = this.transform;
        var position = transform.position;

        var positionInstance = PlayerPosition.instance;
        if(positionInstance != null)
            positionInstance.transform.position = position;
        
        var rotationInstance = PlayerRotation.instance;
        if(rotationInstance != null)
            rotationInstance.transform.rotation = transform.rotation;

        JoystickAnimatorController.Update(animator);

        /*if (position.y > _minJumpHeight)
        {
            float velocity = position.y - __jumpHeight;
            int sign = Mathf.Abs(velocity) > _minJumpVelocity ? (int)Mathf.Sign(velocity) : 0;
            //Debug.LogError(sign);
            animator.SetInteger(Jump, sign);

            animator.SetInteger(Status, (int)AnimatorStatus.Jump);
        }
        else
        {
            animator.SetInteger(Jump, 0);
            
            animator.SetInteger(Status, (int)AnimatorStatus.Normal);
        }

        __jumpHeight = position.y;*/
    }
}
