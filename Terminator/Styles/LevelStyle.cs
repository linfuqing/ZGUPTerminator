using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZG;
using ZG.UI;

public class LevelStyle : MonoBehaviour, IScrollRectSubmitHandler
{
    public StringEvent onEnergy;
    public StringEvent onTitle;
    
    public Toggle toggle;

    public Button button;

    public Progressbar progressbar;
    
    public StageStyle stageStyle;

    public StageRewardStyle rewardStyle;

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
