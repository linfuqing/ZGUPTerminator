using System;
using System.Collections;
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

    private LevelStyle[] __styles;

    private int __selectedIndex;
    private float _energyUnitTime;

    public static uint? userID
    {
        get;

        private set;
    } = null;

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
        get;

        set;
    }

    public int energyMax
    {
        get;

        private set;
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
        
        assetManager.LoadScene(_levels[__selectedIndex].name, null, new GameSceneActivation());
    }

    private void __LoadScene()
    {
        StartCoroutine(__Start());
    }

    private void __ApplyLevel(Memory<UserSkill> skills)
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

    private IEnumerator __Start()
    {
        yield return IUserData.instance.QuerySkills(userID.Value, __ApplyLevel);

        _onStart.Invoke();
    }

    IEnumerator Start()
    {
        instance = this;

        int level = -1;
        var userData = IUserData.instance;
        if (userData == null)
            userID = 0;
        else
        {
            yield return userData.QueryUser(GameUser.Shared.channelName, GameUser.Shared.channelUser, (user, energy) =>
            {
                userID = user.id;
                gold = user.gold;
                level = user.level;

                _energyUnitTime = energy.unitTime * 0.001f;

                this.energy =
                    Mathf.Clamp(
                        energy.value + (int)((DateTime.UtcNow.Ticks - energy.tick) /
                        (TimeSpan.TicksPerMillisecond * energy.unitTime)), 0, energy.max);

                energyMax = energy.max;

            });
        }
        
        int numLevels = _levels == null ? 0 : _levels.Length;
        if (level > 0)
            numLevels = Mathf.Min(numLevels, level);

        Transform parent = _style.transform.parent;
        LevelStyle style;
        __styles = new LevelStyle[numLevels];
        for (int i = 0; i < numLevels; ++i)
        {
            style = Instantiate(_style, parent);
            
            if(style.onImage != null)
                style.onImage.Invoke(_levels[i].sprite);

            int index = i;
            style.toggle.onValueChanged.AddListener(x =>
            {
                if(x)
                    __selectedIndex = index;
            });
            
            style.button.onClick.RemoveAllListeners();
            style.button.onClick.AddListener(__LoadScene);
            
            style.gameObject.SetActive(true);

            __styles[i] = style;
        }
    }
}
