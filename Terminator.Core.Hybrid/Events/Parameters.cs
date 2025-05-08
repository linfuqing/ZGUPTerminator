using System.Collections.Generic;
using UnityEngine;
using ZG;

[CreateAssetMenu(menuName = "Parameters", fileName = "Parameters")]
public class Parameters : ScriptableObject, IMessage
{
    private static Dictionary<int, int> __values;

    public int count => __values.Count;

    public IEnumerable<int> values => __values.Values;

    public int this[int id] => __values[id];

    public bool TryGet(int id, out int value)
    {
        return __values.TryGetValue(id, out value);
    }

    public void Set(int id, int value)
    {
        if (__values == null)
            __values = new Dictionary<int, int>();
        
        __values[id] = value;
    }

    public void Clear()
    {
        if(__values != null )
            __values.Clear();
    }
}
