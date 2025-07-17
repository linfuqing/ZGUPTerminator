using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using ZG;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
[RequireComponent(typeof(BulletAuthoring))]
public class SkillAuthoring : MonoBehaviour, IMessageOverride
{
    [Serializable]
    public struct MessageData : IEquatable<MessageData>
    {
        public string name;
        public string messageName;
        public Object messageValue;
        public SkillMessageType type;

        public bool Equals(MessageData other)
        {
            return type == other.type && 
                   name == other.name && 
                   messageName == other.messageName && 
                   messageValue == other.messageValue;
        }
    }
    
    [Serializable]
    public struct BulletData : IEquatable<BulletData>
    {
        public string name;
        public float damageScale;
        public float chance;

        public bool Equals(BulletData other)
        {
            return name == other.name && damageScale == other.damageScale && chance.Equals(other.chance);
        }
    }

    [Serializable]
    public struct SkillData
    {
        public string name;

        [Tooltip("如果填写，则对应的子弹标签被激活时，技能才生效")]
        public LayerMask layerMask;
        
        public float rage;
        
        public float duration;
        public float cooldown;
        public BulletData[] bullets;
        public string[] messageNames;
        public string[] preNames;
        
        #region CSV
        [CSVField]
        public string 技能名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public int 技能标签
        {
            set
            {
                layerMask = value;
            }
        }

        [CSVField]
        public float 技能需要怒气
        {
            set
            {
                rage = value;
            }
        }
        
        [CSVField]
        public float 技能持续时间
        {
            set
            {
                duration = value;
            }
        }
        
        [CSVField]
        public float 技能冷却时间
        {
            set
            {
                cooldown = value;
            }
        }
        
        [CSVField]
        public string 技能子弹
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    bullets = null;
                    return;
                }
                
                var parameters = value.Split("/");
                int numParameters = parameters == null ? 0 : parameters.Length, index, count;

                bullets = new BulletData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    ref var parameter = ref parameters[i];
                    ref var bullet = ref bullets[i];

                    index = parameter.IndexOf(':');
                    if (index == -1)
                    {
                        bullet.name = parameter;
                        bullet.damageScale = 0.0f;
                        bullet.chance = 1.0f;
                    }
                    else
                    {
                        bullet.name = parameter.Remove(index);
                        
                        count = parameter.IndexOf(':', index + 1);
                        if (count == -1)
                        {
                            bullet.damageScale = float.Parse(parameter.Substring(index + 1));
                            bullet.chance = 1.0f;
                        }
                        else
                        {
                            bullet.damageScale = float.Parse(parameter.Substring(index + 1, count - index - 1));
                            bullet.chance = float.Parse(parameter.Substring(count + 1));
                        }
                    }
                }
            }
        }
        
        [CSVField]
        public string 技能消息
        {
            set
            {
                messageNames = string.IsNullOrEmpty(value) ? null : value.Split('/');
            }
        }
        
        [CSVField]
        public string 技能前置
        {
            set
            {
                preNames = string.IsNullOrEmpty(value) ? null : value.Split('/');
            }
        }
        
        #endregion
    }

    [Serializable]
    internal struct ActiveData
    {
        public string name;

        public float damageScale;
        
        #region CSV
        [CSVField]
        public string 技能激活名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public float 技能激活伤害倍率
        {
            set
            {
                damageScale = value;
            }
        }
        #endregion
    }

    class Baker : Baker<SkillAuthoring>
    {
        public override void Bake(SkillAuthoring authoring)
        {
            SkillDefinitionData instance;
            int i, j, 
                numSkills = authoring._skills.Length,
                numMessages = authoring._messages == null ? 0 : authoring._messages.Length, 
                numAttributeMessages = (authoring._attributeParameter == null ? 0 : 1);
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                var bulletAuthoring = GetComponent<BulletAuthoring>();
                var bulletList = new List<SkillDefinition.Bullet>();
                var bulletDataIndices = new Dictionary<BulletData, int>();
                
                ref var root = ref builder.ConstructRoot<SkillDefinition>();

                int k,
                    bulletDataIndex,
                    numMessageIndices,
                    numPreIndices,
                    numBulletIndices,
                    numBullets = bulletAuthoring._bullets.Length;
                string messageName, preName;
                SkillDefinition.Bullet destinationBullet;
                BlobBuilderArray<int> bulletIndices, messageIndices, preIndices;
                var skills = builder.Allocate(ref root.skills, numSkills);
                for (i = 0; i < numSkills; ++i)
                {
                    ref var source = ref authoring._skills[i];
                    ref var destination = ref skills[i];

                    destination.layerMask = source.layerMask;
                    destination.duration = source.duration;
                    destination.cooldown = source.cooldown;
                    destination.rage = source.rage;

                    numBulletIndices = source.bullets.Length;
                    bulletIndices = builder.Allocate(ref destination.bulletIndices, numBulletIndices);
                    for (j = 0; j < numBulletIndices; ++j)
                    {
                        ref var sourceBullet = ref source.bullets[j];
                        if (!bulletDataIndices.TryGetValue(sourceBullet, out bulletDataIndex))
                        {
                            destinationBullet.index = -1;
                            for (k = 0; k < numBullets; ++k)
                            {
                                if (bulletAuthoring._bullets[k].name == sourceBullet.name)
                                {
                                    destinationBullet.index = k;

                                    break;
                                }
                            }

                            if (destinationBullet.index == -1)
                            {
                                Debug.LogError(
                                    $"Bullet {sourceBullet.name} of skill {source.name} can not been found!");

                                bulletDataIndex = -1;
                            }
                            else
                            {
                                destinationBullet.damageScale = sourceBullet.damageScale;
                                destinationBullet.chance = sourceBullet.chance;

                                bulletDataIndex = bulletList.Count;
                                bulletDataIndices[sourceBullet] = bulletDataIndex;
                                
                                bulletList.Add(destinationBullet);
                            }
                        }

                        bulletIndices[j] = bulletDataIndex;
                    }

                    numMessageIndices = source.messageNames == null ? 0 : source.messageNames.Length;
                    messageIndices = builder.Allocate(
                        ref destination.messageIndices, 
                        numMessageIndices + (source.rage > Mathf.Epsilon ? numAttributeMessages : 0));
                    for (j = 0; j < numMessageIndices; ++j)
                    {
                        messageIndices[j] = -1;

                        messageName = source.messageNames[j];
                        for (k = 0; k < numMessages; ++k)
                        {
                            if (authoring._messages[k].name == messageName)
                            {
                                messageIndices[j] = k;
                                
                                break;
                            }
                        }

                        if (messageIndices[j] == -1)
                            Debug.LogError($"Message {messageName} of skill {source.name} can not been found!");
                    }

                    if (source.rage > Mathf.Epsilon && numAttributeMessages > 0)
                        messageIndices[numMessageIndices] = numMessages;

                    numPreIndices = source.preNames == null ? 0 : source.preNames.Length;
                    preIndices = builder.Allocate(ref destination.preIndices, numPreIndices);
                    for (j = 0; j < numPreIndices; ++j)
                    {
                        preIndices[j] = -1;
                        
                        preName = source.preNames[j];
                        for (k = 0; k < numSkills; ++k)
                        {
                            if (authoring._skills[k].name == preName)
                            {
                                preIndices[j] = k;
                                
                                break;
                            }
                        }

                        if (preIndices[j] == -1)
                            Debug.LogError($"Pre {preName} of skill {source.name} can not been found!");
                    }
                }

                numBullets = bulletList.Count;
                var bullets = builder.Allocate(ref root.bullets, numBullets);
                for (i = 0; i < numBullets; ++i)
                    bullets[i] = bulletList[i];

                instance.definition = builder.CreateBlobAssetReference<SkillDefinition>(Allocator.Persistent);
            }
            
            AddBlobAsset(ref instance.definition, out _);

            Entity entity = GetEntity(authoring, TransformUsageFlags.None);
            AddComponent(entity, instance);

            SkillCooldownScale cooldownScale;
            cooldownScale.value = authoring._cooldownScale;
            AddComponent(entity, cooldownScale);

            //AddComponent<SkillLayerMask>(entity);
            AddComponent<SkillRage>(entity);

            var activeIndices = AddBuffer<SkillActiveIndex>(entity);
            int numActives = authoring._actives.Length;
            activeIndices.ResizeUninitialized(numActives);
            for (i = 0; i < numActives; ++i)
            {
                ref var source = ref authoring._actives[i];
                ref var destination = ref activeIndices.ElementAt(i);
                destination.value = -1;
                for (j = 0; j < numSkills; ++j)
                {
                    if (authoring._skills[j].name == source.name)
                    {
                        destination.value = j;

                        break;
                    }
                }

                if (destination.value == -1)
                    Debug.LogError(
                        $"Active skill {source.name} can not been found!");

                destination.damageScale = Mathf.Abs(source.damageScale) > Mathf.Epsilon ?  source.damageScale : 1.0f;
            }
            AddComponent<SkillStatus>(entity);

            var messages = AddBuffer<SkillMessage>(entity);
            messages.ResizeUninitialized(numMessages + numAttributeMessages);
            for (i = 0; i < numMessages; ++i)
            {
                ref var source = ref authoring._messages[i];
                ref var destination = ref messages.ElementAt(i);

                destination.type = source.type;
                destination.name = source.messageName;
                if(source.messageValue == null)
                    destination.value = default;
                else
                {
                    DependsOn(source.messageValue);
                    
                    destination.value = source.messageValue;
                }
            }

            if (authoring._attributeParameter != null)
            {
                SkillMessage message;
                message.type = SkillMessageType.Running;
                message.name = "UpdateAttribute";
                message.value = authoring._attributeParameter;
                
                messages[numMessages] = message;
            }
        }
    }

    [SerializeField] 
    internal float _cooldownScale = 1.0f;

    [SerializeField] 
    internal MessageData[] _messages;

    [SerializeField]
    internal SkillData[] _skills;
    
    public SkillData[] skills => _skills;

    #region CSV
    [SerializeField]
    [CSV("_skills", guidIndex = -1, nameIndex = 0)]
    internal string _skillsPath;
    #endregion

    [SerializeField] 
    internal ActiveData[] _actives;
    
    #region CSV
    [SerializeField]
    [CSV("_actives", guidIndex = -1, nameIndex = 0)]
    internal string _activesPath;
    #endregion

    [SerializeField] 
    internal int _rateMax = 100;

    [SerializeField] 
    internal Object _attributeParameter;

    public bool Apply(ref DynamicBuffer<Message> messages, ref DynamicBuffer<MessageParameter> messageParameters)
    {
        if (_attributeParameter == null)
            return false;
        
        Message message;
        message.key = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        message.name = "UpdateAttribute";
        message.value = _attributeParameter;
        messages.Add(message);

        MessageParameter parameter;
        parameter.messageKey = message.key;
        parameter.id = (int)EffectAttributeID.RageMax;
        parameter.value = _rateMax;
        messageParameters.Add(parameter);
        
        return true;
    }
}
#endif