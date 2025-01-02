using System;
using UnityEngine;

public class AttributeEventReceiver : MonoBehaviour
{
    [SerializeField] 
    internal AttributeSpace _space;
    
    [SerializeField] 
    internal int _styleIndex;

    private int __instanceID;

    private int __hpMax;

    public void Die()
    {
        AttributeManager.instance.Set(
            _space, 
            __instanceID, 
            _styleIndex, 
            0, 
            __hpMax);
    }
    
    [UnityEngine.Scripting.Preserve]
    public void UpdateAttribute(Parameters parameters)
    {
        if(__instanceID == 0)
            __instanceID = transform.GetInstanceID();

        if (parameters.TryGet((int)EffectAttributeID.HPMax, out int hpMax))
            __hpMax = hpMax;

        if (!parameters.TryGet((int)EffectAttributeID.HP, out int hp))
            hp = __hpMax;
        
        AttributeManager.instance.Set(
            _space, 
            __instanceID, 
            _styleIndex, 
            hp, 
            __hpMax);
    }

    public void OnDisable()
    {
        if (__instanceID == 0)
            return;
        
        AttributeManager.instance.Unset(__instanceID);

        __instanceID = 0;
    }
}
