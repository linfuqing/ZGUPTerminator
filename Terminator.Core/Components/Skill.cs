using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
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
        public int layerMaskInclude;
        public int layerMaskExclude;
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
        ref BulletDefinition bulletDefinition, 
        out int layerMask)
    {
        bulletActiveIndices.Clear();
        
        states.Resize(skills.Length, NativeArrayOptions.ClearMemory);

        SkillMessage inputMessage;
        SkillActiveIndex skillActiveIndex;
        BulletActiveIndex bulletActiveIndex;
        Random random;
        float chance, value;
        long hash;
        int numActiveIndices = skillActiveIndices.Length,
            numPreIndices, 
            numBulletIndices,
            numMessageIndices,
            messageOffset = outputMessages.IsCreated ? outputMessages.Length : -1,
            layerMaskInclude = 0, 
            layerMaskExclude = 0, 
            preIndex,
            i, j, k;
        bool isCooldown, isSelected, result = false;
        for (i = 0; i < numActiveIndices; ++i)
        {
            skillActiveIndex = skillActiveIndices[i];
            ref var skill = ref skills[skillActiveIndex.value];
            ref var status = ref states.ElementAt(skillActiveIndex.value);
            
            numBulletIndices = skill.bulletIndices.Length;
            if (status.cooldown > time)
                continue;
            
            numPreIndices = skill.preIndices.Length;
            isSelected = numPreIndices < 1;
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
                    isSelected = true;
                    break;
                }
            }

            if (!isSelected)
                continue;
            
            isCooldown = status.cooldown + skill.duration > time;
            if (isCooldown)
            {
                isSelected = false;
                for (j = 0; j < numBulletIndices; ++j)
                {
                    ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                    if (bullet.index < bulletStates.Length)
                    {
                        ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                        if (bulletStatus.cooldown > time || bulletStatus.version != 0)
                        {
                            isSelected = true;

                            break;
                        }
                    }
                }

                if (!isSelected)
                    status.cooldown = time;
            }
            else
            {
                status.cooldown = time + skill.cooldown * cooldownScale;
                //status.cooldown = cooldown + skill.duration;

                if (status.cooldown > time)
                {
                    for (j = 0; j < numBulletIndices; ++j)
                    {
                        ref var bullet = ref this.bullets[skill.bulletIndices[j]];
                        if (bullet.index < bulletStates.Length)
                        {
                            ref var bulletStatus = ref bulletStates.ElementAt(bullet.index);
                            bulletStatus.cooldown = status.cooldown + bulletDefinition.bullets[bullet.index].startTime;
                            bulletStatus.count = 0;
                            bulletStatus.version = 0;
                        }
                    }
                }
                else
                    isCooldown = true;
            }

            if (isCooldown == (SkillMessageType.Cooldown != status.messageType))
            {
                status.messageType = isCooldown ? SkillMessageType.Running : SkillMessageType.Cooldown;
                
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
                            if(inputMessage.type != status.messageType)
                                continue;
                                
                            outputMessage.key = 0;
                            outputMessage.name = inputMessage.name;
                            outputMessage.value = inputMessage.value;
                        }

                        messageOffset += numMessageIndices;
                    }
                }
            }
            
            if (isCooldown)
            {
                layerMaskInclude |= skill.layerMaskInclude;
                layerMaskExclude |= skill.layerMaskExclude;
                
                hash = math.aslong(status.cooldown);
                random = Random.CreateFromIndex((uint)(hash >> 32) ^ (uint)hash);
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
                    bulletActiveIndex.damageScale = bullet.damageScale * skillActiveIndex.damageScale;
                    bulletActiveIndices.Add(bulletActiveIndex);
                }
            }
        }

        layerMask = layerMaskInclude & ~layerMaskExclude;

        return result;
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

public struct SkillLayerMask : IComponentData
{
    public int value;
}

public struct SkillMessage : IBufferElementData
{
    public SkillMessageType type;
    public FixedString128Bytes name;
    public WeakObjectReference<Object> value;
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
