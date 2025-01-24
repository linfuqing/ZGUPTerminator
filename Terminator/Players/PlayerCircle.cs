using UnityEngine;

public class PlayerCircle : MonoBehaviour
{
    public float radius;
    
    protected void LateUpdate()
    {
        var positionInstance = PlayerPosition.instance;
        if (positionInstance == null)
            return;

        var lookAtInstance = PlayerLookAt.instance;
        if (lookAtInstance == null)
            return;

        var center = lookAtInstance.transform.position;
        var distance = positionInstance.transform.position - center;
        //distance.y = 0.0f;

        transform.position = distance.normalized * radius + center;
        transform.rotation = Quaternion.LookRotation(new Vector3(-distance.x, 0.0f, -distance.z), Vector3.up);
    }
}
