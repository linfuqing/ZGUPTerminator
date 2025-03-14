using UnityEngine;

public interface IVibrate
{
    public static IVibrate instance;
    
    void Apply();
}

public static class VibrateUtility
{
    public static void Apply()
    {
        var vibrate = IVibrate.instance;
        if(vibrate == null)
            Handheld.Vibrate();
        else
            vibrate.Apply();
    }
}
