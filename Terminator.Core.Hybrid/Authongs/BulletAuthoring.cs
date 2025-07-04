using System;
using System.Collections.Generic;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Scenes;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using Math = ZG.Mathematics.Math;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

#if UNITY_EDITOR
using UnityEditor;
using ZG;
public class BulletAuthoring : MonoBehaviour, IEffectAuthoring
{
    [Serializable]
    public struct MessageData
    {
        public string name;
        
        [Tooltip("填Play")]
        [UnityEngine.Serialization.FormerlySerializedAs("name")]
        public string messageName;

        [Tooltip("攻击Transition")]
        [UnityEngine.Serialization.FormerlySerializedAs("value")]
        public Object messageValue;
    }
    
    [Serializable]
    public struct TargetData
    {
        public string name;

        [Tooltip("碰撞体")]
        public GameObject prefab;

        [Tooltip("出手点")]
        public Bounds bounds;
        [Tooltip("出手点旋转（w为随机角度）")]
        public Vector4 rotation;
        
        [Tooltip("出手点随机轴，x,y代表椭圆，z代表椭圆距离")]
        public Vector3 aixes;

        [Tooltip("目标冷却时间")]
        public float cooldown;
        
        [Tooltip("最大索敌角度")]
        public float maxAngle;
        [Tooltip("自动索敌最大距离")]
        [UnityEngine.Serialization.FormerlySerializedAs("distance")]
        public float maxDistance;
        
        [Tooltip("自动索敌最小距离")]
        public float minDistance;
        
        [Tooltip("伤害标签，这里跟碰撞标签（碰撞标签可能会碰地形）是不同的")]
        public LayerMask hitWith;

        [Tooltip("地面标签，检测目标具体哪一个地面上")]
        public LayerMask groundBelongsTo;
        
        public BulletLocation location;

        public BulletTargetSpace space;

        public BulletTargetCoordinate coordinate;
        
        #region CSV
        [CSVField]
        public string 子弹目标名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 子弹目标路径
        {
            set
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(value);
            }
        }
        
        [CSVField]
        public string 子弹目标出手点
        {
            set
            {
                var parameters = value.Split('/');

                Vector3 center;
                center.x = float.Parse(parameters[0]);
                center.y = float.Parse(parameters[1]);
                center.z = float.Parse(parameters[2]);

                Vector3 extends;
                if (parameters.Length > 3)
                {
                    extends.x = float.Parse(parameters[3]);
                    extends.y = float.Parse(parameters[4]);
                    extends.z = float.Parse(parameters[5]);
                }
                else
                    extends = Vector3.zero;

                bounds = new Bounds(center, extends * 2.0f);
            }
        }
        
        [CSVField]
        public string 子弹目标出手点旋转
        {
            set
            {
                var parameters = value.Split('/');
                
                rotation.x = float.Parse(parameters[0]);
                rotation.y = float.Parse(parameters[1]);
                rotation.z = float.Parse(parameters[2]);
                rotation.w = parameters.Length > 3 ? float.Parse(parameters[3]) : 0.0f;
            }
        }

        [CSVField]
        public string 子弹目标出手点随机轴
        {
            set
            {
                var parameters = value.Split('/');
                
                aixes.x = float.Parse(parameters[0]);
                aixes.y = float.Parse(parameters[1]);
                aixes.z = float.Parse(parameters[2]);
            }
        }

        [CSVField]
        public float 子弹目标冷却时间
        {
            set
            {
                cooldown = value;
            }
        }

        [CSVField]
        public float 子弹目标索敌角度
        {
            set
            {
                maxAngle = value;
            }
        }
        
        [CSVField]
        public float 子弹目标索敌最大距离
        {
            set
            {
                maxDistance = value;
            }
        }
        
        [CSVField]
        public float 子弹目标索敌最小距离
        {
            set
            {
                minDistance = value;
            }
        }
        
        [CSVField]
        public int 子弹目标命中标签
        {
            set
            {
                hitWith = value;
            }
        }
        
        [CSVField]
        public int 子弹目标地面标签
        {
            set
            {
                groundBelongsTo = value;
            }
        }
                
        [CSVField]
        public int 子弹目标位置
        {
            set
            {
                location = (BulletLocation)value;
            }
        }
        
        [CSVField]
        public int 子弹目标空间
        {
            set
            {
                space = (BulletTargetSpace)value;
            }
        }
        
        [CSVField]
        public int 子弹目标准心
        {
            set
            {
                coordinate = (BulletTargetCoordinate)value;
            }
        }
        #endregion
    }
    
    [Serializable]
    public struct BulletData
    {
        [Serializable]
        public struct StandTime : IEquatable<StandTime>
        {
            public float start;
            public float end;

            public static implicit operator BulletDefinition.StandTime(StandTime standTime)
            {
                BulletDefinition.StandTime result;
                result.start = standTime.start;
                result.end = standTime.end;
                return result;
            }

            public bool Equals(StandTime other)
            {
                return Mathf.Approximately(start, other.start) && Mathf.Approximately(end, other.end);
            }

            public override int GetHashCode()
            {
                return start.GetHashCode() ^ end.GetHashCode();
            }
        }

        public string name;

        public string targetName;
        
        [Tooltip("碰撞体")]
        public GameObject prefab;
        [Tooltip("出手点旋转")]
        public Quaternion rotation;
        [Tooltip("出手点")]
        public Vector3 position;
        [Tooltip("角速度")]
        public Vector3 angularSpeed;
        [Tooltip("发射速度")]
        public float linearSpeed;
        [Tooltip("动画速度")] 
        public float animationCurveSpeed;
        [Tooltip("出手点向前延申的插值，与目标的距离为1个单位")]
        [UnityEngine.Serialization.FormerlySerializedAs("distance")]
        public float targetPositionInterpolation;
        [Tooltip("开始释放时间，可以利用这个时间错开几个子弹的释放间距")]
        public float startTime;
        [Tooltip("延迟释放时间，用来配合角色动作")]
        public float delayTime;
        [Tooltip("间隔时间")]
        public float interval;
        [Tooltip("装弹时间")]
        public float cooldown;
        //public float endTime;
        [Tooltip("弹夹容量")]
        public int capacity;
        [Tooltip("释放次数，每次打空弹夹算一次，填零为无限次释放。注意：每次条件不满足时，则释放次数将被清空，满足条件后重新计数")]
        public int times;

        [Tooltip("子弹标签，用技能开关")]
        public LayerMask layerMask;

        public BulletSpace space;
        public BulletSpace targetSpace;
        public BulletLocation location;
        public BulletLocation targetLocation;
        [Tooltip("方向，决定是向前发射还水平发射")]
        public BulletDirection direction;
        public BulletFollowTarget followTarget;
        
        public string[] messageNames;

        public StandTime[] standTimes;
        
        #region CSV
        [CSVField]
        public string 子弹名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 子弹消息名称
        {
            set
            {
                messageNames = string.IsNullOrEmpty(value) ? null : value.Split('/');
            }
        }
        
        [CSVField]
        public string 子弹站立时间
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                
                var parameters = value.Split('/');
                
                int numParameters = parameters.Length, index;
                string parameter;
                StandTime standTime;
                standTimes = new StandTime[numParameters];
                for (int i = 0; i < numParameters; ++i)
                {
                    parameter = parameters[i];
                    index = parameter.IndexOf(':');
                    if (index == -1)
                        standTime.start = 0.0f;
                    else
                    {
                        standTime.start = float.Parse(parameter.Remove(index));
                        
                        parameter = parameter.Substring(index + 1);
                    }
                    
                    standTime.end = float.Parse(parameter);

                    standTimes[i] = standTime;
                }
            }
        }

        [CSVField]
        public string 子弹目标
        {
            set
            {
                targetName = value;
            }
        }
        
        [CSVField]
        public string 子弹预制体路径
        {
            set
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(value);
            }
        }
        
        [CSVField]
        public string 子弹出手点旋转
        {
            set
            {
                var parameters = value.Split('/');

                rotation = Quaternion.Euler(float.Parse(parameters[0]), float.Parse(parameters[1]), float.Parse(parameters[2]));
            }
        }
        
        [CSVField]
        public string 子弹出手点位置
        {
            set
            {
                var parameters = value.Split('/');

                position.x = float.Parse(parameters[0]);
                position.y = float.Parse(parameters[1]);
                position.z = float.Parse(parameters[2]);
            }
        }
        
        [CSVField]
        public string 子弹角速度
        {
            set
            {
                var parameters = value.Split('/');

                angularSpeed.x = float.Parse(parameters[0]);
                angularSpeed.y = float.Parse(parameters[1]);
                angularSpeed.z = float.Parse(parameters[2]);
            }
        }

        [CSVField]
        public float 子弹出手点到目标点插值
        {
            set
            {
                targetPositionInterpolation = value;
            }
        }
        
        [CSVField]
        public float 子弹动画速度
        {
            set
            {
                animationCurveSpeed = value;
            }
        }
        
        [CSVField]
        public float 子弹发射速度
        {
            set
            {
                linearSpeed = value;
            }
        }
        
        [CSVField]
        public float 子弹释放时间
        {
            set
            {
                startTime = value;
            }
        }

        [CSVField]
        public float 子弹延迟时间
        {
            set
            {
                delayTime = value;
            }
        }
        
        [CSVField]
        public float 子弹间隔时间
        {
            set
            {
                interval = value;
            }
        }
        
        [CSVField]
        public float 子弹装弹时间
        {
            set
            {
                cooldown = value;
            }
        }
        
        [CSVField]
        public int 子弹弹夹容量
        {
            set
            {
                capacity = value;
            }
        }
        
        [CSVField]
        public int 子弹释放次数
        {
            set
            {
                times = value;
            }
        }
        
        [CSVField]
        public int 子弹标签
        {
            set
            {
                layerMask = value;
            }
        }

        [CSVField]
        public int 子弹空间
        {
            set
            {
                space = (BulletSpace)value;
            }
        }
        
        [CSVField]
        public int 子弹锁定目标空间
        {
            set
            {
                targetSpace = (BulletSpace)value;
            }
        }

        [CSVField]
        public int 子弹发射者位置
        {
            set
            {
                location = (BulletLocation)value;
            }
        }
        
        [CSVField]
        public int 子弹锁定目标位置
        {
            set
            {
                targetLocation = (BulletLocation)value;
            }
        }

        [CSVField]
        public int 子弹方向
        {
            set
            {
                direction = (BulletDirection)value;
            }
        }
        
        [CSVField]
        public int 子弹跟随目标
        {
            set
            {
                followTarget = (BulletFollowTarget)value;
            }
        }
        #endregion
    }

    [Serializable]
    public struct ActiveData
    {
        public string name;
        public float damageScale;
        
        #region CSV
        [CSVField]
        public string 子弹激活名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public float 子弹激活伤害
        {
            set
            {
                damageScale = value;
            }
        }
        #endregion
    }
    
    class Baker : Baker<BulletAuthoring>
    {
        public override void Bake(BulletAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            int numBullets = authoring._bullets.Length;

            var prefabIndices = new Dictionary<GameObject, int>();
            var prefabs = AddBuffer<BulletPrefab>(entity);
            BulletDefinitionData instance;
            int i, j;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<BulletDefinition>();
                root.minAirSpeed = authoring._minAirSpeed;
                root.maxAirSpeed = authoring._maxAirSpeed;
                
                var numTargets = authoring._targets.Length;
                BulletPrefab prefab;
                //RequestEntityPrefabLoaded requestEntityPrefabLoaded;
                var targets = builder.Allocate(ref root.targets, numTargets);
                for (i = 0; i < numTargets; ++i)
                {
                    ref var destination = ref targets[i];
                    ref var source = ref authoring._targets[i];
                    if (source.prefab == null)
                        destination.prefabLoaderIndex = -1;
                    else if (!prefabIndices.TryGetValue(source.prefab, out destination.prefabLoaderIndex))
                    {
                        /*prefab.loader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                            source.prefab.name);

                        requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.prefab);

                        AddComponent(prefab.loader, requestEntityPrefabLoaded);*/

                        destination.prefabLoaderIndex = prefabs.Length;
                        
                        prefabIndices[source.prefab] = destination.prefabLoaderIndex;

                        prefab.entityPrefabReference = new EntityPrefabReference(source.prefab);
                        prefabs.Add(prefab);
                    }

                    if(destination.prefabLoaderIndex == -1 && source.maxDistance > source.minDistance)
                        Debug.LogError($"The bullet target {source.name} need a prefab!");

                    destination.aabb.Center = source.bounds.center;
                    destination.aabb.Extents = source.bounds.extents;
                    destination.rotation = Quaternion.Euler(source.rotation.x, source.rotation.y, source.rotation.z);
                    destination.randomAngle = math.radians(source.rotation.w);
                    destination.randomAxes = source.aixes;
                    destination.dot = math.cos(math.radians(source.maxAngle));
                    destination.minDistance = source.minDistance;
                    destination.maxDistance = source.maxDistance;
                    destination.cooldown = source.cooldown;
                    destination.hitWith = (uint)source.hitWith.value;
                    destination.groundBelongsTo = (uint)source.groundBelongsTo.value;
                    destination.location = source.location;
                    destination.space = source.space;
                    destination.coordinate = source.coordinate;
                    //destination.direction = source.direction;
                }

                var bullets = builder.Allocate(ref root.bullets, numBullets);

                var standTimeIndices = new Dictionary<BulletData.StandTime, int>();
                var messages = AddBuffer<BulletMessage>(entity);
                string messageName;
                BulletMessage destinationMessage;
                BlobBuilderArray<int> indices;
                int count, index, numMessages = authoring._messages == null ? 0 : authoring._messages.Length, k;
                for (i = 0; i < numBullets; ++i)
                {
                    ref var destination = ref bullets[i];
                    ref var source = ref authoring._bullets[i];
                    if (source.prefab == null)
                        Debug.LogError(source.name);

                    if (source.prefab == null)
                        destination.prefabLoaderIndex = -1;
                    else if (!prefabIndices.TryGetValue(source.prefab, out destination.prefabLoaderIndex))
                    {
                        /*prefab.loader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                            source.prefab.name);

                        requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.prefab);

                        AddComponent(prefab.loader, requestEntityPrefabLoaded);*/

                        destination.prefabLoaderIndex = prefabs.Length;
                        
                        prefabIndices[source.prefab] = destination.prefabLoaderIndex;

                        prefab.entityPrefabReference = new EntityPrefabReference(source.prefab);
                        prefabs.Add(prefab);
                    }

                    count = source.messageNames == null ? 0 : source.messageNames.Length;
                    indices = builder.Allocate(ref destination.messageIndices, count);
                    for (j = 0; j < count; ++j)
                    {
                        indices[j] = -1;
                        
                        messageName = source.messageNames[j];
                        for (k = 0; k < numMessages; ++k)
                        {
                            ref var sourceMessage = ref authoring._messages[k];
                            if (sourceMessage.name == messageName)
                            {
                                //destinationMessage.key = sourceMessage.name;
                                destinationMessage.name = sourceMessage.messageName;
                                destinationMessage.value = sourceMessage.messageValue;

                                indices[j] = messages.Length;
                                messages.Add(destinationMessage);

                                break;
                            }
                        }

                        if (indices[j] == -1)
                            Debug.LogError(
                                $"Message {messageName} of bullet {source.name} can not been found!");
                    }
                    
                    count = source.standTimes == null ? 0 : source.standTimes.Length;
                    indices = builder.Allocate(ref destination.standTimeIndices, count);
                    for (j = 0; j < count; ++j)
                    {
                        ref var standTime = ref source.standTimes[j];
                        if (!standTimeIndices.TryGetValue(standTime, out index))
                        {
                            index = standTimeIndices.Count;
                            
                            standTimeIndices[standTime] = index;
                        }

                        indices[j] = index;
                    }
                    
                    destination.transform = math.RigidTransform(source.rotation, source.position);
                    destination.angularSpeed = source.angularSpeed;
                    destination.linearSpeed = source.linearSpeed;
                    destination.animationCurveSpeed = source.animationCurveSpeed;
                    destination.targetPositionInterpolation = source.targetPositionInterpolation;
                    destination.interval = source.interval;
                    destination.cooldown = source.cooldown;
                    destination.startTime = source.startTime;
                    destination.delayTime = source.delayTime;
                    destination.capacity = source.capacity;
                    destination.times = source.times;

                    destination.targetIndex = -1;
                    for (j = 0; j < numTargets; ++j)
                    {
                        if (authoring._targets[j].name == source.targetName)
                        {
                            destination.targetIndex = j;

                            break;
                        }
                    }

                    if (destination.targetIndex == -1)
                        Debug.LogError(
                            $"Bullet target {source.targetName} of bullet {source.name} can not been found!");
                    
                    destination.layerMask = source.layerMask.value;
                    destination.space = source.space;
                    destination.targetSpace = source.targetSpace;
                    destination.location = source.location;
                    destination.targetLocation = source.targetLocation;
                    destination.direction = source.direction;
                    destination.followTarget = source.followTarget;
                }
                
                var standTimes = builder.Allocate(ref root.standTimes, standTimeIndices.Count);
                foreach (var standTimeIndex in standTimeIndices)
                    standTimes[standTimeIndex.Value] = standTimeIndex.Key;

                instance.definition = builder.CreateBlobAssetReference<BulletDefinition>(Allocator.Persistent);
            }
            
            AddBlobAsset(ref instance.definition, out _);
            
            AddComponent(entity, instance);

            /*BulletDamageScale damageScale;
            damageScale.value = authoring._damageScale;
            AddComponent(entity, damageScale);*/

            BulletLayerMask layerMask;
            layerMask.value = authoring._layerMask.value;
            AddComponent(entity, layerMask);
            
            AddComponent<BulletStatus>(entity);
            AddComponent<BulletTargetStatus>(entity);

            AddComponent<BulletInstance>(entity);
            
            AddComponent<BulletVersion>(entity);
            
            var activeIndices = AddBuffer<BulletActiveIndex>(entity);

            var numActiveIndices = authoring._actives.Length;
            activeIndices.Resize(numActiveIndices, NativeArrayOptions.UninitializedMemory);
            for (i = 0; i < numActiveIndices; ++i)
            {
                ref var source = ref authoring._actives[i];
                ref var destination = ref activeIndices.ElementAt(i);
                destination.damageScale = source.damageScale;
                destination.value = -1;
                for (j = 0; j < numBullets; ++j)
                {
                    if (source.name == authoring._bullets[j].name)
                    {
                        destination.value = j;
                        
                        break;
                    }
                }
                
                if(destination.value == -1)
                    Debug.LogError($"Bullet active name {source.name} can not been found!");
            }
            
            AddBuffer<EffectDamageStatistic>(entity).Resize(numBullets, NativeArrayOptions.ClearMemory);

            if(authoring.GetComponent<EffectAuthoring>() == null)
                IEffectAuthoring.Bake(entity, this);
        }
    }

    [SerializeField] 
    internal float _minAirSpeed = -2f;
    
    [SerializeField] 
    internal float _maxAirSpeed = 0.1f;
    
    [SerializeField]
    internal LayerMask _layerMask;

    //[SerializeField] 
    //internal float _damageScale = 1.0f;

    [SerializeField] 
    internal MessageData[] _messages;

    [SerializeField] 
    internal TargetData[] _targets;
    
    #region CSV
    [SerializeField]
    [CSV("_targets", guidIndex = -1, nameIndex = 0)]
    internal string _targetsPath;
    #endregion
    
    [SerializeField]
    internal BulletData[] _bullets;

    #region CSV
    [SerializeField]
    [CSV("_bullets", guidIndex = -1, nameIndex = 0)]
    internal string _bulletsPath;
    #endregion

    [SerializeField] 
    internal ActiveData[] _actives;
    
    #region CSV
    [SerializeField]
    [CSV("_actives", guidIndex = -1, nameIndex = 0)]
    internal string _activesPath;
    #endregion

    /*private void OnValidate()
    {
        int numBullets = _bullets == null ? 0 : _bullets.Length;
        for (int i = 0; i < numBullets; ++i)
        {
            ref var bullet = ref _bullets[i];
            if (bullet.rotation == default)
                bullet.rotation = quaternion.identity;
            
            if (bullet.rotation == Quaternion.identity)
            {
                foreach (var target in _targets)
                {
                    if (target.name == bullet.targetName)
                    {
                        if ((target.flag & BulletTargetFlag.LookAt) != BulletTargetFlag.LookAt)
                        {
                            bullet.messageName = string.Empty;
                            bullet.messageValue = default;
                        }

                        break;
                    }
                }
            }
            else
            {
                bullet.messageName = string.Empty;
                bullet.messageValue = default;
            }
        }
    }*/
}
#endif