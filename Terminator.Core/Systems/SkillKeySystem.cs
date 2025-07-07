using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

[BurstCompile]
public partial struct SkillKeySystem : ISystem
{
    [BurstCompile]
    private struct Rebuild : IJobChunk
    {
        public BufferTypeHandle<BulletTag> bulletTagType;

        [ReadOnly] 
        public BufferTypeHandle<SkillActiveIndex> skillActiveIndexType;

        [ReadOnly] 
        public ComponentTypeHandle<SkillKeyDefinitionData> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var skillActiveIndices = chunk.GetBufferAccessor(ref skillActiveIndexType);
            var instances = chunk.GetNativeArray(ref instanceType);
            var bulletTagAccessor = chunk.GetBufferAccessor(ref bulletTagType);
            DynamicBuffer<BulletTag> bulletTags;
            BulletTag bulletTag;
            UnsafeHashMap<int, int> counts = default;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                bulletTags = bulletTagAccessor[i];
                
                instances[i].definition.Value
                    .GetBulletTags(skillActiveIndices[i].AsNativeArray(), ref bulletTags, ref counts);
            }
        }
    }
    
    private BufferTypeHandle<BulletTag> __bulletTagType;

    private BufferTypeHandle<SkillActiveIndex> __skillActiveIndexType;

    private ComponentTypeHandle<SkillKeyDefinitionData> __instanceType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __bulletTagType = state.GetBufferTypeHandle<BulletTag>();
        __skillActiveIndexType = state.GetBufferTypeHandle<SkillActiveIndex>(true);
        __instanceType = state.GetComponentTypeHandle<SkillKeyDefinitionData>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SkillActiveIndex, SkillKeyDefinitionData>()
                .WithAllRW<BulletTag>()
                .Build(ref state);
        
        __group.AddChangedVersionFilter(ComponentType.ReadOnly<SkillActiveIndex>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __bulletTagType.Update(ref state);
        __skillActiveIndexType.Update(ref state);
        __instanceType.Update(ref state);
        
        Rebuild rebuild;
        rebuild.bulletTagType = __bulletTagType;
        rebuild.skillActiveIndexType = __skillActiveIndexType;
        rebuild.instanceType = __instanceType;

        state.Dependency = rebuild.ScheduleParallelByRef(__group, state.Dependency);
    }
}
