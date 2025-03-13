using System;
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

        StartCoroutine(__WaitToClearTimeScale(_time));
        
        Handheld.Vibrate();
    }

    private IEnumerator __WaitToClearTimeScale(float time)
    {
        yield return new WaitForSecondsRealtime(time);

        __ClearTimeScale();
    }

    private void __ClearTimeScale()
    {
        TimeScaleUtility.Remove(__timeScaleIndex);

        __timeScaleIndex = -1;
    }

    void OnDisable()
    {
        __ClearTimeScale();
    }
}
