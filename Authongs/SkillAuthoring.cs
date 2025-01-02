using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using UnityEngine;
using ZG;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

#if UNITY_EDITOR
[RequireComponent(typeof(BulletAuthoring))]
public class SkillAuthoring : MonoBehaviour
{
    [Serializable]
    public struct MessageData : IEquatable<MessageData>
    {
        public string name;
        public string messageName;
        public Object messageValue;

        public bool Equals(MessageData other)
        {
            return name == other.name && messageName == other.messageName && messageValue.Equals(other.messageValue);
        }
    }
    
    [Serializable]
    public struct BulletData : IEquatable<BulletData>
    {
        public string name;
        public int damage;
        public float chance;

        public bool Equals(BulletData other)
        {
            return name == other.name && damage == other.damage && chance.Equals(other.chance);
        }
    }
    
    [Serializable]
    internal struct SkillData
    {
        public string name;
        
        public float duration;
        public float cooldown;
        public BulletData[] bullets;
        public string[] messageNames;
        
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
                        bullet.damage = 0;
                        bullet.chance = 1.0f;
                    }
                    else
                    {
                        bullet.name = parameter.Remove(index);
                        
                        count = parameter.IndexOf(':', index + 1);
                        if (count == -1)
                        {
                            bullet.damage = int.Parse(parameter.Substring(index + 1));
                            bullet.chance = 1.0f;
                        }
                        else
                        {
                            bullet.damage = int.Parse(parameter.Substring(index + 1, count - index - 1));
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
        #endregion
    }

    [Serializable]
    internal struct ActiveData
    {
        public string name;
        
        #region CSV
        [CSVField]
        public string 技能激活名称
        {
            set
            {
                name = value;
            }
        }
        #endregion
    }

    class Baker : Baker<SkillAuthoring>
    {
        public override void Bake(SkillAuthoring authoring)
        {
            SkillDefinitionData instance;
            int i, j, numSkills = authoring._skills.Length, numMessages = authoring._messages == null ? 0 : authoring._messages.Length;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                var bulletAuthoring = GetComponent<BulletAuthoring>();
                var bulletList = new List<SkillDefinition.Bullet>();
                var bulletDataIndices = new Dictionary<BulletData, int>();
                
                ref var root = ref builder.ConstructRoot<SkillDefinition>();
                
                int k, bulletDataIndex, numMessageIndices, numBulletIndices, numBullets = bulletAuthoring._bullets.Length;
                string messageName;
                SkillDefinition.Bullet destinationBullet;
                BlobBuilderArray<int> bulletIndices, messageIndices;
                var skills = builder.Allocate(ref root.skills, numSkills);
                for (i = 0; i < numSkills; ++i)
                {
                    ref var source = ref authoring._skills[i];
                    ref var destination = ref skills[i];

                    destination.duration = source.duration;
                    destination.cooldown = source.cooldown;

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
                                destinationBullet.damage = sourceBullet.damage;
                                destinationBullet.chance = sourceBullet.chance;

                                bulletDataIndex = bulletList.Count;
                                bulletDataIndices[sourceBullet] = bulletDataIndex;
                                
                                bulletList.Add(destinationBullet);
                            }
                        }

                        bulletIndices[j] = bulletDataIndex;
                    }

                    numMessageIndices = source.messageNames == null ? 0 : source.messageNames.Length;
                    messageIndices = builder.Allocate(ref destination.messageIndices, numMessageIndices);
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
            }
            AddComponent<SkillStatus>(entity);

            var messages = AddBuffer<SkillMessage>(entity);
            messages.ResizeUninitialized(numMessages);
            for (i = 0; i < numMessages; ++i)
            {
                ref var source = ref authoring._messages[i];
                ref var destination = ref messages.ElementAt(i);
                destination.name = source.messageName;
                if(source.messageValue == null)
                    destination.value = default;
                else
                {
                    DependsOn(source.messageValue);
                    
                    destination.value = new WeakObjectReference<Object>(source.messageValue);
                }
            }
        }
    }

    [SerializeField] 
    internal MessageData[] _messages;
    
    [SerializeField]
    internal SkillData[] _skills;

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

}
#endif

public struct SkillDefinition
{
    public struct Bullet
    {
        public int index;
        public int damage;
        public float chance;
    }
    
    public struct Skill
    {
        public float duration;
        public float cooldown;
        public BlobArray<int> bulletIndices;
        public BlobArray<int> messageIndices;
    }

    public BlobArray<Bullet> bullets;
    public BlobArray<Skill> skills;
    
    public bool Update(
        double time, 
        in DynamicBuffer<SkillMessage> inputMessages, 
        in DynamicBuffer<SkillActiveIndex> skillActiveIndices, 
        ref DynamicBuffer<BulletActiveIndex> bulletActiveIndices, 
        ref DynamicBuffer<BulletStatus> bulletStates, 
        ref DynamicBuffer<SkillStatus> states, 
        ref DynamicBuffer<Message> outputMessages, 
        ref BulletDefinition bulletDefinition)
    {
        bulletActiveIndices.Clear();
        
        states.Resize(skills.Length, NativeArrayOptions.ClearMemory);

        SkillMessage inputMessage;
        SkillActiveIndex skillActiveIndex;
        BulletActiveIndex bulletActiveIndex;
        double cooldown, oldCooldown;
        float chance, value;
        int numSkillActiveIndices = skillActiveIndices.Length,
            numBulletIndices,
            numMessageIndices,
            messageOffset = outputMessages.IsCreated ? outputMessages.Length : -1,
            i,
            j;
        bool isCooldown, isSelected, result = false;
        for (i = 0; i < numSkillActiveIndices; ++i)
        {
            skillActiveIndex = skillActiveIndices[i];
            ref var skill = ref skills[skillActiveIndex.value];
            ref var status = ref states.ElementAt(skillActiveIndex.value);
            
            numBulletIndices = skill.bulletIndices.Length;
            cooldown = status.cooldown - time;
            if (cooldown > math.DBL_MIN_NORMAL)
                isCooldown = cooldown >= skill.cooldown;
            else
            {
                isCooldown = status.cooldown > math.DBL_MIN_NORMAL;
                if (isCooldown)
                {
                    oldCooldown = status.cooldown - (skill.duration + skill.cooldown);
                    for (j = 0; j < numBulletIndices; ++j)
                    {
                        ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                        if (bullet.index < bulletStates.Length)
                        {
                            ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                            if (bulletStatus.cooldown > oldCooldown + bulletDefinition.bullets[bullet.index].startTime)
                            {
                                isCooldown = false;

                                break;
                            }
                        }
                    }
                }

                if (!isCooldown)
                {
                    status.cooldown = time + (skill.duration + skill.cooldown);

                    if (skill.cooldown > math.FLT_MIN_NORMAL)
                    {
                        for (j = 0; j < numBulletIndices; ++j)
                        {
                            ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                            if (bullet.index < bulletStates.Length)
                            {
                                ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                                bulletStatus.cooldown = time + bulletDefinition.bullets[bullet.index].startTime;
                                bulletStatus.count = 0;
                            }
                        }
                        
                        if (messageOffset >= 0)
                        {
                            numMessageIndices = skill.messageIndices.Length;
                            if (numMessageIndices > 0)
                            {
                                result = true;

                                outputMessages.ResizeUninitialized(messageOffset + numMessageIndices);
                                for (j = 0; j < numMessageIndices; ++j)
                                {
                                    ref var outputMessage = ref outputMessages.ElementAt(messageOffset + j);
                                    inputMessage = inputMessages[skill.messageIndices[j]];
                                    outputMessage.key = 0;
                                    outputMessage.name = inputMessage.name;
                                    outputMessage.value = inputMessage.value;
                                }

                                messageOffset += numMessageIndices;
                            }
                        }
                    }
                    else
                        isCooldown = true;
                }
            }

            if (isCooldown)
            {
                long hash = math.aslong(status.cooldown);
                var random = Random.CreateFromIndex((uint)(hash >> 32) ^ (uint)hash);
                value = random.NextFloat();
                chance = 0;
                isSelected = false;
                for (j = 0; j < numBulletIndices; ++j)
                {
                    ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                    chance += bullet.chance;
                    
                    if (chance > 1.0f)
                    {
                        chance -= 1.0f;
                        
                        value = random.NextFloat();

                        isSelected = false;
                    }

                    if (isSelected || chance < value)
                        continue;

                    isSelected = true;
                    
                    bulletActiveIndex.value = bullet.index;
                    bulletActiveIndex.damage = bullet.damage;
                    bulletActiveIndices.Add(bulletActiveIndex);
                }
            }
        }

        return result;
    }
}

public struct SkillDefinitionData : IComponentData
{
    public BlobAssetReference<SkillDefinition> definition;
}

public struct SkillMessage : IBufferElementData
{
    public FixedString128Bytes name;
    public WeakObjectReference<Object> value;
}

public struct SkillActiveIndex : IBufferElementData
{
    public int value;
}

public struct SkillStatus : IBufferElementData
{
    public double cooldown;
}