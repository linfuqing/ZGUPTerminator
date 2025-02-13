using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
//[RequireComponent(typeof(SimulationEventAuthoring))]
public class EffectAuthoring : MonoBehaviour
{
    [Serializable]
    internal struct PrefabData
    {
        public string name;

        public EffectSpace space;

        public float chance;

        public GameObject gameObject;
    }
    
    [Serializable]
    internal struct MessageData
    {
        public string name;

        [UnityEngine.Serialization.FormerlySerializedAs("name")]
        public string messageName;

        public Object value;
        
        public GameObject receiverPrefab;
    }

    [Serializable]
    internal struct DamageData : IEquatable<DamageData>
    {
        public LayerMask layerMask;

        [Tooltip("用来判断是否计算次数,配合messageLayerMask可以配出弹板不被黑球影响")]
        public LayerMask entityLayerMask;

        [Tooltip("命中后激活对应标签动画")]
        public LayerMask messageLayerMask;

        [Tooltip("掉落伤害")]
        public int value;
        
        [Tooltip("掉落伤害")]
        public int valueToDrop;
        
        [Tooltip("弹射速度，向Y轴匀速弹射")]
        public float spring;

        [Tooltip("爆炸速度，向XZ平面匀速弹射")]
        public float explosion;

        [Tooltip("增加的延迟销毁时间")]
        public float delayDestroyTime;
        
        [Tooltip("消息名称，用来触发触碰动画")]
        public string[] messageNames;

        [Tooltip("击中掉落")]
        public PrefabData[] prefabs;
        
        public bool Equals(DamageData other)
        {
            return layerMask == other.layerMask &&
                   value == other.value &&
                   valueToDrop == other.valueToDrop &&
                   Mathf.Approximately(spring, other.spring) &&
                   Mathf.Approximately(explosion, other.explosion) &&
                   Mathf.Approximately(delayDestroyTime, other.delayDestroyTime);
        }
    }
    
    [Serializable]
    internal struct EffectData
    {
        public string name;

        [Tooltip("结算次数，结算时间不为零，按照时间结算（单位时间内，碰到的每个敌人之会结算一次伤害），否则每碰到一个敌人算一次次数，填0代表无限次结算")]
        public int count;

        [Tooltip("结算时间，结算时间不为零时，按照时间结算次数（单位时间内，碰到的每个敌人只会结算一次伤害），否则每碰到一个敌人算一次次数")]
        public float time;

        [Tooltip("这组效果的开始时间，用来配合动画做受击延迟")]
        public float startTime;

        //[Tooltip("消息名称，用来触发触碰动画")]
        //public string[] messageNames;

        public DamageData[] damages;
        
        [Tooltip("结算掉落")]
        public PrefabData[] prefabs;
    }

    class Baker : Baker<EffectAuthoring>
    {
        public override void Bake(EffectAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            int numMessages = authoring._messages.Length;
            if (numMessages > 0)
            {
                var messages = AddBuffer<EffectMessage>(entity);
                messages.ResizeUninitialized(numMessages);
                
                var prefabLoaders = new Dictionary<GameObject, EntityPrefabReference>();
                for (int i = 0; i < numMessages; ++i)
                {
                    ref var source = ref authoring._messages[i];
                    ref var destination = ref messages.ElementAt(i);

                    destination.name = source.messageName;
                    destination.value = new WeakObjectReference<Object>(source.value);
                    
                    if (source.receiverPrefab == null)
                        destination.entityPrefabReference = default;
                    else if (!prefabLoaders.TryGetValue(source.receiverPrefab, out destination.entityPrefabReference))
                    {
                        destination.entityPrefabReference = new EntityPrefabReference(source.receiverPrefab);

                        prefabLoaders[source.receiverPrefab] = destination.entityPrefabReference;
                    }
                }
                
                //AddComponent<Message>(entity);
                //SetComponentEnabled<Message>(entity, false);
                
                //AddComponent<MessageParameter>(entity);
            }

            var prefabIndices = new Dictionary<GameObject, int>();
            EffectDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<EffectDefinition>();
                
                int i, j, k, damageIndex, numDamages, numPrefabs, numMessageNames, numEffects = authoring._effects.Length;
                string messageName;
                BlobBuilderArray<int> messageIndices, damageIndices;
                BlobBuilderArray<EffectDefinition.Prefab> prefabs;
                var damageDataIndices = new Dictionary<DamageData, int>();
                var damageDatas = new List<DamageData>();
                var effects = builder.Allocate(ref root.effects, numEffects);
                for (i = 0; i < numEffects; ++i)
                {
                    ref var source = ref authoring._effects[i];
                    ref var destination = ref effects[i];

                    //destination.damage = source.damage;
                    //destination.dropToDamage = source.dropToDamage;
                    destination.count = source.count;
                    //destination.spring = source.spring;
                    //destination.explosion = source.explosion;
                    //destination.suction = source.suction;
                    destination.time = source.time;
                    destination.startTime = source.startTime;

                    numDamages = source.damages.Length;
                    damageIndices = builder.Allocate(ref destination.damageIndices, numDamages);
                    for (j = 0; j < numDamages; ++j)
                    {
                        ref var damage = ref source.damages[j];
                        if (!damageDataIndices.TryGetValue(damage, out damageIndex))
                        {
                            damageIndex = damageDatas.Count;
                            damageDatas.Add(damage);

                            damageDataIndices[damage] = damageIndex;
                        }

                        damageIndices[j] = damageIndex;
                    }
                    
                    numPrefabs = source.prefabs == null ? 0 : source.prefabs.Length;
                    prefabs = builder.Allocate(ref destination.prefabs, numPrefabs);
                    for(j = 0; j < numPrefabs; ++j)
                    {
                        ref var sourcePrefab = ref source.prefabs[j];
                        ref var destinationPrefab = ref prefabs[j];
                        destinationPrefab.space = sourcePrefab.space;
                        
                        if (!prefabIndices.TryGetValue(sourcePrefab.gameObject, out destinationPrefab.index))
                        {
                            destinationPrefab.index = prefabIndices.Count;
                            
                            prefabIndices[sourcePrefab.gameObject] = destinationPrefab.index;
                        }

                        destinationPrefab.chance = sourcePrefab.chance;
                    }
                }

                numDamages = damageDatas.Count;
                var damages = builder.Allocate(ref root.damages, numDamages);
                for (i = 0; i < numDamages; ++i)
                {
                    var source = damageDatas[i];
                    ref var destination = ref damages[i];
                    destination.layerMask = source.layerMask;
                    destination.entityLayerMask = source.entityLayerMask.value;
                    destination.messageLayerMask = source.messageLayerMask.value;
                    destination.value = source.value;
                    destination.valueToDrop = source.valueToDrop;
                    destination.spring = source.spring;
                    destination.explosion = source.explosion;
                    destination.delayDestroyTime = source.delayDestroyTime;
                    
                    numMessageNames = source.messageNames == null ? 0 : source.messageNames.Length;
                    messageIndices = builder.Allocate(ref destination.messageIndices, numMessageNames);
                    for(j = 0; j < numMessageNames; ++j)
                    {
                        messageIndices[j] = -1;
                        
                        messageName = source.messageNames[j];
                        for (k = 0; k < numMessages; ++k)
                        {
                            if (authoring._messages[k].name == messageName)
                            {
                                messageIndices[j] = k;
                                
                                break;
                            }
                        }
                         
                        if(messageIndices[j] == -1)
                            Debug.LogError($"Message {messageName} of effect damage {i} in {authoring} can not been found!");
                    }
                    
                    numPrefabs = source.prefabs == null ? 0 : source.prefabs.Length;
                    prefabs = builder.Allocate(ref destination.prefabs, numPrefabs);
                    for(j = 0; j < numPrefabs; ++j)
                    {
                        ref var sourcePrefab = ref source.prefabs[j];
                        ref var destinationPrefab = ref prefabs[j];
                        destinationPrefab.space = sourcePrefab.space;
                        
                        if (!prefabIndices.TryGetValue(sourcePrefab.gameObject, out destinationPrefab.index))
                        {
                            destinationPrefab.index = prefabIndices.Count;
                            
                            prefabIndices[sourcePrefab.gameObject] = destinationPrefab.index;
                        }

                        destinationPrefab.chance = sourcePrefab.chance;
                    }
                }

                instance.definition = builder.CreateBlobAssetReference<EffectDefinition>(Allocator.Persistent);
            }
            
            if (prefabIndices.Count > 0)
            {
                var prefabs = AddBuffer<EffectPrefab>(entity);
                EffectPrefab prefab;
                foreach (var gameObject in prefabIndices.Keys)
                {
                    prefab.entityPrefabReference = new EntityPrefabReference(gameObject);

                    prefabs.Add(prefab);
                }
            }

            AddBlobAsset(ref instance.definition, out _);

            AddComponent(entity, instance);

            AddComponent<EffectStatus>(entity);
            AddComponent<EffectStatusTarget>(entity);
            //AddComponent<SimulationEvent>(entity);

            EffectDamageParent parent;
            parent.index = -1;
            
            Entity child = entity;
            var parentGameObject = GetParent();
            while (parentGameObject != null)
            {
                parent.entity = GetEntity(parentGameObject, TransformUsageFlags.None);
                AddComponent(child, parent);
                
                if (parentGameObject.GetComponent<EffectAuthoring>() != null)
                    break;
                
                parentGameObject = GetParent(parentGameObject);

                child = parent.entity;
            }
        }
    }

    [SerializeField]
    internal MessageData[] _messages;
    
    [SerializeField]
    [UnityEngine.Serialization.FormerlySerializedAs("_damages")]
    internal EffectData[] _effects;

    /*private void OnValidate()
    {
        var temp = PrefabUtility.GetCorrespondingObjectFromSource(this);
        if (temp != null && temp != this)
            return;
        
        bool isDirty = false;
        int numEffects = _effects == null ? 0 : _effects.Length, numDamages, i, j;
        for (i = 0; i < numEffects; ++i)
        {
            ref var effect = ref _effects[i];
            if(effect.messageNames == null || effect.messageNames.Length < 1)
                continue;
            
            numDamages = effect.damages == null ? 0 : effect.damages.Length;
            if (numDamages > 0)
            {
                for (j = 0; j < numDamages; ++j)
                {
                    ref var damage = ref effect.damages[j];
                    if (damage.messageNames == null || damage.messageNames.Length < 1)
                    {
                        damage.messageNames = (string[])effect.messageNames.Clone();
                        
                        isDirty = true;
                    }
                }
            }
        }
        
        if(isDirty)
            UnityEditor.EditorApplication.delayCall += () =>
        {
            UnityEditor.EditorUtility.SetDirty(this);
        }
        ;
    }*/
}
#endif