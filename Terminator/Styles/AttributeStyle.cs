using System;
using UnityEngine;

public class AttributeStyle : MonoBehaviour
{
    [Serializable]
    public struct Attribute
    {
        public ZG.UI.Progressbar progressbar;

        public StringEvent onValue;
        public StringEvent onMax;
    }
    
    //public ZG.UI.Progressbar progressbar;

    //public StringEvent onValue;
    //public StringEvent onMax;
    
    public Attribute[] attributes;
}
