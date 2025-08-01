using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using ZG;

public partial class LevelManager
{
    private struct SkillActive
    {
        private List<ActiveSkillStyle[]> __styles;
        
        public SkillActive(
            int siblingIndex, 
            SkillActiveData[] instances)
        {
            int numStyles = instances == null ? 0 : instances.Length;
            ActiveSkillStyle style;
            var styles = new ActiveSkillStyle[numStyles];
            for (int i = 0; i < numStyles; ++i)
            {
                style = instances[i].style;
                if(style == null)
                    continue;

                style = Instantiate(style, style.transform.parent);
                
                style.transform.SetSiblingIndex(siblingIndex);
                
                styles[i] = style;
            }

            __styles = new List<ActiveSkillStyle[]>();
            __styles.Add(styles);
        }

        public bool Dispose(int level)
        {
            var styles = __styles[level];
            
            foreach (var style in styles)
            {
                if (style == null)
                    continue;
                
                Destroy(style.gameObject);
            }

            __styles[level] = null;

            foreach (var temp in __styles)
            {
                if (temp != null)
                    return false;
            }

            return true;
        }

        public void Reset(int level, in SkillAsset asset, Sprite[] keyIcons)
        {
            while(__styles.Count < level)
                __styles.Add(null);

            int numStyles;
            ActiveSkillStyle[] styles;
            if (__styles.Count == level)
            {
                ActiveSkillStyle style;
                styles = (ActiveSkillStyle[])__styles[0].Clone();
                numStyles = styles.Length;
                for(int i = 0; i < numStyles; ++i)
                {
                    style = styles[i];
                    style = style == null ? null : style.GetChild(level);
                    style = style == null ? null : Instantiate(style, style.transform.parent);
                    styles[i] = style;
                }
                
                __styles.Add(styles);
            }

            styles = __styles[level];
            foreach (var style in styles)
            {
                if(style == null)
                    continue;
                
                style.SetAsset(asset, keyIcons);
            }
        }
        
        public bool Contains(int level)
        {
            return __styles.Count > level;
        }

        public ActiveSkillStyle[] Get(int level)
        {
            return __styles[level];
        }

        public void Set(int level, float cooldown, float elapsedTime)
        {
            if (__styles != null && __styles.Count > level)
            {
                var styles = __styles[level];
                if (styles == null || styles.Length < 1)
                    return;
                
                float value = cooldown > Mathf.Epsilon ? elapsedTime / cooldown : 1.0f;
                foreach (var style in styles)
                {
                    if(style == null || style.cooldown == null)
                        continue;
                
                    style.cooldown.value = value;
                }
            }
        }
    }

    [Serializable]
    internal struct SkillActiveData
    {
        public string name;

        public ActiveSkillStyle style;

        public LevelSkillKeyStyle keyStyle;
        public ResultSkillKeyStyle resultKeyStyle;
        
        public float resultKeyStyleDestroyTime;

        public UnityEvent onKeyEnable;
        public UnityEvent onKeyDisable;
    }

    private struct SkillActiveKeyData
    {
        public int count;
        public int rank;
        public LevelSkillKeyStyle[] styles;
    }
    
    [SerializeField] 
    internal SkillActiveData[] _skillActiveDatas;

    private Pool<SkillActive> __skillActives;

    private Dictionary<string, SkillActiveKeyData> __skillActiveKeys;

    public int GetSkillActiveKeyCount(string keyName)
    {
        return __skillActiveKeys != null && __skillActiveKeys.TryGetValue(keyName, out var skillActiveKey)
            ? skillActiveKey.count
            : 0;
    }

    public bool HasActiveSkill(int index, int level)
    {
        return __skillActives != null && 
               __skillActives.TryGetValue(index, out var activeSkill) &&
               activeSkill.Contains(level);
    }

    public ReadOnlySpan<ActiveSkillStyle> GetActiveSkill(int index, int level)
    {
        return __skillActives[index].Get(level);
    }

    public bool UnsetActiveSkill(int index, int level, in FixedString128Bytes name)
    {
        if (!__skillActiveNames.Remove((index, level)))
        {
            Debug.LogError($"Skill {name} has not been active in the level {level} of {index}");

            return false;
        }

        if (__skillActives != null && __skillActives.TryGetValue(index, out var origin) && origin.Dispose(level) &&
            __skillActives.RemoveAt(index))
        {
            if(SkillManager.TryGetAsset(name, out _, out var keys, out _) && keys != null)
            {
                foreach (var key in keys)
                    __RemoveActiveSkillKey(key);
            }

            return true;
        }

        return false;
    }

    public void SetActiveSkill(int index, int level, in FixedString128Bytes name)
    {
        if (__skillActiveNames == null)
            __skillActiveNames = new Dictionary<(int, int), FixedString128Bytes>();

        if ((!__skillActiveNames.TryGetValue((index, level), out var oldName) || oldName != name) &&
            SkillManager.TryGetAsset(name, out var asset, out var keys, out var keyIcons))
        {
            __skillActiveNames[(index, level)] = name;

            IAnalytics.instance?.SetActiveSkill(asset.name);

            if (__skillActives == null)
                __skillActives = new Pool<SkillActive>();

            if (!__skillActives.TryGetValue(index, out var origin))
            {
                origin = new SkillActive(index, _skillActiveDatas);

                __skillActives.Insert(index, origin);
            }

            origin.Reset(level, asset, keyIcons);

            int numKeys = keys == null ? 0 : keys.Length;
            if (!oldName.IsEmpty && SkillManager.TryGetAsset(oldName, out _, out var oldKeys, out _) && oldKeys != null)
            {
                keys = keys?.Clone() as string[];

                int keyIndex;
                foreach (var key in oldKeys)
                {
                    keyIndex = keys == null ? -1 : Array.IndexOf(keys, key);
                    if (keyIndex != -1)
                    {
                        keys[keyIndex] = keys[--numKeys];
                        
                        continue;
                    }
                    
                    __RemoveActiveSkillKey(key);
                }
            }
            
            if (numKeys > 0)
            {
                for(int i = 0; i < numKeys; ++i)
                    __AddActiveSkillKey(keys[i]);
            }
        }
    }

    public void SetActiveSkill(int index, int level, float cooldown, float elapsedTime)
    {
        if (__skillActives == null || !__skillActives.TryGetValue(index, out var value))
            return;
        
        value.Set(level, cooldown, elapsedTime);
    }

    private bool __RemoveActiveSkillKey(string name)
    {
        if (__skillActiveKeys == null || !__skillActiveKeys.TryGetValue(name, out var result))
            return false;

        bool isDisable = false;
        if (result.count > 1)
        {
            --result.count;

            if (result.styles != null && SkillManager.TryGetAsset(name, out SkillKeyAsset asset))
            {
                result.rank = asset.BinarySearch(result.count);
                if (result.rank < 0)
                {
                    isDisable = true;
                    
                    foreach (var style in result.styles)
                    {
                        if(style == null)
                            continue;

                        Destroy(style.gameObject);
                    }
                }
                else
                {
                    foreach (var style in result.styles)
                    {
                        if (style == null)
                            continue;

                        style.SetAsset(asset, result.count);
                    }
                }
            }

            __skillActiveKeys[name] = result;
        }
        else
            __skillActiveKeys.Remove(name);

        if (isDisable)
        {
            foreach (var value in __skillActiveKeys.Values)
            {
                if (value.rank >= 0)
                {
                    isDisable = false;

                    break;
                }
            }

            if (isDisable)
            {
                foreach (var skillActiveData in _skillActiveDatas)
                    skillActiveData.onKeyDisable?.Invoke();
            }
        }

        return true;
    }
    
    
    private void __AddActiveSkillKey(string name)
    {
        if (__skillActiveKeys == null)
            __skillActiveKeys = new Dictionary<string, SkillActiveKeyData>();

        int numStyles = _skillActiveDatas.Length;
        if (!__skillActiveKeys.TryGetValue(name, out var result))
        {
            result.count = 0;

            result.styles = null;
        }

        ++result.count;
        int rank = result.rank;

        if (SkillManager.TryGetAsset(name, out SkillKeyAsset asset))
        {
            result.rank = asset.BinarySearch(result.count);
            if (result.rank != rank && result.rank >= 0)
            {
                bool isEnable = false;
                if (result.styles == null)
                {
                    isEnable = true;
                    foreach (var value in __skillActiveKeys.Values)
                    {
                        if (value.rank >= 0)
                        {
                            isEnable = false;
                            
                            break;
                        }
                    }

                    result.styles = new LevelSkillKeyStyle[numStyles];
                    for (int i = 0; i < numStyles; ++i)
                    {
                        ref var skillActiveData = ref _skillActiveDatas[i];
                
                        result.styles[i] = skillActiveData.keyStyle == null ? null : 
                            Instantiate(skillActiveData.keyStyle, skillActiveData.keyStyle.transform.parent);
                    }
                }
                
                Coroutine coroutine;
                for (int i = 0; i < numStyles; ++i)
                {
                    ref var skillActiveData = ref _skillActiveDatas[i];

                    coroutine = StartCoroutine(__ReturnResultKey(
                        isEnable ? skillActiveData.onKeyEnable : null, 
                        result.styles[i], 
                        skillActiveData.resultKeyStyle, 
                        asset, 
                        result.count,
                        skillActiveData.resultKeyStyleDestroyTime));
                    
                    if (__skillSelectionCoroutines == null)
                        __skillSelectionCoroutines = new Queue<Coroutine>();
        
                    __skillSelectionCoroutines.Enqueue(coroutine);
                }
            }

        }

        __skillActiveKeys[name] = result;
    }

    private IEnumerator __ReturnResultKey(
        UnityEvent onEnable, 
        LevelSkillKeyStyle style,
        ResultSkillKeyStyle resultStyle, 
        SkillKeyAsset asset, 
        int count, 
        float destroyTime)
    {
        yield return null;

        if (resultStyle != null)
        {
            resultStyle = Instantiate(resultStyle, resultStyle.transform.parent);
            resultStyle.SetAsset(asset, count, false);

            bool isConform = resultStyle.button == null;
            if (!isConform)
            {
                var onClick = resultStyle.button.onClick;
                onClick.RemoveAllListeners();
                onClick.AddListener(() =>
                {
                    isConform = true;
                });
            }

            var gameObject = resultStyle.gameObject;
            gameObject.SetActive(true);

            while (!isConform)
                yield return null;

            yield return new WaitForSecondsRealtime(destroyTime);

            Destroy(gameObject);
        }

        if(style != null)
        {
            style.gameObject.SetActive(true);
            style.SetAsset(asset, count);
        }
        
        onEnable?.Invoke();
    }
}
