using UnityEngine;

public class PlayerLookAt : MonoBehaviour
{
    public static PlayerLookAt instance;

    private void OnEnable()
    {
        if(instance == null)
            instance = this;
    }

    protected void OnDisable()
    {
        if(instance == this)
            instance = null;
    }
}
