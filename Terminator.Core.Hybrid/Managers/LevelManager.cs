using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager : MonoBehaviour
{
    [Serializable]
    internal struct Stage
    {
        public string name;

        public int max;

        public StringEvent onCount;
        
        public UnityEvent onEnable;
        public UnityEvent onDisable;
    }

    public static LevelManager instance
    {
        get;

        private set;
    }

    //[SerializeField] 
    //internal int _max = 100;

    //[SerializeField] 
    //internal string _gameTimeFormat = "mm:ss";

    [SerializeField] 
    internal UnityEvent _onQuit;
    [SerializeField] 
    internal StringEvent _onGameTime;
    [SerializeField] 
    internal StringEvent _onKillCount;
    [SerializeField] 
    internal StringEvent _onGoldCount;
    
    [SerializeField] 
    internal ZG.UI.Progressbar _progressbar;
    [SerializeField] 
    internal ZG.UI.Progressbar _expProgressbar;

    [SerializeField] 
    internal Stage[] _stages;

    private int __count;
    private int __gold;
    private int __stage;

    //private int __stageExp;
    //private int __stageExpMax;
    //private string[] __stageActiveSkillNames;

    private ILevelData.Flag __dataFlag;
    
    private float __startTime;
    
    private Coroutine __coroutine;

    private List<GameObject> __gameObjectsToDestroy;

    private HashSet<string> __activeSkillNames;

    private HashSet<int> __stages;

    private Dictionary<string, int> __stageIndices;

    public bool debugLevelUp
    {
        get;

        set;
    }

    public bool isRestart
    {
        get;

        set;
    } = true;

    public void Set(
        int value, 
        int max, 
        int maxExp, 
        int exp, 
        int count, 
        int gold, 
        int stage)
    {
        IAnalytics.instance?.Set(value, max, maxExp, exp, count, gold, stage);
        
        if (__stages != null)
        {
            foreach (var stageIndex in __stages)
            {
                ref var temp = ref _stages[stageIndex];
                if(temp.max > 0 && temp.onCount != null)
                    temp.onCount.Invoke(Mathf.Max(0, temp.max - value).ToString());
            }
        }
        
        if (_progressbar != null)
            _progressbar.value = Mathf.Clamp01(value * 1.0f / max);

        if (_expProgressbar != null)
            _expProgressbar.value = Mathf.Clamp01(exp * 1.0f / maxExp);

        if (count != __count)
        {
            if (_onKillCount != null)
                _onKillCount.Invoke(__count.ToString());

            __count = count;
        }

        if (gold != __gold)
        {
            if (_onGoldCount != null)
                _onGoldCount.Invoke(__gold.ToString());
            
            __gold = gold;
        }

        if (stage != __stage)
        {
            if (!isRestart && stage > __stage)
            {
                int numActiveSkillNames = __activeSkillNames == null ? 0 : __activeSkillNames.Count;
                var activeSkillNames = numActiveSkillNames > 0 ? new string[numActiveSkillNames] : null;
                if(numActiveSkillNames > 0)
                    __activeSkillNames.CopyTo(activeSkillNames, 0);

                var levelData = ILevelData.instance;
                if (levelData != null)
                {
                    if(__coroutine != null)
                        StopCoroutine(__coroutine);

                    __coroutine = StartCoroutine(levelData.SubmitStage(
                        __dataFlag, 
                        stage,
                        gold,
                        exp,
                        maxExp,
                        activeSkillNames,
                        __OnStageChanged));
                }
            }

            __dataFlag = 0;

            __stage = stage;
        }

        if (isRestart)
            __dataFlag = 0;
    }

    public bool EnableStage(string name)
    {
        IAnalytics.instance?.EnableStage(name);
        
        if (__stageIndices == null)
        {
            __stageIndices = new Dictionary<string, int>();
            int numEvents = _stages == null ? 0 : _stages.Length;
            for(int i = 0; i < numEvents; ++i)
                __stageIndices.Add(_stages[i].name, i);
        }

        if (__stageIndices.TryGetValue(name, out int stageIndex))
        {
            if (__stages == null)
                __stages = new HashSet<int>();

            if (__stages.Add(stageIndex))
            {
                var onEnable = _stages[stageIndex].onEnable;
                if (onEnable != null)
                    onEnable.Invoke();

                return true;
            }
        }

        return false;
    }

    public bool DisableStage(string name)
    {
        IAnalytics.instance?.DisableStage(name);

        if (__stageIndices != null && 
            __stageIndices.TryGetValue(name, out int stageIndex) && 
            __stages != null && 
            __stages.Remove(stageIndex))
        {
            var onDisable = _stages[stageIndex].onDisable;
            if(onDisable != null)
                onDisable.Invoke();

            return true;
        }

        return false;
    }

    [UnityEngine.Scripting.Preserve]
    public void TimeScale(float value)
    {
        Time.timeScale = value;
    }

    [UnityEngine.Scripting.Preserve]
    public void Pause()
    {
        IAnalytics.instance?.Pause();
        
        if (_onGameTime != null)
        {
            var timeSpan = new TimeSpan((long)((Time.time - __startTime) * TimeSpan.TicksPerSecond));
            _onGameTime.Invoke($"{timeSpan.Minutes} : {timeSpan.Seconds}");
        }
    }
    
    [UnityEngine.Scripting.Preserve]
    public void Restart()
    {
        IAnalytics.instance?.Restart();
        
        isRestart = true;

        __startTime = Time.time;
    }

    [UnityEngine.Scripting.Preserve]
    public void Quit()
    {
        //IAnalytics.instance?.Quit();
        
        __Submit(__OnQuit);
    }

    private void __Submit(Action<bool> onComplete)
    {
        if (__coroutine != null)
        {
            StopCoroutine(__coroutine);

            __coroutine = null;
        }

        var levelData = ILevelData.instance;
        if (levelData == null)
            onComplete(false);
        else
        {
            int numActiveSkillNames = __activeSkillNames == null ? 0 : __activeSkillNames.Count;
            string[] activeSkillNames = numActiveSkillNames > 0 ? new string[numActiveSkillNames] : null;
            if(numActiveSkillNames > 0)
                __activeSkillNames.CopyTo(activeSkillNames, 0);
            
            if(__coroutine != null)
                StopCoroutine(__coroutine);

            __coroutine = StartCoroutine(levelData.SubmitLevel(
                __dataFlag, 
                __stage,
                __gold,
                onComplete));
        }
    }

    private void __OnStageChanged(bool result)
    {
        __coroutine = null;
    }
    
    private void __OnQuit(bool result)
    {
        Time.timeScale = 1.0f;
        
        if (_onQuit != null)
            _onQuit.Invoke();
        
        IAnalytics.instance?.Quit();
    }

    void Start()
    {
        __startTime = Time.time;
        
        instance = this;
    }
}
