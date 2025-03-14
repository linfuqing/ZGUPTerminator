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
            Handheld.Vibrate();
        else
            Vibration.Apply(type);
    }
}
