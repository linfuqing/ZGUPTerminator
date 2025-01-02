using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class WorldRectTransformManager : MonoBehaviour
{
    [SerializeField] 
    internal float _smoothTime = 0.1f;
    [SerializeField] 
    internal float _maxSpeed = float.MaxValue;
    [SerializeField] 
    internal Vector2 _offset = Vector2.one;
    [SerializeField] 
    internal Canvas _canvas;

    [SerializeField] 
    internal WorldRectTransformStyle _style;

    private HashSet<Transform> __transforms;
    private List<WorldRectTransformStyle> __pool;
    private Dictionary<Transform, WorldRectTransformStyle> __styles;

    public void Add(Transform transform)
    {
        if (__styles == null)
            __styles = new Dictionary<Transform, WorldRectTransformStyle>();

        WorldRectTransformStyle style;
        int count = __pool == null ? 0 : __pool.Count;
        if(count < 1)
            style = Instantiate(_style, _style.transform.parent);
        else
        {
            style = __pool[--count];
            
            __pool.RemoveAt(count);
        }
        
        style.gameObject.SetActive(true);
        style.SetPosition(transform.position, _offset, _canvas);
        
        __styles.Add(transform, style);
    }
    
    public void Remove(Transform transform)
    {
        if (__transforms == null)
            __transforms = new HashSet<Transform>();
        
        __transforms.Add(transform);
    }

    void Update()
    {
        if (__styles == null)
            return;
        
        float deltaTime = Time.deltaTime;
        Transform transform;
        WorldRectTransformStyle style;
        foreach (var pair in __styles)
        {
            transform = pair.Key;
            if (transform == null)
            {
                Remove(transform);
                
                continue;
            }

            style = pair.Value;
            style.SetPosition(transform.position, _canvas);
            style.SmoothUpdate(deltaTime, _smoothTime, _maxSpeed, _offset, _canvas);
        }

        if (__transforms != null)
        {
            foreach (var key in __transforms)
            {
                if (__styles.Remove(key, out style))
                {
                    style.gameObject.SetActive(false);
                    
                    if (__pool == null)
                        __pool = new List<WorldRectTransformStyle>();
                    
                    __pool.Add(style);
                }
            }
            
            __transforms.Clear();
        }
    }
}
