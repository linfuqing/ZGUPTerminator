using System;
using System.Collections;
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
        if (__instances != null)
        {
            foreach (var instance in __instances)
            {
                if(instance._onRespawn != null)
                    instance._onRespawn.Invoke();
            }
        }
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
        __instances.Remove(this);
    }
}
