using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZG;
using ZG.UI;

public class LevelStyle : MonoBehaviour, IScrollRectSubmitHandler
{
    [Serializable]
    public struct Scene
    {
        public UnityEvent onActive;

        public StageStyle stageStyle;
        
        public StageRewardStyle stageRewardStyle;
    }
    
    public StringEvent onEnergy;
    public StringEvent onTitle;

    public Transform root;
    
    public Toggle toggle;

    public Button button;

    public Progressbar progressbar;
    
    //public StageStyle stageStyle;

    //public StageRewardStyle rewardStyle;

    public Scene[] scenes;

    void ISubmitHandler.OnSubmit(BaseEventData eventData)
    {
        if (toggle != null)
            toggle.isOn = true;
    }
    
    void IScrollRectSubmitHandler.OnScrollRectDrag(float value)
    {
        if (progressbar != null)
            progressbar.value = value;
    }
}
