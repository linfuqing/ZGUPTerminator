using UnityEngine;

public class PlayerPosition : MonoBehaviour
{
    public static PlayerPosition instance;
    
    public void OnEnable()
    {
        instance = this;
    }
}
