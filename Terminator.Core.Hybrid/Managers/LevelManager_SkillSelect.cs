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
    internal string[] _skillSelectionGuides;

    [SerializeField] 
    internal SkillSelection[] _skillSelections;

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_skills")] 
    internal SkillSelectionData[] _skillSelectionDatas;

    private SkillSelectionStatus __skillSelectionStatus;
    private Action __onSkillSelectionComplete;

    private List<int> __selectedSkillIndices;

    private List<ResultSkillStyle> __resultSkillStyles;
    private Queue<Coroutine> __skillSelectionCoroutines;
    
    private Dictionary<string, LevelSkillStyle> __skillStyles;

    public bool isClear => __gameObjectsToDestroy == null || __gameObjectsToDestroy.Count < 1;
    
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
            StartCoroutine(__StartSkillSelection(selectionIndex, 0.0f, timeScale));
        else
        {
            var selection = _skillSelections[selectionIndex];
            selection.onEnable.Invoke();

            //float timeScale = (selection.flag & SkillSelection.Flag.DontPause) == SkillSelection.Flag.DontPause ? 1.0f : 0.0f;

            if (selection.start == null)
                StartCoroutine(__StartSkillSelection(
                    selectionIndex,
                    selection.startTime, 
                    timeScale));
            else
            {
                //放在这里无害，最后会被清理掉
                TimeScale(timeScale);
                
                var onClick = selection.start.onClick;
                onClick.RemoveAllListeners();
                onClick.AddListener(() => StartCoroutine(__StartSkillSelection(selectionIndex, selection.startTime, timeScale)));
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
        while ((SkillSelectionStatus.Start & __skillSelectionStatus) == 0)
            yield return null;

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
                
                yield return __SelectSkill(i == endIndex, 0.0f, skill);

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

                    yield return __SelectSkill(i == endIndex, destination.destroyTime, skill);

                    result = true;
                }
            }
            else
            {
                destination.onEnable.Invoke();

                int //index = 0,
                    //guidePriority = 0,
                    guideIndex = -1,
                    recommendIndex = -1,
                    recommendCount = 0,
                    recommendKeyCount = Mathf.Max(maxSkillActiveKeyCount, destination.style.child == null ? 0 : 1), //destination.style.child == null ? 0 : 1,
                    skillKeyCount = 0, 
                    keyCount;
                SkillAsset asset;
                string[] keyNames, oldKeyNames;
                Sprite[] keyIcons;
                LevelSkillStyle style;
                LevelSkillData? skill = null;
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

                    if ((__skillSelectionGuideNames == null || !__skillSelectionGuideNames.Contains(source.name)) &&
                        (guideIndex == -1/* || guidePriority < asset.priority*/) &&
                        _skillSelectionGuides != null &&
                        Array.IndexOf(_skillSelectionGuides, source.name) != -1)
                    {
                        //guidePriority = asset.priority;
                        guideIndex = i;
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

                    style.SetAsset(asset, keyIcons);

                    if (string.IsNullOrEmpty(source.parentName) || !SkillManager.TryGetAsset(source.parentName, out _, out oldKeyNames, out _))
                        oldKeyNames = null;
                    
                    keyCount = __SetSkillKeyStyles(style.keyStyles, keyNames, oldKeyNames);
                    if (destination.style.child == null || !string.IsNullOrEmpty(source.parentName))
                    {
                        keyNames = SkillManager.GetChildKeyNames(source.name);
                        if (keyNames != null)
                        {
                            foreach (var keyName in keyNames)
                            {
                                if(oldKeyNames != null && Array.IndexOf(oldKeyNames, keyName) != -1)
                                    continue;
                                
                                keyCount = Mathf.Max(keyCount, GetSkillActiveKeyCount(keyName) + 1);
                            }
                        }

                        if (keyCount > recommendKeyCount)
                        {
                            recommendKeyCount = keyCount;
                            recommendIndex = i;

                            recommendCount = 1;
                        }
                        else if (keyCount == recommendKeyCount)
                            ++recommendCount;

                        ++skillKeyCount;
                    }
                    
                    /*else if (keyCount == recommendKeyCount)
                        recommendIndex = -1;*/

                    if (__skillStyles == null)
                        __skillStyles = new Dictionary<string, LevelSkillStyle>();

                    __skillStyles.Add(source.name, style);

                    if (destination.delayTime > 0.0f)
                        yield return new WaitForSecondsRealtime(destination.delayTime);
                }

                if (recommendIndex != -1 && recommendCount == skillKeyCount)
                {
                    var skillName = skills[recommendIndex].name;
                    style = __skillStyles[skillName];
                    if (style.onRecommend != null)
                        style.onRecommend.Invoke();
                }

                if (guideIndex != -1)
                {
                    var skillName = skills[guideIndex].name;
                    style = __skillStyles[skillName];
                    if (style.onGuide != null)
                        style.onGuide.Invoke();

                    if (_onSkillSelectionGuide != null)
                        _onSkillSelectionGuide.Invoke(Array.IndexOf(_skillSelectionGuides, skillName));
                }

                if (result)
                {
                    while (skill == null)
                        yield return null;
                    
                    yield return __SelectSkill(true, destination.destroyTime, skill.Value);

                    if (__onSkillSelectionComplete != null)
                    {
                        __onSkillSelectionComplete();

                        __onSkillSelectionComplete = null;
                    }
                    //destination.onDisable.Invoke();
                }
            }
        }

        if (result)
            yield break;
        
        //等待队列
        yield return null;
        
        if (__skillStyles != null)
        {
            foreach (var skillStyle in __skillStyles.Values)
            {
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

    private IEnumerator __SelectSkill(bool isEnd, float destroyTime, LevelSkillData value)
    {
        if (__skillStyles != null)
        {
            foreach (var skillStyle in __skillStyles.Values)
            {
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
                
                while (__skillSelectionCoroutines != null &&
                       __skillSelectionCoroutines.TryDequeue(out var coroutine))
                    yield return coroutine;

                __CloseSkillSelectionRightNow();
            }
        }
        
        if(destroyTime > Mathf.Epsilon)
            yield return new WaitForSecondsRealtime(destroyTime);

        __DestroyGameObjects();

        while (__skillSelectionCoroutines != null && __skillSelectionCoroutines.TryDequeue(out var coroutine))
            yield return coroutine;
        
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
                if(__gameObjectsToDestroy == null)
                    __gameObjectsToDestroy = new List<GameObject>();
                
                __gameObjectsToDestroy.Add(resultSkillStyle.gameObject);
            }
            
            __resultSkillStyles.Clear();
        }
        
        yield return new WaitForSecondsRealtime(selection.destroyTime);

        __DestroyGameObjects();
    }

    private IEnumerator __StartSkillSelection(int selectionIndex, float time, float timeScale)
    {
        if(time > 0.0f)
            yield return new WaitForSecondsRealtime(time);

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
        
        ClearTimeScales();
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

    private int __SetSkillKeyStyles(
        IReadOnlyList<SkillKeyStyle> styles, 
        string[] names, 
        string[] oldNames)
    {
        int maxCount = 0, count;
        string name;
        LevelSkillKeyStyle style; 
        SkillKeyAsset asset;
        int numStyles = styles == null ? 0 : styles.Count, numNames = names == null ? 0 : names.Length;
        for (int i = 0; i < numNames; ++i)
        {
            name = names[i];
            count = GetSkillActiveKeyCount(name);

            maxCount = Mathf.Max(maxCount, count + (oldNames != null && Array.IndexOf(oldNames, name) != -1 ? 0 : 1));

            style = i < numStyles ? styles[i] as LevelSkillKeyStyle : null;
            if (style == null)
                continue;
            
            if(!SkillManager.TryGetAsset(name, out asset))
                continue;

            style.SetAsset(asset, count);
            
            //style.gameObject.SetActive(true);
        }

        return maxCount;
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
