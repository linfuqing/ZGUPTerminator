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
        public ActiveSkillStyle[] styles;

        public ActiveSkill(int siblingIndex, in SkillAsset asset, ActiveSkillStyle[] styles)
        {
            int numStyles = styles == null ? 0 : styles.Length;
            ActiveSkillStyle style;
            this.styles = numStyles > 0 ? new ActiveSkillStyle[numStyles] : null;
            for (int i = 0; i < numStyles; ++i)
            {
                style = styles[i];
                if(style == null)
                    continue;

                style = Instantiate(style, style.transform.parent);
                
                style.transform.SetSiblingIndex(siblingIndex);
                style.SetAsset(asset);
                
                this.styles[i] = style;
            }
        }

        public void Dispose()
        {
            if (styles == null)
                return;
            
            foreach (var style in styles)
                Destroy(style.gameObject);

            styles = null;
        }

        public void Reset(in SkillAsset asset)
        {
            ActiveSkillStyle style;
            int numStyles = styles == null ? 0 : styles.Length;
            for (int i = 0; i < numStyles; ++i)
            {
                style = styles[i];
                if(style == null)
                    continue;
                
                style.SetAsset(asset);
            }
        }
        
        public void Set(float cooldown, float elapsedTime)
        {
            if (styles != null)
            {
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
    
    public void SetActiveSkill(int index, in SkillAsset? value)
    {
        if (value == null)
        {
            if (__activeSkills != null && __activeSkills.RemoveAt(index, out var origin))
                origin.Dispose();
        }
        else
        {
            if (__activeSkills == null)
                __activeSkills = new Pool<ActiveSkill>();
            
            if(__activeSkills.TryGetValue(index, out var origin))
                origin.Reset(value.Value);
            else
                __activeSkills.Insert(index, new ActiveSkill(index, value.Value, _activeSkillStyles));
        }
    }
    
    public void SetActiveSkill(int index, float cooldown, float elapsedTime)
    {
        if (!__activeSkills.TryGetValue(index, out var value))
            return;
        
        value.Set(cooldown, elapsedTime);
    }
}
