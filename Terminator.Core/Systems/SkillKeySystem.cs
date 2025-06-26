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

        public ComponentTypeHandle<SkillKeyLayerMask> layerMaskType;
        
        public ComponentTypeHandle<BulletLayerMask> bulletLayerMaskType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var skillActiveIndices = chunk.GetBufferAccessor(ref skillActiveIndexType);
            var instances = chunk.GetNativeArray(ref instanceType);
            var layerMasks = chunk.GetNativeArray(ref layerMaskType);
            var bulletLayerMasks = chunk.GetNativeArray(ref bulletLayerMaskType);
            BulletLayerMask bulletLayerMask;
            SkillKeyLayerMask layerMask;
            UnsafeHashMap<int, int> counts = default;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                layerMask = layerMasks[i];
                
                bulletLayerMask = bulletLayerMasks[i];

                bulletLayerMask.value &= ~layerMask.value;
                
                layerMask.value = instances[i].definition.Value
                    .GetBulletLayerMask(skillActiveIndices[i].AsNativeArray(), ref counts);
                
                bulletLayerMask.value |= layerMask.value;

                bulletLayerMasks[i] = bulletLayerMask;

                layerMasks[i] = layerMask;
            }
        }
    }
    
    private BufferTypeHandle<SkillActiveIndex> __skillActiveIndexType;

    private ComponentTypeHandle<SkillKeyDefinitionData> __instanceType;

    private ComponentTypeHandle<SkillKeyLayerMask> __layerMaskType;
        
    private ComponentTypeHandle<BulletLayerMask> __bulletLayerMaskType;

    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __skillActiveIndexType = state.GetBufferTypeHandle<SkillActiveIndex>(true);
        __instanceType = state.GetComponentTypeHandle<SkillKeyDefinitionData>(true);
        __layerMaskType = state.GetComponentTypeHandle<SkillKeyLayerMask>();
        __bulletLayerMaskType = state.GetComponentTypeHandle<BulletLayerMask>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SkillActiveIndex, SkillKeyDefinitionData>()
                .WithAllRW<SkillKeyLayerMask, BulletLayerMask>()
                .Build(ref state);
        
        __group.AddChangedVersionFilter(ComponentType.ReadOnly<SkillActiveIndex>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __skillActiveIndexType.Update(ref state);
        __instanceType.Update(ref state);
        __layerMaskType.Update(ref state);
        __bulletLayerMaskType.Update(ref state);
        
        Rebuild rebuild;
        rebuild.skillActiveIndexType = __skillActiveIndexType;
        rebuild.instanceType = __instanceType;
        rebuild.layerMaskType = __layerMaskType;
        rebuild.bulletLayerMaskType = __bulletLayerMaskType;

        state.Dependency = rebuild.ScheduleParallelByRef(__group, state.Dependency);
    }
}
