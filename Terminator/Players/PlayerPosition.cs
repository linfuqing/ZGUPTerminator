using System.Collections.Generic;
using UnityEngine;

public class PlayerPosition : MonoBehaviour
{
    public static PlayerPosition[] instances;

    [SerializeField] 
    internal PlayerType _type;

    [SerializeField] 
    internal Camera _camera;

    [SerializeField] 
    internal Transform[] _children;

    public void SetPosition(in Vector3 position)
    {
        transform.position = position;
        
        if (_children != null)
        {
            Vector2 point;
            foreach (var child in _children)
            {
                if (child == null)
                    continue;

                if (child is RectTransform rectTransform)
                {
                    point = RectTransformUtility.WorldToScreenPoint(_camera, transform.position);

                    if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                           rectTransform.parent as RectTransform,
                           point,
                           _camera,
                           out point))
                        continue;

                    rectTransform.anchoredPosition = point;
                }
                else
                    child.position = position;
            }
        }
    }
    
    void OnEnable()
    {
        instances ??= new PlayerPosition[(int)PlayerType.Total];

        instances[(int)_type] = this;
    }

    void OnDisable()
    {
        instances[(int)_type] = null;
    }
}
