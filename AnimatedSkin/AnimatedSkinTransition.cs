using UnityEngine;

[CreateAssetMenu(menuName = "AnimatedSkin/Transition", fileName = "AnimatedSkin Transition")]
public class AnimatedSkinTransition : ScriptableObject
{
    public bool isLoop = true;
    public float offsetSeconds;
    public string animationName;
    public AnimatedSkinTransition next;
}
