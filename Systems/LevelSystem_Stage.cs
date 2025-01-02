using Unity.Collections;
using Unity.Entities;

public partial class LevelSystemManaged
{
    private struct Stage
    {
        private NativeList<int> __values;

        public Stage(SystemBase system)
        {
            system.RequireForUpdate<LevelDefinitionData>();
            system.RequireForUpdate<LevelStage>();
            
            __values = new NativeList<int>(Allocator.Persistent);
        }

        public void Dispose()
        {
            __values.Dispose();
        }

        public void Update(
            LevelManager manager, 
            in DynamicBuffer<LevelStage> stages, 
            ref LevelDefinition definition)
        {
            int numStages = stages.Length;
            __values.Resize(numStages, NativeArrayOptions.ClearMemory);
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
        bool isRestart = manager.isRestart;
        var stages = SystemAPI.GetSingletonBuffer<LevelStage>(!isRestart);
        
        ref var definition = ref SystemAPI.GetSingleton<LevelDefinitionData>().definition.Value;
        if (isRestart)
        {
            var stageResultStates = SystemAPI.GetSingletonBuffer<LevelStageResultStatus>();
            int numDefaultStages = definition.defaultStages.Length;

            SpawnerLayerMaskInclude include = default;
            SpawnerLayerMaskExclude exclude = default;
            stages.ResizeUninitialized(numDefaultStages);
            stageResultStates.ResizeUninitialized(numDefaultStages);
            for (int i = 0; i < numDefaultStages; ++i)
            {
                ref var source = ref definition.defaultStages[i];
                ref var destination = ref stageResultStates.ElementAt(i);
                
                stages.ElementAt(i).value = source.index;

                destination.layerMaskInclude = source.layerMaskInclude;
                destination.layerMaskExclude = source.layerMaskExclude;

                include.value |= source.layerMaskInclude;
                exclude.value |= source.layerMaskExclude;
            }
            
            if(SystemAPI.HasSingleton<SpawnerLayerMaskInclude>())
                SystemAPI.SetSingleton(include);
            
            if(SystemAPI.HasSingleton<SpawnerLayerMaskExclude>())
                SystemAPI.SetSingleton(exclude);
        }
        
        __stage.Update(manager, stages, ref definition);
    }
}
