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
public class BulletAuthoring : MonoBehaviour
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
        [Tooltip("间隔时间")]
        public float interval;
        [Tooltip("装弹时间")]
        public float cooldown;
        //public float endTime;
        [Tooltip("弹夹容量")]
        public int capacity;
        [Tooltip("释放次数，每次打空弹夹算一次，填零为无限次释放。注意：每次条件不满足时，则释放次数将被清空，满足条件后重新计数")]
        public int times;

        public BulletSpace space;
        public BulletSpace targetSpace;
        public BulletLocation location;
        public BulletLocation targetLocation;
        [Tooltip("方向，决定是向前发射还水平发射")]
        public BulletDirection direction;
        public BulletFollowTarget followTarget;
        
        public string[] messageNames;
        
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
        public int damage;
        
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
        public int 子弹激活伤害
        {
            set
            {
                damage = value;
            }
        }
        #endregion
    }
    
    class Baker : Baker<BulletAuthoring>
    {
        public override void Bake(BulletAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            var prefabIndices = new Dictionary<GameObject, int>();
            var prefabs = AddBuffer<BulletPrefab>(entity);
            BulletDefinitionData instance;
            int i, j, numBullets = authoring._bullets.Length;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<BulletDefinition>();
                root.minAirSpeed = authoring._minAirSpeed;
                root.maxAirSpeed = authoring._maxAirSpeed;
                
                var numTargets = authoring._targets.Length;
                BulletPrefab prefab;
                RequestEntityPrefabLoaded requestEntityPrefabLoaded;
                var targets = builder.Allocate(ref root.targets, numTargets);
                for (i = 0; i < numTargets; ++i)
                {
                    ref var destination = ref targets[i];
                    ref var source = ref authoring._targets[i];
                    if (source.prefab == null)
                        destination.prefabLoaderIndex = -1;
                    else if (!prefabIndices.TryGetValue(source.prefab, out destination.prefabLoaderIndex))
                    {
                        prefab.loader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                            source.prefab.name);

                        requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.prefab);

                        AddComponent(prefab.loader, requestEntityPrefabLoaded);

                        destination.prefabLoaderIndex = prefabs.Length;
                        
                        prefabIndices[source.prefab] = destination.prefabLoaderIndex;

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

                var messages = AddBuffer<BulletMessage>(entity);
                string messageName;
                BulletMessage destinationMessage;
                BlobBuilderArray<int> messageIndices;
                int numMessageNames, numMessages = authoring._messages == null ? 0 : authoring._messages.Length, k;
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
                        prefab.loader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                            source.prefab.name);

                        requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.prefab);

                        AddComponent(prefab.loader, requestEntityPrefabLoaded);

                        destination.prefabLoaderIndex = prefabs.Length;
                        
                        prefabIndices[source.prefab] = destination.prefabLoaderIndex;

                        prefabs.Add(prefab);
                    }

                    numMessageNames = source.messageNames == null ? 0 : source.messageNames.Length;
                    messageIndices = builder.Allocate(ref destination.messageIndices, numMessageNames);
                    for (j = 0; j < numMessageNames; ++j)
                    {
                        messageIndices[j] = -1;
                        
                        messageName = source.messageNames[j];
                        for (k = 0; k < numMessages; ++k)
                        {
                            ref var sourceMessage = ref authoring._messages[k];
                            if (sourceMessage.name == messageName)
                            {
                                //destinationMessage.key = sourceMessage.name;
                                destinationMessage.name = sourceMessage.messageName;
                                destinationMessage.value = sourceMessage.messageValue == null
                                    ? default
                                    : new WeakObjectReference<Object>(sourceMessage.messageValue);

                                messageIndices[j] = messages.Length;
                                messages.Add(destinationMessage);

                                break;
                            }
                        }

                        if (messageIndices[j] == -1)
                            Debug.LogError(
                                $"Message {messageName} of bullet {source.name} can not been found!");
                    }

                    destination.transform = math.RigidTransform(source.rotation, source.position);
                    destination.angularSpeed = source.angularSpeed;
                    destination.linearSpeed = source.linearSpeed;
                    destination.animationCurveSpeed = source.animationCurveSpeed;
                    destination.targetPositionInterpolation = source.targetPositionInterpolation;
                    destination.interval = source.interval;
                    destination.cooldown = source.cooldown;
                    destination.startTime = source.startTime;
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

                    destination.space = source.space;
                    destination.targetSpace = source.targetSpace;
                    destination.location = source.location;
                    destination.targetLocation = source.targetLocation;
                    destination.direction = source.direction;
                    destination.followTarget = source.followTarget;
                }

                instance.definition = builder.CreateBlobAssetReference<BulletDefinition>(Allocator.Persistent);
            }
            
            AddBlobAsset(ref instance.definition, out _);
            
            AddComponent(entity, instance);

            AddComponent<BulletStatus>(entity);
            AddComponent<BulletTargetStatus>(entity);

            AddComponent<BulletVersion>(entity);
            
            var activeIndices = AddBuffer<BulletActiveIndex>(entity);

            var numActiveIndices = authoring._actives.Length;
            activeIndices.Resize(numActiveIndices, NativeArrayOptions.UninitializedMemory);
            for (i = 0; i < numActiveIndices; ++i)
            {
                ref var source = ref authoring._actives[i];
                ref var destination = ref activeIndices.ElementAt(i);
                destination.damage = source.damage;
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
        }
    }

    [SerializeField] 
    internal float _minAirSpeed = -2f;
    
    [SerializeField] 
    internal float _maxAirSpeed = 0.1f;

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

[Flags]
public enum BulletLocation
{
    Ground = 0x01, 
    Air = 0x02
}

public enum BulletSpace
{
    World, 
    Local
}

public enum BulletTargetSpace
{
    Camera, 
    World, 
    Local
}

public enum BulletTargetCoordinate
{
    Origin,
    Center
}

public enum BulletDirection
{
    Forward,
    Horizontal
}

public enum BulletFollowTarget
{
    None, 
    Source, 
    Destination
}

public struct BulletDefinition
{
    private struct Collector : ICollector<DistanceHit>
    {
        private CollisionWorld __collisionWorld;
        //private BlobAssetReference<Collider> __collider;
        private ComponentLookup<KinematicCharacterBody> __characterBodies;
        private NativeArray<BulletTargetStatus> __states;
        private RigidTransform __transform;
        private double __time;
        private float __minDistance;
        private float __dot;
        private uint __hitWith;
        private uint __groundBelongsTo;
        private int __count;
        private int __version;
        private BulletLocation __location;

        public bool EarlyOutOnFirstHit => false;

        public int NumHits { get; private set; }

        public float MaxFraction { get; private set; }

        public DistanceHit closestHit { get; private set; }

        public Collector(
            in RigidTransform transform,
            in ComponentLookup<KinematicCharacterBody> characterBodies, 
            in NativeArray<BulletTargetStatus> states, 
            //in BlobAssetReference<Collider> collider,
            in CollisionWorld collisionWorld,
            double time, 
            //in float3 up, 
            float minDistance,
            float maxDistance, 
            float dot, 
            uint hitWith, 
            uint groundBelongsTo, 
            int version, 
            BulletLocation location)
        {
            __transform = transform;
            __states = states;
            __characterBodies = characterBodies;
            //__collider = collider;
            __collisionWorld = collisionWorld;
            __time = time;
            //__up = up;
            __minDistance = minDistance;
            __dot = dot;

            __count = int.MaxValue;

            __hitWith = hitWith;
            __groundBelongsTo = groundBelongsTo;
            __version = version;
            __location = location;
            
            NumHits = 0;

            MaxFraction = maxDistance;

            closestHit = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            if ((__collisionWorld.Bodies[hit.RigidBodyIndex].Collider.Value.GetCollisionFilter(hit.ColliderKey)
                    .BelongsTo & __hitWith) == 0)
                return false;
            
            if (!__Check(__dot, __minDistance, hit.Position - __transform.pos, __transform.rot))
                return false;
            
            if (!__Check(__location, __groundBelongsTo, hit.Entity, __collisionWorld, __characterBodies))
                return false;

            /*float3 position = hit.Position;
            var input = new ColliderCastInput(
                __collider,
                __transform.pos,
                position,
                quaternion.LookRotationSafe(position - __transform.pos, __up));

            if (!__collisionWorld.CastCollider(input))
                return false;*/

            NumHits = 1;
            
            //if (__Check(__dot, __minDistance, hit.Position - __transform.pos, __transform.rot))
                MaxFraction = hit.Fraction;

            int count = 0;
            Entity entity = hit.Entity;
            foreach (var status in __states)
            {
                if ((status.version == __version || status.cooldown > __time) && status.target == entity)
                    ++count;
            }

            if (count > __count)
                return false;

            __count = count;

            closestHit = hit;

            return true;
        }
    }

    public struct Target
    {
        public AABB aabb;
        public quaternion rotation;
        public float3 randomAxes;
        public float randomAngle;
        
        public float dot;
        public float minDistance;
        public float maxDistance;

        public float cooldown;

        public int prefabLoaderIndex;

        public uint hitWith;
        public uint groundBelongsTo;

        public BulletLocation location;

        public BulletTargetSpace space;

        public BulletTargetCoordinate coordinate;
        
        public static float3 GetRandomPointInEllipse(in float3 axes, ref Random random)
        {
            float theta = random.NextFloat() * 2.0f * math.PI,
                radius = math.sqrt(random.NextFloat()),
                x = radius * math.cos(theta) * axes.x,
                y = radius * math.sin(theta) * axes.y;
            return math.float3(x, y, axes.z);
        }

        public bool Update(
            int version,
            double time, 
            //in float3 up,
            in quaternion cameraRotation,
            in float4x4 transform,
            in Entity lookAt,
            in CollisionWorld collisionWorld,
            in ComponentLookup<PhysicsCollider> physicsColliders,
            in ComponentLookup<KinematicCharacterBody> characterBodies,
            in DynamicBuffer<BulletPrefab> prefabs,
            in ComponentLookup<PrefabLoadResult> prefabLoadResults,
            in NativeArray<BulletTargetStatus> states,
            ref BulletTargetStatus status,
            ref Random random)
        {
            if (version == status.version)
                return minDistance >= maxDistance || status.target != Entity.Null;

            RigidTransform targetTransform;
            targetTransform.pos = random.NextFloat3(aabb.Min, aabb.Max);
            targetTransform.rot = rotation;

            if (randomAxes.z > math.FLT_MIN_NORMAL)
            {
                float3 point = GetRandomPointInEllipse(randomAxes, ref random), direction = math.normalize(point);
                quaternion randomRotation = Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), direction);
                targetTransform.rot = math.mul(targetTransform.rot, randomRotation);
            }

            if (randomAngle > math.FLT_MIN_NORMAL)
            {
                float randomAngle = random.NextFloat(this.randomAngle);
                float3 randomAxis = random.NextFloat3Direction();
                quaternion randomRotation = quaternion.AxisAngle(randomAxis, randomAngle);
                targetTransform.rot = math.mul(targetTransform.rot, randomRotation);
            }

            switch (space)
            {
                case BulletTargetSpace.Camera:
                    status.transform.rot = math.mul(cameraRotation, targetTransform.rot);
                    status.transform.pos = math.transform(math.RigidTransform(cameraRotation, transform.c3.xyz),
                        targetTransform.pos);
                    break;
                case BulletTargetSpace.World:
                    status.transform.rot = targetTransform.rot;
                    status.transform.pos = math.transform(transform, targetTransform.pos);
                    break;
                case BulletTargetSpace.Local:
                    status.transform.rot = math.mul(math.quaternion(transform), targetTransform.rot);
                    status.transform.pos = math.transform(transform, targetTransform.pos);
                    break;
            }

            if (minDistance < maxDistance)
            {
                bool hasTarget;
                if (time < status.cooldown)
                {
                    int rigidBodyIndex = status.target == Entity.Null
                        ? -1
                        : collisionWorld.GetRigidBodyIndex(status.target);

                    hasTarget = rigidBodyIndex != -1;
                    if (hasTarget)
                    {
                        var body = collisionWorld.Bodies[rigidBodyIndex];
                        switch (coordinate)
                        {
                            case BulletTargetCoordinate.Center:
                                status.targetPosition = body.CalculateAabb().Center;
                                break;
                            default:
                                status.targetPosition = body.WorldFromBody.pos;
                                break;
                        }
                    }
                }
                else
                    hasTarget = false;

                if (!hasTarget)
                {
                    if (prefabLoadResults.TryGetComponent(prefabs[prefabLoaderIndex].loader, out var result) &&
                        physicsColliders.TryGetComponent(result.PrefabRoot, out var physicsCollider) &&
                        physicsCollider.IsValid)
                    {
                        //var rigidTransform = math.RigidTransform(math.inverse(status.transform.rot), status.transform.pos);
                        if (__Check(
                                version,
                                time,
                                lookAt,
                                status.transform,
                                physicsCollider.Value,
                                collisionWorld,
                                characterBodies,
                                states,
                                out status.targetPosition))
                        {
                            status.target = lookAt;

                            hasTarget = true;
                        }
                        else
                            hasTarget = __Check(
                                version,
                                time,
                                status.target,
                                status.transform,
                                physicsCollider.Value,
                                collisionWorld,
                                characterBodies,
                                states,
                                out status.targetPosition);

                        if (!hasTarget)
                        {
                            status.target = Entity.Null;

                            //var filter = physicsCollider.Value.Value.GetCollisionFilter();
                            //filter.CollidesWith = hitWith;

                            var input = new ColliderDistanceInput(physicsCollider.Value, maxDistance, status.transform);
                            //input = status.transform.pos;
                            //input.MaxDistance = maxDistance;
                            //input.Filter = filter;

                            var collector = new Collector(
                                status.transform,
                                characterBodies,
                                states,
                                collisionWorld,
                                time,
                                minDistance,
                                maxDistance,
                                dot,
                                hitWith,
                                groundBelongsTo,
                                version,
                                location);
                            if (collisionWorld.CalculateDistance(input, ref collector))
                            {
                                var closestHit = collector.closestHit;
                                var body = collisionWorld.Bodies[closestHit.RigidBodyIndex];
                                switch (coordinate)
                                {
                                    case BulletTargetCoordinate.Center:
                                        status.targetPosition = body.CalculateAabb().Center;
                                        break;
                                    default:
                                        status.targetPosition = body.WorldFromBody.pos;
                                        break;
                                }

                                status.target = closestHit.Entity;
                            }

                            if (status.target == Entity.Null)
                                return false;
                        }

                        float3 direction = math.normalizesafe(status.targetPosition - status.transform.pos);
                        /*quaternion rotation;
                        switch (this.direction)
                        {
                            case BulletTargetDirection.Horizontal:
                                rotation = MathUtilities.CreateRotationWithUpPriority(up, direction);
                                break;
                            default:
                                rotation = Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), direction);
                                break;
                        }*/

                        /*if (dot < 1.0f)
                        {
                            float angle = math.angle(rotation, status.transform.rot),
                                t = math.saturate(math.acos(dot) / math.abs(angle));
                            rotation = math.slerp(status.transform.rot, rotation, t);
                        }*/

                        status.transform.rot =
                            Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), direction); //rotation;
                        
                        status.cooldown = time + this.cooldown;
                    }
                    else
                    {
                        status.target = Entity.Null;

                        return false;
                    }
                }
            }
            else
                status.targetPosition = status.transform.pos;

            status.version = version;

            return true;
        }

        private bool __Check(
            int version, 
            double time, 
            in Entity entity, 
            in RigidTransform transform, 
            in BlobAssetReference<Collider> collider, 
            in CollisionWorld collisionWorld, 
            in ComponentLookup<KinematicCharacterBody> characterBodies, 
            in NativeArray<BulletTargetStatus> states, 
            out float3 targetPosition)
        {
            int rigidBodyIndex = entity == Entity.Null ? -1 : collisionWorld.GetRigidBodyIndex(entity);
            if (rigidBodyIndex == -1)
            {
                targetPosition = float3.zero;

                return false;
            }

            var rigidBody = collisionWorld.Bodies[rigidBodyIndex];
            switch (coordinate)
            {
                case BulletTargetCoordinate.Center:
                    targetPosition = rigidBody.CalculateAabb().Center;
                    break;
                default:
                    targetPosition = rigidBody.WorldFromBody.pos;
                    break;
            }
            
            /*if (rigidBody.Collider.IsCreated && (rigidBody.Collider.Value.GetCollisionFilter().BelongsTo & hitWith) == 0)
                return false;*/

            /*float distanceSQ = math.lengthsq(distance);
            if (distanceSQ < minDistance * minDistance || distanceSQ > maxDistance * maxDistance)
                return false;*/
            
            if (!rigidBody.CalculateDistance(new ColliderDistanceInput(collider, maxDistance, transform),
                    out var closestHit))
                return false;
            
            if((rigidBody.Collider.Value.GetCollisionFilter(closestHit.ColliderKey).BelongsTo & hitWith) == 0)
                return false;

            if (!BulletDefinition.__Check(dot, minDistance, closestHit.Position - transform.pos, transform.rot))
                return false;
            
            if (!BulletDefinition.__Check(location, groundBelongsTo, entity, collisionWorld, characterBodies))
                return false;
            
            foreach (var status in states)
            {
                if ((status.version == version || status.cooldown > time) && status.target == entity)
                    return false;
            }

            return true;
        }
    }

    public struct Bullet
    {
        public RigidTransform transform;
        public float3 angularSpeed;
        public float linearSpeed;
        public float animationCurveSpeed;
        public float targetPositionInterpolation;
        public float interval;
        public float cooldown;
        public float startTime;
        public int capacity;
        public int times;

        public int targetIndex;

        public int prefabLoaderIndex;

        public BulletLocation location;
        public BulletLocation targetLocation;
        public BulletSpace space;
        public BulletSpace targetSpace;
        public BulletDirection direction;
        public BulletFollowTarget followTarget;
        public BlobArray<int> messageIndices;
    }

    public float minAirSpeed;
    public float maxAirSpeed;
    public BlobArray<Target> targets;
    public BlobArray<Bullet> bullets;

    public bool Update(
        BulletLocation location, 
        int damage, 
        int index,
        int version,
        double time,
        in float3 up, 
        in quaternion cameraRotation, 
        in float4x4 transform,
        in Entity parent,
        in Entity lookAt, 
        in CollisionWorld collisionWorld,
        in ComponentLookup<PhysicsCollider> physicsColliders,
        in ComponentLookup<KinematicCharacterBody> characterBodies, 
        in ComponentLookup<ThirdPersonCharacterControl> characterControls,
        in ComponentLookup<AnimationCurveTime> animationCurveTimes,
        in ComponentLookup<PrefabLoadResult> prefabLoadResults,
        in DynamicBuffer<BulletPrefab> prefabs,
        in DynamicBuffer<BulletMessage> inputMessages,
        ref DynamicBuffer<Message> outputMessages,
        ref DynamicBuffer<BulletTargetStatus> targetStates,
        ref EntityCommandBuffer.ParallelWriter entityManager,
        ref BulletStatus status, 
        ref Random random)
    {
        if (status.cooldown > time)
            return false;

        bool result;
        ref var data = ref bullets[index];
        if (data.location == 0)
            result = true;
        else
            result = (data.location & location) != 0;

        if (targetStates.Length <= data.targetIndex)
            targetStates.Resize(targets.Length, NativeArrayOptions.ClearMemory);

        ref var targetStatus = ref targetStates.ElementAt(data.targetIndex);
        if (result)
        {
            ref var target = ref targets[data.targetIndex];
        
            result = target.Update(
                //(location & BulletLocation.Ground) == BulletLocation.Ground,
                version,
                time, 
                //up, 
                cameraRotation,
                transform,
                lookAt,
                collisionWorld,
                physicsColliders,
                characterBodies,
                prefabs,
                prefabLoadResults,
                targetStates.AsNativeArray(),
                ref targetStatus,
                ref random);
            
            if (result && targetStatus.target != Entity.Null && data.targetLocation != 0)
            {
                if (characterBodies.TryGetComponent(targetStatus.target, out var characterBody) && characterBody.IsGrounded)
                    result = (data.targetLocation & BulletLocation.Ground) == BulletLocation.Ground;
                else
                    result = (data.targetLocation & BulletLocation.Air) == BulletLocation.Air;
            }
        }

        if (!result)
        {
            status.times = 0;
            status.cooldown = 0.0f;

            return false;
        }

        if (data.times > 0 && data.times <= status.times)
        {
            status.cooldown = 0.0f;
            
            return false;
        }

        if (status.cooldown < math.DBL_MIN_NORMAL)
        {
            status.cooldown = time + data.startTime;

            if (data.startTime > math.FLT_MIN_NORMAL)
                return false;
        }

        if (!prefabLoadResults.TryGetComponent(prefabs[data.prefabLoaderIndex].loader, out var prefabLoadResult))
            return false;

        int numMessageIndices = data.messageIndices.Length;
        if (numMessageIndices > 0)
        {
            Message outputMessage;
            for (int i = 0; i < numMessageIndices; ++i)
            {
                var inputMessage = inputMessages[data.messageIndices[i]];

                outputMessage.key = 0;//random.NextInt();
                outputMessage.name = inputMessage.name;
                outputMessage.value = inputMessage.value;
                outputMessages.Add(outputMessage);
            }
        }

        /*double cooldown;
        if (status.count < 0)
        {
            status.count = 0;
            cooldown = status.cooldown;
        }
        else
            cooldown = time;*/
        double cooldown = status.cooldown;

        int statusCount = status.count, entityCount = 0;

        if (data.capacity > 0)
        {
            //status.cooldown = cooldown;

            int count;
            do
            {
                if (data.interval > math.FLT_MIN_NORMAL)
                {
                    count = data.capacity - status.count;
                    count = math.min(count, (int)math.floor((time - status.cooldown) / data.interval) + 1);

                    status.cooldown += data.interval * count;
                }
                else
                    count = 1;

                entityCount += count;

                status.count += count;
                if (status.count == data.capacity)
                {
                    status.cooldown += data.cooldown;
                    status.count = 0;
                    if (++status.times >= data.times && data.times > 0)
                    {
                        status.cooldown = 0.0;
                        
                        break;
                    }
                }
            } while (status.cooldown <= time);
        }
        else
        {
            if (status.count > 0)
                return false;

            status.count = 1;

            entityCount = 1;
        }

        RigidTransform transformResult;
        transformResult.pos = targetStatus.transform.pos;

        switch (data.direction)
        {
            case BulletDirection.Horizontal:
                transformResult.rot = MathUtilities.CreateRotationWithUpPriority(up, math.forward(targetStatus.transform.rot));
                break;
            default:
                transformResult.rot = targetStatus.transform.rot;
                break;
        }

        transformResult = math.mul(transformResult, data.transform);

        if (data.targetPositionInterpolation > math.FLT_MIN_NORMAL)
        {
            transformResult.pos = math.lerp(transformResult.pos, targetStatus.targetPosition, data.targetPositionInterpolation);
            transformResult.rot = math.slerp(transformResult.rot, cameraRotation, data.targetPositionInterpolation);

            /*float targetDistance =
                data.distance * math.distance(targetStatus.targetTransform.pos, targetStatus.transform.pos);
            var distance = transformResult.pos - targetStatus.transform.pos;
            float distanceSQ = math.lengthsq(distance);
            if (distanceSQ > targetDistance * targetDistance)
                transformResult.pos =
                    targetDistance * math.rsqrt(distanceSQ) * distance + targetStatus.transform.pos;*/
        }
        
        if (data.space == BulletSpace.Local)
            transformResult = math.mul(math.inverse(math.RigidTransform(transform)), transformResult);

        if(entityCount == 1)
        {
            var entity = entityManager.Instantiate(0, prefabLoadResult.PrefabRoot);

            __Instantiate(
                damage, 
                index, 
                cooldown, 
                transform.c3.xyz,
                transformResult, 
                parent, 
                entity, 
                prefabLoadResult.PrefabRoot, 
                //cameraRotation, 
                collisionWorld, 
                //characterBodies, 
                characterControls, 
                animationCurveTimes, 
                targetStatus, 
                //status, 
                ref data, 
                //ref target, 
                ref entityManager);
        }
        else
        {
            using(var entityArray = new NativeArray<Entity>(entityCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
            {
                entityManager.Instantiate(0, prefabLoadResult.PrefabRoot, entityArray);

                entityCount = 0;
                int count, i;
                do
                {
                    if (data.interval > math.FLT_MIN_NORMAL)
                    {
                        count = data.capacity - statusCount;
                        count = math.min(count, (int)math.floor((time - cooldown) / data.interval) + 1);
                    }
                    else
                        count = 1;

                    for(i = 0; i < count; ++i)
                    {
                        __Instantiate(
                            damage,
                            index,
                            cooldown,
                            transform.c3.xyz,
                            transformResult, 
                            parent,
                            entityArray[entityCount + i],
                            prefabLoadResult.PrefabRoot,
                            //cameraRotation,
                            collisionWorld,
                            //characterBodies,
                            characterControls,
                            animationCurveTimes,
                            targetStatus,
                            //status,
                            ref data,
                            //ref target,
                            ref entityManager);

                        cooldown += data.interval;
                    }

                    entityCount += count;

                    statusCount += count;
                    if (statusCount == data.capacity)
                    {
                        statusCount = 0;

                        cooldown += data.cooldown;
                    }
                } while (cooldown <= time);
            }
        }

        return true;
    }

    public void Update(
        BulletLocation location, 
        double time,
        in float3 up, 
        in quaternion cameraRotation, 
        in float4x4 transform,
        in Entity entity,
        in Entity lookAt, 
        in CollisionWorld collisionWorld,
        in ComponentLookup<Parent> parents,
        in ComponentLookup<PhysicsCollider> physicsColliders,
        in ComponentLookup<KinematicCharacterBody> characterBodies, 
        in ComponentLookup<ThirdPersonCharacterControl> characterControls,
        in ComponentLookup<AnimationCurveTime> animationCurveTimes,
        in ComponentLookup<PrefabLoadResult> prefabLoadResults,
        in DynamicBuffer<BulletPrefab> prefabs,
        in DynamicBuffer<BulletActiveIndex> activeIndices, 
        in DynamicBuffer<BulletMessage> inputMessages,
        ref DynamicBuffer<Message> outputMessages,
        ref DynamicBuffer<BulletTargetStatus> targetStates,
        ref DynamicBuffer<BulletStatus> states,
        ref EntityCommandBuffer.ParallelWriter entityManager,
        ref BulletVersion version, 
        ref Random random)
    {
        //time = math.max(time, math.DBL_MIN_NORMAL);
        int numStates = states.Length, numBullets = bullets.Length;
        if (numStates < numBullets)
        {
            Entity temp = entity;
            AnimationCurveTime animationCurveTime;
            while(!animationCurveTimes.TryGetComponent(temp, out animationCurveTime) && parents.TryGetComponent(temp, out var parent))
                temp = parent.Value;

            if (animationCurveTime.start > 0.0)
                time = animationCurveTime.start;

            states.Resize(numBullets, NativeArrayOptions.ClearMemory);

            /*BulletStatus status;
            status.times = 0;
            status.count = 0;
            for (int i = numStates; i < numBullets; ++i)
            {
                status.cooldown = time + bullets[i].startTime;

                states[i] = status;
            }*/
        }
        
        ++version.value;

        //var lookAt = lookAts[index];
        int numActiveIndices = activeIndices.Length;
        BulletActiveIndex activeIndex;
        for (int i = 0; i < numActiveIndices; ++i)
        {
            activeIndex = activeIndices[i];
            Update(
                location, 
                activeIndex.damage, 
                activeIndex.value, 
                version.value,
                time,
                up, 
                cameraRotation, 
                transform,
                entity, 
                lookAt, 
                collisionWorld,
                physicsColliders,
                characterBodies, 
                characterControls, 
                animationCurveTimes, 
                prefabLoadResults,
                prefabs, 
                inputMessages,
                ref outputMessages,
                ref targetStates,
                ref entityManager, 
                ref states.ElementAt(activeIndex.value), 
                ref random);
        }
    }

    private static bool __Check(float minDot, float minDistance, in float3 distance, in quaternion rotation)
    {
        if (minDot < 1.0f || minDistance > math.FLT_MIN_NORMAL)
        {
            float3 vector = math.mul(math.inverse(rotation), distance);
            if (math.abs(vector.z) < minDistance)
                return false;

            if (minDot < 1.0f)
            {
                float dot = math.normalizesafe(vector).z;
                if (dot < minDot)
                    return false;
            }
        }

        return true;
    }
    
    private static bool __Check(
        BulletLocation location, 
        uint groundBelongsTo, 
        in Entity entity, 
        in CollisionWorld collisionWorld, 
        in ComponentLookup<KinematicCharacterBody> characterBodies)
    {
        if (location != 0)
        {
            if (characterBodies.TryGetComponent(entity, out var characterBody) &&
                characterBodies.IsComponentEnabled(entity) && 
                characterBody.IsGrounded &&
                (groundBelongsTo == 0 ||
                 (groundBelongsTo & collisionWorld.Bodies[characterBody.GroundHit.RigidBodyIndex].Collider.Value
                     .GetCollisionFilter(characterBody.GroundHit.ColliderKey).BelongsTo) != 0))
            {
                if ((location & BulletLocation.Ground) != BulletLocation.Ground)
                    return false;
            }
            else
            {
                if ((location & BulletLocation.Air) != BulletLocation.Air)
                    return false;
            }
        }

        return true;
    }

    private static void __Instantiate(
        int damage, 
        int index, 
        double time, 
        in float3 parentPosition,
        in RigidTransform transform, 
        in Entity parent,
        in Entity entity,
        in Entity prefabRoot,
        //in quaternion cameraRotation,
        in CollisionWorld collisionWorld,
        //in ComponentLookup<KinematicCharacterBody> characterBodies,
        in ComponentLookup<ThirdPersonCharacterControl> characterControls,
        in ComponentLookup<AnimationCurveTime> animationCurveTimes,
        in BulletTargetStatus targetStatus,
        //in BulletStatus status,
        ref Bullet data, 
        //ref Target target,
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        //var entity = entityManager.Instantiate(0, prefabRoot);

        BulletEntity bulletEntity;
        bulletEntity.index = index;
        bulletEntity.parent = parent;
        entityManager.AddComponent(1, entity, bulletEntity);

        if (data.space == BulletSpace.Local)
        {
            Parent temp;
            temp.Value = parent;
            entityManager.AddComponent(1, entity, temp);

            //transformResult = math.mul(math.inverse(math.RigidTransform(transform)), transformResult);
        }

        var transformResult = transform;
        
        int rigidBodyIndex = targetStatus.target == Entity.Null ? -1 : collisionWorld.GetRigidBodyIndex(targetStatus.target);
        float3 targetPosition = rigidBodyIndex == -1
            ? targetStatus.targetPosition
            : collisionWorld.Bodies[rigidBodyIndex].WorldFromBody.pos;
        if (data.targetSpace == BulletSpace.Local && targetStatus.target != Entity.Null)
        {
            Parent temp;
            temp.Value = targetStatus.target;
            entityManager.AddComponent(1, entity, temp);

            transformResult.pos -= targetPosition;
        }

        FollowTarget followTarget;
        switch (data.followTarget)
        {
            case BulletFollowTarget.Source:
                //followTarget.flag = 0;
                followTarget.entity = parent;
                followTarget.offset = data.space == BulletSpace.Local ? transformResult.pos : transformResult.pos - parentPosition;//transform.c3.xyz;
                followTarget.space = FollowTargetSpace.Camera;
                entityManager.SetComponent(1, entity, followTarget);
                entityManager.SetComponentEnabled<FollowTarget>(1, entity, true);
                break;
            case BulletFollowTarget.Destination:
                if (targetStatus.target != Entity.Null)
                {
                    followTarget.entity = targetStatus.target;
                    
                    followTarget.offset = targetStatus.targetPosition - targetPosition;
                        /*if (target.coordinate == BulletTargetCoordinate.Origin)
                            followTarget.offset = float3.zero;
                        else
                        {
                            int rigidBodyIndex = collisionWorld.GetRigidBodyIndex(entity);
                            if (rigidBodyIndex == -1)
                                followTarget.offset = float3.zero;
                            else
                            {
                                var collider = collisionWorld.Bodies[rigidBodyIndex].Collider;

                                followTarget.offset = collider.IsCreated ? collider.Value.MassProperties.MassDistribution.Transform.pos : float3.zero;
                            }
                        }*/

                    followTarget.space = FollowTargetSpace.World;
                    //followTarget.flag = characterBodies.HasComponent(prefabRoot) ? 0 : FollowTargetFlag.KeepVelocity;
                    entityManager.SetComponent(1, entity, followTarget);
                    entityManager.SetComponentEnabled<FollowTarget>(1, entity, true);
                }

                break;
        }

        LocalTransform localTransform;
        localTransform.Scale = 1.0f;
        localTransform.Position = transformResult.pos;
        localTransform.Rotation = transformResult.rot;
        entityManager.SetComponent(1, entity, localTransform);


        if (animationCurveTimes.HasComponent(prefabRoot))
        {
            AnimationCurveTime animationCurveTime;
            //animationCurveTime.version = 0;
            animationCurveTime.value = 0;
            animationCurveTime.elapsed = time;
            animationCurveTime.start = time;
            entityManager.SetComponent(1, entity, animationCurveTime);
        }

        if (data.animationCurveSpeed > math.FLT_MIN_NORMAL)
        {
            AnimationCurveSpeed animationCurveSpeed;
            animationCurveSpeed.value = data.animationCurveSpeed;
            entityManager.SetComponent(1, entity, animationCurveSpeed);
        }

        if (data.linearSpeed > math.FLT_MIN_NORMAL)
        {
            var forward = math.forward(transformResult.rot);

            if (characterControls.TryGetComponent(prefabRoot, out var control))
            {
                control.MoveVector = forward * data.linearSpeed;
                entityManager.SetComponent(1, entity, control);
            }
            else
            {
                PhysicsVelocity velocity;
                velocity.Angular = data.angularSpeed;
                velocity.Linear = forward * data.linearSpeed;
                //velocity.Linear.y = 0.0f;
                entityManager.SetComponent(1, entity, velocity);
            }
        }

        if (damage != 0)
        {
            EffectDamage effectDamage;
            effectDamage.scale = damage;
            entityManager.AddComponent(1, entity, effectDamage);
        }

    }
}

public struct BulletDefinitionData : IComponentData
{
    public BlobAssetReference<BulletDefinition> definition;
}

public struct BulletStatus : IBufferElementData
{
    public int times;
    public int count;
    public double cooldown;
}

public struct BulletTargetStatus : IBufferElementData
{
    public int version;
    public double cooldown;
    public Entity target;
    public float3 targetPosition;
    public RigidTransform transform;
}

public struct BulletPrefab : IBufferElementData
{
    public Entity loader;
}

public struct BulletMessage : IBufferElementData
{
    //public FixedString128Bytes key;

    public FixedString128Bytes name;

    public WeakObjectReference<Object> value;
}

public struct BulletActiveIndex : IBufferElementData
{
    public int value;
    public int damage;
}

public struct BulletVersion : IComponentData
{
    public int value;
}

public struct BulletEntity : IComponentData
{
    public int index;
    public Entity parent;
}