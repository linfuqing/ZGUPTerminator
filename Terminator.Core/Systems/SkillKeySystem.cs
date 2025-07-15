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

        public ComponentTypeHandle<BulletLayerMask> bulletLayerMaskType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var skillActiveIndices = chunk.GetBufferAccessor(ref skillActiveIndexType);
            var instances = chunk.GetNativeArray(ref instanceType);
            var bulletLayerMasks = chunk.GetNativeArray(ref bulletLayerMaskType);
            BulletLayerMask bulletLayerMask;
            UnsafeHashMap<int, int> counts = default;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                bulletLayerMask = bulletLayerMasks[i];
                
                bulletLayerMask.tags = instances[i].definition.Value
                    .GetBulletTags(skillActiveIndices[i].AsNativeArray(), ref counts);

                bulletLayerMasks[i] = bulletLayerMask;
            }

            if (counts.IsCreated)
                counts.Dispose();
        }
    }
    
    private BufferTypeHandle<SkillActiveIndex> __skillActiveIndexType;

    private ComponentTypeHandle<SkillKeyDefinitionData> __instanceType;

    private ComponentTypeHandle<BulletLayerMask> __bulletLayerMaskType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __skillActiveIndexType = state.GetBufferTypeHandle<SkillActiveIndex>(true);
        __instanceType = state.GetComponentTypeHandle<SkillKeyDefinitionData>(true);
        __bulletLayerMaskType = state.GetComponentTypeHandle<BulletLayerMask>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SkillActiveIndex, SkillKeyDefinitionData>()
                .WithAllRW<BulletLayerMask>()
                .Build(ref state);
        
        __group.AddChangedVersionFilter(ComponentType.ReadOnly<SkillActiveIndex>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __skillActiveIndexType.Update(ref state);
        __instanceType.Update(ref state);
        __bulletLayerMaskType.Update(ref state);
        
        Rebuild rebuild;
        rebuild.skillActiveIndexType = __skillActiveIndexType;
        rebuild.instanceType = __instanceType;
        rebuild.bulletLayerMaskType = __bulletLayerMaskType;

        state.Dependency = rebuild.ScheduleParallelByRef(__group, state.Dependency);
    }
}
