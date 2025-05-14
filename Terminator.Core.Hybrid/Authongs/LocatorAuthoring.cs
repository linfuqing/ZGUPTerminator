using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
public class LocatorAuthoring : MonoBehaviour
{
    [System.Serializable]
    internal struct MessageData
    {
        public string name;
        
        public LocatorMessageType type;
        
        public string messageName;
        
        public Object messageValue;
    }

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

        [Tooltip("消息")]
        public string[] messageNames;
        
        [Tooltip("行动时间,该时间决定行走速度，不填则为原始速度")]
        public float time;
        [Tooltip("开始时间")]
        public float startTime;
        
        [Tooltip("填写后可决定位移时候的上方向")]
        public float3 up;

        public LocatorDirection direction;
    }

    class Baker : Baker<LocatorAuthoring>
    {
        public override void Bake(LocatorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            int numMessages = authoring._messages == null ? 0 : authoring._messages.Length;
            if (numMessages > 0 || authoring._parameters != null)
            {
                var messages = AddBuffer<LocatorMessage>(entity);
                
                LocatorMessage message;
                message.type = LocatorMessageType.Bold;
                message.name = "SetAxis";
                message.value = authoring._parameters;
                messages.Add(message);

                foreach (var messageTemp in authoring._messages)
                {
                    message.type = messageTemp.type;
                    message.name = messageTemp.messageName;
                    message.value = messageTemp.messageValue;
                    messages.Add(message);
                }
            }
            
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

                int j, k, numAreaNames, 
                    numMessageNames, 
                    messageOffset = authoring._parameters == null ? 0 : 1, 
                    numActions = authoring._actions == null ? 0 : authoring._actions.Length;
                BlobBuilderArray<int> areaIndices, messageIndices;
                var actions = builder.Allocate(ref root.actions, numActions);
                string areaName, messageName;
                for (i = 0; i < numActions; ++i)
                {
                    ref var source = ref authoring._actions[i];
                    ref var destination = ref actions[i];
                    
                    destination.direction = source.direction;
                    destination.time = source.time;
                    destination.startTime = source.startTime;
                    if (i > 0)
                    {
                        ref var previous = ref authoring._actions[i - 1];
                        destination.startTime -= previous.startTime + previous.time;
                        
                        if(destination.startTime < 0.0f)
                            Debug.LogError($"The error startTime of action {source.name}!");
                    }
                    
                    destination.up = source.up;

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

                    numMessageNames = source.messageNames == null ? 0 : source.messageNames.Length;
                    messageIndices = builder.Allocate(ref destination.messageIndices, numMessageNames + messageOffset);
                    if (messageOffset > 0)
                        messageIndices[0] = 0;
                    
                    for (j = 0; j < numMessageNames; ++j)
                    {
                        messageIndices[messageOffset + j] = -1;
                        
                        messageName = source.messageNames[j];
                        for (k = 0; k < numMessageNames; ++k)
                        {
                            if (authoring._messages[k].name == messageName)
                            {
                                messageIndices[messageOffset + j] = k;
                                
                                break;
                            }
                        }
                        
                        if(messageIndices[messageOffset + j] == -1)
                            Debug.LogError($"Message {messageName} of action {source.name} can not been found!");
                    }
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
        }
    }

    [SerializeField]
    internal float _speed = 1.0f;

    [SerializeField] 
    internal float _cooldown;
    
    [SerializeField]
    internal AreaData[] _areas;

    [SerializeField] 
    internal MessageData[] _messages;
    
    [SerializeField]
    internal ActionData[] _actions;

    [SerializeField]
    internal Parameters _parameters;
}
#endif