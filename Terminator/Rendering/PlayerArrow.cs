using Unity.Mathematics;
using UnityEngine;

public class PlayerArrow : MonoBehaviour
{
    [SerializeField] 
    internal Vector2 _offsetSpeed = new float2(0, 2f);
    
    private Renderer __renderer;
    
    public 
        #if UNITY_EDITOR
        new 
        #endif
        Renderer renderer
    {
        get
        {
            if (__renderer == null)
                __renderer = GetComponentInChildren<Renderer>();

            return __renderer;
        }
    }
    
    protected void LateUpdate()
    {
        var player = PlayerPosition.instance;
        if (player == null)
            return;

        var material = renderer.material;
        var transform = this.transform;
        var distance = player.transform.position - transform.position;
        distance.y = 0.0f;
        float sqrMagnitude = distance.sqrMagnitude;
        if (sqrMagnitude > Mathf.Epsilon)
        {
            var rotation = Quaternion.FromToRotation(Vector3.forward, distance * math.rsqrt(sqrMagnitude));
            transform.rotation = rotation;
            Vector3 localScale = transform.localScale;
            localScale.z = (Quaternion.Inverse(rotation) * distance).z;
            transform.localScale = localScale;

            material.mainTextureScale = new Vector2(1.0f, localScale.z);
        }

        material.mainTextureOffset += _offsetSpeed * Time.deltaTime;
    }
}
