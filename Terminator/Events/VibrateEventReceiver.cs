using UnityEngine;

public class VibrateEventReceiver : MonoBehaviour
{
    [UnityEngine.Scripting.Preserve]
    public void Vibrate()
    {
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }
}
