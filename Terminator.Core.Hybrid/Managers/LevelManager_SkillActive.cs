using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using ZG;

public partial class LevelManager
{
    private struct ActiveSkill
    {
        //private ActiveSkillStyle[] __styles;
        private List<ActiveSkillStyle[]> __styles;

        public ActiveSkill(
            int siblingIndex, 
            ActiveSkillStyle[] styles)
        {
            int numStyles = styles == null ? 0 : styles.Length;
            ActiveSkillStyle style;
            var results = new ActiveSkillStyle[numStyles];
            for (int i = 0; i < numStyles; ++i)
            {
                style = styles[i];
                if(style == null)
                    continue;

                style = Instantiate(style, style.transform.parent);
                
                style.transform.SetSiblingIndex(siblingIndex);
                
                results[i] = style;
            }

            __styles = new List<ActiveSkillStyle[]>();
            __styles.Add(results);
        }

        public bool Contains(int level)
        {
            return __styles.Count > level;
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

        public void Reset(int level, in SkillAsset asset)
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
                    style = style.GetChild(level);
                    if(style == null)
                        continue;
                    
                    style = Instantiate(style, style.transform.parent);
                    styles[i] = style;
                }
                
                __styles.Add(styles);
            }

            styles = __styles[level];
            foreach (var style in styles)
            {
                if(style == null)
                    continue;
                
                style.SetAsset(asset);
            }

            /*ActiveSkillStyle style;
            int numStyles = __styles == null ? 0 : __styles.Length;
            for (int i = 0; i < numStyles; ++i)
            {
                style = __styles[i];
                if(style == null)
                    continue;

                style.SetAsset(asset);
            }*/
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
                    if(style.cooldown == null)
                        continue;
                
                    style.cooldown.value = value;
                }
            }
        }
    }

    [SerializeField] 
    internal ActiveSkillStyle[] _activeSkillStyles;

    private Pool<ActiveSkill> __activeSkills;

    public bool HasActiveSkill(int index, int level)
    {
        return __activeSkills != null && 
               __activeSkills.TryGetValue(index, out var activeSkill) &&
               activeSkill.Contains(level);
    }
    
    public void SetActiveSkill(int index, int level, in SkillAsset? value)
    {
        if (value == null)
        {
            if (__activeSkills != null && __activeSkills.TryGetValue(index, out var origin) && origin.Dispose(level))
                __activeSkills.RemoveAt(index);
        }
        else
        {
            if (__activeSkills == null)
                __activeSkills = new Pool<ActiveSkill>();
            
            if(!__activeSkills.TryGetValue(index, out var origin))
            {
                origin = new ActiveSkill(index, _activeSkillStyles);
                
                __activeSkills.Insert(index, origin);
            }
            
            origin.Reset(level, value.Value);
        }
    }
    
    public void SetActiveSkill(int index, int level, float cooldown, float elapsedTime)
    {
        if (!__activeSkills.TryGetValue(index, out var value))
            return;
        
        value.Set(level, cooldown, elapsedTime);
    }
}
