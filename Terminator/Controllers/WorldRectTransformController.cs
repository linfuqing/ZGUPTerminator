using UnityEngine;
using ZG;

public sealed class WorldRectTransformController : MonoBehaviour
{
    [SerializeField] 
    internal string _managerComponentName;

    private WorldRectTransformManager __manager;
    
    void OnEnable()
    {
        __manager = ComponentManager<WorldRectTransformManager>.Find(_managerComponentName);
        if(__manager != null)
            __manager.Add(transform);
    }
    
    void OnDisable()
    {
        if(__manager != null)
            __manager.Remove(transform);
    }
}
