using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;

public class PlayerEvents : MonoBehaviour
{
    [SerializeField]
    internal UnityEvent _onAllDead;
    [SerializeField]
    internal UnityEvent _onEnable;
    [SerializeField]
    internal UnityEvent _onDisable;
    [SerializeField]
    internal UnityEvent _onRespawn;
    [SerializeField]
    internal UnityEvent _noRecoveryExtra;
    [SerializeField]
    internal UnityEvent _dontKeepRecoveryTime;
    [SerializeField]
    internal UnityEvent _multiplayer;

    private static HashSet<PlayerEvents> __instances;

    private static int __timeScaleIndex = -1;
    
    private static int __survivingCount;

    private static bool __isRespawning;

    private static bool __isActive;
    
    public static bool isActive
    {
        get => __isActive;

        set
        {
            if (value == __isActive)
                return;

            if (__instances != null)
            {
                if (value)
                {
                    foreach (var instance in __instances)
                    {
                        if(instance._onEnable != null)
                            instance._onEnable.Invoke();
                    }

                    __ClearTimeScale();
                }
                else
                {
                    if(__survivingCount < 2)
                        __SetTimeScale();

                    foreach (var instance in __instances)
                    {
                        if(instance._onDisable != null)
                            instance._onDisable.Invoke();
                    }
                }
            }

            __isActive = value;
        }
    }

    public static bool isFocusRemotePlayer
    {
        get;

        private set;
    }

    public static int survivingCount
    {
        get => __survivingCount;
        
        set
        {
            if (value == __survivingCount)
                return;

            if (value == 0)
            {
                __SetTimeScale();
                
                foreach (var instance in __instances)
                    instance._onAllDead?.Invoke();
            }

            __survivingCount = value;
        }
    }

    public static void Restart()
    {
        if (__instances == null)
            return;
        
        var levelData = ILevelData.instance;
        if (levelData != null && !levelData.canRecoveryExtra)
        {
            foreach (var instance in __instances)
                instance._noRecoveryExtra?.Invoke();
        }

        if (!EffectShared.keepRecoveryTime)
        {
            foreach (var instance in __instances)
                instance._dontKeepRecoveryTime?.Invoke();
        }
        
        if (RemotePlayer.Status.Disabled != RemotePlayer.status)
        {
            foreach (var instance in __instances)
                instance._multiplayer?.Invoke();
        }
    }

    public static void RespawnStart()
    {
        if (__survivingCount < 2)
        {
            __isRespawning = true;

            __SetTimeScale();
        }

        if (__instances != null)
        {
            foreach (var instance in __instances)
                instance._onRespawn?.Invoke();
        }
    }

    public static void RespawnEnd()
    {
        if (__isRespawning)
        {
            __isRespawning = false;
            
            __ClearTimeScale();
        }
    }

    private static void __SetTimeScale()
    {
        if(__instances == null || __instances.Count < 1)
            return;;
        
        if (__timeScaleIndex == -1)
            __timeScaleIndex = TimeScaleUtility.Add(0.0f);
    }

    private static void __ClearTimeScale()
    {
        TimeScaleUtility.Remove(__timeScaleIndex);

        __timeScaleIndex = -1;
    }

    [Preserve]
    public void FocusRemotePlayer()
    {
        isFocusRemotePlayer = true;
    }

    void OnEnable()
    {
        if (__instances == null)
            __instances = new HashSet<PlayerEvents>();

        if (__instances.Add(this) && __instances.Count == 1)
            __isActive = true;
    }

    void OnDisable()
    {
        if (__instances.Remove(this) && __instances.Count == 0)
        {
            __ClearTimeScale();

            isFocusRemotePlayer = false;
        }
    }
}
