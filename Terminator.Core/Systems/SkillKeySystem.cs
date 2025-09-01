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
        [ReadOnly] 
        public BufferTypeHandle<SkillActiveIndex> skillActiveIndexType;

        [ReadOnly] 
        public ComponentTypeHandle<SkillKeyDefinitionData> instanceType;

        public ComponentTypeHandle<BulletLayerMaskAndTags> bulletLayerMaskAndTagsType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var skillActiveIndices = chunk.GetBufferAccessor(ref skillActiveIndexType);
            var instances = chunk.GetNativeArray(ref instanceType);
            var bulletLayerMaskAndTagsArray = chunk.GetNativeArray(ref bulletLayerMaskAndTagsType);
            BulletLayerMaskAndTags bulletLayerMaskAndTags;
            UnsafeHashMap<int, int> counts = default;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                bulletLayerMaskAndTags = bulletLayerMaskAndTagsArray[i];
                
                bulletLayerMaskAndTags.value.tags = instances[i].definition.Value
                    .GetBulletTags(skillActiveIndices[i].AsNativeArray(), ref counts);

                bulletLayerMaskAndTagsArray[i] = bulletLayerMaskAndTags;
            }

            if (counts.IsCreated)
                counts.Dispose();
        }
    }
    
    private BufferTypeHandle<SkillActiveIndex> __skillActiveIndexType;

    private ComponentTypeHandle<SkillKeyDefinitionData> __instanceType;

    private ComponentTypeHandle<BulletLayerMaskAndTags> __bulletLayerMaskAndTagsType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __skillActiveIndexType = state.GetBufferTypeHandle<SkillActiveIndex>(true);
        __instanceType = state.GetComponentTypeHandle<SkillKeyDefinitionData>(true);
        __bulletLayerMaskAndTagsType = state.GetComponentTypeHandle<BulletLayerMaskAndTags>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SkillActiveIndex, SkillKeyDefinitionData>()
                .WithAllRW<BulletLayerMaskAndTags>()
                .Build(ref state);
        
        __group.AddChangedVersionFilter(ComponentType.ReadOnly<SkillActiveIndex>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __skillActiveIndexType.Update(ref state);
        __instanceType.Update(ref state);
        __bulletLayerMaskAndTagsType.Update(ref state);
        
        Rebuild rebuild;
        rebuild.skillActiveIndexType = __skillActiveIndexType;
        rebuild.instanceType = __instanceType;
        rebuild.bulletLayerMaskAndTagsType = __bulletLayerMaskAndTagsType;

        state.Dependency = rebuild.ScheduleParallelByRef(__group, state.Dependency);
    }
}
