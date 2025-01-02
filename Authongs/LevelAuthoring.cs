using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEngine;
using Random = Unity.Mathematics.Random;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using ZG;

[CustomPropertyDrawer(typeof(LevelStageOption))]
public class LevelStageOptionDrawer : PropertyDrawer
{
    private static int FieldToLayerMask(int field)
    {
        int mask = 0;
        var layers = InternalEditorUtility.layers;
        bool everything = true;
        for (int c = 0; c < layers.Length; c++)
        {
            if ((field & (1 << c)) != 0)
                mask |= 1 << LayerMask.NameToLayer(layers[c]);
            else
            {
                mask &= ~(1 << LayerMask.NameToLayer(layers[c]));
                everything = false;
            }
        }

        return everything ? -1 : mask;
    }

    private static int LayerMaskToField(int mask)
    {
        int field = 0;
        var layers = InternalEditorUtility.layers;
        bool everything = true;
        for (int c = 0; c < layers.Length; c++)
        {
            if ((mask & (1 << LayerMask.NameToLayer(layers[c]))) != 0)
                field |= 1 << c;
            else
                everything = false;
        }

        return everything ? -1 : field;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 2.0f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        position.height *= 0.5f;
        var type = property.FindPropertyRelative("type");
        EditorGUI.PropertyField(position, type);

        var value = property.FindPropertyRelative("value");
        position.y += position.height;
        switch ((LevelStageOption.Type)type.enumValueFlag)
        {
            case LevelStageOption.Type.SpawnerLayerMaskInclude:
            case LevelStageOption.Type.SpawnerLayerMaskExclude:
            case LevelStageOption.Type.SpawnerEntityRemaining:
                EditorGUI.BeginProperty(position, label, value);
                value.intValue = FieldToLayerMask(
                    EditorGUI.MaskField(position, new GUIContent(value.displayName), LayerMaskToField(value.intValue), InternalEditorUtility.layers));
                EditorGUI.EndProperty();
                break;
            default:
                EditorGUI.PropertyField(position, value);
                break;
        }
    }
}

public class LevelAuthoring : MonoBehaviour
{
    [Serializable]
    public struct DefaultStage
    {
        public string name;
        public LayerMask layerMask;
    }
    
    [Serializable]
    public struct Stage
    {
        public string name;
        
        [Tooltip("下一阶段，不填则没有")]
        public string nextStageName;
        
        //[Tooltip("阶段经验值满足后，激活刷怪标签并跳到下一阶段（如有）")]
        //public int exp;
        
        //[Tooltip("阶段经验值满足后，激活该刷怪标签")]
        //public LayerMask spawnerLayerMaskInclude;
        //[Tooltip("阶段经验值满足后，剔除该刷怪标签")]
        //public LayerMask spawnerLayerMaskExclude;

        [Tooltip("阶段所有条件满足后，激活刷怪标签并跳到下一阶段（如有）")]
        public LevelStageOption[] conditions;
        
        [Tooltip("完成该阶段执行的结果")]
        public LevelStageOption[] results;
        
        #region CSV

        [CSVField]
        public string 阶段名称
        {
            set
            {
                name = value;
            }
        }

        [CSVField]
        public string 下一阶段
        {
            set
            {
                nextStageName = value;
            }
        }

        [CSVField]
        public string 阶段条件
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    conditions = null;

                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length, index;

                conditions = new LevelStageOption[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    ref var condition = ref conditions[i];
                    ref var parameter = ref parameters[i];

                    index = parameter.IndexOf(':');
                    condition.type = (LevelStageOption.Type)int.Parse(parameter.Remove(index));
                    condition.value = int.Parse(parameter.Substring(index + 1));
                }
            }
        }
        
        [CSVField]
        public string 阶段结果
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    results = null;

                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length, index;

                results = new LevelStageOption[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    ref var result = ref results[i];
                    ref var parameter = ref parameters[i];

                    index = parameter.IndexOf(':');
                    result.type = (LevelStageOption.Type)int.Parse(parameter.Remove(index));
                    result.value = int.Parse(parameter.Substring(index + 1));
                }
            }
        }
        #endregion
    }

    [Serializable]
    internal struct Area
    {
        public string name;
        
        public float3 position;
        public float width;
        public float height;
        public float length;
    }

    class Baker : Baker<LevelAuthoring>
    {
        public override void Bake(LevelAuthoring authoring)
        {
            Entity entity = GetEntity(authoring, TransformUsageFlags.None);

            int numDefaultStages = authoring._defaultStages == null ? 0 : authoring._defaultStages.Length, 
                numStages = authoring._stages.Length, i, j;
            LevelDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<LevelDefinition>();

                var defaultStage = builder.Allocate(ref root.defaultStages, numDefaultStages);
                for (i = 0; i < numDefaultStages; ++i)
                {
                    ref var source = ref authoring._defaultStages[i];
                    ref var destination = ref defaultStage[i];
                
                    destination.index = -1;
                    for (j = 0; j < numStages; ++j)
                    {
                        if (authoring._stages[j].name == source.name)
                        {
                            destination.index = j;
                        
                            break;
                        }
                    }
                
                    if (destination.index == -1)
                        Debug.LogError(
                            $"The default stage {source.name} can not been found!");

                    destination.layerMaskInclude = source.layerMask.value;
                    destination.layerMaskExclude = 0;
                }

                int numOptions;
                BlobBuilderArray<LevelStageOption> options;
                var stages = builder.Allocate(ref root.stages, numStages);
                for (i = 0; i < numStages; ++i)
                {
                    ref var source = ref authoring._stages[i];
                    ref var destination = ref stages[i];

                    destination.name = source.name;

                    //destination.exp = source.exp;
                    //destination.spawnerLayerMaskInclude = source.spawnerLayerMaskInclude.value;
                    //destination.spawnerLayerMaskExclude = source.spawnerLayerMaskExclude.value;
                    //destination.nextStage = -1;

                    numOptions = source.conditions == null ? 0 : source.conditions.Length;
                    options = builder.Allocate(ref destination.conditions, numOptions);
                    for (j = 0; j < numOptions; ++j)
                        options[j] = source.conditions[j];
                    
                    numOptions = source.results == null ? 0 : source.results.Length;
                    options = builder.Allocate(ref destination.results, numOptions);
                    for (j = 0; j < numOptions; ++j)
                        options[j] = source.results[j];

                    destination.nextStageIndex = -1;
                    if (!string.IsNullOrEmpty(source.nextStageName))
                    {
                        for (j = 0; j < numStages; ++j)
                        {
                            if (source.nextStageName == authoring._stages[j].name)
                            {
                                destination.nextStageIndex = j;

                                break;
                            }
                        }
                        
                        if (destination.nextStageIndex == -1)
                            Debug.LogError(
                                $"The next stage name {source.nextStageName} of stage {source.name} can not been found!");
                    }
                }

                int numAreas = authoring._areas == null ? 0 : authoring._areas.Length;
                var areas = builder.Allocate(ref root.areas, numAreas);
                for (i = 0; i < numAreas; ++i)
                {
                    ref var source = ref authoring._areas[i];
                    ref var destination = ref areas[i];

                    destination.aabb.Center = source.position;
                    destination.aabb.Extents = math.float3(source.width * 0.5f, source.height * 0.5f, source.length * 0.5f);
                }

                instance.definition = builder.CreateBlobAssetReference<LevelDefinition>(Allocator.Persistent);
            }

            AddBlobAsset(ref instance.definition, out _);

            AddComponent(entity, instance);

            LevelStatus status;
            status.value = 0;
            status.max = authoring._max;
            status.expMax = authoring._expMax;
            status.exp = 0;
            status.gold = 0;
            status.count = 0;
            AddComponent(entity, status);

            var defaultStages = AddBuffer<LevelStage>(entity);
            var stageResultStates = AddBuffer<LevelStageResultStatus>(entity);
            defaultStages.ResizeUninitialized(numDefaultStages);
            stageResultStates.Resize(numDefaultStages, NativeArrayOptions.ClearMemory);
            for (i = 0; i < numDefaultStages; ++i)
            {
                ref var source = ref authoring._defaultStages[i];
                ref var destination = ref defaultStages.ElementAt(i);
                
                destination.value = -1;
                for (j = 0; j < numStages; ++j)
                {
                    if (authoring._stages[j].name == source.name)
                    {
                        destination.value = j;
                        
                        break;
                    }
                }
                
                if (destination.value == -1)
                    Debug.LogError(
                        $"The default stage {source.name} can not been found!");

                stageResultStates.ElementAt(i).layerMaskInclude = source.layerMask.value;
            }
            
            AddComponent<LevelStageConditionStatus>(entity);
            
            int numPrefabs = authoring._prefabs == null ? 0 : authoring._prefabs.Length;
            if (numPrefabs > 0)
            {
                var levelPrefabs = AddBuffer<LevelPrefab>(entity);
                levelPrefabs.ResizeUninitialized(numPrefabs);
                for (i = 0; i < numPrefabs; ++i)
                    levelPrefabs.ElementAt(i).reference = new EntityPrefabReference(authoring._prefabs[i]);
            }
        }
    }

    [SerializeField] 
    internal int _max = 150;

    [SerializeField] 
    internal int _expMax = 10;

    [SerializeField] 
    internal DefaultStage[] _defaultStages;

    //[SerializeField] 
    //internal string[] _defaultStageNames;

    [SerializeField] 
    internal Stage[] _stages;

    [SerializeField] 
    internal Area[] _areas;

    [SerializeField] 
    internal GameObject[] _prefabs;

    [CSV("_stages", guidIndex = -1, nameIndex = 0)]
    [SerializeField]
    internal string _stagesPath;
}
#endif

[Serializable]
public struct LevelStageOption
{
    public enum Type
    {
        SpawnerLayerMaskInclude = 1,
        SpawnerLayerMaskExclude = 2,
        SpawnerEntityRemaining = 7, 
        PrefabRemaining = 3, 
        PlayerArea = 4, 
        Millisecond = 5,
        Value = 0, 
        Max = 6, 
        ExpMax = 8, 
        Exp = 9
    }

    [Tooltip("SpawnerLayerMaskOverride:激活对应标签的刷怪圈。SpawnerEntityRemaining：对应刷怪圈标签的怪都被消灭。PrefabRemaining：对应预制体索引的怪都被消灭。Value：关卡进度")]
    public Type type;
    
    public int value;
    
    public bool Judge(
        float deltaTime, 
        ref LevelStageConditionStatus condition, 
        ref BlobArray<LevelDefinition.Area> areas, 
        in float3 playerPosition, 
        in LevelStatus status, 
        in SpawnerLayerMaskOverride spawnerLayerMaskOverride, 
        in SpawnerSingleton spawnerSingleton, 
        in NativeArray<LevelPrefab> prefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in ComponentLookup<SpawnerDefinitionData> spawners, 
        in ComponentLookup<RequestEntityPrefabLoaded> prefabReferences)
    {
        switch (type)
        {
            case Type.Value:
                return value <= status.value;
            case Type.Max:
                return value < status.max;
            case Type.ExpMax:
                return value < status.expMax;
            case Type.Exp:
                return value <= status.exp;
            case Type.SpawnerLayerMaskInclude:
                return (value & spawnerLayerMaskOverride.value) == value;
            case Type.SpawnerLayerMaskExclude:
                return (value & spawnerLayerMaskOverride.value) == 0;
            case Type.SpawnerEntityRemaining:
                if (condition.version != spawnerSingleton.version)
                {
                    condition.version = spawnerSingleton.version;
                    condition.value = 0;
                    using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                    {
                        SpawnerEntity spawnerEntity;
                        SpawnerDefinitionData spawner;
                        int numKeys = spawnerEntities.Unique();
                        for (int i = 0; i < numKeys; ++i)
                        {
                            spawnerEntity = spawnerEntities[i];
                            if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                                continue;

                            ref var definition = ref spawner.definition.Value;
                            if(definition.spawners.Length <= spawnerEntity.index)
                                continue;

                            if ((definition.spawners[spawnerEntity.index].layerMask & value) == 0)
                                continue;

                            condition.value = 1;

                            break;
                        }
                    }
                }

                return value == 0;
            case Type.PrefabRemaining:
                if (condition.version != spawnerSingleton.version)
                {
                    condition.version = spawnerSingleton.version;
                    condition.value = 0;

                    using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                    {
                        RequestEntityPrefabLoaded prefabReference;
                        SpawnerEntity spawnerEntity;
                        SpawnerDefinitionData spawner;
                        DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                        var prefab = prefabs[value];
                        int numKeys = spawnerEntities.Unique();
                        for(int i = 0; i < numKeys; ++i)
                        {
                            spawnerEntity = spawnerEntities[i];
                            if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                                continue;

                            ref var definition = ref spawner.definition.Value;
                            if(definition.spawners.Length <= spawnerEntity.index)
                                continue;

                            spawnerPrefabBuffer = spawnerPrefabs[spawnerEntity.spawner];

                            if (!prefabReferences.TryGetComponent(spawnerPrefabBuffer[definition.spawners[spawnerEntity.index].loaderIndex].loader,
                                    out prefabReference) || prefabReference.Prefab != prefab.reference)
                                continue;

                            condition.value = 1;

                            break;
                        }
                    }
                }

                return condition.value == 0;
            case Type.PlayerArea:
                return areas[value].Contains(playerPosition);
            case Type.Millisecond:
                condition.value += (int)(deltaTime * 1000);

                return condition.value >= value;
        }

        return false;
    }
    
    public void Apply(
        ref EntityCommandBuffer.ParallelWriter entityManager, 
        ref BlobArray<LevelDefinition.Area> areas, 
        ref float3 playerPosition, 
        ref Random random, 
        ref LevelStatus status, 
        ref SpawnerLayerMaskInclude spawnerLayerMaskInclude, 
        ref SpawnerLayerMaskExclude spawnerLayerMaskExclude, 
        in SpawnerSingleton spawnerSingleton, 
        in NativeArray<LevelPrefab> prefabs, 
        in BufferLookup<SpawnerPrefab> spawnerPrefabs, 
        in ComponentLookup<SpawnerDefinitionData> spawners, 
        in ComponentLookup<RequestEntityPrefabLoaded> prefabReferences)
    {
        switch (type)
        {
            case Type.Value:
                status.value = value;
                break;
            case Type.Max:
                status.max = value;
                break;
            case Type.ExpMax:
                status.expMax = value;
                break;
            case Type.Exp:
                status.exp = value;
                break;
            case Type.SpawnerLayerMaskInclude:
                spawnerLayerMaskInclude.value = value;
                break;
            case Type.SpawnerLayerMaskExclude:
                spawnerLayerMaskExclude.value = value;
                break;
            case Type.SpawnerEntityRemaining:
                using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                {
                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawner;
                    int numKeys = spawnerEntities.Unique();
                    for (int i = 0; i < numKeys; ++i)
                    {
                        spawnerEntity = spawnerEntities[i];
                        if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                            continue;

                        ref var definition = ref spawner.definition.Value;
                        if(definition.spawners.Length <= spawnerEntity.index)
                            continue;

                        if ((definition.spawners[spawnerEntity.index].layerMask & value) == 0)
                            continue;

                        foreach (var entity in spawnerSingleton.entities.GetValuesForKey(spawnerEntity))
                            entityManager.DestroyEntity(0, entity);
                    }
                }

                break;
            case Type.PrefabRemaining:
                using (var spawnerEntities = spawnerSingleton.entities.GetKeyArray(Allocator.Temp))
                {
                    RequestEntityPrefabLoaded prefabReference;
                    SpawnerEntity spawnerEntity;
                    SpawnerDefinitionData spawner;
                    DynamicBuffer<SpawnerPrefab> spawnerPrefabBuffer;
                    var prefab = prefabs[value];
                    int numKeys = spawnerEntities.Unique();
                    for(int i = 0; i < numKeys; ++i)
                    {
                        spawnerEntity = spawnerEntities[i];
                        if (!spawners.TryGetComponent(spawnerEntity.spawner, out spawner))
                            continue;

                        ref var definition = ref spawner.definition.Value;
                        if(definition.spawners.Length <= spawnerEntity.index)
                            continue;

                        spawnerPrefabBuffer = spawnerPrefabs[spawnerEntity.spawner];

                        if (!prefabReferences.TryGetComponent(spawnerPrefabBuffer[definition.spawners[spawnerEntity.index].loaderIndex].loader,
                                out prefabReference) || prefabReference.Prefab != prefab.reference)
                            continue;

                        foreach (var entity in spawnerSingleton.entities.GetValuesForKey(spawnerEntity))
                            entityManager.DestroyEntity(0, entity);
                    }
                }
                break;
            case Type.PlayerArea:
                playerPosition += areas[value].GetPosition(ref random);
                break;
            case Type.Millisecond:
                //status.time += value * 1000.0f;
                break;
        }
    }
}

public struct LevelDefinition
{
    public struct DefaultStage
    {
        public int index;
        public int layerMaskInclude;
        public int layerMaskExclude;
    }
    
    public struct Stage
    {
        //public int spawnerLayerMaskInclude;
        //public int spawnerLayerMaskExclude;

        //public int exp;
        public int nextStageIndex;

        public FixedString128Bytes name;

        public BlobArray<LevelStageOption> conditions;
        public BlobArray<LevelStageOption> results;
    }

    public struct Area
    {
        public AABB aabb;

        public bool Contains(in float3 position)
        {
            return aabb.Contains(position);
        }

        public float3 GetPosition(ref Random random)
        {
            return random.NextFloat3(aabb.Min, aabb.Max);
        }
    }

    public BlobArray<DefaultStage> defaultStages;
    public BlobArray<Stage> stages;
    public BlobArray<Area> areas;
}

public struct LevelDefinitionData : IComponentData
{
    public BlobAssetReference<LevelDefinition> definition;
}

public struct LevelStatus : IComponentData
{
    public int value;
    public int max;
    public int expMax;
    public int exp;
    public int gold;
    public int count;
}

public struct LevelPrefab : IBufferElementData
{
    public EntityPrefabReference reference;
}

public struct LevelStage : IBufferElementData
{
    public int value;
}

public struct LevelStageConditionStatus : IBufferElementData
{
    public int version;
    public int value;
}

public struct LevelStageResultStatus : IBufferElementData
{
    public int layerMaskInclude;
    public int layerMaskExclude;
}
