using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TimeScaleEventReceiver : MonoBehaviour
{
    public static float minInterval = 10.0f;
    private static double __previousTime;

    [SerializeField] 
    internal UnityEvent _onInvoke;
    
    [SerializeField] 
    internal float _value = 0.2f;
    
    [SerializeField] 
    internal float _time = 1f;

    private int __timeScaleIndex = -1;

    [UnityEngine.Scripting.Preserve]
    public void TimeScale()
    {
        double time = Time.timeAsDouble;
        if (__previousTime > 0.0f && __previousTime + minInterval > time)
            return;
        
        __previousTime = time;
        
        __timeScaleIndex = TimeScaleUtility.Add(_value);

        StartCoroutine(__WaitToClearTimeScale(_time));
        
        VibrateUtility.Apply(VibrationType.Nope);
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
