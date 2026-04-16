using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    private enum RecoveryStatus
    {
        None, 
        Failed, 
        UserConfirmed, 
        UserBuy, 
        WaitingForUser, 
        WaitingForTime, 
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
    
    public bool IsRecovery(out bool isWaitingForUser)
    {
        if (isRestart)
        {
            isWaitingForUser = false;
            
            __recoveredStatus = RecoveryStatus.None;

            hasBeenRecovered = false;
        }
        else
        {
            isWaitingForUser = false;
            switch (__recoveredStatus)
            {
                //case RecoveryStatus.WaitingForTime:
                case RecoveryStatus.WaitingForUser:
                    //bulletStatus = LevelBulletStatus.DestroyAll;
                    isWaitingForUser = true;
                    break;
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

    public void BuyRecovery()
    {
        if(RecoveryStatus.WaitingForUser == __recoveredStatus)
            __recoveredStatus = RecoveryStatus.UserBuy;
    }

    public void ConfirmRecovery()
    {
        if (isRestart)
        {
            __recoveredStatus = RecoveryStatus.None;

            return;
        }

        switch (__recoveredStatus)
        {
            case RecoveryStatus.WaitingForTime:
                __recoveredStatus = RecoveryStatus.None;
                break;
            case RecoveryStatus.WaitingForUser:
                __recoveredStatus = RecoveryStatus.UserConfirmed;
                break;
        }
    }

    public void ScheduleRecovery(System.Action<bool> waitingForTime)
    {
        __StartCoroutine(nameof(__Recovering), __Recovering(waitingForTime));
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
        ConfirmRecovery();
        //Recovery(null);
    }

    private IEnumerator __Recovering(System.Action<bool> waitingForTime)
    {
        if (RecoveryStatus.None == __recoveredStatus)
        {
            var levelData = ILevelData.instance;
            if (levelData == null)
            {
                if (waitingForTime != null)
                {
                    __recoveredStatus = RecoveryStatus.WaitingForUser;

                    waitingForTime(false);

                    while (RecoveryStatus.WaitingForUser == __recoveredStatus)
                    {
                        if (isRestart || __isQuitting)
                        {
                            __recoveredStatus = RecoveryStatus.None;

                            hasBeenRecovered = false;
                        }
                        else
                            yield return null;
                    }
                }
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
                            if (waitingForTime != null)
                            {
                                __recoveredStatus = RecoveryStatus.WaitingForTime;

                                waitingForTime(true);

                                while (RecoveryStatus.WaitingForTime == __recoveredStatus)
                                {
                                    if (isRestart || __isQuitting)
                                    {
                                        __recoveredStatus = RecoveryStatus.None;

                                        hasBeenRecovered = false;
                                    }
                                    else
                                        yield return null;
                                }
                            }
                            
                            yield break;
                        }
                        
                        recoveryStatus = RecoveryStatus.TheLastTime;
                    }
                    else
                    {
                        do
                        {
                            if (waitingForTime != null)
                            {
                                __recoveredStatus = RecoveryStatus.WaitingForUser;

                                waitingForTime(false);

                                do
                                {
                                    if (isRestart || __isQuitting)
                                    {
                                        __recoveredStatus = RecoveryStatus.None;

                                        hasBeenRecovered = false;
                                    }
                                    else
                                        yield return null;
                                } while (RecoveryStatus.WaitingForUser == __recoveredStatus);
                            }

                            switch (__recoveredStatus)
                            {
                                case RecoveryStatus.UserBuy:
                                case RecoveryStatus.UserConfirmed:
                                    __recoveredStatus = RecoveryStatus.WaitingForQuery;

                                    _onRecovering?.Invoke();

                                    yield return levelData.Buy(x =>
                                    {
                                        if (RecoveryStatus.WaitingForQuery == __recoveredStatus)
                                            __recoveredStatus = x ? RecoveryStatus.TheLastTime : RecoveryStatus.Failed;

                                        if (x)
                                        {
                                            LevelPlayerShared<LocalPlayer>.property.effectTargetRecoveryTimes = 2;

                                            _onRecoveredSuccess?.Invoke();
                                        }
                                        else
                                            _onRecoveredFailure?.Invoke();
                                    });

                                    break;
                            }

                        } while (RecoveryStatus.Failed == __recoveredStatus);

                        yield break;
                    }
                }
                else
                {
                    hasBeenRecovered = true;

                    if (EffectShared.keepRecoveryTime)
                    {
                        if (waitingForTime != null)
                        {
                            __recoveredStatus = RecoveryStatus.WaitingForTime;

                            waitingForTime(true);

                            while (RecoveryStatus.WaitingForTime == __recoveredStatus)
                            {
                                if (isRestart || __isQuitting)
                                {
                                    __recoveredStatus = RecoveryStatus.None;

                                    hasBeenRecovered = false;
                                }
                                else
                                    yield return null;
                            }
                        }
                        
                        yield break;
                    }
                }

                do
                {
                    if (waitingForTime != null)
                    {
                        __recoveredStatus = RecoveryStatus.WaitingForUser;

                        waitingForTime(false);

                        do
                        {
                            if (isRestart || __isQuitting)
                            {
                                __recoveredStatus = RecoveryStatus.None;

                                hasBeenRecovered = false;
                            }
                            else
                                yield return null;
                        } while (RecoveryStatus.WaitingForUser == __recoveredStatus);
                    }

                    switch (__recoveredStatus)
                    {
                        case RecoveryStatus.UserBuy:
                        case RecoveryStatus.UserConfirmed:

                            bool hasBuy = RecoveryStatus.UserBuy == __recoveredStatus;

                            __recoveredStatus = RecoveryStatus.WaitingForQuery;

                            _onRecovering?.Invoke();

                            if (hasBuy)
                            {
                                yield return levelData.BuyToSkip(x =>
                                {
                                    if (RecoveryStatus.WaitingForQuery == __recoveredStatus)
                                        __recoveredStatus = x ? recoveryStatus : RecoveryStatus.Failed;

                                    if (x)
                                    {
                                        EffectShared.keepRecoveryTime = true;

                                        _onRecoveredSuccess?.Invoke();
                                    }
                                    else
                                        _onRecoveredFailure?.Invoke();
                                });
                            }
                            else
                                yield return levelData.Broadcast(x =>
                                {
                                    if (RecoveryStatus.WaitingForQuery == __recoveredStatus)
                                        __recoveredStatus = x ? recoveryStatus : RecoveryStatus.Failed;

                                    if (x)
                                        _onRecoveredSuccess?.Invoke();
                                    else
                                        _onRecoveredFailure?.Invoke();
                                });

                            break;
                    }
                } while (RecoveryStatus.Failed == __recoveredStatus);
            }
        }
    }
}
