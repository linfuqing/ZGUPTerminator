using UnityEngine;

public class SkillStyle : MonoBehaviour
{
    public StringEvent onName;
    public StringEvent onDetail;

    public SpriteEvent onImage;
    
    //public ZG.UI.Progressbar cooldown;
    
    public GameObject[] levels;
    public GameObject[] rarities;
    
    public void SetAsset(in SkillAsset value)
    {
        if(onName != null)
            onName.Invoke(value.name);
            
        if(onDetail != null)
            onDetail.Invoke(value.detail);
            
        if(onImage != null)
            onImage.Invoke(value.sprite);

        __SetActive(levels, value.level);
        
        __SetActive(rarities, value.rarity);
        
        gameObject.SetActive(true);
    }
    
    private static void __SetActive(GameObject[] gameObjects, int index)
    {
        int numGameObjects = gameObjects.Length;
        for(int i = 0; i < numGameObjects; ++i)
            gameObjects[i].SetActive(i == index);
    }
}
