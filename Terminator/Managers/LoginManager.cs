using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using ZG.UI;

public sealed class LoginManager : MonoBehaviour
{
    public class RewardData : IRewardData
    {
        private uint __userID;

        public RewardData(uint userID)
        {
            __userID = userID;
        }

        public IEnumerator ApplyReward(
            string poolName, 
            Action<IRewardData.Rewards> onComplete)
        {
            return IUserData.instance.ApplyReward(
                __userID,
                poolName,
                x =>
                {
                    if (x.IsEmpty)
                    {
                        onComplete(default);
                        
                        return;
                    }
                    
                    IRewardData.Rewards result;
                    result.poolName = poolName;

                    int numValues = x.Length;
                    result.values = new IRewardData.Reward[numValues];
                    for (int i = 0; i < numValues; ++i)
                    {
                        ref var source = ref x.Span[i];
                        ref var destination = ref result.values[i];
                        
                        destination.name = source.name;
                        destination.count = source.count;
                    }

                    onComplete(result);
                });
        }
    }

    [Serializable]
    internal struct Level
    {
        public string name;
        public string title;
        public Sprite sprite;
    }

    [Serializable]
    internal struct Reward
    {
        public string name;
        public Sprite sprite;
    }

    public static event Action<Memory<UserRewardData>> onAwake;
    
    public static event Action<uint> onStageChanged;
    
    public event Action<int> onEnergyChanged;

    [SerializeField]
    internal float _stageStyleDestroyTime;
    
    [SerializeField]
    internal float _rewardStyleDestroyTime;

    [SerializeField] 
    internal string _energyTimeFormat = @"mm\'ss\'\'";

    [SerializeField]
    internal UnityEvent _onStart;

    [SerializeField]
    internal ActiveEvent _onHot;

    [SerializeField]
    internal StringEvent _onGold;

    [SerializeField]
    internal StringEvent _onEnergyMax;
    
    [SerializeField]
    internal StringEvent _onEnergy;

    [SerializeField]
    internal StringEvent _onEnergyTime;

    [SerializeField] 
    internal Progressbar _energy;

    [SerializeField]
    internal LevelStyle _style;

    [SerializeField] 
    internal Level[] _levels;

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_skills")] 
    internal Reward[] _rewards;

    private List<StageRewardStyle> __rewardStyles;
    private List<StageStyle> __stageStyles;
    private Dictionary<int, LevelStyle> __styles;
    private Dictionary<string, int> __rewardIndices;

    private float __energyNextTime;
    private float __energyUnitTime;

    private int __gold;

    private int __energy;

    private int __energyMax;

    private int __selectedLevelEnergy;
    private int __selectedLevelIndex;
    private uint __selectedUserLevelID;
    private uint __selectedUserStageID;

    private bool __isStart;

    public static uint? userID
    {
        get;

        private set;
    }

    public static LoginManager instance
    {
        get;

        private set;
    }

    public int gold
    {
        get => __gold;

        set
        {
            __gold = value;
            
            if(_onGold != null)
                _onGold.Invoke(value.ToString());
        }
    }

    public int energy
    {
        get => __energy;

        set
        {
            __energy = value;

            if (_energy != null && __energyMax > Mathf.Epsilon)
                _energy.value = value * 1.0f / __energyMax;
            
            if(_onEnergy != null)
                _onEnergy.Invoke(value.ToString());

            if(onEnergyChanged != null)
                onEnergyChanged(value);
        }
    }

    public int energyMax
    {
        get => __energyMax;

        set
        {
            __energyMax = value;
            
            if(_onEnergyMax != null)
                _onEnergyMax.Invoke(value.ToString());
        }
    }

    /// <summary>
    /// 总攻击倍率
    /// </summary>
    [Obsolete]
    public float bulletDamageScale
    {
        get;

        set;
    }
    
    /// <summary>
    /// 总防御倍率
    /// </summary>
    [Obsolete]
    public float effectTargetDamageScale
    {
        get;

        set;
    }
    
    /// <summary>
    /// 总HP倍率
    /// </summary>
    [Obsolete]
    public float effectTargetHPScale
    {
        get;

        set;
    }

    /// <summary>
    /// 选择的超能武器的名字
    /// </summary>
    [Obsolete]
    public string[] activeSkillNames
    {
        get;

        set;
    }

    [Preserve]
    public void LoadScene()
    {
        var assetManager = GameAssetManager.instance;
        if (assetManager == null)
        {
            var gameObject = new GameObject("GameAssetManager");
            DontDestroyOnLoad(gameObject);
            assetManager = gameObject.AddComponent<GameAssetManager>();
        }

        assetManager.LoadScene(_levels[__selectedLevelIndex].name, null, new GameSceneActivation());
    }

    public void ApplyStart(bool isRestart)
    {
        StartCoroutine(__Start(isRestart));
    }
    
    private void __ApplyStart()
    {
        ApplyStart(true);
    }

#if USER_DATA_LEGACY
    private void __ApplySkills(Memory<UserSkill> skills)
    {
        ref var skillGroups = ref LevelPlayerShared.skillGroups;
        skillGroups.Clear();

        LevelPlayerSkillGroup skillGroup;
        foreach (var skill in skills.Span)
        {
            skillGroup.name = skill.name;
            skillGroups.Add(skillGroup);
        }

        ref var activeSkills = ref LevelPlayerShared.activeSkills;
        activeSkills.Clear();
        if (activeSkillNames != null && activeSkillNames.Length > 0)
        {
            LevelPlayerActiveSkill activeSkill;

            foreach (var activeSkillName in activeSkillNames)
            {
                activeSkill.name = activeSkillName;
                activeSkills.Add(activeSkill);
            }
        }

        LevelPlayerShared.effectDamageScale = bulletDamageScale;
        LevelPlayerShared.effectTargetDamageScale = effectTargetDamageScale;
        LevelPlayerShared.effectTargetHPScale = effectTargetHPScale;
    }
#endif
    
    private void __ApplyLevels(Memory<UserLevel> userLevels)
    {
        int numLevels = _levels.Length;
        var levelIndices = new Dictionary<string, int>(numLevels);
        for (int i = 0; i < numLevels; ++i)
            levelIndices[_levels[i].name] = i;

        bool isHot = false;
        Level level;
        Transform parent = _style.transform.parent;
        __styles = new Dictionary<int, LevelStyle>(userLevels.Length);
        foreach (var userLevel in userLevels.Span)
        {
            if(!levelIndices.TryGetValue(userLevel.name, out int index))
                continue;
            
            if (!isHot && userLevel.stages != null)
            {
                foreach (var stage in userLevel.stages)
                {
                    if (stage.rewardFlags == null)
                        break;

                    foreach (var rewardFlag in stage.rewardFlags)
                    {
                        if ((rewardFlag & UserStageReward.Flag.Unlock) == UserStageReward.Flag.Unlock &&
                            (rewardFlag & UserStageReward.Flag.Collected) != UserStageReward.Flag.Collected)
                        {
                            isHot = true;

                            break;
                        }
                    }
                }
            }

            var style = Instantiate(_style, parent);
            
            var selectedLevel = userLevel;
            
            if(style.onEnergy != null)
                style.onEnergy.Invoke(selectedLevel.energy.ToString());

            level = _levels[index];
            
            if(style.onTitle != null)
                style.onTitle.Invoke(level.title);

            if(style.onImage != null)
                style.onImage.Invoke(level.sprite);

            style.toggle.onValueChanged.AddListener(x =>
            {
                __DestroyRewards();

                if (__stageStyles != null)
                {
                    foreach (var stageStyle in __stageStyles)
                    {
                        if (stageStyle.onDestroy != null)
                            stageStyle.onDestroy.Invoke();
                            
                        Destroy(stageStyle.gameObject, _stageStyleDestroyTime);
                    }
                        
                    __stageStyles.Clear();
                }
                
                if (x)
                {
                    __selectedLevelEnergy = selectedLevel.energy;
                    
                    if (style.button != null)
                        style.button.interactable = __selectedLevelEnergy <= energy && !__isStart;
                    
                    __selectedLevelIndex = index;
                    __selectedUserLevelID = selectedLevel.id;

                    int numStages = selectedLevel.stages == null ? 0 : selectedLevel.stages.Length;
                    if (numStages > 0)
                    {
                        if (style.stageStyle == null)
                        {
                            foreach (var stage in selectedLevel.stages)
                                __CreateRewards(style.rewardStyle, stage.rewards);
                        }
                        else
                        {
                            if (__stageStyles == null)
                                __stageStyles = new List<StageStyle>();

                            bool isHot;
                            int i,
                                j,
                                numRanks,
                                numRewardFlags,
                                stageStyleStartIndex = __stageStyles.Count,
                                selectedStageIndex = 0;
                            UserStageReward.Flag rewardFlag;
                            StageStyle stageStyle;
                            GameObject rank;
                            for (i = 0; i < numStages; ++i)
                            {
                                var stage = selectedLevel.stages[i];

                                stageStyle = style.stageStyle;
                                stageStyle = Instantiate(stageStyle, stageStyle.transform.parent);

                                if (stageStyle.onTitle != null)
                                    stageStyle.onTitle.Invoke(i.ToString());

                                if (stage.rewardFlags == null)
                                {
                                    if (stageStyle.onHot != null)
                                        stageStyle.onHot.Invoke(false);

                                    if (stageStyle.toggle != null)
                                    {
                                        stageStyle.toggle.interactable = false;

                                        stageStyle.toggle.isOn = false;
                                    }
                                }
                                else
                                {
                                    isHot = false;
                                    numRanks = stageStyle.ranks == null ? 0 : stageStyle.ranks.Length;
                                    numRewardFlags = stage.rewardFlags.Length;
                                    for (j = 0; j < numRewardFlags; ++j)
                                    {
                                        rewardFlag = stage.rewardFlags[j];
                                        if ((rewardFlag & UserStageReward.Flag.Unlock) == UserStageReward.Flag.Unlock)
                                        {
                                            rank = numRanks > j ? stageStyle.ranks[j] : null;
                                            if (rank != null)
                                                rank.SetActive(true);

                                            if ((rewardFlag & UserStageReward.Flag.Collected) !=
                                                UserStageReward.Flag.Collected)
                                                isHot = true;
                                        }
                                    }

                                    if (stageStyle.onHot != null)
                                        stageStyle.onHot.Invoke(isHot);

                                    if (stageStyle.toggle != null)
                                    {
                                        int stageIndex = i;

                                        stageStyle.toggle.isOn = false;
                                        stageStyle.toggle.interactable = true;
                                        stageStyle.toggle.onValueChanged.AddListener(x =>
                                        {
                                            __DestroyRewards();
                                            
                                            if (x)
                                            {
                                                __selectedUserStageID = stage.id;

                                                LevelShared.stage = stageIndex;

                                                __CreateRewards(style.rewardStyle, stage.rewards);

                                                if (onStageChanged != null)
                                                    onStageChanged.Invoke(stage.id);
                                            }
                                        });
                                    }

                                    selectedStageIndex = __stageStyles.Count;
                                }
                                //stageStyle.gameObject.SetActive(true);

                                __stageStyles.Add(stageStyle);
                            }

                            __stageStyles[selectedStageIndex].toggle.isOn = true;

                            int numStageStyles = __stageStyles.Count;
                            for (i = stageStyleStartIndex; i < numStageStyles; ++i)
                                __stageStyles[i].gameObject.SetActive(true);
                        }
                    }
                }
            });

            if (style.button != null)
            {
                style.button.onClick.RemoveAllListeners();
                style.button.onClick.AddListener(__ApplyStart);
            }

            style.gameObject.SetActive(true);
            
            __styles[index] = style;
        }

        var scrollRect = parent.GetComponentInParent<ZG.ScrollRectComponentEx>();
        if(scrollRect != null)
            scrollRect.MoveTo(userLevels.Length - 1);
        
        if(_onHot != null)
            _onHot.Invoke(isHot);
    }

    private void __ApplyLevel(Memory<UserReward> rewards)
    {
        int numRewards = rewards.Length;
        UserRewardData result;
        var results = new UserRewardData[numRewards];
        for(int i = 0; i < numRewards; ++i)
        {
            ref var reward = ref rewards.Span[i];

            switch (reward.type)
            {
                case UserRewardType.Gold:
                    gold += reward.count;
                    break;
                case UserRewardType.Energy:
                    energy += reward.count;
                    break;
                case UserRewardType.EnergyMax:
                    energyMax += reward.count;
                    break;
            }
            
            result.name = reward.name;
            result.count = reward.count;
            result.type = reward.type;
            
            results[i] = result;
        }

        if(onAwake != null)
            onAwake(results);
    }

    private void __ApplyLevel(IUserData.Property property)
    {
        LevelShared.exp = 0;
        LevelShared.expMax = 0;

        __SubmitStage(property);
    }
    
    private void __ApplyStage(IUserData.StageProperty property)
    {
        if (property.cache.skills == null)
        {
            __isStart = false;
            
            return;
        }
        
        LevelShared.exp = property.cache.exp;
        LevelShared.expMax = property.cache.expMax;
        
        __SubmitStage(property.value);
    }

    private void __SubmitStage(IUserData.Property property)
    {
        if (property.skills == null && property.attributes == null)
        {
            __isStart = false;

            return;
        }

        float effectDamageScale = 0.0f, 
            effectTargetDamageScale = 0.0f, 
            effectTargetHPScale = 0.0f, 
            effectTargetRecovery = 0.0f;
        if (property.attributes != null)
        {
            foreach (var attribute in property.attributes)
            {
                switch (attribute.type)
                {
                    case UserAttributeType.Hp:
                        effectTargetHPScale += attribute.value;
                        break;
                    case UserAttributeType.Attack:
                        effectDamageScale += attribute.value;
                        break;
                    case UserAttributeType.Defence:
                        effectTargetDamageScale += attribute.value;
                        break;
                    case UserAttributeType.Recovery:
                        effectTargetRecovery += attribute.value;
                        break;
                }
            }
        }
        
        LevelPlayerShared.effectTargetHPScale = effectTargetHPScale;
        LevelPlayerShared.effectDamageScale = effectDamageScale;
        LevelPlayerShared.effectTargetDamageScale = effectTargetDamageScale;
        LevelPlayerShared.effectTargetRecovery = effectTargetRecovery;
        
        LevelPlayerShared.instanceName = property.name;

        ref var activeSkills = ref LevelPlayerShared.activeSkills;
        activeSkills.Clear();
        
        ref var skillGroups = ref LevelPlayerShared.skillGroups;
        skillGroups.Clear();
        
        if (property.skills != null)
        {
            LevelPlayerActiveSkill activeSkill;
            LevelPlayerSkillGroup skillGroup;
            foreach (var skill in property.skills)
            {
                switch (skill.type)
                {
                    case UserSkillType.Individual:
                        activeSkill.name = skill.name;
                        activeSkill.damageScale = skill.damage;
                        activeSkills.Add(activeSkill);
                        break;
                    case UserSkillType.Group:
                        skillGroup.name = skill.name;
                        skillGroup.damageScale = skill.damage;
                        skillGroups.Add(skillGroup);
                        break;
                }
            }
        }

        uint userID = LoginManager.userID.Value;
        
        ILevelData.instance = new GameLevelData(userID);

        IRewardData.instance = new RewardData(userID);
    }
    
    private void __ApplyEnergy(User user, UserEnergy userEnergy)
    {
        userID = user.id;
        gold = user.gold;

        energyMax = userEnergy.max;

        __energyUnitTime = userEnergy.unitTime * 0.001f;
        float energyValueFloat = (float)((double)(DateTime.UtcNow.Ticks - userEnergy.tick) /
                                      (TimeSpan.TicksPerMillisecond * userEnergy.unitTime));
        int energyValueInt = Mathf.FloorToInt(energyValueFloat);

        this.energy =
            Mathf.Clamp(userEnergy.value + energyValueInt, 0, userEnergy.max);

        __energyNextTime = (1.0f - (energyValueFloat - energyValueInt)) * __energyUnitTime;

        InvokeRepeating(
            nameof(__IncreaseEnergy), 
            __energyNextTime,
            __energyUnitTime);
        
        (IAnalytics.instance as IAnalyticsEx)?.Login(user.id);
    }

    private void __IncreaseEnergy()
    {
        energy = Mathf.Min(energy + 1, energyMax);
        
        if(!__isStart && 
           __selectedLevelEnergy <= energy && 
           __styles.TryGetValue(__selectedLevelIndex, out var style) && 
           style.button != null)
            style.button.interactable = true;
    }
    
    private void __CreateRewards(StageRewardStyle style, UserRewardData[] values)
    {
        if (style != null && values != null &&
            values.Length > 0)
        {
            if (__rewardIndices == null)
            {
                __rewardIndices = new Dictionary<string, int>();
                int numSkills = _rewards == null ? 0 : _rewards.Length;
                for (int i = 0; i < numSkills; ++i)
                    __rewardIndices[_rewards[i].name] = i;
            }

            if (__rewardStyles == null)
                __rewardStyles = new List<StageRewardStyle>();

            StageRewardStyle rewardStyle;
            foreach (var value in values)
            {
                ref var reward = ref _rewards[__rewardIndices[value.name]];
                rewardStyle = Instantiate(style, style.transform.parent);

                if (rewardStyle.onSprite != null)
                    rewardStyle.onSprite.Invoke(reward.sprite);

                rewardStyle.gameObject.SetActive(true);

                __rewardStyles.Add(rewardStyle);
            }
        }
    }

    private void __DestroyRewards()
    {
        if (__rewardStyles != null)
        {
            foreach (var rewardStyle in __rewardStyles)
            {
                if (rewardStyle.onDestroy != null)
                    rewardStyle.onDestroy.Invoke();
                            
                Destroy(rewardStyle.gameObject, _rewardStyleDestroyTime);
            }
                        
            __rewardStyles.Clear();
        }
    }

    private IEnumerator __Start(bool isRestart)
    {
        __isStart = true;
        
        foreach (var style in __styles.Values)
        {
            if (style.button != null)
                style.button.interactable = false;
        }

        var userData = IUserData.instance;

        uint userID = LoginManager.userID.Value;
        
#if USER_DATA_LEGACY
        yield return userData.QuerySkills(userID, __ApplySkills);
#endif

        if (isRestart)
            yield return userData.ApplyLevel(userID, __selectedUserLevelID, __ApplyLevel);
        else
            yield return userData.ApplyStage(userID, __selectedUserStageID, __ApplyStage);
            
        if (!__isStart)
        {
            __isStart = false;
            
            yield break;
        }

        _onStart.Invoke();
        
        var analytics = IAnalytics.instance as IAnalyticsEx;
        analytics?.StartLevel(_levels[__selectedLevelIndex].name);
    }

    IEnumerator Start()
    {
        instance = this;

        while (IUserData.instance == null)
            yield return null;
        
        var userData = IUserData.instance;
        yield return userData.QueryUser(GameUser.Shared.channelName, GameUser.Shared.channelUser, __ApplyEnergy);
        yield return userData.CollectLevel(userID.Value, __ApplyLevel);
        yield return userData.QueryLevels(userID.Value, __ApplyLevels);
    }

    void Update()
    {
        if (energy < energyMax)
        {
            var timeSpan = TimeSpan.FromSeconds(__energyNextTime);

            __energyNextTime -= Time.deltaTime;
            if (__energyNextTime < 0.0f)
                __energyNextTime += __energyUnitTime;
            
            if (_onEnergyTime != null)
                _onEnergyTime.Invoke(timeSpan.ToString(_energyTimeFormat));
        }
        else
        {
            __energyNextTime = __energyUnitTime;
            
            if(_onEnergyTime != null)
                _onEnergyTime.Invoke(string.Empty);
        }
    }
}
