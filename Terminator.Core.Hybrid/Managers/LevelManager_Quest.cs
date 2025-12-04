using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    [System.Serializable]
    internal struct QuestStyle
    {
        public float destroyTime;
        public LevelQuestStyle value;
    }

    private readonly struct QuestStatus
    {
        public readonly float DestroyTime;
        
        public readonly LevelQuest Value;

        public readonly LevelQuestStyle Style;

        public QuestStatus(in LevelQuest value, QuestStyle[] styles)
        {
            Value = value;

            var style = styles[(int)value.type];
            DestroyTime = style.destroyTime;
            
            Style = Instantiate(style.value, style.value.transform.parent);
            
            if(value.value > 1)
                Style.onCapacity?.Invoke(value.value.ToString());

            Style.gameObject.SetActive(true);
        }

        public void Dispose()
        {
            Style.onDestroy.Invoke();
            Destroy(Style.gameObject, DestroyTime);
        }

        public void SetCount(int count, int oldCount)
        {
            Style.onCount?.Invoke(count.ToString());
            
            if(Style.progressbar != null)
                Style.progressbar.value = count * 1.0f / Value.value;

            if (count < Value.value != oldCount < Value.value)
            {
                if(count < Value.value)
                    Style.onDisable?.Invoke();
                else
                    Style.onEnable?.Invoke();
            }
        }
        
        public void SetResult(bool value)
        {
            if (value)
                Style.onSuccess?.Invoke();
            else
                Style.onFail?.Invoke();
        }
    }

    [SerializeField] 
    internal float _questInterval = 0.5f;

    [SerializeField] 
    internal float _questDestroyTime = 3.0f;

    [SerializeField] 
    internal UnityEvent _onQuestActive;

    [SerializeField] 
    internal QuestStyle[] _questStyles;
    
    private List<QuestStatus> __questStates;

    private int __stageKillCount;
    private int __stageGold;
    private int __hpPercentage;

    public int hpPercentage
    {
        set
        {
            __SetStageQuestValue(value, __hpPercentage, LevelQuestType.HPPercentage);
            
            __hpPercentage = value;
        }
    }

    public int stageKillCount => __killCount - __stageKillCount;
    
    public int stageGold => __gold - __stageGold;

    public void Set(
        int value, 
        int max, 
        int maxExp, 
        int exp, 
        int killCount, 
        int killBossCount, 
        int gold, 
        int stage, 
        in Unity.Entities.DynamicBuffer<LevelItem> levelItems)
    {
        bool isDirty = false;
        int time = __GetStageTime(out float now);
        if (stage != __stage)
        {
            print($"Stage has been changed to {stage} : {__stage} : {isRestart}");
            isDirty = true;

            //__CreateStageQuests(stage);
            StartCoroutine(__DestroyAndCreateStageQuests(stage));
            
            __stageKillCount = __killCount;
            __stageGold = __gold;

            if (_onStage != null)
                _onStage.Invoke(stage.ToString());
            
            if (!isRestart && stage > __stage)
                __Submit(stage, gold, exp, maxExp, time, levelItems);

            __stageTime = now;

            //__dataFlag = 0;
            
            __stage = stage;
        }

        if (value != __value)
        {
            isDirty = true;
            
            if (__stages != null)
            {
                foreach (var stageIndex in __stages)
                {
                    ref var temp = ref _stages[stageIndex];
                    if (temp.max > 0 && temp.onCount != null)
                        temp.onCount.Invoke(Mathf.Max(0, temp.max - value).ToString());
                }
            }

            if (_progressbar != null)
                _progressbar.value = Mathf.Clamp01(value * 1.0f / max);

            __value = value;
        }
        else if (max != __max)
        {
            isDirty = true;

            if (_progressbar != null)
                _progressbar.value = Mathf.Clamp01(value * 1.0f / max);

            __max = max;
        }

        if (exp != __exp || maxExp != __maxExp)
        {
            isDirty = true;

            if (_expProgressbar != null)
                _expProgressbar.value = Mathf.Clamp01(exp * 1.0f / maxExp); 
            else if(exp != __exp && _onExp != null)
                _onExp.Invoke(exp.ToString());

            __exp = exp;
            __maxExp = maxExp;
        }

        if (killCount != __killCount)
        {
            isDirty = true;

            __SetStageQuestValue(killCount - __stageKillCount, stageKillCount, LevelQuestType.KillCount);

            if (_onKillCount != null)
                _onKillCount.Invoke(killCount.ToString());

            __killCount = killCount;
        }
        
        if (killBossCount != __killBossCount)
        {
            if (killBossCount > 0)
            {
                isDirty = true;

                if (_onKillBossCount != null)
                    _onKillBossCount.Invoke(killBossCount.ToString());

                __killBossCount = killBossCount;
            }
            //else
            //    __ShowTime();
        }

        if (gold != __gold)
        {
            isDirty = true;

            __SetStageQuestValue(gold - __stageGold, stageGold, LevelQuestType.Gold);

            if (_onGoldCount != null)
                _onGoldCount.Invoke(gold.ToString());
            
            __gold = gold;
        }

        if (isRestart)
        {
            //__dataFlag = 0;
            
            if(__skillSelectionGuideNames != null)
                __skillSelectionGuideNames.Clear();
        }

        if (isDirty)
        {
            __SetStageQuestValue(time, __time, LevelQuestType.Time);

            IAnalytics.instance?.Set(value, max, maxExp, exp, killCount, killBossCount, gold, stage);
        }

        __time = time;
    }

    private void __SetStageQuestValue(int value, int oldValue, LevelQuestType type)
    {
        if (__questStates != null)
        {
            foreach (var questStatus in __questStates)
            {
                if (questStatus.Value.type == type)
                    questStatus.SetCount(value, oldValue);
            }
        }
    }

    private IEnumerator __DestroyStageQuests()
    {
        float destroyTime = 0.0f;
        if (__questStates != null)
        {
            foreach (var questStatus in __questStates)
            {
                switch (questStatus.Value.type)
                {
                    case LevelQuestType.Once:
                        if(LevelShared.stage == questStatus.Value.value)
                            questStatus.SetResult(true);
                        break;
                    case LevelQuestType.HPPercentage:
                        questStatus.SetResult(__hpPercentage >= questStatus.Value.value);
                        break;
                    case LevelQuestType.KillCount:
                        questStatus.SetResult(stageKillCount >= questStatus.Value.value);
                        break;
                    case LevelQuestType.Gold:
                        questStatus.SetResult(stageGold >= questStatus.Value.value);
                        break;
                    case LevelQuestType.Time:
                        questStatus.SetResult(__GetStageTime(out _) <= questStatus.Value.value);
                        break;
                }
                
                destroyTime = Mathf.Max(destroyTime, questStatus.DestroyTime);
                questStatus.Dispose();

                if(_questInterval > Mathf.Epsilon)
                    yield return new WaitForSeconds(_questInterval);
            }
            
            __questStates.Clear();
        }
        
        if(_questDestroyTime > Mathf.Epsilon)
            yield return new WaitForSeconds(_questDestroyTime);
    }

    private void __CreateStageQuests(int value)
    {
        ref var stages = ref LevelShared.stages;
        if (stages.Length > value)
        {
            var stage = stages[value];

            if (stage.quests.Length > 0)
            {
                if (__questStates == null)
                    __questStates = new List<QuestStatus>();
                
                foreach (var quest in stage.quests)
                    __questStates.Add(new QuestStatus(quest, _questStyles));
                
                foreach (var questStatus in __questStates)
                {
                    switch (questStatus.Value.type)
                    {
                        case LevelQuestType.Once:
                            if(LevelShared.stage != questStatus.Value.value)
                                questStatus.SetResult(false);
                            break;
                        case LevelQuestType.HPPercentage:
                            questStatus.SetCount(__hpPercentage, 0);
                            break;
                        case LevelQuestType.KillCount:
                            questStatus.SetCount(0, stageKillCount);
                            break;
                        case LevelQuestType.Gold:
                            questStatus.SetCount(0, stageGold);
                            break;
                        /*case LevelManagerShared.QuestType.Time:
                            questStatus.SetCount(__GetStageTime(out _) <= questStatus.Value.count);
                            break;*/
                    }
                }
                
                if(_onQuestActive != null)
                    _onQuestActive.Invoke();
            }
        }
    }

    private IEnumerator __DestroyAndCreateStageQuests(int value)
    {
        yield return __DestroyStageQuests();
        
        __CreateStageQuests(value);
    }

    /*void Awake()
    {
        __CreateStageQuests(0);
    }*/
}
