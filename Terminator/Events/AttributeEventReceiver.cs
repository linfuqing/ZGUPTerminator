using System;
using UnityEngine;
using ZG;

public class AttributeEventReceiver : MonoBehaviour
{
    private enum AttributeType
    {
        HP, 
        Rage//, 
        //RageCount
    }
    
    [SerializeField] 
    internal AttributeSpace _space;
    
    [SerializeField] 
    internal int _styleIndex;

    private int __instanceID;

    private int __hpMax;
    private int __rageMax;
    private int __rage;

    public event Action<int> onHPMaxChanged;
    public event Action<int> onHPChanged;

    public event Action<int> onRageMaxChanged;
    public event Action<int> onRageChanged;
    
    public void Die()
    {
        AttributeManager.instance.Set(
            _space, 
            __instanceID, 
            _styleIndex, 
            (int)AttributeType.HP, 
            0, 
            __hpMax);
    }
    
    [UnityEngine.Scripting.Preserve]
    public void UpdateAttribute(Parameters parameters)
    {
        if(__instanceID == 0)
            __instanceID = transform.GetInstanceID();

        int dirtyFlag = 0;
        if (parameters.TryGet((int)EffectAttributeID.HPMax, out int hpMax))
        {
            dirtyFlag |= 1 << (int)AttributeType.HP;
            
            onHPMaxChanged?.Invoke(hpMax);
            
            __hpMax = hpMax;
        }

        if (parameters.TryGet((int)EffectAttributeID.HP, out int hp))
            dirtyFlag |= 1 << (int)AttributeType.HP;
        else
            hp = __hpMax;

        if((dirtyFlag & (1 << (int)AttributeType.HP)) != 0)
        {
            onHPChanged?.Invoke(hp);

            AttributeManager.instance.Set(
                _space,
                __instanceID,
                _styleIndex,
                (int)AttributeType.HP,
                hp,
                __hpMax);
        }

        if (parameters.TryGet((int)EffectAttributeID.RageMax, out int rageMax))
        {
            dirtyFlag |= 1 << (int)AttributeType.Rage;
            
            onRageMaxChanged?.Invoke(rageMax);
            
            __rageMax = rageMax;
        }

        if (parameters.TryGet((int)EffectAttributeID.Rage, out int rage) && rage != 0)
        {
            __rage += rage;
            
            dirtyFlag |= 1 << (int)AttributeType.Rage;
        }

        if((dirtyFlag & (1 << (int)AttributeType.Rage)) != 0)
        {
            onRageChanged?.Invoke(__rage);

            AttributeManager.instance.Set(
                _space,
                __instanceID,
                _styleIndex,
                (int)AttributeType.Rage,
                __rage, // % __rageMax,
                __rageMax);
            
            /*AttributeManager.instance.Set(
                _space,
                __instanceID,
                _styleIndex,
                (int)AttributeType.RageCount,
                rage / __rageMax,
                __rageMax);*/
        }
    }

    public void OnDisable()
    {
        if (__instanceID == 0)
            return;
        
        AttributeManager.instance.Unset(__instanceID);

        __instanceID = 0;
    }
}
