using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class AttributeEventReceiver : MonoBehaviour
{
    private enum AttributeType
    {
        HP, 
        Rage, 
        Shield
    }

    [Serializable]
    internal struct AttributeData
    {
        public int id;
        public int idMax;
        public int index;
    }
    
    [SerializeField]
    internal AttributeData[] _attributes =
    {
        new ()
        {
            id = (int)EffectAttributeID.HP, 
            idMax = (int)EffectAttributeID.HPMax, 
            index = (int)AttributeType.HP
        },
        
        new ()
        {
            id = (int)EffectAttributeID.Rage, 
            idMax = (int)EffectAttributeID.RageMax, 
            index = (int)AttributeType.Rage
        }, 
        
        new ()
        {
            id = (int)EffectAttributeID.Shield, 
            idMax = (int)EffectAttributeID.HPMax, 
            index = (int)AttributeType.Shield
        }
    };
    
    [SerializeField] 
    internal AttributeSpace _space;
    
    [SerializeField] 
    internal int _styleIndex;

    private int __instanceID;

    /*private int __shield;
    private int __hpMax;
    private int __rageMax;
    private int __rage;

    public event Action<int> onHPMaxChanged;
    public event Action<int> onHPChanged;

    public event Action<int> onRageMaxChanged;
    public event Action<int> onRageChanged;*/

    private Dictionary<int, int> __attributes;

    public event Action<int, int> onChanged;
    
    public void Clear()
    {
        if(!isActiveAndEnabled || __instanceID == 0)
            return;
        
        int numAttributes = _attributes.Length, value, max;
        for (int i = 0; i < numAttributes; ++i)
        {
            ref var attribute = ref _attributes[i];
            value = __attributes.TryGetValue(attribute.id, out value) ? value : 0;
            if(value == 0)
                continue;
            
            max = __attributes.TryGetValue(attribute.idMax, out max) ? max : value;
            AttributeManager.instance.Set(
                _space, 
                __instanceID, 
                _styleIndex, 
                attribute.index, 
                0, 
                max);
        }
        
        /*AttributeManager.instance.Set(
            _space, 
            __instanceID, 
            _styleIndex, 
            (int)AttributeType.HP, 
            0, 
            __hpMax);*/
    }
    
    [UnityEngine.Scripting.Preserve]
    public void UpdateAttribute(Parameters parameters)
    {
        if(!isActiveAndEnabled)
            return;
        
        if(__instanceID == 0)
            __instanceID = transform.GetInstanceID();

        int dirtyFlag = 0, numAttributes = _attributes == null ? 0 : _attributes.Length, id, destination, source, i;
        foreach (var pair in parameters.values)
        {
            id = pair.Key;
            destination = pair.Value;
            
            if(__attributes == null)
                __attributes = new Dictionary<int, int>();
            
            if(__attributes.TryGetValue(id, out source) && source == destination)
                continue;
            
            if(onChanged != null)
                onChanged(id, destination);

            __attributes[id] = destination;
            
            for (i = 0; i < numAttributes; ++i)
            {
                ref var attribute = ref _attributes[i];
                if (attribute.id == id || attribute.idMax == id)
                    dirtyFlag |= 1 << i;
            }
        }

        int count = ZG.MathUtility.GetHighestBit((uint)dirtyFlag);
        if (count > 0)
        {
            for (i = ZG.MathUtility.GetLowerstBit((uint)dirtyFlag) - 1; i < count; ++i)
            {
                if((dirtyFlag & (1 << i)) == 0)
                    continue;
                
                ref var attribute = ref _attributes[i];
                source = __attributes.TryGetValue(attribute.id, out source) ? source : 0;
                destination = __attributes.TryGetValue(attribute.idMax, out destination) ? destination : source;
                AttributeManager.instance.Set(
                    _space, 
                    __instanceID, 
                    _styleIndex, 
                    attribute.index, 
                    source, 
                    destination);
            }
        }
        /*int dirtyFlag = 0;
        if (parameters.TryGet((int)EffectAttributeID.HPMax, out int hpMax))
        {
            dirtyFlag |= 1 << (int)AttributeType.HP;

            onHPMaxChanged?.Invoke(hpMax);

            __hpMax = hpMax;
        }

        if (parameters.TryGet((int)EffectAttributeID.HP, out int hp))
        {
            if (__hpMax == 0)
                __hpMax = hp;

            dirtyFlag |= 1 << (int)AttributeType.HP;
        }
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

        if (parameters.TryGet((int)EffectAttributeID.Shield, out int shield) && shield != __shield)
        {
            __shield = shield;

            dirtyFlag |= 1 << (int)AttributeType.Shield;
        }

        if ((dirtyFlag & (1 << (int)AttributeType.Shield)) != 0)
            AttributeManager.instance.Set(
                _space,
                __instanceID,
                _styleIndex,
                (int)AttributeType.Shield,
                shield,
                __hpMax);

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
                __rage,
                __rageMax);
        }*/
    }

    public void OnDisable()
    {
        if (__instanceID == 0)
            return;
        
        AttributeManager.instance.Unset(__instanceID);

        __instanceID = 0;
    }
}
