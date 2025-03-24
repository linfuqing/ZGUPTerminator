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
        
        //[Tooltip("下一阶段，不填则没有")]
        //public string nextStageName;
        
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
        
        [Tooltip("下一阶段，不填则没有")]
        public string[] nextStageNames;

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
                if (string.IsNullOrEmpty(value))
                {
                    nextStageNames = null;
                    
                    return;
                }
                
                nextStageNames = value.Split('/');
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
                    condition.value = (int)uint.Parse(parameter.Substring(index + 1));
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
                    result.value = (int)uint.Parse(parameter.Substring(index + 1));
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

                int numNextStageNames, numOptions, k;
                BlobBuilderArray<int> nextStageIndices;
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

                    numNextStageNames = source.nextStageNames == null ? 0 : source.nextStageNames.Length;
                    nextStageIndices = builder.Allocate(ref destination.nextStageIndies, numNextStageNames);
                    for (j = 0; j < numNextStageNames; ++j)
                    {
                        nextStageIndices[j] = -1;
                        ref var nextStageName = ref source.nextStageNames[j];
                        for (k = 0; k < numStages; ++k)
                        {
                            if (nextStageName == authoring._stages[k].name)
                            {
                                nextStageIndices[j] = k;

                                break;
                            }
                        }

                        if (nextStageIndices[j] == -1)
                            Debug.LogError(
                                $"The next stage name {nextStageName} of stage {source.name} can not been found!");
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
            status.count = 0;
            status.gold = 0;
            status.stage = 0;
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
                {
                    UnityEngine.Assertions.Assert.IsNotNull(authoring._prefabs[i], authoring.name);
                    levelPrefabs.ElementAt(i).reference = new EntityPrefabReference(authoring._prefabs[i]);
                }
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