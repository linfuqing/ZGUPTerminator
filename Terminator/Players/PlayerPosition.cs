using System.Collections.Generic;
using UnityEngine;

public class PlayerPosition : MonoBehaviour
{
    public static PlayerPosition[] instances;

    [SerializeField] 
    internal PlayerType _type;
    
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
