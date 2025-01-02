using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

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
