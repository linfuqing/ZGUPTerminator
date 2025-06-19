using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using UnityEngine.UI;
using ZG.UI;

public sealed class LoginManager : MonoBehaviour
{
    public struct Stage
    {
        public string name;
        public string levelName;
        public uint id;
    }

    [Serializable]
    internal struct Scene
    {
        public string name;
        
        public int[] stageIndices;
    }

    [Serializable]
    internal struct Level
    {
        public string name;
        public string title;
        public GameObject prefab;
        
        public Scene[] scenes;
    }

    [Serializable]
    internal struct Reward
    {
        public string name;
        public Sprite sprite;
    }

    public static event Action<Memory<UserRewardData>> onAwake;
    
    public static event Action<Stage> onStageChanged;
    
    public event Action<int> onEnergyChanged;

    [SerializeField] 
    internal float _startTime = 0.5f;

    [SerializeField]
    internal float _stageStyleDestroyTime;
    
    [SerializeField]
    internal float _rewardStyleDestroyTime;

    [SerializeField] 
    internal string _energyTimeFormat = @"mm\'ss\'\'";

    [SerializeField] 
    internal UnityEvent _onStageFailed;

    [SerializeField]
    internal UnityEvent _onStart;

    [SerializeField]
    internal UnityEvent _onHotEnable;

    [SerializeField]
    internal UnityEvent _onHotDisable;

    [SerializeField]
    internal UnityEvent _onEnergyEnable;

    [SerializeField]
    internal UnityEvent _onEnergyDisable;

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
    private Dictionary<int, LevelStyle> __levelStyles;
    private Dictionary<string, int> __rewardIndices;

    private string __levelName;
    private string __sceneName;

    private float __energyNextTime;
    private float __energyUnitTime;

    private int __gold;

    private int __energy;

    private int __energyMax;

    //private int __selectedLevelEnergy;
    private int __selectedEnergy;
    //private int __selectedLevelIndex;
    private uint __selectedUserLevelID;
    private uint __selectedUserStageID;
    private int __selectedStageIndex;

    private int __sceneActiveDepth;
    
    private bool __isStart;
    private bool __isEnergyActive = true;

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

    public bool isEnergyActive
    {
        get => __isEnergyActive;

        set
        {
            if (value == __isEnergyActive)
                return;

            if (value)
            {
                if(_onEnergyEnable != null)
                    _onEnergyEnable.Invoke();
            }
            else
            {
                if(_onEnergyDisable != null)
                    _onEnergyDisable.Invoke();
            }
            
            __isEnergyActive = value;
        }
    }

    public int selectedEnergy
    {
        get => __selectedEnergy;

        private set
        {
            __selectedEnergy = value;
            
            isEnergyActive = value <= energy;
        }
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

    public IReadOnlyCollection<int> levelIndices => __levelStyles.Keys;

    [Obsolete]
    public string[] activeSkillNames;

    [Preserve]
    public void RefreshLevel()
    {
        if (__sceneActiveDepth > 0)
        {
            if (--__sceneActiveDepth > 0)
                return;
        }
        else
            return;
        
        foreach (var levelStyle in __levelStyles.Values)
        {
            if (levelStyle.toggle.isOn)
            {
                levelStyle.toggle.onValueChanged.Invoke(true);
                
                break;
            }
        }
        
        var scrollRect = _style.transform.parent.GetComponentInParent<ZG.ScrollRectComponentEx>(true);
        if (scrollRect != null)
            scrollRect.MoveTo(__levelStyles.Count - 1);
    }
    
    [Preserve]
    public void RefreshStages()
    {
        StageStyle stageStyle;
        int numStageStyles = __stageStyles.Count;
        for (int i = numStageStyles - 1; i >= 0; --i)
        {
            stageStyle = __stageStyles[i];
            if(stageStyle == null || !stageStyle.isActiveAndEnabled || !stageStyle.toggle.interactable)
                continue;

            stageStyle.toggle.isOn = true;

            break;
        }
        
        for (int i = 0; i < numStageStyles; ++i)
        {
            stageStyle = __stageStyles[i];
            if(stageStyle == null || stageStyle.isActiveAndEnabled)
                continue;

            stageStyle.toggle.isOn = false;
        }
    }

    public void CollectAndQueryLevels()
    {
        StartCoroutine(__CollectAndQueryLevels());
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
    
    private void __ApplyLevels(IUserData.Levels levels)
    {
        if ((levels.flag & IUserData.Levels.Flag.UnlockFirst) != 0)
            __sceneActiveDepth = Mathf.Max(__sceneActiveDepth + 1, 1);
        
        if (__levelStyles != null)
        {
            foreach (var style in __levelStyles.Values)
                Destroy(style.gameObject);
        }
        
        int numLevels = _levels.Length;
        var levelIndices = new Dictionary<string, int>(numLevels);
        for (int i = 0; i < numLevels; ++i)
            levelIndices[_levels[i].name] = i;

        bool isHot = false;
        Transform parent = _style.transform.parent;
        __levelStyles = new Dictionary<int, LevelStyle>(levels.levels.Length);
        foreach (var userLevel in levels.levels)
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

            var level = _levels[index];
            
            if(style.onTitle != null)
                style.onTitle.Invoke(level.title);

            //if(style.onImage != null)
            //    style.onImage.Invoke(level.sprite);
            Instantiate(level.prefab, style.root);

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
                
                int numScenes = style.scenes == null ? 0 : style.scenes.Length;
                Toggle toggle;
                for(int i = 0; i < numScenes; ++i)
                {
                    toggle = style.scenes[i].toggle;
                    if(toggle == null)
                        continue;
                                                
                    toggle.interactable = false;
                }
                
                if (x)
                {
                    __levelName = selectedLevel.name;
                    
                    selectedEnergy = selectedLevel.energy;

                    /*if (style.button != null)
                        style.button.interactable = __selectedLevelEnergy <= energy && !__isStart;*/
                    
                    //__selectedLevelIndex = index;
                    __selectedUserLevelID = selectedLevel.id;

                    int numStages = selectedLevel.stages == null ? 0 : selectedLevel.stages.Length;
                    if (numStages > 0)
                    {
                        numScenes = Mathf.Min(
                            numScenes,
                            level.scenes.Length);
                        if (numScenes > 0)
                        {
                            if (__stageStyles == null)
                                __stageStyles = new List<StageStyle>();

                            bool isHot;
                            int i,
                                j,
                                numRanks,
                                numRewardFlags,
                                stageStyleStartIndex = __stageStyles.Count,
                                selectedStageIndex = 0, 
                                selectedSceneIndex = 0;
                            UserStageReward.Flag rewardFlag;
                            StageStyle stageStyle;
                            GameObject rank;
                            HashSet<int> sceneIndices = null;
                            for (i = 0; i < numStages; ++i)
                            {
                                var stage = selectedLevel.stages[i];

                                int stageIndex = -1;
                                for (j = 0; j < numScenes; ++j)
                                {
                                    ref var levelScene = ref level.scenes[j];

                                    stageIndex = Array.IndexOf(levelScene.stageIndices, i);
                                    if (stageIndex != -1)
                                        break;
                                }
                                
                                if(j == numScenes)
                                    continue;
                                
                                if(style.scenes == null || style.scenes.Length <= j)
                                    continue;

                                int sceneIndex = j;

                                var styleScene = style.scenes[sceneIndex];

                                stageStyle = styleScene.stageStyle;
                                stageStyle = Instantiate(stageStyle, stageStyle.transform.parent);

                                if (stageStyle.onTitle != null)
                                    stageStyle.onTitle.Invoke((/*i*/stageIndex + 1).ToString());

                                if (stage.rewardFlags == null)
                                {
                                    if (stageStyle.onHot != null)
                                        stageStyle.onHot.Invoke(false);

                                    if (stageStyle.toggle != null)
                                    {
                                        stageStyle.toggle.interactable = false;

                                        stageStyle.toggle.isOn = false;
                                    }
                                    
                                    __CreateRewards(stageStyle.rewardStyle, stage.rewards);
                                }
                                else
                                {
                                    if (sceneIndices == null)
                                        sceneIndices = new HashSet<int>();
                                
                                    sceneIndices.Add(sceneIndex);

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
                                        int selectedStage = i, stageListIndex = __stageStyles.Count;
                                        
                                        var onSelected = stageStyle.onSelected;

                                        stageStyle.toggle.isOn = false;
                                        stageStyle.toggle.interactable = true;
                                        stageStyle.toggle.onValueChanged.AddListener(x =>
                                        {
                                            //__DestroyRewards();
                                            
                                            if (x)
                                            {
                                                if (selectedStageIndex != stageListIndex)
                                                {
                                                    if (onSelected != null)
                                                        onSelected.Invoke();
                                                }

                                                __sceneName = level.scenes[sceneIndex].name;
                                                __selectedUserStageID = stage.id;

                                                __selectedStageIndex = selectedStage;

                                                //LevelShared.stage = selectedStage;

                                                if (onStageChanged != null)
                                                {
                                                    Stage result;
                                                    result.name = (stageIndex + 1).ToString();
                                                    result.levelName = level.title;
                                                    result.id = stage.id;
                                                    onStageChanged.Invoke(result);
                                                }
                                                
                                                selectedEnergy = stage.energy;
                                            }
                                        });
                                    }

                                    if (__sceneActiveDepth <= 0 || 
                                        GameMain.GetSceneTimes(level.scenes[sceneIndex].name) > 0)
                                    {
                                        //__sceneActiveStatus = SceneActiveStatus.None;

                                        selectedStageIndex = __stageStyles.Count;
                                        selectedSceneIndex = sceneIndex;
                                    }
                                }
                                //stageStyle.gameObject.SetActive(true);

                                __stageStyles.Add(stageStyle);
                            }

                            UnityAction<bool> handler;
                            Toggle.ToggleEvent onValueChanged;
                            for (i = 0; i < numScenes; ++i)
                            {
                                ref var styleScene = ref style.scenes[i];

                                if (styleScene.toggle != null)
                                {
                                    onValueChanged = styleScene.toggle.onValueChanged;
                                    onValueChanged.RemoveAllListeners();

                                    int currentSceneIndex = i;
                                    handler = x =>
                                    {
                                        if (x)
                                        {
                                            if (__sceneActiveDepth != 0 ||
                                                GameMain.GetSceneTimes(level.scenes[currentSceneIndex].name) > 0)
                                                style.scenes[currentSceneIndex].onActive.Invoke();
                                            else
                                            {
                                                style.scenes[currentSceneIndex].onActiveFirst.Invoke();

                                                __sceneActiveDepth = -1;
                                                //__sceneActiveStatus = SceneActiveStatus.None;
                                            }

                                            Toggle toggle;
                                            for(int i = 0; i < numScenes; ++i)
                                            {
                                                toggle = style.scenes[i].toggle;
                                                if(toggle == null)
                                                    continue;
                                                
                                                toggle.interactable = i != currentSceneIndex && sceneIndices.Contains(i);
                                            }
                                        }
                                    };
                                    
                                    onValueChanged.AddListener(handler);

                                    if (i == selectedSceneIndex)
                                    {
                                        if (styleScene.toggle.isOn)
                                            handler(true);
                                        else
                                            styleScene.toggle.isOn = true;
                                    }
                                }
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
                style.button.onClick.RemoveListener(__ApplyStart);
                style.button.onClick.AddListener(__ApplyStart);
            }

            style.gameObject.SetActive(true);
            
            __levelStyles[index] = style;
        }

        var scrollRect = parent.GetComponentInParent<ZG.ScrollRectComponentEx>(true);
        if (scrollRect != null)
        {
            int i, end = __levelStyles.Count - 1;
            if (__sceneActiveDepth > 0)
            {
                for (i = end; i >= 0; --i)
                {
                    if (GameMain.GetLevelTimes(_levels[i].name) < 1)
                        continue;

                    scrollRect.MoveTo(i);

                    break;
                }
            }
            else
                i = -1;

            if (i < 0)
                scrollRect.MoveTo(end);
        }

        if (isHot)
        {
            if (_onHotEnable != null)
                _onHotEnable.Invoke();
        }
        else if(_onHotDisable != null)
            _onHotDisable.Invoke();
    }

    private void __ApplyLevel(Memory<UserReward> rewards)
    {
        int numRewards = rewards.Length;
        UserRewardData[] results;
        if (numRewards > 0)
        {
            __sceneActiveDepth = Mathf.Max(__sceneActiveDepth + 1, 1);
            
            UserRewardData result;
            results = new UserRewardData[numRewards];
            for (int i = 0; i < numRewards; ++i)
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
        }
        else
            results = null;

        if(onAwake != null)
            onAwake(results);
    }

    private void __ApplyLevel(IUserData.LevelProperty property)
    {
        LevelPlayerShared.skillRage = 0;
        LevelShared.exp = 0;
        LevelShared.expMax = 0;
        
        LevelShared.stage = property.stage;

        __SubmitStage(property.value);
    }
    
    private void __ApplyStage(IUserData.StageProperty property)
    {
        if (property.cache.skills == null)
        {
            __isStart = false;

            //__styles[__selectedLevelIndex].button.interactable = true;
            
            if(_onStageFailed != null)
                _onStageFailed.Invoke();
            
            return;
        }
        
        LevelPlayerShared.skillRage = property.cache.rage;
        LevelShared.exp = property.cache.exp;
        LevelShared.expMax = property.cache.expMax;
        
        LevelShared.stage = property.stage;
        
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

        IRewardData.instance = new GameRewardData(userID);
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

        if (__selectedEnergy == energy)
            isEnergyActive = true;
        
        /*if(!__isStart &&
           __selectedLevelEnergy <= energy &&
           __styles != null &&
           __styles.TryGetValue(__selectedLevelIndex, out var style) &&
           style.button != null)
            style.button.interactable = true;*/
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

            int rewardIndex;
            StageRewardStyle rewardStyle;
            foreach (var value in values)
            {
                if(!__rewardIndices.TryGetValue(value.name, out rewardIndex))
                    continue;
                
                ref var reward = ref _rewards[rewardIndex];
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

    private void __LoadScene()
    {
        GameMain.IncrementLevelTimes(__levelName);
        GameMain.IncrementSceneTimes(__sceneName);
        
        var assetManager = GameAssetManager.instance;
        if (assetManager == null)
        {
            var gameObject = new GameObject("GameAssetManager");
            DontDestroyOnLoad(gameObject);
            assetManager = gameObject.AddComponent<GameAssetManager>();
        }

        assetManager.LoadScene(__sceneName/*_levels[__selectedLevelIndex].name*/, null, new GameSceneActivation());
    }

    private IEnumerator __CollectAndQueryLevels()
    {
        var userData = IUserData.instance;
        yield return userData.CollectLevel(userID.Value, __ApplyLevel);
        yield return userData.QueryLevels(userID.Value, __ApplyLevels);
    }

    private IEnumerator __Start(bool isRestart)
    {
        if(__isStart)
            yield break;
        
        __isStart = true;
        
        //__styles[__selectedLevelIndex].button.interactable = false;
        
        /*foreach (var style in __styles.Values)
        {
            if (style.button != null)
                style.button.interactable = false;
        }*/

        var userData = IUserData.instance;

        uint userID = LoginManager.userID.Value;
        
#if USER_DATA_LEGACY
        yield return userData.QuerySkills(userID, __ApplySkills);
#endif

        if (isRestart)
            yield return userData.ApplyLevel(userID, __selectedUserLevelID, __selectedStageIndex, __ApplyLevel);
        else
            yield return userData.ApplyStage(userID, __selectedUserStageID, __ApplyStage);
            
        if (!__isStart)
        {
            __isStart = false;
            
            yield break;
        }

        _onStart.Invoke();
        
        var analytics = IAnalytics.instance as IAnalyticsEx;
        analytics?.StartLevel(__sceneName/*_levels[__selectedLevelIndex].name*/);
        
        Invoke(nameof(__LoadScene), _startTime);
    }

    IEnumerator Start()
    {
        instance = this;

        while (IUserData.instance == null)
            yield return null;
        
        var userData = IUserData.instance;
        yield return userData.QueryUser(GameUser.Shared.channelName, GameUser.Shared.channelUser, __ApplyEnergy);
        yield return __CollectAndQueryLevels();
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
