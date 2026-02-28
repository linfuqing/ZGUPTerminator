using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    private bool __isRecovery;
    
    public bool IsRecovery()
    {
        if (__isRecovery)
        {
            __isRecovery = false;

            return true;
        }

        return false;
    }

    public void Recovery(System.Action<bool> callback)
    {
        if (__isRecovery)
        {
            callback(false);
            
            return;
        }

        if (EffectShared.keepRecoveryTime)
        {
            __isRecovery = true;
        }
        else
            __StartCoroutine(nameof(ILevelData.Recovery), ILevelData.instance?.Recovery(x =>
            {
                callback(x);

                __isRecovery = x;
            }));
    }
}
