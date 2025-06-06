using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ZG;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;


public enum SkillMessageType
{
    Cooldown,
    Running
}

public struct SkillDefinition
{
    public struct Bullet
    {
        public int index;
        public float damageScale;
        public float chance;
    }
    
    public struct Skill
    {
        public int layerMask;
        public int layerMaskInclude;
        public int layerMaskExclude;
        public float rage;
        public float duration;
        public float cooldown;
        public BlobArray<int> bulletIndices;
        public BlobArray<int> messageIndices;
        public BlobArray<int> preIndices;
    }

    public BlobArray<Bullet> bullets;
    public BlobArray<Skill> skills;
    
    public bool Update(
        float cooldownScale, 
        double time, 
        in DynamicBuffer<SkillMessage> inputMessages, 
        in DynamicBuffer<SkillActiveIndex> skillActiveIndices, 
        ref DynamicBuffer<BulletActiveIndex> bulletActiveIndices, 
        ref DynamicBuffer<BulletStatus> bulletStates, 
        ref DynamicBuffer<SkillStatus> states, 
        ref DynamicBuffer<Message> outputMessages, 
        ref DynamicBuffer<MessageParameter> outputMessageParameters, 
        ref BulletDefinition bulletDefinition, 
        ref int layerMask, 
        ref float rage)
    {
        bulletActiveIndices.Clear();
        
        states.Resize(skills.Length, NativeArrayOptions.ClearMemory);

        MessageParameter messageParameter;
        Message outputMessage;
        SkillMessage inputMessage;
        SkillActiveIndex skillActiveIndex;
        BulletActiveIndex bulletActiveIndex;
        Random random;
        float chance, value;
        int numActiveIndices = skillActiveIndices.Length,
            numPreIndices, 
            numBulletIndices,
            numMessageIndices,
            layerMaskInclude = 0, 
            layerMaskExclude = 0, 
            preIndex,
            i, j, k;
        bool isCooldown, isChanged, isReload, result = false;
        for (i = 0; i < numActiveIndices; ++i)
        {
            skillActiveIndex = skillActiveIndices[i];
            ref var skill = ref skills[skillActiveIndex.value];
            ref var status = ref states.ElementAt(skillActiveIndex.value);

            if (status.cooldown > math.DBL_MIN_NORMAL)
            {
                if (status.cooldown > time)
                    continue;
            }
            else
                status.cooldown = time;

            numBulletIndices = skill.bulletIndices.Length;

            isCooldown = status.cooldown + skill.duration > time;
            if (isCooldown)
            {
                isChanged = false;
                for (j = 0; j < numBulletIndices; ++j)
                {
                    ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                    if (bullet.index < bulletStates.Length)
                    {
                        ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                        if (/*bulletStatus.cooldown > time || */bulletStatus.version != 0)
                        {
                            isChanged = true;

                            break;
                        }
                    }
                }

                if (!isChanged)
                    status.cooldown = time;

                isChanged = SkillMessageType.Cooldown == status.messageType;

                isReload = true;
            }
            else
            {
                value = skill.cooldown * cooldownScale;
                if (value > math.FLT_MIN_NORMAL)
                {
                    status.cooldown = time + value;

                    isChanged = false;
                }
                else
                {
                    status.messageType = SkillMessageType.Cooldown;

                    isCooldown = true;

                    isChanged = true;
                }
                
                isReload = false;
            }
            
            if (isChanged)
            {
                isCooldown = skill.layerMask == 0 || (skill.layerMask & layerMask) != 0;
                if (isCooldown)
                {
                    numPreIndices = skill.preIndices.Length;
                    isCooldown = numPreIndices < 1;
                    for (j = 0; j < numPreIndices; ++j)
                    {
                        preIndex = skill.preIndices[j];
                        for (k = 0; k < numActiveIndices; ++k)
                        {
                            if (skillActiveIndices[k].value == preIndex)
                                break;
                        }

                        if (k < numActiveIndices)
                        {
                            isCooldown = true;
                            break;
                        }
                    }
                }

                if (isCooldown)
                {
                    if (rage < skill.rage)
                        isCooldown = false;
                    else
                        rage -= skill.rage;
                }

                if (isCooldown && isReload)
                {
                    for (j = 0; j < numBulletIndices; ++j)
                    {
                        ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                        if (bullet.index < bulletStates.Length)
                        {
                            ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                            bulletStatus.cooldown =
                                time + bulletDefinition.bullets[bullet.index].startTime;
                            bulletStatus.times = 0;
                            bulletStatus.count = 0;
                            bulletStatus.version = 0;
                        }
                    }
                }
            }

            random = default;

            if (isCooldown == (SkillMessageType.Cooldown == status.messageType))
            {
                status.messageType = isCooldown ? SkillMessageType.Running : SkillMessageType.Cooldown;
                
                if (outputMessages.IsCreated)
                {
                    numMessageIndices = skill.messageIndices.Length;
                    if (numMessageIndices > 0)
                    {
                        messageParameter.value = -(int)math.round(skill.rage);
                        messageParameter.id = (int)EffectAttributeID.Rage;
                        
                        for (j = 0; j < numMessageIndices; ++j)
                        {
                            inputMessage = inputMessages[skill.messageIndices[j]];
                            if(inputMessage.type != status.messageType)
                                continue;
                                
                            result = true;

                            outputMessage.name = inputMessage.name;
                            outputMessage.value = inputMessage.value;

                            if (isChanged)
                            {
                                __GetOrCreateRandom(status.cooldown, ref random);
                                
                                outputMessage.key = random.NextInt();

                                if (messageParameter.value != 0)
                                {
                                    messageParameter.messageKey = outputMessage.key;

                                    outputMessageParameters.Add(messageParameter);
                                }
                            }
                            else
                                outputMessage.key = 0;

                            outputMessages.Add(outputMessage);
                        }
                    }
                }
                
                if (isCooldown && !isReload)
                {
                    for (j = 0; j < numBulletIndices; ++j)
                    {
                        ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                        if (bullet.index < bulletStates.Length)
                        {
                            ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                            //bulletStatus.cooldown = math.max(bulletStatus.cooldown, time);bulletStatus.cooldown =
                            bulletStatus.cooldown = time + bulletDefinition.bullets[bullet.index].startTime;
                            bulletStatus.times = 0;
                            bulletStatus.count = 0;
                            bulletStatus.version = 0;
                        }
                    }
                }
            }
            
            if (isCooldown)
            {
                layerMaskInclude |= skill.layerMaskInclude;
                layerMaskExclude |= skill.layerMaskExclude;
                
                __GetOrCreateRandom(status.cooldown, ref random);

                value = random.NextFloat();
                chance = 0;
                isChanged = false;
                for (j = 0; j < numBulletIndices; ++j)
                {
                    ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                    chance += bullet.chance;
                    
                    if (chance > 1.0f)
                    {
                        chance -= 1.0f;
                        
                        value = random.NextFloat();

                        isChanged = false;
                    }

                    if (isChanged || chance < value)
                        continue;

                    isChanged = true;
                    
                    bulletActiveIndex.value = bullet.index;
                    bulletActiveIndex.damageScale = bullet.damageScale * skillActiveIndex.damageScale;
                    bulletActiveIndices.Add(bulletActiveIndex);
                }
            }
        }

        layerMask = layerMaskInclude & ~layerMaskExclude;

        return result;
    }

    private void __GetOrCreateRandom(double cooldown, ref Random random)
    {
        if (random.state != 0)
            return;
        
        long hash = math.aslong(cooldown);
        random = Random.CreateFromIndex((uint)(hash >> 32) ^ (uint)hash);
    }
}

public struct SkillDefinitionData : IComponentData
{
    public BlobAssetReference<SkillDefinition> definition;
}

public struct SkillCooldownScale : IComponentData
{
    public float value;
}

public struct SkillRage : IComponentData
{
    public float value;
}

public struct SkillLayerMask : IComponentData
{
    public int value;
}

public struct SkillMessage : IBufferElementData
{
    public SkillMessageType type;
    public FixedString128Bytes name;
    public UnityObjectRef<Object> value;
}

public struct SkillActiveIndex : IBufferElementData
{
    public int value;

    public float damageScale;
}

public struct SkillStatus : IBufferElementData
{
    public SkillMessageType messageType;
    
    //public double time;
    public double cooldown;
}
