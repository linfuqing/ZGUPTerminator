using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;

public class MessageEventReceiver : MonoBehaviour
{
    [Serializable]
    public struct Event
    {
        public string name;

        public UnityEvent value;
    }

    [SerializeField] 
    internal UnityEvent _onReset;

    [SerializeField]
    internal Event[] _values;

    private Dictionary<string, int> __indices;
    
    [Preserve]
    public void Call(MessageEvent messageEvent)
    {
        if (__indices == null)
        {
            int numValues = _values.Length;
            __indices = new Dictionary<string, int>(numValues);

            for(int i = 0; i < numValues; ++i)
                __indices.Add(_values[i].name, i);
        }

        if (__indices.TryGetValue(messageEvent.name, out int index))
            _values[index].value.Invoke();
    }

    protected void OnDisable()
    {
        if(_onReset != null)
            _onReset.Invoke();
    }
}
