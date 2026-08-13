using Unity.Collections;
using Unity.Entities;

#if UNITY_6000_5_OR_NEWER
using EntityID = UnityEngine.EntityId;
#else
using EntityID = System.Int32;
#endif

using static ZG.InstanceUtility;

public partial class LevelSystemManaged
{
    private struct Stage
    {
        private EntityID __managerEntityID;
        private NativeList<int> __values;

        public Stage(in AllocatorManager.AllocatorHandle allocator)
        {
            //system.RequireForUpdate<LevelDefinitionData>();
            //system.RequireForUpdate<LevelStage>();

            __managerEntityID = default;
            
            __values = new NativeList<int>(allocator);
        }

        public void Dispose()
        {
            __values.Dispose();
        }

        public void Clear(LevelManager manager, ref LevelDefinition definition)
        {
            var managerEntityID = manager == null ? default : manager.GetEntityID();
            if (managerEntityID != __managerEntityID)
                manager = null;
            
            int numValues = __values.Length;
            for(int i = 0; i < numValues; ++i)
            {
                ref var value = ref __values.ElementAt(i);
                if (value != -1)
                {
                    if(manager != null)
                        manager.DisableStage(definition.stages[value].name.ToString());

                    __values[i] = -1;
                }
            }
            
            __values.Clear();
        }

        public void Update(
            LevelManager manager, 
            in DynamicBuffer<LevelStage> stages, 
            ref LevelDefinition definition)
        {
            var managerEntityID = manager == null ? default : manager.GetEntityID();
            if (managerEntityID != __managerEntityID)
            {
                Clear(null, ref definition);
                
                __managerEntityID = managerEntityID;
            }
            
            int numStages = stages.Length, numValues = __values.Length;
            if (numValues < numStages)
            {
                __values.Resize(numStages, NativeArrayOptions.UninitializedMemory);

                for (int i = numValues; i < numStages; i++)
                    __values[i] = -1;
            }

            for(int i = 0; i < numStages; ++i)
            {
                ref var source = ref __values.ElementAt(i);
                var destination = stages[i];
                if(destination.value == source)
                    continue;

                if(source != -1)
                    manager.DisableStage(definition.stages[source].name.ToString());
            
                if(destination.value != -1)
                    manager.EnableStage(definition.stages[destination.value].name.ToString());
            
                __values[i] = destination.value;
            }
        }
    }
    
    private Stage __stage;

    private void __UpdateStage(LevelManager manager)
    {
        if (!SystemAPI.HasSingleton<LevelStage>() || !SystemAPI.HasSingleton<LevelDefinitionData>())
            return;

        bool isRestart = manager.isRestart;
        var stages = SystemAPI.GetSingletonBuffer<LevelStage>(!isRestart);
        
        ref var definition = ref SystemAPI.GetSingleton<LevelDefinitionData>().definition.Value;
        if (isRestart)
        {
            var stageResultStates = SystemAPI.GetSingletonBuffer<LevelStageResultStatus>();
            int numDefaultStages = definition.defaultStages.Length;

            SpawnerLayerMaskAndTagsInclude include = default;
            SpawnerLayerMaskAndTagsExclude exclude = default;
            stages.ResizeUninitialized(numDefaultStages);
            stageResultStates.ResizeUninitialized(numDefaultStages);
            for (int i = 0; i < numDefaultStages; ++i)
            {
                ref var source = ref definition.defaultStages[i];
                ref var destination = ref stageResultStates.ElementAt(i);
                
                stages.ElementAt(i).value = source.index;

                destination.layerMaskAndTagsInclude = definition.layerMaskAndTags[source.layerMaskAndTagsIncludeIndex];
                destination.layerMaskAndTagsExclude = definition.layerMaskAndTags[source.layerMaskAndTagsExcludeIndex];

                include.value |= destination.layerMaskAndTagsInclude;
                exclude.value |= destination.layerMaskAndTagsExclude;
            }
            
            if(SystemAPI.HasSingleton<SpawnerLayerMaskAndTagsInclude>())
                SystemAPI.SetSingleton(include);
            
            if(SystemAPI.HasSingleton<SpawnerLayerMaskAndTagsExclude>())
                SystemAPI.SetSingleton(exclude);
            
            __stage.Clear(manager, ref definition);
        }
        
        __stage.Update(manager, stages, ref definition);
    }
}