using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

public class PlayerArrow : MonoBehaviour
{
    [SerializeField] 
    internal bool _isUp;

    [SerializeField] 
    internal float _scale = 1.0f;

    [SerializeField] 
    internal Vector2 _offsetSpeed = new float2(0, 2f);
    
    [SerializeField] 
    internal Vector3 _playerOffset;

    private Transform __transform;
    
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

    [Preserve]
    public void SetPlayerTransform(Parameters parameters)
    {
        if (parameters.TryGet((int)BulletAttributeID.ShooterTransform, out int instanceID))
            __transform = Resources.InstanceIDToObject(instanceID) as Transform;
    }
    
    protected void LateUpdate()
    {
        var player = __transform == null ? (PlayerPosition.instances == null ? null : PlayerPosition.instances[(int)PlayerType.Local].transform) : __transform;
        if (player == null)
            return;

        var material = renderer.material;
        var transform = this.transform;
        var distance = player.position + _playerOffset - transform.position;
        if(!_isUp)
            distance.y = 0.0f;
        
        float sqrMagnitude = distance.sqrMagnitude;
        if (sqrMagnitude > Mathf.Epsilon)
        {
            var rotation = Quaternion.LookRotation(distance * math.rsqrt(sqrMagnitude), Vector3.up);//Quaternion.FromToRotation(Vector3.forward, distance * math.rsqrt(sqrMagnitude));
            transform.rotation = rotation;
            Vector3 localScale = transform.localScale;
            localScale.z = (Quaternion.Inverse(rotation) * distance).z;
            transform.localScale = localScale;

            material.mainTextureScale = new Vector2(1.0f, localScale.z * (_scale > Mathf.Epsilon ? _scale : 1.0f));
        }

        material.mainTextureOffset += _offsetSpeed * Time.deltaTime;
    }
}
