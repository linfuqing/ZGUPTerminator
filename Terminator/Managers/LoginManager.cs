using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using UnityEngine.UI;
using ZG;
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
        
        public AssetObjectLoader prefab;
        
        public int[] stageIndices;
    }

    [Serializable]
    internal struct Level
    {
        public string name;
        public string title;
        
        public Scene[] scenes;
    }

    [Serializable]
    internal struct Reward
    {
        public string name;
        public Sprite sprite;
    }

    private struct Loader
    {
        public Progressbar progressbar;

        public AssetObjectLoader assetObject;

        public Loader(Progressbar progressbar, AssetObjectLoader assetObject)
        {
            this.progressbar = progressbar;
            this.assetObject = assetObject;
        }

        public void Dispose()
        {
            assetObject?.Dispose();
        }

        public void Load(AssetManager assetManager)
        {
            assetObject?.Load(assetManager);
        }
        
        public void Update()
        {
            if (progressbar == null || assetObject == null)
                return;
            
            progressbar.value = assetObject.progress;
        }
    }

    private struct Loaders
    {
        public Loader[] values;

        public void Load(AssetManager assetManager)
        {
            if (values == null)
                return;
            
            foreach (var loader in values)
                loader.Load(assetManager);
        }

        public void Update()
        {
            if (values == null)
                return;
            
            foreach (var loader in values)
                loader.Update();
        }
        
        public void Dispose()
        {
            if (values == null)
                return;
            
            foreach (var loader in values)
                loader.Dispose();
        }
    }

    public static event Action<IUserData.LevelStage> onAwake;
    
    /// <summary>
    /// 可以激活商业化，不需要播放章节解锁或者下篇动画
    /// </summary>
    public static event Action onLevelActivated;

    /// <summary>
    /// 可以激活商业化，要先等待播放章节解锁或者下篇动画
    /// </summary>
    public static event Action onLevelActivatedFirst;

    public static event Action<IUserData.LevelChapters> onChapterLoaded;

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
    internal UnityEvent _onLevelEnable;

    [SerializeField]
    internal StringEvent _onLevelDisable;

    [SerializeField]
    internal StringEvent _onStageReward;

    [SerializeField]
    internal StringEvent _onStageRewardAll;

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
    internal Reward[] _rewards;

    private List<StageRewardStyle> __rewardStyles;
    private List<StageStyle>[] __stageStyles;
    private Dictionary<int, LevelStyle> __levelStyles;
    private Dictionary<string, int> __rewardIndices;
    private LinkedList<Loaders> __loaders;

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
    //private uint __selectedUserStageID;
    private int __selectedStageIndex;

    private int __sceneActiveDepth;
    
    private bool __isStart;
    private bool __isEnergyActive = true;
    private bool? __isLevelActive;

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
    
    public event UnityAction<string> onGoldChanged
    {
        add => _onGold.AddListener(value);

        remove => _onGold.RemoveListener(value);
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
        var stageStyles = __stageStyles[0];
        StageStyle stageStyle;
        int numStageStyles = stageStyles.Count;
        for (int i = numStageStyles - 1; i >= 0; --i)
        {
            stageStyle = stageStyles[i];
            if(stageStyle == null || !stageStyle.isActiveAndEnabled || !stageStyle.toggle.interactable)
                continue;

            stageStyle.toggle.isOn = true;

            break;
        }
        
        for (int i = 0; i < numStageStyles; ++i)
        {
            stageStyle = stageStyles[i];
            if(stageStyle == null || stageStyle.isActiveAndEnabled)
                continue;

            stageStyle.toggle.isOn = false;
        }
    }

    public void CollectAndQueryLevels()
    {
        StartCoroutine(__CollectAndQueryLevels());
    }

    public void ApplyStart(
        bool isRestart, 
        uint userLevelID, 
        int stageIndex,
        string levelName, 
        string sceneName)
    {
        StartCoroutine(__Start(isRestart, userLevelID,  stageIndex, levelName, sceneName));
    }

    public void ApplyStart(bool isRestart)
    {
        ApplyStart(isRestart, __selectedUserLevelID, __selectedStageIndex, __levelName, __sceneName);
    }
    
    private void __ApplyLevelChapters(IUserData.LevelChapters levelChapters)
    {
        if ((levelChapters.flag & IUserData.LevelChapters.Flag.UnlockFirst) != 0)
            __sceneActiveDepth = Mathf.Max(__sceneActiveDepth + 1, 1);
        
        if (__levelStyles != null)
        {
            foreach (var style in __levelStyles.Values)
                Destroy(style.gameObject);
        }

        if (__loaders == null)
            __loaders = new LinkedList<Loaders>();
        else
        {
            foreach (var loader in __loaders)
                loader.Dispose();
            
            __loaders.Clear();
        }

        bool isSelected = false;
        foreach (var level in levelChapters.levels)
        {
            if (level.id == __selectedUserLevelID)
            {
                isSelected = true;
                break;
            }
        }

        if (!isSelected)
        {
            __selectedUserLevelID = 0;
            __selectedStageIndex = -1;
        }
        
        int i, numLevels = _levels.Length;
        var levelIndices = new Dictionary<string, int>(numLevels);
        for (i = 0; i < numLevels; ++i)
            levelIndices[_levels[i].name] = i;

        numLevels = levelChapters.levels.Length;
        bool isHot = false;
        int selectedLevelIndex = -1, 
            finalLevelIndex = -1, 
            endLevelIndex = -1, 
            numStageRewards = 0, 
            numStageRewardsTotal = 0, 
            numStages, 
            index;
        uint selectedStageID = 0;
        UserLevel userLevel;
        Transform parent = _style.transform.parent;
        __levelStyles = new Dictionary<int, LevelStyle>(levelChapters.levels.Length);
        for(i = 0; i < numLevels; ++i)
        {
            int userLevelIndex = i;
            userLevel = levelChapters.levels[userLevelIndex];

            //bool isEndOfLevels = userLevelIndex + 1 == numLevels;
            isSelected = false;
            if (userLevel.stages != null)
            {
                if (!isHot)
                {
                    foreach (var stage in userLevel.stages)
                    {
                        if (stage.rewardFlags == null)
                            break;

                        foreach (var rewardFlag in stage.rewardFlags)
                        {
                            if ((rewardFlag & UserStageReward.Flag.Unlocked) == UserStageReward.Flag.Unlocked &&
                                (rewardFlag & UserStageReward.Flag.Collected) != UserStageReward.Flag.Collected)
                            {
                                isHot = true;

                                break;
                            }
                        }
                    }
                }

                numStages = 0;
                foreach (var stage in userLevel.stages)
                {
                    if(__selectedStageIndex == numStages++ && __selectedUserLevelID == userLevel.id)
                        selectedStageID = stage.id;
                    
                    if (stage.rewardFlags == null)
                        break;
                    
                    foreach (var rewardFlag in stage.rewardFlags)
                    {
                        if ((rewardFlag & UserStageReward.Flag.Unlocked) == UserStageReward.Flag.Unlocked)
                        {
                            ++numStageRewards;

                            isSelected = true;
                        }
                    }
                    
                    numStageRewardsTotal += stage.rewardFlags.Length;
                }
            }

            if (__selectedUserLevelID == 0)
                isSelected |= __sceneActiveDepth == 0;
            else if (__sceneActiveDepth == 0)
                isSelected = __selectedUserLevelID == userLevel.id;
            else if (isSelected && selectedLevelIndex != -1 &&
                     levelChapters.levels[selectedLevelIndex].id == __selectedUserLevelID)
                isSelected = false;

            if (isSelected)
            {
                selectedLevelIndex = userLevelIndex;

                finalLevelIndex = userLevelIndex;
            }
            else if(__sceneActiveDepth != 0)
                finalLevelIndex = userLevelIndex;

            endLevelIndex = userLevelIndex;

            var style = Instantiate(_style, parent);
            style.name = userLevel.name;
            
            var selectedLevel = userLevel;

            index = levelIndices[userLevel.name];
            var level = _levels[index];
            
            if(style.onTitle != null)
                style.onTitle.Invoke(level.title);

            //if(style.onImage != null)
            //    style.onImage.Invoke(level.sprite);

            //int numScenes = style.scenes.Length, numPrefabs = Mathf.Min(numScenes, level.scenes.Length);
            //var prefabs = new GameObject[numPrefabs];
            //for(j = 0; j < numPrefabs; ++j)
            //    prefabs[j] = Instantiate(level.scenes[j].prefab, style.scenes[j].root);

            var loader = __loaders.AddLast(default(Loaders));

            style.toggle.onValueChanged.AddListener(x =>
            {
                __DestroyRewards();

                if (__stageStyles != null)
                {
                    foreach (var stageStyles in __stageStyles)
                    {
                        if(stageStyles == null)
                            continue;
                        
                        foreach (var stageStyle in stageStyles)
                        {
                            if (stageStyle.onDestroy != null)
                                stageStyle.onDestroy.Invoke();

                            Destroy(stageStyle.gameObject, _stageStyleDestroyTime);
                        }

                        stageStyles.Clear();
                    }
                }
                
                Toggle toggle;
                int numScenes = style.scenes.Length;
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
                    
                    //selectedEnergy = selectedLevel.energy;

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
                            bool isHot, isUnlocked, temp;
                            int i, j, k, 
                                numStageStyles, 
                                numRanks,
                                numRewardFlags,
                                selectedStageIndex = 0, 
                                selectedSceneIndex = 0, 
                                previousSceneIndex = -1;
                            uint currentStageID = 0;
                            UserStageReward.Flag rewardFlag;
                            StageStyle stageStyle;
                            GameObject rank;
                            List<StageStyle> stageStyles;
                            Dictionary<int, bool> sceneUnlocked = null;
                            for (i = 0; i < numStages; ++i)
                            {
                                var stage = selectedLevel.stages[i];

                                int sceneStageIndex = -1;
                                for (j = 0; j < numScenes; ++j)
                                {
                                    ref var levelScene = ref level.scenes[j];

                                    sceneStageIndex = Array.IndexOf(levelScene.stageIndices, i);
                                    if (sceneStageIndex != -1)
                                        break;
                                }
                                
                                if(j == numScenes)
                                    continue;
                                
                                if(style.scenes == null || style.scenes.Length <= j)
                                    continue;

                                int stageIndex = i, sceneIndex = j;

                                var styleScene = style.scenes[sceneIndex];

                                numStageStyles = styleScene.stageStyles == null ? 0 : styleScene.stageStyles.Length;
                                if (numStageStyles > 0)
                                {
                                    if (numStageStyles > (__stageStyles == null ? 0 : __stageStyles.Length))
                                        Array.Resize(ref __stageStyles, numStageStyles);
                                    
                                    for(j = 0; j < numStageStyles; ++j)
                                    {
                                        stageStyles = __stageStyles[j];
                                        if (stageStyles == null)
                                        {
                                            stageStyles = new List<StageStyle>();

                                            __stageStyles[j] = stageStyles;
                                        }

                                        stageStyle = styleScene.stageStyles[j];
                                        stageStyle = Instantiate(stageStyle, stageStyle.transform.parent);

                                        if (stageStyle.onTitle != null)
                                            stageStyle.onTitle.Invoke(( /*i*/sceneStageIndex + 1).ToString());

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
                                            isUnlocked = false;

                                            isHot = false;
                                            numRanks = stageStyle.ranks == null ? 0 : stageStyle.ranks.Length;
                                            numRewardFlags = stage.rewardFlags.Length;
                                            for (k = 0; k < numRewardFlags; ++k)
                                            {
                                                rewardFlag = stage.rewardFlags[k];
                                                if ((rewardFlag & UserStageReward.Flag.Unlocked) ==
                                                    UserStageReward.Flag.Unlocked)
                                                {
                                                    isUnlocked = true;

                                                    rank = numRanks > k ? stageStyle.ranks[k] : null;
                                                    if (rank != null)
                                                        rank.SetActive(true);

                                                    if ((rewardFlag & UserStageReward.Flag.Collected) !=
                                                        UserStageReward.Flag.Collected)
                                                        isHot = true;
                                                }
                                            }

                                            if (!isUnlocked)
                                                __CreateRewards(stageStyle.rewardStyle, stage.rewards);

                                            if (sceneUnlocked == null)
                                                sceneUnlocked = new Dictionary<int, bool>();

                                            sceneUnlocked[sceneIndex] = sceneUnlocked.TryGetValue(sceneIndex, out temp)
                                                ? temp | isUnlocked
                                                : isUnlocked;

                                            if (stageStyle.onHot != null)
                                                stageStyle.onHot.Invoke(isHot);

                                            var onSelected = stageStyle.onSelected;

                                            if (0 == j)
                                            {
                                                if (stageStyle.toggle != null)
                                                {
                                                    //int selectedStage = i;

                                                    stageStyle.toggle.isOn = false;
                                                    stageStyle.toggle.interactable = true;
                                                    stageStyle.toggle.onValueChanged.AddListener(x =>
                                                    {
                                                        //__DestroyRewards();

                                                        if (x)
                                                        {
                                                            if (selectedStageIndex != stageIndex)
                                                            {
                                                                if (onSelected != null)
                                                                    onSelected.Invoke();
                                                            }

                                                            __selectedStageIndex = stageIndex;

                                                            __sceneName = level.scenes[sceneIndex].name;

                                                            if (onStageChanged != null)
                                                            {
                                                                Stage result;
                                                                result.name = (sceneStageIndex + 1).ToString();
                                                                result.levelName = level.title;
                                                                result.id = stage.id;
                                                                onStageChanged.Invoke(result);
                                                            }

                                                            selectedEnergy = stage.energy;

                                                            if (style.onEnergy != null)
                                                                style.onEnergy.Invoke(stage.energy.ToString());

                                                            int numStageStyles, i;
                                                            foreach (var stageStyles in __stageStyles)
                                                            {
                                                                numStageStyles = stageStyles == null
                                                                    ? 0
                                                                    : stageStyles.Count;
                                                                for(i = 0; i < numStageStyles; ++i)
                                                                    stageStyles[i].toggle.SetIsOnWithoutNotify(i == stageIndex);
                                                            }
                                                        }
                                                    });
                                                }

                                                if ((selectedStageID == 0 || selectedStageID != currentStageID) &&
                                                    (__sceneActiveDepth <= 0 ||
                                                     sceneUnlocked != null &&
                                                     sceneUnlocked.TryGetValue(sceneIndex, out temp) &&
                                                     temp))
                                                {
                                                    currentStageID = stage.id;

                                                    selectedStageIndex = stageIndex;

                                                    selectedSceneIndex = sceneIndex;
                                                }
                                            }
                                            else if (stageStyle.toggle != null)
                                            {
                                                stageStyle.toggle.isOn = false;
                                                stageStyle.toggle.interactable = true;
                                                stageStyle.toggle.onValueChanged.AddListener(x =>
                                                {
                                                    //__DestroyRewards();

                                                    if (x)
                                                    {
                                                        if (selectedStageIndex != stageIndex)
                                                        {
                                                            if (onSelected != null)
                                                                onSelected.Invoke();
                                                        }

                                                        __stageStyles[0][stageIndex].toggle.isOn = true;
                                                    }
                                                });
                                            }
                                        }
                                        //stageStyle.gameObject.SetActive(true);

                                        stageStyles.Add(stageStyle);
                                    }
                                }
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
                                            //bool isLevelActive = false;
                                            if (__sceneActiveDepth != 0 || 
                                                sceneUnlocked != null && sceneUnlocked.TryGetValue(currentSceneIndex, out temp) && temp)
                                                //GameMain.GetSceneTimes(level.scenes[currentSceneIndex].name) > 0)
                                            {
                                                if (previousSceneIndex != currentSceneIndex)
                                                {
                                                    if (previousSceneIndex == -1)
                                                        style.scenes[currentSceneIndex].onActive.Invoke();
                                                    else
                                                        style.scenes[currentSceneIndex].onActiveDiff.Invoke();
                                                }

                                                if (__sceneActiveDepth == 0 && 
                                                    finalLevelIndex == userLevelIndex && 
                                                    onLevelActivated != null)
                                                    //isLevelActive = true;
                                                    onLevelActivated();
                                            }
                                            else
                                            {
                                                style.scenes[currentSceneIndex].onActiveFirst.Invoke();

                                                __sceneActiveDepth = -1;
                                                //__sceneActiveStatus = SceneActiveStatus.None;
                                                //isLevelActive = true;
                                                if (/*isEndOfLevels && */onLevelActivatedFirst != null)
                                                    onLevelActivatedFirst();
                                            }

                                            /*if (isLevelActive)
                                            {
                                                if (finalLevelIndex == userLevelIndex)
                                                {
                                                    if(onLevelActivatedFirst != null)
                                                        onLevelActivatedFirst();
                                                }
                                                else if(onLevelActivated != null)
                                                    onLevelActivated();
                                            }*/

                                            previousSceneIndex = currentSceneIndex;

                                            var assetManager = GameAssetManager.instance?.dataManager;
                                            
                                            LevelStyle.Scene levelStyleScene;
                                            AssetObjectLoader source;
                                            Loader destination;
                                            Loaders loaders;
                                            for(int i = 0; i < numScenes; ++i)
                                            {
                                                levelStyleScene = style.scenes[i];
                                                if (levelStyleScene.toggle != null)
                                                    levelStyleScene.toggle.interactable = i != currentSceneIndex &&
                                                                          sceneUnlocked != null &&
                                                                          sceneUnlocked.ContainsKey(i);
                                                
                                                source = level.scenes[i].prefab;
                                                loaders = loader.Value;
                                                if (loaders.values == null || loaders.values.Length < numScenes)
                                                {
                                                    Array.Resize(ref loaders.values, numScenes);

                                                    loader.Value = loaders;
                                                }
                                                
                                                destination = loaders.values[i];
                                                if (destination.assetObject != source)
                                                {
                                                    destination.assetObject?.Dispose();
                                                
                                                    source?.Init(this, style.scenes[currentSceneIndex].root);
                                                    source?.Load(assetManager);

                                                    destination.assetObject = source;
                                                    destination.progressbar = levelStyleScene.loaderProgressbar;

                                                    loaders.values[i] = destination;
                                                }
                                                else if (destination.progressbar != levelStyleScene.loaderProgressbar)
                                                {
                                                    destination.progressbar = levelStyleScene.loaderProgressbar;
                                                    
                                                    loaders.values[i] = destination;
                                                }

                                                loaders.values[i] = destination;
                                            }

                                            /*var prefab = level.scenes[currentSceneIndex].prefab;
                                            if (prefab != loader.Value.Item2)
                                            {
                                                loader.Value.Item2?.Dispose();
                                                
                                                prefab?.Init(this, style.scenes[currentSceneIndex].root);
                                                prefab?.Load(assetManager);

                                                loader.Value = (style.loaderProgressbar, prefab);
                                            }*/

                                            var node = loader.Previous;
                                            if (node != null)
                                            {
                                                node.Value.Load(assetManager);

                                                for (node = node.Previous; node != null; node = node.Previous)
                                                    node.Value.Dispose();
                                            }

                                            node = loader.Next;
                                            if (node != null)
                                            {
                                                node.Value.Load(assetManager);

                                                for (node = node.Next; node != null; node = node.Next)
                                                    node.Value.Dispose();
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

                            stageStyles = __stageStyles[0];
                            stageStyles[selectedStageIndex].toggle.isOn = true;

                            numStageStyles = __stageStyles.Length;
                            for (i = 0; i < numStageStyles; ++i)
                            {
                                stageStyles = __stageStyles[i];
                                if(stageStyles == null)
                                    continue;

                                foreach (var stageStyleTemp in stageStyles)
                                    stageStyleTemp.gameObject.SetActive(true);
                            }
                        }
                    }

                    if (endLevelIndex == userLevelIndex && numStageRewards < levelChapters.stageRewardCount)
                    {
                        if (__isLevelActive == null || __isLevelActive.Value)
                        {
                            __isLevelActive = false;
                            
                            _onLevelDisable?.Invoke(levelChapters.stageRewardCount.ToString());
                        }
                    }
                    else if (__isLevelActive == null || !__isLevelActive.Value)
                    {
                        __isLevelActive = true;
                        
                        _onLevelEnable?.Invoke();
                    }
                }
                /*else if (__selectedUserLevelID == selectedLevel.id)
                {
                    __selectedUserLevelID = 0;
                    __selectedStageIndex = -1;
                }*/
            });

            /*if (style.button != null)
            {
                style.button.onClick.RemoveListener(__ApplyStart);
                style.button.onClick.AddListener(__ApplyStart);
            }*/

            style.gameObject.SetActive(true);
            
            __levelStyles[index] = style;
        }

        var scrollRect = parent.GetComponentInParent<ZG.ScrollRectComponentEx>(true);
        if (scrollRect != null)
            scrollRect.MoveTo(selectedLevelIndex);

        if (isHot)
        {
            if (_onHotEnable != null)
                _onHotEnable.Invoke();
        }
        else if(_onHotDisable != null)
            _onHotDisable.Invoke();
        
        if(_onStageReward != null)
            _onStageReward?.Invoke(numStageRewards.ToString());
        
        if(_onStageRewardAll != null)
            _onStageRewardAll?.Invoke(numStageRewardsTotal.ToString());

        onChapterLoaded?.Invoke(levelChapters);
    }

    private void __ApplyLevel(IUserData.LevelStage levelStage)
    {
        __selectedUserLevelID = levelStage.levelID;
        __selectedStageIndex = levelStage.levelID == 0 ? -1 : levelStage.stage;
        
        int numRewards = levelStage.rewards == null ? 0 : levelStage.rewards.Length;
        if (numRewards > 0)
        {
            bool isReward = false;
            for (int i = 0; i < numRewards; ++i)
            {
                ref var reward = ref levelStage.rewards[i];

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
                    default:
                        isReward = true;
                        break;
                }
            }
            
            if(isReward)
                __sceneActiveDepth = Mathf.Max(__sceneActiveDepth + 1, 1);
        }

        if(onAwake != null)
            onAwake(levelStage);
    }

    private void __ApplyLevel(IUserData.LevelProperty property)
    {
        if (property.spawnerAttributes == null)
        {
            __isStart = false;

            return;
        }

        LevelPlayerShared.effectRage = 0;

        SpawnerShared.layerMaskAndTags = property.value.spawnerLayerMaskAndTags;

        LevelShared.spawnerAttributeScales.Clear();

        foreach (var spawnerAttribute in property.spawnerAttributes)
            LevelShared.spawnerAttributeScales.Add(spawnerAttribute);

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

        LevelPlayerShared.effectRage = property.cache.rage;

        SpawnerShared.layerMaskAndTags = property.value.spawnerLayerMaskAndTags;
        
        LevelShared.spawnerAttributeScales.Clear();

        foreach (var spawnerAttribute in property.spawnerAttributes)
            LevelShared.spawnerAttributeScales.Add(spawnerAttribute);
        
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

        float effectTargetHPScale = 0.0f, 
            effectTargetRecovery = 0.0f, 
            effectTargetDamageScale = 0.0f, 
            effectDamageScale = 0.0f;
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

        LevelPlayerShared.effectTargetHP = property.hpMax;
        LevelPlayerShared.effectTargetHPScale = effectTargetHPScale;
        LevelPlayerShared.effectTargetRecovery = effectTargetRecovery;
        LevelPlayerShared.effectTargetDamageScale = effectTargetDamageScale;
        LevelPlayerShared.effectDamageScale = effectDamageScale;
        
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

        if (userEnergy.value < userEnergy.max)
        {
            float energyValueFloat = (float)((double)(DateTime.UtcNow.Ticks - userEnergy.tick) /
                                             (TimeSpan.TicksPerMillisecond * userEnergy.unitTime));
            int energyValueInt = Mathf.FloorToInt(energyValueFloat);

            this.energy =
                Mathf.Clamp(userEnergy.value + energyValueInt, 0, userEnergy.max);

            __energyNextTime = (1.0f - (energyValueFloat - energyValueInt)) * __energyUnitTime;
        }
        else
        {
            this.energy = userEnergy.value;
            
            __energyNextTime = 0.0f;
        }

        InvokeRepeating(
            nameof(__IncreaseEnergy), 
            __energyNextTime,
            __energyUnitTime);
        
        (IAnalytics.instance as IAnalyticsEx)?.Login(user.id);
    }

    private void __IncreaseEnergy()
    {
        if(__energy < __energyMax)
            energy = __energy + 1;

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

    private IEnumerator __LoadScene(float time, string levelName, string sceneName)
    {
        yield return new WaitForSeconds(time);
        
        //GameMain.IncrementLevelTimes(levelName);
        //GameMain.IncrementSceneTimes(sceneName);
        
        var assetManager = GameAssetManager.instance;
        if (assetManager == null)
        {
            var gameObject = new GameObject("GameAssetManager");
            DontDestroyOnLoad(gameObject);
            assetManager = gameObject.AddComponent<GameAssetManager>();
        }

        assetManager.LoadScene(sceneName, null, new GameSceneActivation());
    }

    private IEnumerator __CollectAndQueryLevels()
    {
        var userData = IUserData.instance;
        yield return userData.CollectLevel(userID.Value, __ApplyLevel);
        yield return userData.QueryLevelChapters(userID.Value, __ApplyLevelChapters);
    }

    private IEnumerator __Start(
        bool isRestart, 
        uint userLevelID, 
        int stageIndex,
        string levelName, 
        string sceneName)
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
            yield return userData.ApplyLevel(userID, userLevelID, stageIndex, __ApplyLevel);
        else
            yield return userData.ApplyStage(userID, userLevelID, stageIndex, __ApplyStage);
            
        if (!__isStart)
        {
            __isStart = false;
            
            yield break;
        }

        _onStart.Invoke();
        
        var analytics = IAnalytics.instance as IAnalyticsEx;
        analytics?.StartLevel(sceneName/*_levels[__selectedLevelIndex].name*/);

        yield return __LoadScene(_startTime, levelName, sceneName);
    }
    
    /*private IEnumerator __Start(bool isRestart)
    {
        return __Start(isRestart, __selectedUserLevelID, __selectedStageIndex, __levelName, __sceneName);
    }*/

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

        if (__loaders != null)
        {
            foreach (var loader in __loaders)
                loader.Update();
        }
    }
}
