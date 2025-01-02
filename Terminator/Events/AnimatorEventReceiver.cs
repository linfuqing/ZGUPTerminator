using UnityEngine;

public class AnimatorEventReceiver : MonoBehaviour
{
    private Animator __animator;
    private AttributeEventReceiver __attributeEventReceiver;

    public Animator animator
    {
        get
        {
            if (__animator == null)
                __animator = GetComponentInChildren<Animator>();

            return __animator;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public void Play(AnimatorParameters parameters)
    {
        if (__attributeEventReceiver != null)
        {
            switch (parameters.name)
            {
                case "Die":
                    __attributeEventReceiver.Die();
                    break;
            }
        }

        //print(parameters);
        parameters.Apply(animator);
    }
    
    protected void Awake()
    {
        if (__attributeEventReceiver == null)
            __attributeEventReceiver = GetComponentInChildren<AttributeEventReceiver>();
    }
}
