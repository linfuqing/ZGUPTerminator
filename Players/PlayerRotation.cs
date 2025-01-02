using UnityEngine;

public class PlayerRotation : MonoBehaviour
{
    public static PlayerRotation instance;
    
    public void OnEnable()
    {
        instance = this;
    }
}
