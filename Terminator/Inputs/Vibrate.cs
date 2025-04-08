using UnityEngine;

public enum VibrationType
{
    //Default,
    Pop, 
    Peek, 
    Nope
}

public interface IVibration
{
    public static IVibration instance;
    
    void Apply(VibrationType type);
}

public static class VibrateUtility
{
    public static void Apply(VibrationType type)
    {
        var Vibration = IVibration.instance;
        if(Vibration == null)
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate()
#endif
                ;
        else
            Vibration.Apply(type);
    }
}
