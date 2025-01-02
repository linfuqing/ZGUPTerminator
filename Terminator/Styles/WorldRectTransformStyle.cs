using System;
using UnityEngine;
using UnityEngine.Events;

public class WorldRectTransformStyle : MonoBehaviour
{
    [Serializable]
    internal class RotationEvent : UnityEvent<float>
    {

    }

    [SerializeField] 
    internal RotationEvent _onRotate;
    
    [SerializeField] 
    internal UnityEvent _onForward;
    
    [SerializeField] 
    internal UnityEvent _onBackward;

    [SerializeField] 
    internal Vector3 _position;
    
    private Vector3 __velocity;

    public float rotation
    {
        get => transform.eulerAngles.z;

        set
        {
            Vector3 eulerAngles = transform.eulerAngles;

            eulerAngles.z = value;

            transform.eulerAngles = eulerAngles;
        }
    }
    
    public void SetPosition(in Vector3 position, in Vector2 offset, Canvas canvas)
    {
        SetPosition(position, canvas);

        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            transform.position = position;
        else
        {
            RectTransform transform = base.transform as RectTransform;
            if (transform != null)
            {
                SetPosition(_position, canvas);

                RectTransform parent = transform == null ? null : transform.parent as RectTransform;
                if (parent != null)
                {
                    Rect rect = parent.rect;

                    SetPosition(Vector2.Max(Vector2.Min((Vector2)position + offset, rect.max), rect.min), canvas);
                }

                transform.anchoredPosition3D = position;
            }
        }
    }
    
    public void SetPosition(in Vector3 value, Canvas canvas)
    {
        RectTransform rectTransform = base.transform as RectTransform,
            parent = rectTransform == null ? null : rectTransform.parent as RectTransform;
        if (parent == null)
            return;

        var renderMode = canvas.renderMode;
        switch (renderMode)
        {
            case RenderMode.ScreenSpaceOverlay:
            case RenderMode.ScreenSpaceCamera:
                Camera source = canvas.worldCamera, destination = source == null ? Camera.main : source;
                if (destination == null)
                    return;

                bool isInvert = false;
                Vector3 position = destination.WorldToScreenPoint(value);
                if (position.z < 0.0f)
                {
                    if (_position.z >= 0.0f)
                    {
                        if (_onBackward != null)
                            _onBackward.Invoke();
                    }

                    position = -position;

                    isInvert = true;
                }
                else if (_position.z < 0.0f)
                {
                    if (_onForward != null)
                        _onForward.Invoke();
                }

                position.x = Mathf.Clamp(position.x, 0.0f, destination.pixelWidth);
                position.y = Mathf.Clamp(position.y, 0.0f, destination.pixelHeight);

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    position,
                    renderMode == RenderMode.ScreenSpaceOverlay ? null : source,
                    out var point);

                _position.x = point.x;
                _position.y = point.y;
                _position.z = isInvert ? -position.z : position.z;
                break;
            case RenderMode.WorldSpace:
                _position = value;
                break;
        }
    }

    public void SmoothUpdate(
        float deltaTime, 
        float smoothTime, 
        float maxSpeed, 
        in Vector2 offset, 
        Canvas canvas)
    {
        RectTransform transform = base.transform as RectTransform;
        if (transform == null)
            return;

        Vector3 position = _position;

        /*if (radius > 0.0f)
        {
            transform.pivot
        }*/

        RectTransform parent = transform == null ? null : transform.parent as RectTransform;
        if (parent != null)
        {
            Rect rect = parent.rect;

            position = Vector2.Max(Vector2.Min((Vector2)position + offset, rect.max), rect.min);
        }

        position = Vector3.SmoothDamp(transform.anchoredPosition3D, position, ref __velocity, smoothTime, maxSpeed, deltaTime);

        transform.anchoredPosition3D = position;

        if(canvas.renderMode == RenderMode.WorldSpace)
        {
            var worldCamera = canvas.worldCamera;
            transform.rotation = worldCamera.transform.rotation;
        }
        else if (_onRotate != null)
            _onRotate.Invoke(Vector2.SignedAngle(-offset, _position - position));
    }
}
