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

    public static LevelManager instance;

    //[SerializeField] 
    //internal int _max = 100;

    //[SerializeField] 
    //internal string _gameTimeFormat = "mm:ss";

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
        int gold, 
        int count)
    {
        if (__stages != null)
        {
            foreach (var stageIndex in __stages)
            {
                ref var stage = ref _stages[stageIndex];
                if(stage.max > 0 && stage.onCount != null)
                    stage.onCount.Invoke(Mathf.Max(0, stage.max - value).ToString());
            }
        }
        
        if (_progressbar != null)
            _progressbar.value = Mathf.Clamp01(value * 1.0f / max);

        if (_expProgressbar != null)
            _expProgressbar.value = Mathf.Clamp01(exp * 1.0f / maxExp);

        __gold = gold;
        __count = count;
    }

    public bool EnableStage(string name)
    {
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

        if (_onKillCount != null)
            _onKillCount.Invoke(__count.ToString());
        
        if (_onGoldCount != null)
            _onGoldCount.Invoke(__gold.ToString());
    }
    
    [UnityEngine.Scripting.Preserve]
    public void Restart()
    {
        isRestart = true;

        __startTime = Time.time;
    }
    
    protected void OnEnable()
    {
        __startTime = Time.time;
        
        if (instance == null)
            instance = this;
    }

    protected void OnDisable()
    {
        if (instance == this)
            instance = null;
    }

}
