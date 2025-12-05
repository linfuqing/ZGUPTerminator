using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using ZG;

public struct LevelSkillData
{
    public string name;
    public string parentName;

    public int selectIndex;

    //public SkillAsset value;
}

public partial class LevelManager
{
    [Flags]
    private enum SkillSelectionStatus
    {
        Start = 0x01, 
        End = 0x02, 
        Complete = 0x04,  
    }

    [Serializable]
    internal struct SkillSelectionGuide
    {
        [Flags]
        public enum Flag
        {
            Force = 0x01, 
        }
        
        public string name;

        public Flag flag;

        public static int IndexOf(
            IReadOnlyList<SkillSelectionGuide> guides, 
            string name, 
            bool isRecommend)
        {
            SkillSelectionGuide guide;
            int numGuides = guides == null ? 0 : guides.Count;
            for(int i = 0; i < numGuides; ++i)
            {
                guide = guides[i];
                if (guide.name == name && ((guide.flag & Flag.Force) == Flag.Force || isRecommend))
                    return i;
            }

            return -1;
        }
    }
    
    [Serializable]
    internal struct SkillSelection
    {
        /*public enum Flag
        {
            DontPause = 0x01, 
        }*/
        
        public string name;

        //public Flag flag;

        public float startTime;
        public float destroyTime;

        //public float finishTime;
        public UnityEvent onFinish;
        
        public UnityEvent onEnable;
        public UnityEvent onDisable;

        public Button start;

        public ResultSkillStyle style;
    }
    
    [Serializable]
    internal struct SkillSelectionData
    {
        public string name;

        public float delayTime;
        public float destroyTime;
        
        public LevelSkillStyle style;
        
        public UnityEvent onEnable;
        public UnityEvent onDisable;
    }

    [SerializeField]
    internal IntEvent _onSkillSelectionGuide;

    [SerializeField]
    internal SkillSelectionGuide[] _skillSelectionGuides;

    [SerializeField] 
    internal SkillSelection[] _skillSelections;

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_skills")] 
    internal SkillSelectionData[] _skillSelectionDatas;

    private SkillSelectionStatus __skillSelectionStatus;
    private Action __onSkillSelectionComplete;

    private List<int> __selectedSkillIndices;

    private List<ResultSkillStyle> __resultSkillStyles;
    private Dictionary<string, Queue<Coroutine>> __skillSelectionCoroutines;
    
    private Dictionary<string, LevelSkillStyle> __skillStyles;

    public bool isClear => __gameObjectsToDestroy == null || __gameObjectsToDestroy.Count < 1;

    //public bool isSkillSelecting => __skillSelectionStatus != 0;
    
    public int selectedSkillSelectionIndex
    {
        get;

        private set;
    } = -1;

    public int[] CollectSelectedSkillIndices()
    {
        if ((__skillSelectionStatus & SkillSelectionStatus.End) != SkillSelectionStatus.End)
            return null;
        
        __skillSelectionStatus &= ~SkillSelectionStatus.End;

        int[] result;
        if(__selectedSkillIndices == null)
            result = Array.Empty<int>();
        else
        {
            result = __selectedSkillIndices.ToArray();
            
            __selectedSkillIndices.Clear();
        }

        return result;
    }

    public void SelectSkillBegin(int selectionIndex, float timeScale)
    {
        IAnalytics.instance?.SelectSkillBegin(selectionIndex);

        if (selectionIndex == -1)
            StartCoroutine(__StartSkillSelection(selectionIndex, 0.0f, timeScale, null));
        else
        {
            var selection = _skillSelections[selectionIndex];
            selection.onEnable.Invoke();

            //float timeScale = (selection.flag & SkillSelection.Flag.DontPause) == SkillSelection.Flag.DontPause ? 1.0f : 0.0f;

            if (selection.start == null)
                StartCoroutine(__StartSkillSelection(
                    selectionIndex,
                    selection.startTime, 
                    timeScale, 
                    null));
            else
            {
                //放在这里无害，最后会被清理掉
                TimeScale(timeScale);
                
                var onClick = selection.start.onClick;
                onClick.RemoveAllListeners();

                int step = 0;
                onClick.AddListener(() =>
                {
                    if (0 == ++step)
                        StartCoroutine(__StartSkillSelection(
                            selectionIndex, 
                            selection.startTime, 
                            timeScale, 
                            () => step > 1));
                });
            }
        }
    }

    public void SelectSkillEnd()
    {
        IAnalytics.instance?.SelectSkillEnd();

        __StartCoroutine(__FinishSkillSelection());
    }

    public void SelectSkills(int styleIndex, LevelSkillData[] skills)
    {
        IAnalytics.instance?.SelectSkills(styleIndex, skills);

        __StartCoroutine(__SelectSkills(styleIndex, skills));
    }

    private IEnumerator __SelectSkills(int styleIndex, LevelSkillData[] skills)
    {
        do
        {
            //等待队列
            yield return null;
        } while ((SkillSelectionStatus.Start & __skillSelectionStatus) == 0);
            
        bool result = false;
        int numSkills = skills.Length;
        if (styleIndex == -1)
        {
            int endIndex = numSkills - 1;
            LevelSkillData skill;
            for (int i = 0; i < numSkills; ++i)
            {
                skill = skills[i];
                if(skill.selectIndex == -1)
                    continue;
                
                yield return __SelectSkill(i == endIndex, 0.0f, skill, null);

                result = true;
            }
        }
        else
        {
            var destination = _skillSelectionDatas[styleIndex];
            if (destination.style == null)
            {
                int endIndex = numSkills - 1;
                LevelSkillData skill;
                for (int i = 0; i < numSkills; ++i)
                {
                    skill = skills[i];
                    if (skill.selectIndex == -1)
                        continue;

                    yield return __SelectSkill(i == endIndex, destination.destroyTime, skill, destination.name);

                    result = true;
                }
            }
            else
            {
                destination.onEnable.Invoke();

                bool isRecommend;
                int guideSkillIndex = -1, guideIndex = -1;
                SkillAsset asset;
                string[] keyNames, oldKeyNames;
                Sprite[] keyIcons;
                LevelSkillStyle style;
                LevelSkillData? skill = null;
                List<LevelSkillKeyStyle> uprankKeyStyles  = null;
                Dictionary<string, int> uprankKeyCounts = null;
                //List<int> recommendIndices = null;
                for (int i = 0; i < numSkills; ++i)
                {
                    var source = skills[i];
                    if(!SkillManager.TryGetAsset(source.name, out asset, out keyNames, out keyIcons))
                        continue;

                    if (destination.style.child == null || string.IsNullOrEmpty(source.parentName))
                    {
                        if (source.selectIndex == -1 && destination.style.child == null)
                            continue;

                        style = Instantiate(destination.style, destination.style.transform.parent);
                    }
                    else
                    {
                        style = __skillStyles[source.parentName];
                        style = Instantiate(style.child, style.child.transform.parent);
                    }

                    style.SetAsset(asset, keyIcons);

                    if (string.IsNullOrEmpty(source.parentName) || !SkillManager.TryGetAsset(source.parentName, out _, out oldKeyNames, out _))
                        oldKeyNames = null;

                    if (uprankKeyCounts == null)
                        uprankKeyCounts = style.uprankKeyStyle == null ? null : new Dictionary<string, int>();
                    else
                        uprankKeyCounts.Clear();
                    
                    isRecommend = __SetSkillKeyStyles(
                        style.keyStyles, 
                        keyNames, 
                        oldKeyNames, 
                        uprankKeyCounts == null ? null : uprankKeyCounts.Add);
                    if (destination.style.child == null || !string.IsNullOrEmpty(source.parentName))
                    {
                        if (!isRecommend)
                        {
                            keyNames = SkillManager.GetChildKeyNames(source.name);
                            if (keyNames != null)
                            {
                                int count;
                                SkillKeyAsset keyAsset;
                                foreach (var keyName in keyNames)
                                {
                                    if (oldKeyNames != null && Array.IndexOf(oldKeyNames, keyName) != -1)
                                        continue;
                                    
                                    if(!SkillManager.TryGetAsset(keyName, out keyAsset))
                                        continue;

                                    count = GetSkillActiveKeyCount(keyName);
                                    if(keyAsset.BinarySearch(count) < keyAsset.BinarySearch(count + GetSkillChildKeyCount(keyName)))
                                    {
                                        uprankKeyCounts.Add(keyName, count);
                                        
                                        isRecommend = true;

                                        //break;
                                    }
                                }
                            }
                        }

                        if (isRecommend && style.onRecommend != null)
                            style.onRecommend.Invoke();

                        if (style.uprankKeyStyle != null && uprankKeyCounts.Count > 0)
                        {
                            SkillKeyAsset keyAsset;
                            LevelSkillKeyStyle uprankKeyStyle;
                            var uprankKeyStyleParent = style.uprankKeyStyle.transform.parent;
                            foreach (var pair in uprankKeyCounts)
                            {
                                if(!SkillManager.TryGetAsset(pair.Key, out keyAsset))
                                    continue;
                                
                                uprankKeyStyle = Instantiate(style.uprankKeyStyle, uprankKeyStyleParent);
                                uprankKeyStyle.SetAsset(keyAsset, pair.Value);
                                uprankKeyStyle.gameObject.SetActive(true);
                                
                                if(uprankKeyStyles == null)
                                    uprankKeyStyles = new List<LevelSkillKeyStyle>();
                                
                                uprankKeyStyles.Add(uprankKeyStyle);
                            }
                        }
                    }

                    if ((__skillSelectionGuideNames == null || !__skillSelectionGuideNames.Contains(source.name)) &&
                        guideSkillIndex == -1)
                    {
                        guideIndex = SkillSelectionGuide.IndexOf(_skillSelectionGuides, source.name, isRecommend);
                        if(guideIndex != -1)
                            guideSkillIndex = i;
                    }

                    if (style.button != null && source.selectIndex != -1)
                    {
                        result = true;

                        style.button.onClick.RemoveAllListeners();
                        style.button.onClick.AddListener(() =>
                        {
                            if (__skillSelectionGuideNames == null)
                                __skillSelectionGuideNames = new HashSet<string>();

                            __skillSelectionGuideNames.Add(source.name);

                            skill = source;

                            __onSkillSelectionComplete = destination.onDisable == null ? null : destination.onDisable.Invoke;
                        });
                    }

                    if (__skillStyles == null)
                        __skillStyles = new Dictionary<string, LevelSkillStyle>();

                    __skillStyles.Add(source.name, style);

                    if (destination.delayTime > 0.0f)
                        yield return new WaitForSecondsRealtime(destination.delayTime);
                }

                if (guideSkillIndex != -1)
                {
                    var skillName = skills[guideSkillIndex].name;
                    style = __skillStyles[skillName];
                    if (style.onGuide != null)
                        style.onGuide.Invoke();

                    if (_onSkillSelectionGuide != null)
                        _onSkillSelectionGuide.Invoke(guideIndex);
                }

                if (result)
                {
                    while (skill == null)
                        yield return null;
                    
                    yield return __SelectSkill(true, destination.destroyTime, skill.Value, destination.name);

                    if (__onSkillSelectionComplete != null)
                    {
                        __onSkillSelectionComplete();

                        __onSkillSelectionComplete = null;
                    }
                    //destination.onDisable.Invoke();
                }

                if (uprankKeyStyles != null)
                {
                    foreach (var uprankKeyStyle in uprankKeyStyles)
                    {
                        if(uprankKeyStyle == null)
                            continue;
                        
                        if (__gameObjectsToDestroy == null)
                            __gameObjectsToDestroy = new List<GameObject>();

                        __gameObjectsToDestroy.Add(uprankKeyStyle.gameObject);
                    }
                    
                    uprankKeyStyles.Clear();
                }
            }
        }

        if (result)
        {
            __DestroyGameObjects();

            yield break;
        }
        
        //等待队列
        //yield return null;
        
        if (__skillStyles != null)
        {
            foreach (var skillStyle in __skillStyles.Values)
            {
                if(skillStyle == null)
                    continue;
                
                if (__gameObjectsToDestroy == null)
                    __gameObjectsToDestroy = new List<GameObject>();

                __gameObjectsToDestroy.Add(skillStyle.gameObject);
            }

            __skillStyles.Clear();
        }

        __DestroyGameObjects();

        __skillSelectionStatus |= SkillSelectionStatus.End;
        
        //if (selectedSkillSelectionIndex == -1)
        //    yield return __CompleteSkillSelection();
        //else if((SkillSelectionStatus.Finish & __skillSelectionStatus) == SkillSelectionStatus.Finish)
        //    yield return __FinishSkillSelection(_skillSelections[selectedSkillSelectionIndex]);
    }

    private IEnumerator __SelectSkill(bool isEnd, float destroyTime, LevelSkillData value, string selectionName)
    {
        if (__skillStyles != null)
        {
            foreach (var skillStyle in __skillStyles.Values)
            {
                if(skillStyle == null)
                    continue;
                
                skillStyle.onDestroy?.Invoke();
                
                if (__gameObjectsToDestroy == null)
                    __gameObjectsToDestroy = new List<GameObject>();

                __gameObjectsToDestroy.Add(skillStyle.gameObject);
            }

            __skillStyles.Clear();
        }

        if (__selectedSkillIndices == null)
            __selectedSkillIndices = new List<int>();

        __selectedSkillIndices.Add(value.selectIndex);

        if (isEnd)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(SkillSelectionStatus.End,
                __skillSelectionStatus & SkillSelectionStatus.End);
            
            __skillSelectionStatus |= SkillSelectionStatus.End;

            if (selectedSkillSelectionIndex == -1)
            {
                do
                {
                    yield return null;
                    
                }while((__skillSelectionStatus & SkillSelectionStatus.End) == SkillSelectionStatus.End);
                
                yield return null;

                yield return __WaitForSelectionCoroutines(selectionName);

                __CloseSkillSelectionRightNow();
            }
        }
        
        if(destroyTime > Mathf.Epsilon)
            yield return new WaitForSecondsRealtime(destroyTime);

        __DestroyGameObjects();

        yield return __WaitForSelectionCoroutines(selectionName);

        if (selectedSkillSelectionIndex != -1 && 
            SkillManager.TryGetAsset(value.name, out var asset, out var keyNames, out var keyIcons))
        {
            var selection = _skillSelections[selectedSkillSelectionIndex];
            var style = selection.style == null ? null : Instantiate(selection.style, selection.style.transform.parent);
            if (style != null)
            {
                style.SetAsset(asset, keyIcons);
                
                __SetSkillKeyStyles(style.keyStyles, keyNames, null);

                if (style.close == null)
                    Destroy(style.gameObject, selection.destroyTime);
                else
                {
                    var onClick = style.close.onClick;
                    onClick.RemoveAllListeners();
                    onClick.AddListener(__CloseSkillSelectionRightNow);

                    if (__resultSkillStyles == null)
                        __resultSkillStyles = new List<ResultSkillStyle>();

                    __resultSkillStyles.Add(style);
                }
            }

            //if((SkillSelectionStatus.Finish & __skillSelectionStatus) == SkillSelectionStatus.Finish)
            //    yield return __FinishSkillSelection(selection);
        }
    }

    private IEnumerator __WaitForSelectionCoroutines(string selectionName)
    {
        if (__skillSelectionCoroutines != null)
        {
            if(__skillSelectionCoroutines.TryGetValue(selectionName, out var skillSelectionCoroutines))
            {
                while (skillSelectionCoroutines.TryDequeue(out var skillSelectionCoroutine))
                    yield return skillSelectionCoroutine;
            }
            else
            {
                foreach (var coroutines in __skillSelectionCoroutines.Values)
                {
                    while (coroutines.TryDequeue(out var skillSelectionCoroutine))
                        yield return skillSelectionCoroutine;
                }
            }
        }
    }

    private IEnumerator __FinishSkillSelection(SkillSelection selection)
    {
        /*while ((SkillSelectionStatus.Finish & __skillSelectionStatus) == 0)
            yield return null;*/

        selection.onFinish.Invoke();
        
        //yield return new WaitForSecondsRealtime(selection.finishTime);

        if (__resultSkillStyles != null && __resultSkillStyles.Count > 0)
        {
            foreach (var resultSkillStyle in __resultSkillStyles)
                resultSkillStyle.onFinish.Invoke();
            
            while ((SkillSelectionStatus.Complete & __skillSelectionStatus) != SkillSelectionStatus.Complete)
                yield return null;
        }
        
        //UnityEngine.Assertions.Assert.IsTrue((SkillSelectionStatus.Complete & __skillSelectionStatus) == SkillSelectionStatus.Complete);
        //UnityEngine.Assertions.Assert.AreNotEqual(-1, selectedSkillSelectionIndex);

        //var selection = _skillSelections[selectedSkillSelectionIndex];
        selection.onDisable.Invoke();
        
        yield return __CompleteSkillSelection();

        UnityEngine.Assertions.Assert.AreEqual((SkillSelectionStatus)0, __skillSelectionStatus);
        
        selectedSkillSelectionIndex = -1;
        //__skillSelectionStatus = 0;
        
        //ClearTimeScales();

        if (__resultSkillStyles != null)
        {
            foreach (var resultSkillStyle in __resultSkillStyles)
            {
                if(resultSkillStyle == null)
                    continue;
                
                if(__gameObjectsToDestroy == null)
                    __gameObjectsToDestroy = new List<GameObject>();
                
                __gameObjectsToDestroy.Add(resultSkillStyle.gameObject);
            }
            
            __resultSkillStyles.Clear();
        }
        
        yield return new WaitForSecondsRealtime(selection.destroyTime);

        __DestroyGameObjects();
    }

    private IEnumerator __StartSkillSelection(int selectionIndex, float time, float timeScale, Func<bool> skip)
    {
        if (time > 0.0f)
        {
            if(skip == null)
                yield return new WaitForSecondsRealtime(time);
            else
            {
                float startTime = Time.unscaledTime;
                while(Time.unscaledTime - startTime < time && !skip())
                    yield return null;
            }
        }

        //不行
        //if (__coroutine != null)
        //    yield return __coroutine;
        while (0 != __skillSelectionStatus || selectedSkillSelectionIndex != -1)
            yield return null;

        UnityEngine.Assertions.Assert.AreEqual(-1, selectedSkillSelectionIndex);
        UnityEngine.Assertions.Assert.AreEqual(0, (int)__skillSelectionStatus);

        selectedSkillSelectionIndex = selectionIndex;

        __skillSelectionStatus |= SkillSelectionStatus.Start;
        
        TimeScale(timeScale);

        while ((__skillSelectionStatus & SkillSelectionStatus.Complete) != SkillSelectionStatus.Complete)
            yield return null;
        
        __ClearTimeScales();
    }

    private IEnumerator __FinishSkillSelection()
    {
        //等待队列
        do
        {
            yield return null;
        }
        while ((__skillSelectionStatus & SkillSelectionStatus.Start) != SkillSelectionStatus.Start);

        if (selectedSkillSelectionIndex == -1)
            yield return __CompleteSkillSelection();
        else
            yield return __FinishSkillSelection(_skillSelections[selectedSkillSelectionIndex]);
    }

    private IEnumerator __CompleteSkillSelection()
    {
        //进入队列
        yield return null;
        
        UnityEngine.Assertions.Assert.AreEqual((SkillSelectionStatus)0, (SkillSelectionStatus.End & __skillSelectionStatus));

        __skillSelectionStatus = 0;
    }

    private void __CloseSkillSelectionRightNow()
    {
        __skillSelectionStatus |= SkillSelectionStatus.Complete;
        
        if (__onSkillSelectionComplete != null)
        {
            __onSkillSelectionComplete();

            __onSkillSelectionComplete = null;
        }
    }

    private void __DestroyGameObjects()
    {
        if (__gameObjectsToDestroy != null)
        {
            foreach (var gameObjectToDestroy in __gameObjectsToDestroy)
                Destroy(gameObjectToDestroy);
            
            __gameObjectsToDestroy.Clear();
        }
    }

    private bool __SetSkillKeyStyles(
        IReadOnlyList<SkillKeyStyle> styles, 
        string[] names, 
        string[] oldNames, 
        Action<string, int> uprankKeyCounts = null)
    {
        //int maxCount = 0, count;
        bool result = false;
        int count, rank;
        string name;
        LevelSkillKeyStyle style; 
        SkillKeyAsset asset;
        int numStyles = styles == null ? 0 : styles.Count, numNames = names == null ? 0 : names.Length;
        for (int i = 0; i < numNames; ++i)
        {
            name = names[i];
            /*count = GetSkillActiveKeyCount(name);
            count = (oldNames != null && Array.IndexOf(oldNames, name) != -1 ? 0 : 1)

            maxCount = Mathf.Max(maxCount, count + (oldNames != null && Array.IndexOf(oldNames, name) != -1 ? 0 : 1));*/

            if(!SkillManager.TryGetAsset(name, out asset))
                continue;

            count = GetSkillActiveKeyCount(name);
            style = i < numStyles ? styles[i] as LevelSkillKeyStyle : null;
            if (style == null)
            {
                if(result && uprankKeyCounts == null)
                    continue;

                rank = asset.BinarySearch(count);
            }
            else
            {
                rank = style.SetAsset(asset, count);
                
                if(result && uprankKeyCounts == null)
                    continue;
            }

            if ((oldNames == null || Array.IndexOf(oldNames, name) == -1) &&
                asset.BinarySearch(count + GetSkillChildKeyCount(name)) > rank)
            {
                if (uprankKeyCounts != null)
                    uprankKeyCounts(name, count);
                
                result = true;
            }
            
            //style.gameObject.SetActive(true);
        }

        return result;
    }

    void OnDestroy()
    {
        if (__skillStyles != null)
        {
            foreach (var skillStyle in __skillStyles.Values)
                Destroy(skillStyle.gameObject);

            __skillStyles.Clear();
        }
        
        if (__resultSkillStyles != null)
        {
            foreach (var resultSkillStyle in __resultSkillStyles)
                Destroy(resultSkillStyle.gameObject);
            
            __resultSkillStyles.Clear();
        }
    }

}
