using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
//[RequireComponent(typeof(SimulationEventAuthoring))]
public class EffectAuthoring : MonoBehaviour
{
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

        [Tooltip("消息名称，用来触发触碰动画")]
        public string[] messageNames;

        public DamageData[] damages;
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
                
                var prefabLoaders = new Dictionary<GameObject, Entity>();
                RequestEntityPrefabLoaded requestEntityPrefabLoaded;
                for (int i = 0; i < numMessages; ++i)
                {
                    ref var source = ref authoring._messages[i];
                    ref var destination = ref messages.ElementAt(i);

                    destination.name = source.messageName;
                    destination.value = new WeakObjectReference<Object>(source.value);
                    
                    if (source.receiverPrefab == null)
                        destination.receiverPrefabLoader = Entity.Null;
                    else if (!prefabLoaders.TryGetValue(source.receiverPrefab, out destination.receiverPrefabLoader))
                    {
                        destination.receiverPrefabLoader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                            source.receiverPrefab.name);

                        requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.receiverPrefab);

                        AddComponent(destination.receiverPrefabLoader, requestEntityPrefabLoaded);

                        prefabLoaders[source.receiverPrefab] = destination.receiverPrefabLoader;
                    }

                }
                
                //AddComponent<Message>(entity);
                //SetComponentEnabled<Message>(entity, false);
                
                //AddComponent<MessageParameter>(entity);
            }

            EffectDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<EffectDefinition>();
                
                int i, j, k, damageIndex, numMessageNames, numDamages, numEffects = authoring._effects.Length;
                string messageName;
                BlobBuilderArray<int> messageIndices, damageIndices;
                var damageDataIndices = new Dictionary<DamageData, int>();
                var damageDatas = new List<DamageData>();
                var effects = builder.Allocate(ref root.effects, numEffects);
                for (i = 0; i < numEffects; ++i)
                {
                    ref var source = ref authoring._effects[i];
                    ref var destination = ref effects[i];

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
                            Debug.LogError($"Message {messageName} of effect {source.name} in {authoring} can not been found!");
                    }

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
                }
                
                instance.definition = builder.CreateBlobAssetReference<EffectDefinition>(Allocator.Persistent);
            }
            
            AddBlobAsset(ref instance.definition, out _);

            AddComponent(entity, instance);

            AddComponent<EffectStatus>(entity);
            AddComponent<EffectStatusTarget>(entity);
            //AddComponent<SimulationEvent>(entity);
        }
    }
    
    [SerializeField]
    internal MessageData[] _messages;
    
    [SerializeField]
    [UnityEngine.Serialization.FormerlySerializedAs("_damages")]
    internal EffectData[] _effects;

    /*private void OnValidate()
    {
        bool isDirty = false;
        int numEffects = _effects == null ? 0 : _effects.Length;
        for (int i = 0; i < numEffects; ++i)
        {
            ref var effect = ref _effects[i];
            if (effect.damages == null || effect.damages.Length < 1 || !effect.damages[0].Equals(new DamageData()
                {
                    layerMask = effect.damages[0].layerMask, 
                    value = effect.damage,
                    valueToDrop = effect.dropToDamage,
                    spring = effect.spring,
                    explosion = effect.explosion,
                }))
            {
                effect.damages = new DamageData[1];

                ref var damage = ref effect.damages[0];
                damage.layerMask = 0;
                damage.value = effect.damage;
                damage.valueToDrop = effect.dropToDamage;
                damage.spring = effect.spring;
                damage.explosion = effect.explosion;

                isDirty = true;
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