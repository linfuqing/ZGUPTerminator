using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using ZG.UI;

public sealed class LoginManager : MonoBehaviour
{
    [Serializable]
    internal struct Level
    {
        public string name;
        public string title;
        public Sprite sprite;
    }

    [SerializeField]
    internal UnityEvent _onStart;

    [SerializeField]
    internal StringEvent _onEnergyMax;
    
    [SerializeField]
    internal StringEvent _onEnergy;

    [SerializeField] 
    internal Progressbar _energy;

    [SerializeField]
    internal LevelStyle _style;

    [SerializeField] 
    internal Level[] _levels;

    private Dictionary<int, LevelStyle> __styles;

    private int __energy;

    private int __energyMax;

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
        get;

        set;
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
    public float effectTargetDamage
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
    public string activeSkillName
    {
        get;

        set;
    }

    [Preserve]
    public void LoadScene()
    {
        var assetManager = GameAssetManager.instance;
        if (assetManager == null)
            assetManager = gameObject.AddComponent<GameAssetManager>();
        
        assetManager.LoadScene(_levels[__selectedLevelIndex].name, null, new GameSceneActivation());
    }

    private void __ApplyStart()
    {
        StartCoroutine(__Start());
    }

    private void __ApplySkills(Memory<UserSkill> skills)
    {
        ref var levelSkillNames = ref LevelPlayerShared.levelSkillNames;
        levelSkillNames.Clear();

        foreach (var skill in skills.Span)
            levelSkillNames.Add(skill.name);
        
        ref var activeSkillNames = ref LevelPlayerShared.activeSkillNames;
        activeSkillNames.Clear();
        if(!string.IsNullOrEmpty(activeSkillName))
            activeSkillNames.Add(activeSkillName);
        
        LevelPlayerShared.effectDamageScale = effectTargetDamage;
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
                    if (style.button != null)
                        style.button.interactable = selectedLevel.energy <= energy && !__isStart;
                    
                    __selectedLevelIndex = index;
                    __selectedUserLevelID = selectedLevel.id;
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
    }

    private void __ApplyEnergy(User user, UserEnergy userEnergy)
    {
        userID = user.id;
        gold = user.gold;

        energyMax = userEnergy.max;

        float energyUnitTime = userEnergy.unitTime * 0.001f,
            energyNextValue = (float)((double)(DateTime.UtcNow.Ticks - userEnergy.tick) /
                                      (TimeSpan.TicksPerMillisecond * userEnergy.unitTime));
        int energyNextValueInt = Mathf.FloorToInt(energyNextValue);

        this.energy =
            Mathf.Clamp(userEnergy.value + energyNextValueInt, 0, userEnergy.max);

        InvokeRepeating(nameof(__IncreaseEnergy), (1.0f - (energyNextValue - energyNextValueInt)) * energyUnitTime,
            energyUnitTime);
    }

    private void __IncreaseEnergy()
    {
        energy = Mathf.Min(energy + 1, energyMax);
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

        _onStart.Invoke();
    }

    IEnumerator Start()
    {
        instance = this;

        while (IUserData.instance == null)
            yield return null;
        
        var userData = IUserData.instance;
        yield return userData.QueryUser(GameUser.Shared.channelName, GameUser.Shared.channelUser, __ApplyEnergy);
        yield return userData.QueryLevels(userID.Value, __ApplyLevels);
    }
}
