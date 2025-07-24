using UnityEngine;
using ZG;
using ZG.UI;

public class LevelSkillKeyStyle : SkillKeyStyle
{
    public StringEvent onTitle;
    public StringEvent onDetail;
    
    public GameObject[] ranks;
    
    public Progressbar progressbar;

    public int SetAsset(in SkillKeyAsset value, int count, bool isIcon = true)
    {
        onTitle.Invoke(value.name);

        onSprite.Invoke(isIcon ? value.icon : value.sprite);
        
        int index = value.BinarySearch(count);
        if(index >= 0 && onDetail != null)
            onDetail.Invoke(value.ranks[index].detail);
        
        SkillStyle.SetActive(ranks, index + 1);

        progressbar.value = count * 1.0f / value.capacity;

        return index;
    }
}
