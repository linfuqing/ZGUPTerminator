using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
public class LocatorAuthoring : MonoBehaviour
{
    [System.Serializable]
    internal struct AreaData
    {
        public string name;
        
        public Bounds bounds;
    }
    
    [System.Serializable]
    internal struct ActionData
    {
        public string name;

        [Tooltip("对应区域名")]
        public string[] areaNames;
        
        [Tooltip("行动时间,该时间决定行走速度，不填则为原始速度")]
        public float time;
        [Tooltip("开始时间")]
        public float startTime;

        public LocatorDirection direction;
    }

    class Baker : Baker<LocatorAuthoring>
    {
        public override void Bake(LocatorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            LocatorDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<LocatorDefinition>();
                root.cooldown = authoring._cooldown;
                
                int i, numAreas = authoring._areas == null ? 0 : authoring._areas.Length;
                var areas = builder.Allocate(ref root.areas, numAreas);
                for (i = 0; i < numAreas; ++i)
                {
                    ref var source = ref authoring._areas[i];
                    ref var destination = ref areas[i];

                    destination.aabb.Center = source.bounds.center;
                    destination.aabb.Extents = source.bounds.extents;
                }

                int j, k, numAreaNames, numActions = authoring._actions == null ? 0 : authoring._actions.Length;
                BlobBuilderArray<int> areaIndices;
                var actions = builder.Allocate(ref root.actions, numActions);
                string areaName;
                for (i = 0; i < numActions; ++i)
                {
                    ref var source = ref authoring._actions[i];
                    ref var destination = ref actions[i];

                    destination.direction = source.direction;
                    destination.messageIndex = authoring._parameters == null ? -1 : 0;
                    destination.time = source.time;
                    destination.startTime = source.startTime;
                    if (i > 0)
                    {
                        ref var previous = ref authoring._actions[i - 1];
                        destination.startTime -= previous.startTime + previous.time;
                        
                        if(destination.startTime < 0.0f)
                            Debug.LogError($"The error startTime of action {source.name}!");
                    }

                    numAreaNames = source.areaNames == null ? 0 : source.areaNames.Length;

                    areaIndices = builder.Allocate(ref destination.areaIndices, numAreaNames);
                    for (j = 0; j < numAreaNames; ++j)
                    {
                        areaIndices[j] = -1;

                        areaName = source.areaNames[j];
                        for (k = 0; k < numAreas; ++k)
                        {
                            if (authoring._areas[k].name == areaName)
                            {
                                areaIndices[j] = k;
                                
                                break;
                            }
                        }
                        
                        if(areaIndices[j] == -1)
                            Debug.LogError($"Area {areaName} of action {source.name} can not been found!");
                    }

                    instance.definition = builder.CreateBlobAssetReference<LocatorDefinition>(Allocator.Persistent);
                }

                instance.definition = builder.CreateBlobAssetReference<LocatorDefinition>(Allocator.Persistent);
            }
                
            AddBlobAsset(ref instance.definition, out _);

            AddComponent(entity, instance);

            LocatorSpeed speed;
            speed.value = authoring._speed;
            AddComponent(entity, speed);
            
            AddComponent<LocatorVelocity>(entity);
            
            SetComponentEnabled<LocatorVelocity>(entity, false);
            
            AddComponent<LocatorTime>(entity);
            
            AddComponent<LocatorStatus>(entity);

            if (authoring._parameters != null)
            {
                LocatorMessage message;
                message.name = "SetAxis";
                message.value = authoring._parameters;

                AddBuffer<LocatorMessage>(entity).Add(message);
            }
        }
    }

    [SerializeField]
    internal float _speed = 1.0f;

    [SerializeField] 
    internal float _cooldown;
    
    [SerializeField]
    internal AreaData[] _areas;
    
    [SerializeField]
    internal ActionData[] _actions;

    [SerializeField]
    internal Parameters _parameters;
}
#endif