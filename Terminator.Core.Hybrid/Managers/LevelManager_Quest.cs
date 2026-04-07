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

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_onQuestActive")] 
    internal UnityEvent _onQuestEnable;
    
    [SerializeField]
    internal UnityEvent _onQuestDisable;

    [SerializeField] 
    internal QuestStyle[] _questStyles;
    
    private List<QuestStatus> __questStates;

    public int damagePercentage
    {
        set
        {
            __SetStageQuestValue(value, __damagePercentage, LevelQuestType.DamagePercentage);
            
            __damagePercentage = value;
        }
    }
    
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

    private IEnumerator __DestroyStageQuests(int stageKillCount, int stageGold, int stageTime)
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
                    case LevelQuestType.DamagePercentage:
                        questStatus.SetResult(__damagePercentage >= questStatus.Value.value);
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
                        questStatus.SetResult(stageTime <= questStatus.Value.value);
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
        bool isActive = false;
        ref var stages = ref LevelShared.stages;
        if (stages.Length > value)
        {
            var stage = stages[value];

            if (stage.quests.Length > 0)
            {
                if (__questStates == null)
                    __questStates = new List<QuestStatus>();

                foreach (var quest in stage.quests)
                {
                    if(quest.type != LevelQuestType.Once && quest.value == 0)
                        continue;
                    
                    __questStates.Add(new QuestStatus(quest, _questStyles));
                }

                if (__questStates.Count > 0)
                {
                    foreach (var questStatus in __questStates)
                    {
                        switch (questStatus.Value.type)
                        {
                            case LevelQuestType.Once:
                                if (LevelShared.stage != questStatus.Value.value)
                                    questStatus.SetResult(false);
                                break;
                            case LevelQuestType.DamagePercentage:
                                questStatus.SetCount(__damagePercentage, 0);
                                break;
                            case LevelQuestType.HPPercentage:
                                questStatus.SetCount(__hpPercentage, 100);
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

                    _onQuestEnable?.Invoke();

                    isActive = true;
                }
            }
        }

        if (!isActive)
            _onQuestDisable?.Invoke();
    }

    private IEnumerator __DestroyAndCreateStageQuests(int value, int stageKillCount, int stageGold, int stageTime)
    {
        yield return __DestroyStageQuests(stageKillCount, stageGold, stageTime);
        
        __CreateStageQuests(value);
    }

    /*void Awake()
    {
        __CreateStageQuests(0);
    }*/
}
