using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimeScaleEventReceiver : MonoBehaviour
{
    [SerializeField] 
    internal float _value = 0.2f;
    
    [SerializeField] 
    internal float _time = 1f;

    private int __timeScaleIndex = -1;

    [UnityEngine.Scripting.Preserve]
    public void TimeScale()
    {
        __timeScaleIndex = TimeScaleUtility.Add(_value);
        
        Invoke(nameof(__ClearTimeScale), _time);
        
        Handheld.Vibrate();
    }

    private void __ClearTimeScale()
    {
        TimeScaleUtility.Remove(__timeScaleIndex);

        __timeScaleIndex = -1;
    }
}
