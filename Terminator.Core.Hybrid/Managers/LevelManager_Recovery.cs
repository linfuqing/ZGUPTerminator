using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    private enum RecoveryStatus
    {
        None, 
        Waiting,
        Recovering,
    }
    
    [SerializeField]
    internal UnityEvent _onRecoveredStart;
    
    [SerializeField]
    internal ActiveEvent _onRecoveredEnd;

    private RecoveryStatus __recoveredStatus;
    
    public bool IsRecovery()
    {
        if (RecoveryStatus.Recovering == __recoveredStatus)
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

        __recoveredStatus = RecoveryStatus.Waiting;
        
        _onRecoveredStart?.Invoke();
        
        __StartCoroutine(nameof(ILevelData.Recovery), levelData.Recovery(x =>
        {
            if(callback != null)
                callback(x);

            __recoveredStatus = x ? RecoveryStatus.Recovering : RecoveryStatus.None;
            
            _onRecoveredEnd?.Invoke(!x);
        }));
    }

    [UnityEngine.Scripting.Preserve]
    public void Recovery()
    {
        Recovery(null);
    }
}
