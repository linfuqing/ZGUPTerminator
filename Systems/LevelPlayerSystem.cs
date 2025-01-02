using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct LevelPlayerSystem : ISystem
{
    private ComponentLookup<ThirdPersonPlayer> __instances;
    private EntityQuery __group;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __instances = state.GetComponentLookup<ThirdPersonPlayer>();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<LevelPlayer, PrefabLoadResult>()
                .WithNone<ThirdPersonPlayer>()
                .Build(ref state);
        
        state.RequireForUpdate(__group);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        using (var entityArray = __group.ToEntityArray(Allocator.Temp))
        using (var prefabLoadResults = __group.ToComponentDataArray<PrefabLoadResult>(Allocator.Temp))
        {
            var entityManager = state.EntityManager;
            entityManager.AddComponent(__group, new ComponentTypeSet(
                ComponentType.ReadWrite<ThirdPersonPlayer>(), 
                ComponentType.ReadWrite<ThirdPersonPlayerInputs>()));

            int count = entityArray.Length;
            var instances = new NativeArray<Entity>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for(int i = 0; i < count; ++i)
                instances[i] = state.EntityManager.Instantiate(prefabLoadResults[i].PrefabRoot);
            
            __instances.Update(ref state);
            
            ThirdPersonPlayer instance;
            instance.ControlledCamera = Entity.Null;
            for(int i = 0; i < count; ++i)
            {
                instance.ControlledCharacter = instances[i];

                __instances[entityArray[i]] = instance;
            }
            
            instances.Dispose();
        }
    }
}
