using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using ZG.UI;

public sealed class LoginManager : MonoBehaviour
{
    private class LevelData : ILevelData
    {
        public IEnumerator SubmitLevel(
            int stage,
            int gold,
            Action<bool> onComplete)
        {
            return IUserData.instance.SubmitLevel(userID.Value, stage, gold, onComplete);
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
    internal struct Skill
    {
        public string name;
        public Sprite sprite;
    }

    public delegate void Awake(string[] rewardSkills);

    public static event Awake onAwake;

    [SerializeField]
    internal float _rewardStyleDestroyTime;

    [SerializeField] 
    internal string _energyTimeFormat = @"mm\'ss\'\'";

    [SerializeField]
    internal UnityEvent _onStart;

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

    [SerializeField] 
    internal Skill[] _skills;

    private List<RewardStyle> __rewardStyles;
    private Dictionary<int, LevelStyle> __styles;
    private Dictionary<string, int> __skillIndices;

    private float __energyNextTime;
    private float __energyUnitTime;

    private int __gold;

    private int __energy;

    private int __energyMax;

    private int __selectedLevelEnergy;
    private int __selectedLevelIndex;
    private uint __selectedUserLevelID;

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

        private set
        {
            __energy = value;

            if (_energy != null && __energyMax > Mathf.Epsilon)
                _energy.value = value * 1.0f / __energyMax;
            
            if(_onEnergy != null)
                _onEnergy.Invoke(value.ToString());
        }
    }

    public int energyMax
    {
        get => __energyMax;

        private set
        {
            __energyMax = value;
            
            if(_onEnergyMax != null)
                _onEnergyMax.Invoke(value.ToString());
        }
    }

    /// <summary>
    /// 总攻击倍率
    /// </summary>
    public float bulletDamageScale
    {
        get;

        set;
    }
    
    /// <summary>
    /// 总防御倍率
    /// </summary>
    public float effectTargetDamageScale
    {
        get;

        set;
    }
    
    /// <summary>
    /// 总HP倍率
    /// </summary>
    public float effectTargetHPScale
    {
        get;

        set;
    }

    /// <summary>
    /// 选择的超能武器的名字
    /// </summary>
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

    private void __ApplyStart()
    {
        StartCoroutine(__Start());
    }

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

        LevelPlayerShared.bulletDamageScale = bulletDamageScale;
        LevelPlayerShared.effectTargetDamageScale = effectTargetDamageScale;
        LevelPlayerShared.effectTargetHPScale = effectTargetHPScale;
    }

    private void __ApplyLevels(Memory<UserLevel> userLevels)
    {
        var levelIndices = new Dictionary<string, int>();
        int numLevels = _levels.Length;
        for (int i = 0; i < numLevels; ++i)
            levelIndices[_levels[i].name] = i;

        Level level;
        Transform parent = _style.transform.parent;
        __styles = new Dictionary<int, LevelStyle>(userLevels.Length);
        foreach (var userLevel in userLevels.Span)
        {
            var style = Instantiate(_style, parent);
            
            var selectedLevel = userLevel;
            
            if(style.onEnergy != null)
                style.onEnergy.Invoke(selectedLevel.energy.ToString());

            int index = levelIndices[selectedLevel.name];
            level = _levels[index];
            
            if(style.onTitle != null)
                style.onTitle.Invoke(level.title);

            if(style.onImage != null)
                style.onImage.Invoke(level.sprite);

            style.toggle.onValueChanged.AddListener(x =>
            {
                if (x)
                {
                    __selectedLevelEnergy = selectedLevel.energy;
                    
                    if (style.button != null)
                        style.button.interactable = __selectedLevelEnergy <= energy && !__isStart;
                    
                    __selectedLevelIndex = index;
                    __selectedUserLevelID = selectedLevel.id;

                    if (style.rewardStyle != null && selectedLevel.rewardSkills != null &&
                        selectedLevel.rewardSkills.Length > 0)
                    {
                        if (__skillIndices == null)
                        {
                            __skillIndices = new Dictionary<string, int>();
                            int numSkills = _skills == null ? 0 : _skills.Length;
                            for (int i = 0; i < numSkills; ++i)
                                __skillIndices[_skills[i].name] = i;
                        }

                        if (__rewardStyles == null)
                            __rewardStyles = new List<RewardStyle>();

                        RewardStyle rewardStyle;
                        foreach (var rewardSkill in selectedLevel.rewardSkills)
                        {
                            ref var skill = ref _skills[__skillIndices[rewardSkill]];
                            rewardStyle = style.rewardStyle;
                            rewardStyle = Instantiate(rewardStyle, rewardStyle.transform.parent);
                            
                            if(rewardStyle.onSprite != null)
                                rewardStyle.onSprite.Invoke(skill.sprite);
                            
                            rewardStyle.gameObject.SetActive(true);
                            
                            __rewardStyles.Add(rewardStyle);
                        }
                    }
                }
                else
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
    }

    private void __ApplyLevel(int gold, string[] rewardSkills)
    {
        if (rewardSkills == null)
            return;

        this.gold += gold;

        if(onAwake != null)
            onAwake(rewardSkills);
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
        
        var analytics = IAnalytics.instance as IAnalyticsEx;
        if(analytics != null)
            analytics.Login(user.id);
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

    private IEnumerator __Start()
    {
        __isStart = true;
        
        foreach (var style in __styles.Values)
        {
            if (style.button != null)
                style.button.interactable = false;
        }

        var userData = IUserData.instance;

        uint userID = LoginManager.userID.Value;
        yield return userData.QuerySkills(userID, __ApplySkills);

        bool result = false;
        yield return userData.ApplyLevel(userID, __selectedUserLevelID, x => result = x);
        if (!result)
        {
            __isStart = false;
            
            yield break;
        }

        ILevelData.instance = new LevelData();

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
