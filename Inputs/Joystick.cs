using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

public class Joystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerExitHandler
{
    public float radius = 100.0f;

    [SerializeField]
    internal RectTransform _circle;

    [SerializeField]
    internal RectTransform _point;

    private Vector2 __origin;

    private int __pointerID = -1;

    public static Vector2 axis
    {
        get;

        private set;
    }
    
    protected void Start()
    {
        __origin = _point.anchoredPosition;
    }

    void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
    {
        if (__pointerID == -1)
        {
            var rectTransform = transform as RectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                 rectTransform, 
                 eventData.position, 
                 eventData.pressEventCamera, 
                 out var position))
                return;

            if(_circle != null)
                _circle.anchoredPosition = position;

            if(_point != null)
                _point.anchoredPosition = position;

            __pointerID = eventData.pointerId;
        }

        ((IDragHandler)this).OnDrag(eventData);
    }
    
    void IDragHandler.OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != __pointerID)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform as RectTransform, 
                eventData.position, 
                eventData.pressEventCamera, 
                out var position))
            return;
        
        Vector2 origin = _circle == null ? __origin : _circle.anchoredPosition, axis = position - origin;
        float magnitudeSQ = axis.sqrMagnitude, magnitudeR;

        if (magnitudeSQ > radius * radius)
        {
            magnitudeR = math.rsqrt(magnitudeSQ);

            if(_point != null)
                _point.anchoredPosition = magnitudeR * radius * axis + origin;
        }
        else 
        {
            magnitudeR = magnitudeSQ > math.FLT_MIN_NORMAL ? math.rsqrt(magnitudeSQ) : 0.0f;//Unity.Mathematics.math.rcp(radius);

            if(_point != null)
                _point.anchoredPosition = position;
        }

        axis *= magnitudeR;

        Joystick.axis = axis;
    }
    
    void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != __pointerID)
            return;
        
        __pointerID = -1;

        if (_circle)
            _circle.anchoredPosition = __origin;

        if (_point != null)
            _point.anchoredPosition = __origin;

        axis = Vector2.zero;
    }

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
    {
        ((IPointerUpHandler)this).OnPointerUp(eventData);
    }
}
