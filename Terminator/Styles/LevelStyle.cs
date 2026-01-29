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
        public UnityEvent onActiveFirst;

        public UnityEvent onActiveDiff;

        public UnityEvent onActive;

        public Transform root;

        public Toggle toggle;
        
        public StringEvent onTitle;
        public StringEvent onDescription;

        public Progressbar loaderProgressbar;
        
        public StageStyle[] stageStyles;
    }
    
    public StringEvent onEnergy;

    public Toggle toggle;

    public Progressbar progressbar;
    
    //public Button button;

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
