using System;
using UnityEngine;

[CreateAssetMenu(menuName = "PlayerAnimatorParameters", fileName = "Player Animator Parameters")]
public class AnimatorParameters : ScriptableObject
{
    public enum ParameterType
    {
        Trigger,
        Bool, 
        Int,
        Float
    }
    
    [Serializable]
    public struct Parameter
    {
        public string name;
        public float value;
        public ParameterType type;

        public void Apply(Animator animator)
        {
            switch (type)
            {
                case ParameterType.Trigger:
                    if(Mathf.Abs(value) > Mathf.Epsilon)
                        animator.ResetTrigger(name);
                    else
                        animator.SetTrigger(name);
                    break;
                case ParameterType.Bool:
                    animator.SetBool(name, Mathf.Abs(value) > Mathf.Epsilon);
                    break;
                case ParameterType.Int:
                    animator.SetInteger(name, Mathf.RoundToInt(value));
                    break;
                case ParameterType.Float:
                    animator.SetFloat(name, value);
                    break;
            }
        }
    }

    [UnityEngine.Serialization.FormerlySerializedAs("parameters")]
    [SerializeField]
    internal Parameter[] _parameters;

    public void Apply(Animator animator)
    {
        foreach (var parameter in _parameters)
            parameter.Apply(animator);
    }
}
