using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public struct LevelSkillData
{
    public string parentName;

    public int selectIndex;

    public SkillAsset value;
}

public partial class LevelManager
{
    [Flags]
    private enum SkillSelectionStatus
    {
        Start = 0x01, 
        Finish = 0x02, 
        Complete = Start | Finish
    }
    
    [Serializable]
    internal struct SkillSelection
    {
        public string name;

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
    internal struct Skill
    {
        public string name;

        public float delayTime;
        public float destroyTime;
        
        public LevelSkillStyle style;
        
        public UnityEvent onEnable;
        public UnityEvent onDisable;
    }

    [SerializeField] 
    internal SkillSelection[] _skillSelections;

    [SerializeField] 
    internal Skill[] _skills;

    private SkillSelectionStatus __skillSelectionStatus;

    private List<int> __selectedSkillIndices;

    private List<ResultSkillStyle> __resultSkillStyles;
    private Dictionary<string, LevelSkillStyle> __skillStyles;

    public bool isClear => __gameObjectsToDestroy == null || __gameObjectsToDestroy.Count < 1;

    public int selectedSkillSelectionIndex
    {
        get;

        private set;
    } = -1;

    public int[] selectedSkillIndices => __selectedSkillIndices == null ? null : __selectedSkillIndices.ToArray();

    public void SelectSkillBegin(int selectionIndex)
    {
        UnityEngine.Assertions.Assert.AreEqual(-1, selectedSkillSelectionIndex);
        UnityEngine.Assertions.Assert.AreEqual(0, (int)__skillSelectionStatus);

        selectedSkillSelectionIndex = selectionIndex;

        if (selectionIndex == -1)
            __skillSelectionStatus |= SkillSelectionStatus.Start;
        else
        {
            var selection = _skillSelections[selectionIndex];
            selection.onEnable.Invoke();

            if (selection.start == null)
                __skillSelectionStatus |= SkillSelectionStatus.Start;
            else
            {
                var onClick = selection.start.onClick;
                onClick.RemoveAllListeners();
                onClick.AddListener(() => StartCoroutine(__Start(selection.startTime)));
            }
        }

        Time.timeScale = 0.0f;
    }

    public void SelectSkillEnd()
    {
        //UnityEngine.Assertions.Assert.AreNotEqual(-1, __skillSelectionIndex);
        __skillSelectionStatus |= SkillSelectionStatus.Finish;

        if (selectedSkillSelectionIndex != -1 && 
            __selectedSkillIndices != null && __selectedSkillIndices.Count > 0 && 
            (__resultSkillStyles == null || __resultSkillStyles.Count < 1))
            StartCoroutine(__FinishSkillSelection(_skillSelections[selectedSkillSelectionIndex]));
    }

    public void SelectSkills(int styleIndex, LevelSkillData[] skills)
    {
        if(__selectedSkillIndices != null)
            __selectedSkillIndices.Clear();

        StartCoroutine(__SelectSkills(styleIndex, skills));
    }

    private IEnumerator __SelectSkills(int styleIndex, LevelSkillData[] skills)
    {
        while ((SkillSelectionStatus.Start & __skillSelectionStatus) == 0)
            yield return null;
        
        int numSkills = skills.Length;
        var destination = _skills[styleIndex];
        if (destination.style == null)
        {
            LevelSkillData skill;
            for (int i = 0; i < numSkills; ++i)
            {
                skill = skills[i];
                if(skill.selectIndex == -1)
                    continue;
                
                if (destination.delayTime > 0.0f)
                    yield return new WaitForSecondsRealtime(destination.delayTime);
                
                yield return __SelectSkill(destination.destroyTime, skill);
            }

            yield break;
        }
        
        destination.onEnable.Invoke();

        LevelSkillStyle style;
        for (int i = 0; i < numSkills; ++i)
        {
            var source = skills[i];
            if (destination.style.child == null || string.IsNullOrEmpty(source.parentName))
            {
                if(source.selectIndex == -1 && destination.style.child == null)
                    continue;
                
                style = Instantiate(destination.style, destination.style.transform.parent);
            }
            else
            {
                style = __skillStyles[source.parentName];
                style = Instantiate(style.child, style.child.transform.parent);
            }
            
            if (style.button != null && source.selectIndex != -1)
            {
                style.button.onClick.AddListener(() =>
                {
                    destination.onDisable.Invoke();

                    StartCoroutine(__SelectSkill(destination.destroyTime, source));
                });
            }

            style.SetAsset(source.value);
            
            if (__skillStyles == null)
                __skillStyles = new Dictionary<string, LevelSkillStyle>();

            __skillStyles.Add(source.value.name, style);

            if (destination.delayTime > 0.0f)
                yield return new WaitForSecondsRealtime(destination.delayTime);
        }
    }

    private IEnumerator __SelectSkill(float destroyTime, LevelSkillData value)
    {
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

        if (selectedSkillSelectionIndex == -1)
        {
            if (SkillSelectionStatus.Complete == __skillSelectionStatus)
            {
                __skillSelectionStatus = 0;
            
                Time.timeScale = 1.0f;
            }
        }
        
        yield return new WaitForSecondsRealtime(destroyTime);

        __DestroyGameObjects();

        if (selectedSkillSelectionIndex == -1)
        {
            if (SkillSelectionStatus.Complete == __skillSelectionStatus)
            {
                __skillSelectionStatus = 0;
                
                Time.timeScale = 1.0f;
            }
        }
        else
        {
            var selection = _skillSelections[selectedSkillSelectionIndex];
            var style = selection.style == null ? null : Instantiate(selection.style, selection.style.transform.parent);
            if (style != null)
            {
                style.SetAsset(value.value);

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

            if ((SkillSelectionStatus.Finish & __skillSelectionStatus) != 0)
                yield return __FinishSkillSelection(selection);
        }
        
        if (__selectedSkillIndices == null)
            __selectedSkillIndices = new List<int>();

        __selectedSkillIndices.Add(value.selectIndex);
    }

    private IEnumerator __FinishSkillSelection(SkillSelection selection)
    {
        selection.onFinish.Invoke();
        
        //yield return new WaitForSecondsRealtime(selection.finishTime);

        if (__resultSkillStyles != null && __resultSkillStyles.Count > 0)
        {
            foreach (var resultSkillStyle in __resultSkillStyles)
                resultSkillStyle.onFinish.Invoke();

            while (-1 != selectedSkillSelectionIndex)
                yield return null;
        }
        else
            yield return __CloseSkillSelection();
    }

    private IEnumerator __CloseSkillSelection()
    {
        UnityEngine.Assertions.Assert.AreEqual(SkillSelectionStatus.Complete, __skillSelectionStatus);
        UnityEngine.Assertions.Assert.AreNotEqual(-1, selectedSkillSelectionIndex);

        var selection = _skillSelections[selectedSkillSelectionIndex];
        selection.onDisable.Invoke();
        
        selectedSkillSelectionIndex = -1;
        __skillSelectionStatus = 0;
        
        Time.timeScale = 1.0f;

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
        
        yield return new WaitForSeconds(selection.destroyTime);

        __DestroyGameObjects();
    }

    private void __CloseSkillSelectionRightNow()
    {
        if(SkillSelectionStatus.Complete == __skillSelectionStatus)
            StartCoroutine(__CloseSkillSelection());
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

    private IEnumerator __Start(float time)
    {
        yield return new WaitForSecondsRealtime(time);
        
        __skillSelectionStatus |= SkillSelectionStatus.Start;
    }
    
    protected void OnDestroy()
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
