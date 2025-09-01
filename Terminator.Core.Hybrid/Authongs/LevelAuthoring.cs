using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEngine;
using Random = Unity.Mathematics.Random;

#if UNITY_EDITOR
using log4net.Core;
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
        public string[] tags;

        public LayerMaskAndTagsAuthoring layerMaskAndTags
        {
            get => new LayerMaskAndTagsAuthoring(layerMask, tags);
        }
    }

    [Serializable]
    public struct NextStage
    {
        public string name;

        public float chance;
    }
    
    [Serializable]
    public struct StageConditionInheritance
    {
        [Tooltip("前一个阶段名字")]
        public string name;
        [Tooltip("前一个阶段条件索引")]
        public int previousConditionIndex;
        [Tooltip("当前阶段条件索引")]
        public int currentConditionIndex;

        [Tooltip("继承倍率")]
        public float scale;
    }

    [Serializable]
    public struct StageOption
    {
        public LevelStageOption.Type type;

        public int value;

        public string[] tags;

        public StageOption(string value)
        {
            int index = value.IndexOf(':');
            type = (LevelStageOption.Type)int.Parse(value.Remove(index));

            int count = value.IndexOf(':', index + 1);
            if (count == -1)
            {
                this.value = (int)long.Parse(value.Substring(index + 1));

                this.tags = null;
            }
            else
            {
                this.value = (int)long.Parse(value.Substring(index + 1, count - index - 1));

                this.tags = value.Substring(count + 1).Split(':');
            }
        }

        public LevelStageOption ToAsset(ref Dictionary<LayerMaskAndTagsAuthoring, int> layerMaskAndTags)
        {
            LevelStageOption result;
            result.type = type;
            
            switch (type)
            {
                case LevelStageOption.Type.SpawnerLayerMask:
                case LevelStageOption.Type.SpawnerLayerMaskInclude:
                case LevelStageOption.Type.SpawnerLayerMaskExclude:
                case LevelStageOption.Type.SpawnerEntityRemaining:
                    if (layerMaskAndTags == null)
                        layerMaskAndTags = new Dictionary<LayerMaskAndTagsAuthoring, int>();

                    var authoring = new LayerMaskAndTagsAuthoring(value, tags);
                    if (!layerMaskAndTags.TryGetValue(authoring, out int index))
                    {
                        index = layerMaskAndTags.Count;
                        layerMaskAndTags[authoring] = index;
                    }

                    result.value = index;
                    break;
                default:
                    result.value = value;
                    break;
            }

            return result;
        }
    }

    [Serializable]
    public struct Stage
    {
        public string name;
        
        [Tooltip("完成该阶段执行的结果")]
        public StageOption[] results;

        [Tooltip("阶段所有条件满足后，激活刷怪标签并跳到下一阶段（如有）")]
        public StageOption[] conditions;

        [Tooltip("阶段所有条件满足后，阶段状态的继承关系（如有）")]
        public StageConditionInheritance[] conditionInheritances;
        
        [Tooltip("下一阶段，不填则没有")]
        public NextStage[] nextStages;

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
                    nextStages = null;
                    
                    return;
                }

                string parameter;
                var parameters = value.Split('/');
                int numParameters = parameters.Length, index;
                nextStages = new NextStage[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    parameter = parameters[i];
                    index = parameter.IndexOf('*');
                    ref var nextStage = ref nextStages[i];
                    if (index == -1)
                    {
                        nextStage.name = parameter;
                        nextStage.chance = 1.0f;
                    }
                    else
                    {
                        nextStage.name = parameter.Remove(index);
                        nextStage.chance = float.Parse(parameter.Substring(index + 1));
                    }
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
                int numParameters = parameters.Length, index, count;

                results = new StageOption[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    results[i] = new StageOption(parameters[i]);
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

                conditions = new StageOption[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    conditions[i] = new StageOption(parameters[i]);
            }
        }
        
        [CSVField]
        public string 阶段条件继承关系
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    conditionInheritances = null;

                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length, index1, index2, index3;

                conditionInheritances = new StageConditionInheritance[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    ref var conditionInheritance = ref conditionInheritances[i];
                    ref var parameter = ref parameters[i];

                    index1 = parameter.IndexOf(':');
                    index2 = parameter.IndexOf(':', index1 + 1);
                    index3 = parameter.IndexOf(':', index2 + 1);
                    index3 = index3 == -1 ? parameter.Length : index3;
                    conditionInheritance.name = parameter.Remove(index1);
                    conditionInheritance.previousConditionIndex = (int)uint.Parse(parameter.Substring(index1 + 1, index2 - index1 - 1));
                    conditionInheritance.currentConditionIndex = (int)uint.Parse(parameter.Substring(index2 + 1, index3 - index2 - 1));
                    conditionInheritance.scale = index3 < parameter.Length ? float.Parse(parameter.Substring(index3 + 1)) : 1.0f;
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

                root.mainStageIndex = authoring._mainStageIndex;

                var layerMaskAndTagsIndices =
                    new Dictionary<LayerMaskAndTagsAuthoring, int>();
                layerMaskAndTagsIndices[default] = 0;
                
                LayerMaskAndTagsAuthoring layerMaskAndTags;
                int layerMaskAndTagsIndex;
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

                    layerMaskAndTags = source.layerMaskAndTags;
                    if (!layerMaskAndTagsIndices.TryGetValue(layerMaskAndTags, out layerMaskAndTagsIndex))
                    {
                        layerMaskAndTagsIndex = layerMaskAndTagsIndices.Count;
                        layerMaskAndTagsIndices[layerMaskAndTags] = layerMaskAndTagsIndex;
                    }

                    destination.layerMaskAndTagsIncludeIndex = layerMaskAndTagsIndex;
                    destination.layerMaskAndTagsExcludeIndex = 0;
                }

                int numNextStages, numOptions, numConditionInheritances, k;
                BlobBuilderArray<LevelStageOption> options;
                BlobBuilderArray<LevelDefinition.NextStage> nextStages;
                BlobBuilderArray<LevelDefinition.StageConditionInheritance> conditionInheritances;
                var stages = builder.Allocate(ref root.stages, numStages);
                for (i = 0; i < numStages; ++i)
                {
                    ref var source = ref authoring._stages[i];
                    ref var destination = ref stages[i];

                    destination.name = source.name;

                    numOptions = source.results == null ? 0 : source.results.Length;
                    options = builder.Allocate(ref destination.results, numOptions);
                    for (j = 0; j < numOptions; ++j)
                        options[j] = source.results[j].ToAsset(ref layerMaskAndTagsIndices);

                    numOptions = source.conditions == null ? 0 : source.conditions.Length;
                    options = builder.Allocate(ref destination.conditions, numOptions);
                    for (j = 0; j < numOptions; ++j)
                        options[j] = source.conditions[j].ToAsset(ref layerMaskAndTagsIndices);
                    
                    numConditionInheritances = source.conditionInheritances == null ? 0 : source.conditionInheritances.Length;
                    conditionInheritances = builder.Allocate(ref destination.conditionInheritances, numConditionInheritances);
                    for (j = 0; j < numConditionInheritances; ++j)
                    {
                        ref var sourceConditionInheritance = ref source.conditionInheritances[j];
                        ref var destinationConditionInheritance = ref conditionInheritances[j];

                        destinationConditionInheritance.stageName = sourceConditionInheritance.name;
                        destinationConditionInheritance.previousConditionIndex =
                            sourceConditionInheritance.previousConditionIndex;
                        destinationConditionInheritance.currentConditionIndex =
                            sourceConditionInheritance.currentConditionIndex;
                        
                        destinationConditionInheritance.scale = sourceConditionInheritance.scale;
                    }

                    numNextStages = source.nextStages == null ? 0 : source.nextStages.Length;
                    nextStages = builder.Allocate(ref destination.nextStages, numNextStages);
                    for (j = 0; j < numNextStages; ++j)
                    {
                        ref var sourceNextStage = ref source.nextStages[j];
                        ref var destinationNextStage = ref nextStages[j];
                        destinationNextStage.index = -1;
                        for (k = 0; k < numStages; ++k)
                        {
                            if (sourceNextStage.name == authoring._stages[k].name)
                            {
                                destinationNextStage.index = k;

                                break;
                            }
                        }

                        if (destinationNextStage.index == -1)
                            Debug.LogError(
                                $"The next stage name {sourceNextStage.name} of stage {source.name} can not been found!");
                        
                        destinationNextStage.chance = sourceNextStage.chance;
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
            status.killCount = 0;
            status.killBossCount = 0;
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

                stageResultStates.ElementAt(i).layerMaskAndTagsInclude = source.layerMaskAndTags;
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
    internal int _mainStageIndex;
    
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