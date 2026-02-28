using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerEvents : MonoBehaviour
{
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

    private static HashSet<PlayerEvents> __instances;

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
                }
                else
                {
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

    public static void Respawn()
    {
        var levelManager = LevelManager.instance;
        if (levelManager == null)
            __Respawn();
        else
            levelManager.Recovery(__Respawn);
    }

    private static void __Respawn()
    {
        if (__instances != null)
        {
            foreach (var instance in __instances)
                instance._onRespawn?.Invoke();
        }
    }
    
    private static void __Respawn(bool result)
    {
        if (result)
            __Respawn();
        else
            isActive = false;
    }

    void OnEnable()
    {
        if (__instances == null)
            __instances = new HashSet<PlayerEvents>();

        if (__instances.Add(this) && __instances.Count == 1)
            __isActive = true;

        var levelData = ILevelData.instance;
        if(levelData != null && !levelData.canRecoveryExtra)
            _noRecoveryExtra?.Invoke();
        
        if(!EffectShared.keepRecoveryTime)
            _dontKeepRecoveryTime.Invoke();
    }

    void OnDisable()
    {
        __instances.Remove(this);
    }
}
