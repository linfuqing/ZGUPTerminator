using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    [Flags]
    private enum Flag
    {
        Show = 0x01,
        New = 0x02, 
        Hot = 0x04
    }

    private struct Notice
    {
        public string code;
        public NoticeStyle style;
    }
    
    public static Action<bool> onNew;

    public static Action<bool> onHot;

    /// <summary>
    /// 显示菊花
    /// </summary>
    public static Action<bool> onLoading;
    
    /// <summary>
    /// 创建邮件可领取的奖励物品
    /// </summary>
    public static Action<Memory<UserRewardData>, Transform> onRewardInit;
    
    /// <summary>
    /// 弹出奖励框
    /// </summary>
    public static Action<Memory<UserReward>> onRewardSubmit;

    [SerializeField] 
    internal float _minQueryNoticeTime = 30.0f;

    [SerializeField] 
    internal string _dealLineFormat;
    
    [SerializeField]
    [Tooltip("普通邮件")]
    internal NoticeStyle _noticeStyle;
    [SerializeField]
    [Tooltip("重要邮件")]
    internal NoticeStyle _noticeStyleImportant;
    [SerializeField]
    [Tooltip("维护公告，不可关闭")]
    internal NoticeStyle _noticeStyleCanNotBeClosed;
    [SerializeField]
    [Tooltip("弹出邮件")]
    internal UnityEvent _onShowNotice;
    [SerializeField]
    [Tooltip("弹出维护公告，不可关闭")]
    internal UnityEvent _onShowNoticeCanNotBeClosed;

    public bool isNew => (__flag & Flag.New) == Flag.New;
    
    public bool isHot => (__flag & Flag.Hot) == Flag.Hot;
    
    private Flag __flag;
    private uint __userID;
    private int __newCount;
    private int __queryNoticeCoroutineIndex = -1;
    private float __queryNoticeTime;
    private Coroutine __coroutine;
    private List<Notice> __notices;
    
    private const string CONSTANT_KEY_VERSION_NOTICE = "NoticeVersion";
    private const string CONSTANT_KEY_VERSION_CODE = "CodeVersion";
    
    private const string NAME_SPACE_TIMES = "GameManagerNoticeTimes";

    public void QueryNotices(bool isForceShow)
    {
        __flag &= ~Flag.Show;
        
        GameProgressbar progressbar;
        if (__queryNoticeCoroutineIndex == -1)
        {
            progressbar = GameProgressbar.instance;
            if (progressbar != null)
            {
                if (progressbar.isProgressing)
                {
                    __queryNoticeCoroutineIndex = progressbar.BeginCoroutine();

                    progressbar.ShowProgressBar(GameProgressbar.ProgressbarType.Other, __queryNoticeCoroutineIndex);
                    progressbar.UpdateProgressBar(1.0f);
                }
                else
                    progressbar = null;
            }
        }
        else
            progressbar = null;
        
        __coroutine = StartCoroutine(__OnQueryNotices(isForceShow, __coroutine));
        
        if(progressbar != null)
            progressbar.EndCoroutine(__queryNoticeCoroutineIndex, __coroutine);
    }

    public bool ApplyNoticeCodes()
    {
        List<string> codes = null;
        Notice notice;
        int numNotices = __notices == null ? 0 : __notices.Count;
        for (int i = 0; i < numNotices; ++i)
        {
            notice = __notices[i];
            if(string.IsNullOrEmpty(notice.code))
                continue;
            
            if(codes == null)
                codes = new List<string>();
            
            codes.Add(notice.code);
        }

        if (codes == null || !ApplyCode(codes.ToArray()))
            return false;
        
        for (int i = 0; i < numNotices; ++i)
        {
            notice = __notices[i];
            if(string.IsNullOrEmpty(notice.code))
                continue;

            notice.code = null;
            if(notice.style.button != null)
                notice.style.button.interactable = false;

            __notices[i] = notice;
        }

        return true;
    }
    
    public bool ApplyCode(params string[] codes)
    {
        if (__coroutine != null)
            return false;
        
        if (onLoading != null)
            onLoading(true);
        
        string version = GameConstantManager.Get(CONSTANT_KEY_VERSION_CODE);
        __coroutine = StartCoroutine(IGameData.instance.ApplyCode(
            GameMain.userID,
            string.IsNullOrEmpty(version) || !uint.TryParse(version, out uint versionValue) ? 0 : versionValue,
            codes,
            __OnApplyCode));
        
        return true;
    }
    
    private void __OnApplyCode(Memory<UserReward> rewards)
    {
        __coroutine = null;
        
        if (onLoading != null)
            onLoading(false);

        if(onRewardSubmit != null)
            onRewardSubmit(rewards);
    }

    private void __OnQueryNotices(IGameData.Notices notices)
    {
        __ClearProgressBar();

        __newCount = 0;
        if (__notices != null)
        {
            foreach (var notice in __notices)
                Destroy(notice.style.gameObject);
            
            __notices.Clear();
        }

        NoticeStyle noticeStyle;
        if ((notices.flag & IGameData.Notices.Flag.NotBeClosed) == IGameData.Notices.Flag.NotBeClosed)
        {
            var parent = _noticeStyleCanNotBeClosed.transform.parent;
            foreach (var notice in notices.notices)
            {
                noticeStyle = Instantiate(_noticeStyleCanNotBeClosed, parent);
                __Init(noticeStyle, notice);
            }

            _onShowNoticeCanNotBeClosed?.Invoke();
            
            __flag |= Flag.Show;
        }
        else
        {
            bool isImportantNew = false;
            NoticeStyle noticeStylePrefab;
            foreach (var notice in notices.notices)
            {
                if ((notice.flag & IGameData.Notice.Flag.Important) == IGameData.Notice.Flag.Important)
                {
                    if(PlayerPrefs.GetInt($"{NAME_SPACE_TIMES}{notice.id}") == 0)
                        isImportantNew = true;

                    noticeStylePrefab = _noticeStyleImportant == null ? _noticeStyle : _noticeStyleImportant;
                }
                else
                    noticeStylePrefab = _noticeStyle;

                noticeStyle = Instantiate(noticeStylePrefab, noticeStylePrefab.transform.parent);
                
                __Init(noticeStyle, notice);
            }

            if (isImportantNew)
            {
                _onShowNotice?.Invoke();
            
                __flag |= Flag.Show;
            }
        }
    }

    private void __Init(NoticeStyle noticeStyle, IGameData.Notice notice)
    {
        string key = $"{NAME_SPACE_TIMES}{notice.id}";
        int times = PlayerPrefs.GetInt(key);
        PlayerPrefs.SetInt(key, times + 1);

        bool isNew = times == 0;
        
        ++__newCount;
        
        noticeStyle.onNew?.Invoke(isNew);
        noticeStyle.onTitle?.Invoke(notice.name);
        noticeStyle.onDetail?.Invoke(notice.text);
        noticeStyle.onDealLine?.Invoke(notice.ticks == 0
            ? string.Empty
            : new DateTime(notice.ticks).ToLocalTime().ToString(_dealLineFormat));


        int noticeIndex = __notices.Count;
        Notice result;
        result.code = null;
        if (notice.rewards != null && notice.rewards.Length > 0)
        {
            if (noticeStyle.button != null)
            {
                if (string.IsNullOrEmpty(notice.code))
                    noticeStyle.button.gameObject.SetActive(false);
                else if((notice.flag & IGameData.Notice.Flag.Used) == IGameData.Notice.Flag.Used)
                    noticeStyle.button.interactable = false;
                else
                {
                    result.code = notice.code;

                    var onClick = noticeStyle.button.onClick;
                    onClick.RemoveAllListeners();
                    onClick.AddListener(() =>
                    {
                        if (!ApplyCode(result.code))
                            return;
                        
                        noticeStyle.button.interactable = false;

                        result.code = null;
                        result.style = noticeStyle;
                        __notices[noticeIndex] = result;
                        
                        __MarkHot();
                    });
                }
            }

            if (onRewardInit != null)
                onRewardInit(notice.rewards, noticeStyle.rewardParent);
        }
        else if(noticeStyle.button != null)
            noticeStyle.button.gameObject.SetActive(false);

        result.style = noticeStyle;
        
        if(__notices == null)
            __notices = new List<Notice>();
        
        __notices.Add(result);
    }

    private void __ClearProgressBar()
    {
        __coroutine = null;

        if (__queryNoticeCoroutineIndex != -1)
        {
            var progressbar = GameProgressbar.instance;
            if (progressbar != null)
                progressbar.ClearProgressBar(GameProgressbar.ProgressbarType.Other, __queryNoticeCoroutineIndex);

            __queryNoticeCoroutineIndex = -1;
        }
    }

    private void __MarkHot()
    {
        bool isHot = false;
        foreach (var notice in __notices)
        {
            if (!string.IsNullOrEmpty(notice.code))
            {
                isHot = true;

                break;
            }
        }
        
        if (isHot != ((__flag & Flag.Hot) == Flag.Hot))
        {
            if (isHot)
                __flag |= Flag.Hot;
            else
                __flag &= ~Flag.Hot;
            
            if(onHot != null)
                onHot(isHot);
        }
    }

    private IEnumerator __OnQueryNotices(bool isForceShow, Coroutine coroutine)
    {
        if(coroutine != null)
            yield return coroutine;
        
        float time = Time.unscaledTime;
        uint userID = GameMain.userID;
        if (time - __queryNoticeTime >= _minQueryNoticeTime || __userID != userID)
        {
            __queryNoticeTime = time;
            __userID = userID;

            if (onLoading != null)
                onLoading(true);

            string version = GameConstantManager.Get(CONSTANT_KEY_VERSION_NOTICE);
            yield return IGameData.instance.QueryNotices(
                userID,
                string.IsNullOrEmpty(version) || !uint.TryParse(version, out uint versionValue) ? 0 : versionValue,
                GameLanguage.overrideLanguage,
                __OnQueryNotices);

            if (onLoading != null)
                onLoading(false);
        }
        else
            __ClearProgressBar();

        if ((__flag & Flag.Show) == Flag.Show)
            __newCount = 0;
        else if (isForceShow)
        {
            __newCount = 0;
            
            __flag |= Flag.Show;

            _onShowNotice?.Invoke();
        }

        bool isNew = __newCount > 0;
        if (isNew != ((__flag & Flag.New) == Flag.New))
        {
            if (isNew)
                __flag |= Flag.New;
            else
                __flag &= ~Flag.New;
            
            if(onNew != null)
                onNew(isNew);
        }

        __MarkHot();
    }
}
