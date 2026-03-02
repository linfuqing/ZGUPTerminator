using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    private enum RecoveryStatus
    {
        None, 
        UserConfirmed, 
        WaitingForUser, 
        WaitingForQuery,
        Recovering,
        TheLastTime, 
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
        else
        {
            switch (__recoveredStatus)
            {
                case RecoveryStatus.Recovering:
                    __recoveredStatus = RecoveryStatus.None;

                    return true;
                case RecoveryStatus.TheLastTime:
                    __recoveredStatus = RecoveryStatus.Finish;
                    return true;
            }
        }
        
        return false;
    }

    public void RecoveryConfirm()
    {
        if(RecoveryStatus.WaitingForUser != __recoveredStatus)
            return;

        __recoveredStatus = RecoveryStatus.UserConfirmed;
    }

    public void Recovery(System.Action<bool> callback)
    {
        __StartCoroutine(nameof(__Recovering), __Recovering(callback));
        /*if (RecoveryStatus.None != __recoveredStatus)
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

        var recoveryStatus = RecoveryStatus.Recovering;
        if (hasBeenRecovered)
        {
            if (levelData.canRecoveryExtra)
            {
                if (EffectShared.keepRecoveryTime)
                {
                    if (callback != null)
                        callback(true);

                    //__recoveredStatus = RecoveryStatus.None;

                    return;
                }

                recoveryStatus = RecoveryStatus.TheLastTime;
            }
            else
            {
                __recoveredStatus = RecoveryStatus.WaitingForQuery;

                _onRecovering?.Invoke();

                __StartCoroutine(nameof(ILevelData.Buy), levelData.Buy(x =>
                {
                    if(callback != null)
                        callback(x);

                    if(RecoveryStatus.WaitingForQuery == __recoveredStatus)
                        __recoveredStatus = x ? RecoveryStatus.TheLastTime : RecoveryStatus.None;

                    if(x)
                        _onRecoveredSuccess?.Invoke();
                    else
                        _onRecoveredFailure?.Invoke();
                }));

                return;
            }
        }
        else
        {
            hasBeenRecovered = true;

            if (EffectShared.keepRecoveryTime)
            {
                if (callback != null)
                    callback(true);

                //__recoveredStatus = RecoveryStatus.None;

                return;
            }
        }

        __recoveredStatus = RecoveryStatus.WaitingForQuery;

        _onRecovering?.Invoke();

        __StartCoroutine(nameof(ILevelData.Broadcast), levelData.Broadcast(x =>
        {
            if(callback != null)
                callback(x);

            if(RecoveryStatus.WaitingForQuery == __recoveredStatus)
                __recoveredStatus = x ? recoveryStatus : RecoveryStatus.None;

            if(x)
                _onRecoveredSuccess?.Invoke();
            else
                _onRecoveredFailure?.Invoke();
        }));*/
    }

    [UnityEngine.Scripting.Preserve]
    public void Recovery()
    {
        RecoveryConfirm();
        //Recovery(null);
    }

    private IEnumerator __Recovering(System.Action<bool> callback)
    {
        if (RecoveryStatus.None != __recoveredStatus)
        {
            if(callback != null)
                callback(false);
        }
        else
        {
            var levelData = ILevelData.instance;
            if (levelData == null)
            {
                __recoveredStatus = RecoveryStatus.WaitingForUser;
                
                if (callback != null)
                    callback(true);

                while (RecoveryStatus.WaitingForUser == __recoveredStatus)
                    yield return null;
            }
            else
            {
                var recoveryStatus = RecoveryStatus.Recovering;
                if (hasBeenRecovered)
                {
                    if (levelData.canRecoveryExtra)
                    {
                        if (EffectShared.keepRecoveryTime)
                        {
                            __recoveredStatus = RecoveryStatus.WaitingForUser;

                            if (callback != null)
                                callback(true);

                            while (RecoveryStatus.WaitingForUser == __recoveredStatus)
                                yield return null;
                        }
                        else
                            recoveryStatus = RecoveryStatus.TheLastTime;
                    }
                    else
                    {
                        __recoveredStatus = RecoveryStatus.WaitingForUser;

                        do
                        {
                            yield return null;
                        } while (RecoveryStatus.WaitingForUser == __recoveredStatus);

                        if (RecoveryStatus.UserConfirmed == __recoveredStatus)
                        {
                            __recoveredStatus = RecoveryStatus.WaitingForQuery;

                            _onRecovering?.Invoke();

                            yield return levelData.Buy(x =>
                            {
                                if (callback != null)
                                    callback(x);

                                if (RecoveryStatus.WaitingForQuery == __recoveredStatus)
                                    __recoveredStatus = x ? RecoveryStatus.TheLastTime : RecoveryStatus.None;

                                if (x)
                                    _onRecoveredSuccess?.Invoke();
                                else
                                    _onRecoveredFailure?.Invoke();
                            });
                        }

                        yield break;
                    }
                }
                else
                {
                    hasBeenRecovered = true;

                    if (EffectShared.keepRecoveryTime)
                    {
                        __recoveredStatus = RecoveryStatus.WaitingForUser;

                        if (callback != null)
                            callback(true);

                        while (RecoveryStatus.WaitingForUser == __recoveredStatus)
                            yield return null;
                        
                        yield break;
                    }
                }

                __recoveredStatus = RecoveryStatus.WaitingForUser;

                do
                {
                    yield return null;
                } while (RecoveryStatus.WaitingForUser == __recoveredStatus);

                if (RecoveryStatus.UserConfirmed == __recoveredStatus)
                {
                    __recoveredStatus = RecoveryStatus.WaitingForQuery;

                    _onRecovering?.Invoke();

                    yield return levelData.Broadcast(x =>
                    {
                        if (callback != null)
                            callback(x);

                        if (RecoveryStatus.WaitingForQuery == __recoveredStatus)
                            __recoveredStatus = x ? recoveryStatus : RecoveryStatus.None;

                        if (x)
                            _onRecoveredSuccess?.Invoke();
                        else
                            _onRecoveredFailure?.Invoke();
                    });
                }
            }
        }
    }
}
