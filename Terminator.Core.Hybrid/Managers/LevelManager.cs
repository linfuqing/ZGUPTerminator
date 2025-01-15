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

    private float __startTime;

    private List<GameObject> __gameObjectsToDestroy;

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
    }

    public void Set(
        int value, 
        int max, 
        int maxExp, 
        int exp, 
        int count, 
        int gold, 
        int stage)
    {
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

        __stage = stage;
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
        if (_onGameTime != null)
        {
            var timeSpan = new TimeSpan((long)((Time.time - __startTime) * TimeSpan.TicksPerSecond));
            _onGameTime.Invoke($"{timeSpan.Minutes} : {timeSpan.Seconds}");
        }
    }
    
    [UnityEngine.Scripting.Preserve]
    public void Restart()
    {
        isRestart = true;

        __startTime = Time.time;
    }

    [UnityEngine.Scripting.Preserve]
    public void Quit()
    {
        StartCoroutine(ILevelData.instance.SubmitLevel(__stage, __gold, __OnQuit));
    }

    private void __OnQuit(bool result)
    {
        Time.timeScale = 1.0f;
        
        if (_onQuit != null)
            _onQuit.Invoke();
        
        IAnalytics.instance?.Quit();
    }
    
    protected void Start()
    {
        __startTime = Time.time;
        
        instance = this;
    }
}
