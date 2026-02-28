using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    private enum RecoveryStatus
    {
        None, 
        Waiting,
        Recovering,
        Finish
    }
    
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_onRecoveredStart")]
    internal UnityEvent _onRecovering;
    
    [SerializeField]
    internal UnityEvent _onRecoveredSuccess;

    [SerializeField]
    internal UnityEvent _onRecoveredFailure;

    private RecoveryStatus __recoveredStatus;
    
    public bool hasBeenRecovered
    {
        get;

        private set;
    }
    
    public bool IsRecovery()
    {
        if (isRestart)
        {
            __recoveredStatus = RecoveryStatus.None;

            hasBeenRecovered = false;
        }
        else if (RecoveryStatus.Recovering == __recoveredStatus)
        {
            __recoveredStatus = RecoveryStatus.None;

            return true;
        }

        return false;
    }

    public void Recovery(System.Action<bool> callback)
    {
        if (RecoveryStatus.None != __recoveredStatus)
        {
            if(callback != null)
                callback(false);
            
            return;
        }

        var levelData = ILevelData.instance;
        if (levelData == null)
        {
            if(callback != null)
                callback(true);

            return;
        }

        if (hasBeenRecovered)
        {
            if (levelData.canRecoveryExtra)
            {
                __recoveredStatus = RecoveryStatus.Finish;
                
                if(callback != null)
                    callback(true);
            }
            else
            {
                __recoveredStatus = RecoveryStatus.Waiting;

                _onRecovering?.Invoke();

                __StartCoroutine(nameof(ILevelData.Buy), levelData.Buy(x =>
                {
                    if(callback != null)
                        callback(x);

                    if(RecoveryStatus.Waiting == __recoveredStatus)
                        __recoveredStatus = x ? RecoveryStatus.Finish : RecoveryStatus.None;
            
                    if(x)
                        _onRecoveredSuccess?.Invoke();
                    else
                        _onRecoveredFailure?.Invoke();
                }));
            }

            return;
        }
        
        hasBeenRecovered = true;

        if (EffectShared.keepRecoveryTime)
        {
            if(callback != null)
                callback(true);

            __recoveredStatus = RecoveryStatus.None;

            return;
        }

        __recoveredStatus = RecoveryStatus.Waiting;

        _onRecovering?.Invoke();
        
        __StartCoroutine(nameof(ILevelData.Broadcast), levelData.Broadcast(x =>
        {
            if(callback != null)
                callback(x);

            if(RecoveryStatus.Waiting == __recoveredStatus)
                __recoveredStatus = x ? RecoveryStatus.Recovering : RecoveryStatus.None;
            
            if(x)
                _onRecoveredSuccess?.Invoke();
            else
                _onRecoveredFailure?.Invoke();
        }));
    }

    [UnityEngine.Scripting.Preserve]
    public void Recovery()
    {
        Recovery(null);
    }
}
