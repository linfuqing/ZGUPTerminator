using UnityEngine;
using ZG;

public class AnimatorComponentEventReceiver : MonoBehaviour
{
    [SerializeField] 
    internal string _componentName;
    
    [UnityEngine.Scripting.Preserve]
    public void PlayComponent(AnimatorParameters parameters)
    {
        var component = ComponentManager<Animator>.Find(_componentName);
        if (component == null)
            return;
        
        //print(parameters);
        parameters.Apply(component);
    }

    [UnityEngine.Scripting.Preserve]
    public void PlayComponentOnAnimation(AnimationEvent animationEvent)
    {
        PlayComponent(animationEvent.objectReferenceParameter as AnimatorParameters);
    }
}
