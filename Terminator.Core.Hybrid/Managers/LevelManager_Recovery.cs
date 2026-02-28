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

    [UnityEngine.Scripting.Preserve]
    public void Recovery()
    {
        if (RecoveryStatus.None != __recoveredStatus)
            return;

        var levelData = ILevelData.instance;
        if (levelData == null)
        {
            __recoveredStatus = RecoveryStatus.Recovering;
            
            return;
        }

        __recoveredStatus = RecoveryStatus.Waiting;
        
        _onRecoveredStart?.Invoke();
        
        __StartCoroutine(nameof(ILevelData.Recovery), levelData.Recovery(x =>
        {
            __recoveredStatus = x ? RecoveryStatus.Recovering : RecoveryStatus.None;
            
            _onRecoveredEnd?.Invoke(!x);
        }));
    }
}
