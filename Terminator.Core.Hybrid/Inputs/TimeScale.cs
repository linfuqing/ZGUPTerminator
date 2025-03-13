using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public static class TimeScaleUtility
{
    private static Pool<float> __values;
    
    public static int Add(float value)
    {
        Time.timeScale *= value;
        
        if (__values == null)
            __values = new Pool<float>();
        
        return __values.Add(value);
    }

    public static void Remove(int index)
    {
        if (__values == null || !__values.RemoveAt(index))
            return;

        float timeScale = 1.0f;
        foreach (var value in (IEnumerable<float>)__values)
            timeScale *= value;

        Time.timeScale = timeScale;
    }
}
