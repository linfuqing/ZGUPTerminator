using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager : MonoBehaviour
{
    [Serializable]
    internal struct Stage
    {
        public string name;
        public string[] sharedNames;

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
    //internal string _gameTimeFormat = "mm:ss"
    //
    [SerializeField] 
    internal float _quitTime = 0.5f;

    [SerializeField] 
    internal UnityEvent _onQuit;
    
    [SerializeField]
    internal UnityEvent _onNextStageEnable;

    [SerializeField]
    internal UnityEvent _onNextStageDisable;

    [SerializeField]
    internal StringEvent _onNextStageEnergy;
    
    [SerializeField]
    internal StringEvent _onEnergyMax;

    [SerializeField] 
    internal StringEvent _onGameTime;
    [SerializeField] 
    internal StringEvent _onKillCount;
    [SerializeField] 
    internal StringEvent _onKillBossCount;
    [SerializeField] 
    internal StringEvent _onGoldCount;
    
    [SerializeField]
    internal StringEvent _onExp;

    [SerializeField]
    internal StringEvent _onStage;

    [SerializeField] 
    internal ZG.UI.Progressbar _progressbar;
    [SerializeField] 
    internal ZG.UI.Progressbar _expProgressbar;

    [SerializeField] 
    internal GameObject[] _ranks;

    [SerializeField] 
    internal Stage[] _stages;

    private int __time;
    private int __max;
    private int __value;
    private int __exp;
    private int __maxExp;
    private int __killCount;
    private int __killBossCount;
    private int __gold;
    private int __stage = -1;

    //private int __stageExp;
    //private int __stageExpMax;
    //private string[] __stageActiveSkillNames;

    //private ILevelData.Flag __dataFlag;
    
    private float __startTime;
    
    private float __stageTime;

    private Coroutine __coroutine;

    private Queue<IEnumerator> __coroutineEnumerators;

    private List<int> __timeScaleIndices;

    private List<GameObject> __gameObjectsToDestroy;

    private Dictionary<(int, int), FixedString128Bytes> __skillActiveNames;
    
    private HashSet<string> __skillSelectionGuideNames;

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

    /*public int dataFlag
    {
        get => (int)__dataFlag;
        
        set => __dataFlag = (ILevelData.Flag)value;
    }*/
    
    public int rage
    {
        get;

        set;
    }

    public bool EnableStage(string name)
    {
        IAnalytics.instance?.EnableStage(name);
        
        if (__stageIndices == null)
        {
            __stageIndices = new Dictionary<string, int>();
            int numEvents = _stages == null ? 0 : _stages.Length;
            for (int i = 0; i < numEvents; ++i)
            {
                ref var stage = ref _stages[i];
                if(stage.sharedNames == null || stage.sharedNames.Length < 1)
                    __stageIndices.Add(stage.name, i);
                else
                {
                    foreach (string sharedName in stage.sharedNames)
                        __stageIndices.Add(sharedName, i);
                }
            }
        }

        if (__stageIndices.TryGetValue(name, out int stageIndex))
        {
            if (__stages == null)
                __stages = new HashSet<int>();

            if (__stages.Add(stageIndex))
            {
                ref var stage = ref _stages[stageIndex];
                if (stage.onEnable != null)
                    stage.onEnable.Invoke();
                
                if (stage.max > 0 && stage.onCount != null)
                    stage.onCount.Invoke(Mathf.Max(0, stage.max - __value).ToString());

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
    public void ClearTimeScales()
    {
        if (__coroutineEnumerators != null && __coroutineEnumerators.Count > 0)
            return;

        __ClearTimeScales();
    }

    [UnityEngine.Scripting.Preserve]
    public void TimeScale(float value)
    {
        if (__timeScaleIndices == null)
            __timeScaleIndices = new List<int>();
        
        __timeScaleIndices.Add(TimeScaleUtility.Add(value));
    }

    private int __pauseTimeScaleIndex = -1;

    [UnityEngine.Scripting.Preserve]
    public void Pause()
    {
        IAnalytics.instance?.Pause();

        //TimeScale(0.0f);
        __pauseTimeScaleIndex = TimeScaleUtility.Add(0.0f);

        __ShowTime();
        
        Debug.Log("Pause");
    }

    [UnityEngine.Scripting.Preserve]
    public void Resume()
    {
        TimeScaleUtility.Remove(__pauseTimeScaleIndex);

        __pauseTimeScaleIndex = -1;
    }
    
    [UnityEngine.Scripting.Preserve]
    public void Restart()
    {
        IAnalytics.instance?.Restart();
        
        isRestart = true;

        __startTime = Time.time;

        __stageTime = __startTime;
    }

    [UnityEngine.Scripting.Preserve]
    public void Quit()
    {
        var levelData = ILevelData.instance;
        if (levelData == null)
            __OnQuit(false);
        else
        {
            /*int numActiveSkillNames = __activeSkillNames == null ? 0 : __activeSkillNames.Count;
            string[] activeSkillNames = numActiveSkillNames > 0 ? new string[numActiveSkillNames] : null;
            if(numActiveSkillNames > 0)
                __activeSkillNames.Values.CopyTo(activeSkillNames, 0);*/
            
            __StartCoroutine(levelData.SubmitLevel(
                //__dataFlag, 
                __stage,
                Mathf.RoundToInt(__GetStageTime(out _)), 
                __hpPercentage, 
                __killCount,
                __killBossCount, 
                __gold,
                __OnQuit));
        }
    }

    private IEnumerator __Coroutine()
    {
        while(__coroutineEnumerators.TryDequeue(out var coroutineEnumerator))
            yield return coroutineEnumerator;

        __coroutine = null;
    }
    
    private void __ClearTimeScales()
    {
        if (__timeScaleIndices != null)
        {
            foreach (var timeScaleIndex in __timeScaleIndices)
                TimeScaleUtility.Remove(timeScaleIndex);
            
            __timeScaleIndices.Clear();
        }
    }
    
    private void __StartCoroutine(IEnumerator enumerator)
    {
        if (__coroutineEnumerators == null)
            __coroutineEnumerators = new Queue<IEnumerator>();
        
        __coroutineEnumerators.Enqueue(enumerator);
        
        if(__coroutine == null)
            __coroutine = StartCoroutine(__Coroutine());
    }

    private void __OnStageChanged(ILevelData.StageResult result)
    {
        __ShowTime();
        
        int numRanks = _ranks == null ? 0 : _ranks.Length;
        for (int i = 0; i < numRanks; ++i)
            _ranks[i].SetActive((result.rankFlag & (1 << i)) != 0);

        if (result.energyStage > 0)
        {
            if(_onEnergyMax != null)
                _onEnergyMax.Invoke(result.energyMax.ToString());

            if(_onNextStageEnergy != null)
                _onNextStageEnergy.Invoke(result.energyStage.ToString());

            if (result.energyStage > result.energyMax)
            {
                if (_onNextStageDisable != null)
                    _onNextStageDisable.Invoke();
            }
            else if(_onNextStageEnable != null)
                _onNextStageEnable.Invoke();
        }
        
        //__coroutine = null;
    }
    
    private void __OnQuit(bool result)
    {
        StartCoroutine(__Quit(_quitTime));
    }

    private IEnumerator __Quit(float time)
    {
        yield return new WaitForSecondsRealtime(time);
        
        __ClearTimeScales();

        if (-1 != __pauseTimeScaleIndex)
        {
            TimeScaleUtility.Remove(__pauseTimeScaleIndex);
            
            __pauseTimeScaleIndex = -1;
        }
        
        if (_onQuit != null)
            _onQuit.Invoke();
        
        IAnalytics.instance?.Quit();
    }

    private void __ShowTime()
    {
        if (_onGameTime != null)
        {
            var timeSpan = new TimeSpan((long)((Time.time - __startTime) * TimeSpan.TicksPerSecond));
            _onGameTime.Invoke($"{timeSpan.Minutes} : {timeSpan.Seconds}");
        }
    }

    private int __GetStageTime(out float now)
    {
        now = Time.time;
        return Mathf.RoundToInt(now - __stageTime);
    }

    private void __Submit(int stage, int gold, int exp, int maxExp, int time)
    {
        var levelData = ILevelData.instance;
        if (levelData == null)
            return;
        
        int numSkillActiveNames = __skillActiveNames == null ? 0 : __skillActiveNames.Count;
        if (numSkillActiveNames < 1)
            return;
        
        var skillActiveNames = new string[numSkillActiveNames];
        numSkillActiveNames = 0;
        foreach (var skillActiveName in __skillActiveNames.Values)
            skillActiveNames[numSkillActiveNames++] = skillActiveName.ToString();

        __StartCoroutine(levelData.SubmitStage(
            //__dataFlag, 
            stage,
            time, 
            __hpPercentage, 
            __killCount, 
            __killBossCount, 
            gold,
            rage, 
            exp,
            maxExp,
            skillActiveNames,
            __OnStageChanged));
    }

    void Start()
    {
        __startTime = Time.time;
        
        __stageTime = __startTime;
        
        instance = this;
    }

    void OnApplicationFocus(bool focus)
    {
        if(!focus)
            __Submit(__stage, __gold, __exp, __maxExp, __GetStageTime(out _));
    }
}
