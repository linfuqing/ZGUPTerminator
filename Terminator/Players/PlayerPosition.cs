using System.Collections.Generic;
using UnityEngine;

public class PlayerPosition : MonoBehaviour
{
    public static PlayerPosition[] instances;

    [SerializeField] 
    internal PlayerType _type;

    [SerializeField] 
    internal Transform[] _children;

    public void SetPosition(in Vector3 position)
    {
        transform.position = position;
        
        if (_children != null)
        {
            foreach (var child in _children)
            {
                if (child == null)
                    continue;

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
