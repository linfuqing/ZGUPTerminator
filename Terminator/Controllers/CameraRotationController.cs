using UnityEngine;

public class CameraRotationController : MonoBehaviour
{
    [SerializeField]
    internal Camera _camera;
    
    void Start()
    {
        if(_camera == null)
            _camera = Camera.main;
    }

    void Update()
    {
        transform.rotation = _camera.transform.rotation;
    }
}
