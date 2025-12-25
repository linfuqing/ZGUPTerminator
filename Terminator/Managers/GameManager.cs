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
        public IGameData.Notice data;
        public NoticeStyle source;
        public NoticeStyle destination;
    }
    
    public static Action<bool> onNoticeNew;

    public static Action<bool> onNoticeHot;

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
    [SerializeField]
    [Tooltip("一键领取失败")]
    internal UnityEvent _onNoticeCodesFail;

    private Flag __flag;
    private uint __userID;
    private int __newCount;
    private int __loadingCount;
    private int __queryNoticeCoroutineIndex = -1;
    private float __queryNoticeTime;
    private Coroutine __noticeCoroutine;
    private List<Notice> __notices;
    
    private const string CONSTANT_KEY_VERSION_NOTICE = "NoticeVersion";
    private const string CONSTANT_KEY_VERSION_CODE = "CodeVersion";
    
    private const string NAME_SPACE_TIMES = "GameManagerNoticeTimes";

    public bool isNoticeShow => (__flag & Flag.Show) == Flag.Show;

    public bool isNoticeNew => (__flag & Flag.New) == Flag.New;
    
    public bool isNoticeHot => (__flag & Flag.Hot) == Flag.Hot;

    public bool isLoading => __loadingCount != 0;

    public static GameManager instance
    {
        get;

        private set;
    }

    [UnityEngine.Scripting.Preserve]
    public void CloseNotices()
    {
        __flag &= ~Flag.Show;
    }

    public void QueryNotices(bool isForceShow)
    {
        //__flag &= ~Flag.Show;
        
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
        
        __noticeCoroutine = StartCoroutine(__OnQueryNotices(isForceShow, __noticeCoroutine));
        
        if(progressbar != null)
            progressbar.EndCoroutine(__queryNoticeCoroutineIndex, __noticeCoroutine);
    }

    [UnityEngine.Scripting.Preserve]
    public void ApplyNoticeCodes()
    {
        List<string> codes = null;
        Notice notice;
        int numNotices = __notices == null ? 0 : __notices.Count;
        for (int i = 0; i < numNotices; ++i)
        {
            notice = __notices[i];
            if((notice.data.flag & IGameData.Notice.Flag.Used) == IGameData.Notice.Flag.Used || 
               string.IsNullOrEmpty(notice.data.code))
                continue;
            
            if(codes == null)
                codes = new List<string>();
            
            codes.Add(notice.data.code);
            
            notice.data.flag |= IGameData.Notice.Flag.Used;
            if(notice.destination.button != null)
                notice.destination.button.interactable = false;

            __notices[i] = notice;
        }

        if(codes != null)
            ApplyCode(codes.ToArray());
        
        if (((__flag & Flag.Hot) == Flag.Hot))
        {
            __flag &= ~Flag.Hot;
            
            if(onNoticeHot != null)
                onNoticeHot(false);
        }
    }
    
    public void ApplyCode(params string[] codes)
    {
        var gameData = IGameData.instance;
        if (gameData == null)
            return;
        
        __RetainLoading();
        
        string version = GameConstantManager.Get(CONSTANT_KEY_VERSION_CODE);
        StartCoroutine(gameData.ApplyCode(
            GameMain.userID,
            string.IsNullOrEmpty(version) || !uint.TryParse(version, out uint versionValue) ? 0 : versionValue,
            codes,
            __OnApplyCode));
    }
    
    private void __OnApplyCode(Memory<UserReward> rewards)
    {
        __ReleaseLoading();

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
                Destroy(notice.destination.gameObject);
            
            __notices.Clear();
        }

        NoticeStyle noticeStyle;
        if ((notices.flag & IGameData.Notices.Flag.NotBeClosed) == IGameData.Notices.Flag.NotBeClosed)
        {
            var parent = _noticeStyleCanNotBeClosed.transform.parent;
            foreach (var notice in notices.notices)
            {
                noticeStyle = Instantiate(_noticeStyleCanNotBeClosed, parent);
                __Init(_noticeStyleCanNotBeClosed, noticeStyle, notice);
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
                
                __Init(noticeStylePrefab, noticeStyle, notice);
            }

            if (isImportantNew)
            {
                _onShowNotice?.Invoke();
            
                __flag |= Flag.Show;
            }
        }
    }

    private void __Init(NoticeStyle source, NoticeStyle destination, IGameData.Notice data, int noticeIndex = -1)
    {
        string key = $"{NAME_SPACE_TIMES}{data.id}";
        int times = PlayerPrefs.GetInt(key);
        PlayerPrefs.SetInt(key, times + 1);

        bool isNew = times == 0;
        if(isNew)
            ++__newCount;
        
        destination.onNew?.Invoke(isNew);
        destination.onTitle?.Invoke(data.name);
        destination.onDetail?.Invoke(data.text);
        destination.onDealLine?.Invoke(data.ticks == 0
            ? string.Empty
            : new DateTime(data.ticks).ToLocalTime().ToString(_dealLineFormat));

        if (noticeIndex == -1)
            noticeIndex = __notices == null ? 0 : __notices.Count;
        
        if (data.rewards != null && data.rewards.Length > 0)
        {
            if (destination.button != null)
            {
                if (string.IsNullOrEmpty(data.code))
                    destination.button.gameObject.SetActive(false);
                else if((data.flag & IGameData.Notice.Flag.Used) == IGameData.Notice.Flag.Used)
                    destination.button.interactable = false;
                else
                {
                    var onClick = destination.button.onClick;
                    onClick.RemoveAllListeners();
                    onClick.AddListener(() =>
                    {
                        var notice = __notices[noticeIndex];
                        if ((notice.data.flag & IGameData.Notice.Flag.Used) == IGameData.Notice.Flag.Used)
                            return;
                        
                        notice.data.flag |= IGameData.Notice.Flag.Used;
                        __notices[noticeIndex] = notice;
                        
                        ApplyCode(data.code);
                        
                        destination.button.interactable = false;

                        __MarkHot();
                    });
                }
            }

            if (onRewardInit != null)
                onRewardInit(data.rewards, destination.rewardParent);
        }
        else if(destination.button != null)
            destination.button.gameObject.SetActive(false);
        
        destination.gameObject.SetActive(true);

        Notice result;
        result.data = data;
        result.source = source;
        result.destination = destination;
        
        if(__notices == null)
            __notices = new List<Notice>();
        
        if(__notices.Count > noticeIndex)
            __notices[noticeIndex] = result;
        else
            __notices.Add(result);
    }

    private void __ClearProgressBar()
    {
        __noticeCoroutine = null;

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
        if (__notices != null)
        {
            foreach (var notice in __notices)
            {
                if((notice.data.flag & IGameData.Notice.Flag.Used) == IGameData.Notice.Flag.Used || 
                   string.IsNullOrEmpty(notice.data.code))
                    continue;

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
            
            if(onNoticeHot != null)
                onNoticeHot(isHot);
        }
    }

    private void __RetainLoading()
    {
        if (__loadingCount++ < 1)
        {
            if (onLoading != null)
                onLoading(true);
        }
    }

    private void __ReleaseLoading()
    {
        if (--__loadingCount < 1)
        {
            if (onLoading != null)
                onLoading(false);
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

            __RetainLoading();

            string version = GameConstantManager.Get(CONSTANT_KEY_VERSION_NOTICE);
            var gameData = IGameData.instance;
            if (gameData == null)
                __OnQueryNotices(default);
            else
                yield return gameData.QueryNotices(
                    userID,
                    string.IsNullOrEmpty(version) || !uint.TryParse(version, out uint versionValue) ? 0 : versionValue,
                    GameLanguage.overrideLanguage,
                    __OnQueryNotices);

            __ReleaseLoading();
        }
        else
        {
            Notice notice;
            int numNotices = __notices == null ? 0 : __notices.Count;
            for(int i = 0; i < numNotices; ++i)
            {
                notice = __notices[i];
                Destroy(notice.destination.gameObject);
                notice.destination = Instantiate(notice.source);
                
                __Init(notice.source, notice.destination, notice.data, i);
            }

            __ClearProgressBar();
            
            
        }

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
            
            if(onNoticeNew != null)
                onNoticeNew(isNew);
        }

        __MarkHot();
    }

    private void Awake()
    {
        instance = this;
    }
}
