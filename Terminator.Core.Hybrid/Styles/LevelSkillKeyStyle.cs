using UnityEngine;
using ZG;
using ZG.UI;

public class LevelSkillKeyStyle : SkillKeyStyle
{
    public StringEvent onTitle;
    public StringEvent onDetail;
    
    public GameObject[] ranks;
    
    public Progressbar progressbar;

    public bool SetAsset(in SkillKeyAsset value, int count)
    {
        onTitle.Invoke(value.name);
        onDetail.Invoke(value.detail);

        onSprite.Invoke(value.sprite);
        
        int index = value.ranks.BinarySearch(count);
        
        SkillStyle.SetActive(ranks, index + 1);

        progressbar.value = count * 1.0f / value.capacity;

        return index >= 0;
    }
}
