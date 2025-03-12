using UnityEngine;

public class VibrateEventReceiver : MonoBehaviour
{
    [UnityEngine.Scripting.Preserve]
    public void Vibrate()
    {
        Handheld.Vibrate();
    }
}
