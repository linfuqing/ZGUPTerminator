#if !UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
 
[UnityEngine.Scripting.Preserve]
public class SkipUnityLogo
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void BeforeSplashScreen()
    {
#if UNITY_WEBGL
        Application.focusChanged += __ApplicationFocusChanged;
#else
        System.Threading.Tasks.Task.Run(__AsyncSkip);
#endif
    }
 
#if UNITY_WEBGL
    private static void __ApplicationFocusChanged(bool obj)
    {
        Application.focusChanged -= __ApplicationFocusChanged;
        SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
    }
#else
    private static void __AsyncSkip()
    {
        SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
    }
#endif
}
#endif