using System;
using System.Collections.Generic;
using UnityEngine;

public enum AttributeSpace
{
    Local,
    World
}

public class AttributeManager : MonoBehaviour
{
    private struct Attribute
    {
        public AttributeSpace space;
        public int styleIndex;
        public AttributeStyle style;
    }

    [SerializeField] 
    internal Camera _camera;

    [SerializeField] 
    internal AttributeStyle[] _styles;

    private Dictionary<int, Attribute> __attributes;

    public static AttributeManager instance
    {
        get;

        private set;
    }

    public void Set(
        AttributeSpace space, 
        int instanceID, 
        int styleIndex, 
        int attributeIndex,
        int value, 
        int max)
    {
        if (__attributes == null)
            __attributes = new Dictionary<int, Attribute>();

        if (__attributes.TryGetValue(instanceID, out var attribute))
        {
            if (attribute.styleIndex != styleIndex && attribute.style != null)
            {
                Destroy(attribute.style.gameObject);

                attribute.style = null;
            }
        }

        if (attribute.style == null)
        {
            var style = _styles[styleIndex];
            attribute.style = Instantiate(style, style.transform.parent);
        }
        
        ref var styleAttribute = ref attribute.style.attributes[attributeIndex];
        if(styleAttribute.onValue != null)
            styleAttribute.onValue.Invoke(value.ToString());

        if(styleAttribute.onMax != null)
            styleAttribute.onMax.Invoke(max.ToString());

        if (styleAttribute.progressbar != null)
            styleAttribute.progressbar.value = value * 1.0f / max;
        
        attribute.style.gameObject.SetActive(true);

        attribute.styleIndex = styleIndex;

        attribute.space = space;

        __attributes[instanceID] = attribute;
    }

    public bool Unset(int instanceID)
    {
        if (__attributes.Remove(instanceID, out var attribute))
        {
            if (attribute.style != null)
                Destroy(attribute.style.gameObject);

            return true;
        }

        return false;
    }

    protected void Start()
    {
        if (_camera == null)
        {
            var canvas = GetComponentInParent<Canvas>(true);
            _camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }
    }

    protected void OnEnable()
    {
        instance = this;
    }

    protected void Update()
    {
        if (__attributes == null)
            return;

        Transform transform;
        RectTransform rectTransform;
        Attribute attribute;
        Vector2 point;
        foreach (var pair in __attributes)
        {
            attribute = pair.Value;
            if(attribute.space == AttributeSpace.World)
                continue;
            
            rectTransform = attribute.style == null ? null : attribute.style.transform as RectTransform;
            if(rectTransform == null)
                continue;

            transform = Resources.InstanceIDToObject(pair.Key) as Transform;
            if(transform == null)
                continue;
            
            point = RectTransformUtility.WorldToScreenPoint(_camera, transform.position);
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                   rectTransform.parent as RectTransform,
                point,
                _camera,
                out point))
                continue;

            rectTransform.anchoredPosition = point;
        }
    }
}
