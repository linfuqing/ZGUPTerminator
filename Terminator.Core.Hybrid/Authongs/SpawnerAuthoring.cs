using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using ZG;

public class SpawnerAuthoring : MonoBehaviour
{
    [Serializable]
    public struct AreaData
    {
        public string name;
        
        //public Bounds bounds;
        //public Color boundColor;
        
        [UnityEngine.Serialization.FormerlySerializedAs("areaColor")]
        public Color color;

        [Tooltip("怪物朝向，from到to之间的随机范围")]
        public Quaternion from;
        
        [Tooltip("怪物朝向，from到to之间的随机范围")]
        public Quaternion to;

        [Tooltip("刷怪区域的中点")]
        public Vector3 position;
        [Tooltip("刷怪区域的宽")]
        public float width;
        [Tooltip("刷怪区域的高")]
        public float height;
        [Tooltip("刷怪区域的长")]
        public float length;

        [Tooltip("相对人的位置还是世界位置")]
        public SpawnerSpace space;
    }

    [Serializable]
    public struct AttributeData
    {
        public string name;
        
        public int hp;
        public int hpMax;
        public int level;
        public int levelMax;
        public int exp;
        public int expMax;
        public int gold;
        public int goldMax;
        
        public float speedScale;
        public float speedScaleMax;
        
        public float speedScaleBuff;
        public float hpBuff;
        public float levelBuff;
        public float expBuff;
        public float goldBuff;
        
        public float interval;
        
        #region CSV
        [CSVField]
        public string 刷怪属性名称
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public int 刷怪血量
        {
            set
            {
                hp = value;
            }
        }

        [CSVField]
        public int 刷怪血量最大值
        {
            set
            {
                hpMax = value;
            }
        }

        [CSVField]
        public int 刷怪获得进度
        {
            set
            {
                level = value;
            }
        }
        
        [CSVField]
        public int 刷怪获得进度最大值
        {
            set
            {
                levelMax = value;
            }
        }
        
        [CSVField]
        public int 刷怪获得经验
        {
            set
            {
                exp = value;
            }
        }
        
        [CSVField]
        public int 刷怪获得经验最大值
        {
            set
            {
                expMax = value;
            }
        }

        [CSVField]
        public int 刷怪获得金币
        {
            set
            {
                gold = value;
            }
        }
        
        [CSVField]
        public int 刷怪获得金币最大值
        {
            set
            {
                goldMax = value;
            }
        }

        [CSVField]
        public float 刷怪速度缩放
        {
            set
            {
                speedScale = value;
            }
        }
        
        [CSVField]
        public float 刷怪速度缩放最大值
        {
            set
            {
                speedScaleMax = value;
            }
        }

        [CSVField]
        public float 刷怪速度缩放每秒增益
        {
            set
            {
                speedScaleBuff = value;
            }
        }
        
        [CSVField]
        public float 刷怪血量每秒增益
        {
            set
            {
                hpBuff = value;
            }
        }
        
        [CSVField]
        public float 刷怪获得进度每秒增益
        {
            set
            {
                levelBuff = value;
            }
        }
        
        [CSVField]
        public float 刷怪获得经验每秒增益
        {
            set
            {
                expBuff = value;
            }
        }
        
        [CSVField]
        public float 刷怪获得金币每秒增益
        {
            set
            {
                goldBuff = value;
            }
        }
        
        [CSVField]
        public float 刷怪增益间隔
        {
            set
            {
                interval = value;
            }
        }
        #endregion
    }

    [Serializable]
    public struct SpawnerData
    {
        [Serializable]
        public struct Area
        {
            public string name;
            public string attributeName;
            
            public LayerMask layerMask;
        }
        
        public string name;

        public Area[] areas;
        
        [Tooltip("怪的预制体")]
        public GameObject prefab;
        [Tooltip("开始时间")]
        public float startTime;
        [Tooltip("结束时间")]
        public float endTime;
        [Tooltip("每个怪刷新的间隔时间")]
        public float interval;
        [Tooltip("每波次的CD")]
        public float cooldown;
        
        [Tooltip("场上所有的怪大于该数量就不会再刷怪")]
        public int maxCount;
        [Tooltip("低于多少同类型怪下一波马上被激活")]
        public int maxCountToNextTime;
        [Tooltip("剩余多少同类型的怪下一波才会被激活，用来控制场上怪的个数")]
        [UnityEngine.Serialization.FormerlySerializedAs("remainingCountToNextTime")]
        public int minCountToNextTime;
        [Tooltip("每波刷几个")]
        public int countPerTime;
        [Tooltip("刷的波数")]
        public int times;
        [Tooltip("失败的时候每个区域尝试刷几次，-1代表不检测，强制刷")]
        public int tryTimesPerArea;
        [Tooltip("区域标签，不填表示默认区域")]
        public LayerMask layerMask;

        #region CSV

        [CSVField]
        public string 刷怪名称
        {
            set
            {
                name = value;
            }
        }

        [CSVField]
        public string 区域名称
        {
            set
            {
                var parameters = value.Split('/');
                string parameter;
                int numParameters = parameters.Length, index, count;
                areas = new Area[numParameters];
                for(int i = 0; i < numParameters; ++i)
                {
                    parameter = parameters[i];
                    index = parameter.IndexOf(":");
                    ref var area = ref areas[i];
                    if (index == -1)
                    {
                        area.name = parameter;
                        area.layerMask.value = 0;
                        area.attributeName = null;
                    }
                    else
                    {
                        area.name = parameter.Remove(index);
                        
                        count = parameter.IndexOf(":", index + 1);
                        if (count == -1)
                        {
                            area.attributeName = null;
                            
                            area.layerMask.value = int.Parse(parameter.Substring(index + 1));
                        }
                        else
                        {
                            area.attributeName = parameter.Substring(index + 1, count - index - 1);
                            
                            area.layerMask.value = int.Parse(parameter.Substring(count + 1));
                        }
                    }
                }
            }
        }

        [CSVField]
        public string 预制体路径
        {
            set
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(value);
            }
        }

        [CSVField]
        public float 开始时间
        {
            set
            {
                startTime = value;
            }
        }
        
        [CSVField]
        public float 结束时间
        {
            set
            {
                endTime = value;
            }
        }
        
        [CSVField]
        public float 间隔时间
        {
            set
            {
                interval = value;
            }
        }

        [CSVField]
        public float 冷却时间
        {
            set
            {
                cooldown = value;
            }
        }

        [CSVField]
        public int 最大数量
        {
            set
            {
                maxCount = value;
            }
        }
        
        [CSVField]
        public int 下一波最大数量
        {
            set
            {
                maxCountToNextTime = value;
            }
        }
        
        [CSVField]
        public int 下一波最小数量
        {
            set
            {
                minCountToNextTime = value;
            }
        }
        
        [CSVField]
        public int 每波数量
        {
            set
            {
                countPerTime = value;
            }
        }
        
        [CSVField]
        public int 波数
        {
            set
            {
                times = value;
            }
        }
        
        [CSVField]
        public int 区域尝试次数
        {
            set
            {
                tryTimesPerArea = value;
            }
        }

        [CSVField]
        public int 标签
        {
            set
            {
                layerMask = value;
            }
        }
        
        #endregion
    }

    [SerializeField] 
    [Tooltip("默认打开的区域标签，触发器生效后该标签被替换")]
    [UnityEngine.Serialization.FormerlySerializedAs("layerMask")]
    internal LayerMask _layerMask;

    //[SerializeField] 
    //[Tooltip("默认打开的关卡标签")]
    //internal LayerMask _layerMaskInclude;

    [SerializeField]
    internal AreaData[] _areas;

    [SerializeField] 
    internal AttributeData[] _attributes;
    
    #region CSV
    [SerializeField]
    [CSV("_attributes", guidIndex = -1, nameIndex = 0)]
    internal string _attributesPath;
    #endregion
    
    [SerializeField]
    [UnityEngine.Serialization.FormerlySerializedAs("_prefabs")]
    internal SpawnerData[] _spawners;
    
    #region CSV
    [SerializeField]
    [CSV("_spawners", guidIndex = -1, nameIndex = 0)]
    internal string _spawnersPath;
    #endregion
    
    class Baker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            SpawnerDefinitionData instance;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<SpawnerDefinition>();

                int i, numAreas = authoring._areas.Length;
                var areas = builder.Allocate(ref root.areas, numAreas);
                for (i = 0; i < numAreas; ++i)
                {
                    ref var source = ref authoring._areas[i];
                    ref var destination = ref areas[i];

                    //destination.layerMask = source.layerMask.value;
                    destination.space = source.space;
                    destination.from = source.from;
                    destination.to = source.to;
                    destination.aabb.Center = source.position;
                    destination.aabb.Extents =
                        math.float3(source.width * 0.5f, source.height * 0.5f, source.length * 0.5f);
                }

                int numAttributes = authoring._attributes.Length;
                var attributes = builder.Allocate(ref root.attributes, numAttributes);
                for (i = 0; i < numAttributes; ++i)
                {
                    ref var source = ref authoring._attributes[i];
                    ref var destination = ref attributes[i];

                    destination.hp = source.hp;
                    destination.hpMax = source.hpMax;
                    destination.level = source.level;
                    destination.levelMax = source.levelMax;
                    destination.exp = source.exp;
                    destination.expMax = source.expMax;
                    destination.gold = source.gold;
                    destination.goldMax = source.goldMax;
                    destination.speedScale = source.speedScale;
                    destination.speedScaleMax = source.speedScaleMax;
                    destination.speedScaleBuff = source.speedScaleBuff;
                    destination.hpBuff = source.hpBuff;
                    destination.levelBuff = source.levelBuff;
                    destination.expBuff = source.expBuff;
                    destination.goldBuff = source.goldBuff;
                    destination.interval = source.interval;
                }

                var numSpawners = authoring._spawners.Length;
                var prefabLoaders = AddBuffer<SpawnerPrefab>(entity);
                //prefabLoaders.ResizeUninitialized(numPrefabs);
                
                var prefabs = builder.Allocate(ref root.spawners, numSpawners);
                BlobBuilderArray<SpawnerDefinition.AreaIndex> areaIndices;
                var prefabEntities = new Dictionary<GameObject, int>();
                RequestEntityPrefabLoaded requestEntityPrefabLoaded;
                SpawnerPrefab prefab;
                int prefabLoaderIndex, numAreaIndices, j, k;
                for (i = 0; i < numSpawners; ++i)
                {
                    ref var destination = ref prefabs[i];
                    ref var source = ref authoring._spawners[i];
                    destination.startTime = source.startTime;
                    destination.endTime = source.endTime > math.FLT_MIN_NORMAL ? source.endTime : float.MaxValue;
                    destination.interval = source.interval;
                    destination.cooldown = source.cooldown;
                    destination.maxCount = source.maxCount;
                    destination.maxCountToNextTime = source.maxCountToNextTime;
                    destination.minCountToNextTime = source.minCountToNextTime;
                    destination.countPerTime = source.countPerTime;
                    destination.times = source.times;
                    destination.tryTimesPerArea = source.tryTimesPerArea;

                    numAreaIndices = source.areas == null ? 0 : source.areas.Length;
                    areaIndices = builder.Allocate(ref destination.areaIndices, numAreaIndices);
                    for (j = 0; j < numAreaIndices; ++j)
                    {
                        ref var area = ref source.areas[j];
                        ref var areaIndex = ref areaIndices[j];
                        areaIndex.value = -1;
                        for (k = 0; k < numAreas; ++k)
                        {
                            if (authoring._areas[k].name == area.name)
                            {
                                areaIndex.value = k;

                                break;
                            }
                        }

                        if (areaIndex.value == -1)
                            Debug.LogError(
                                $"Area {area.name} of spawner {source.name} can not been found!");

                        areaIndex.attributeIndex = -1;
                        if (!string.IsNullOrEmpty(area.attributeName))
                        {
                            for (k = 0; k < numAttributes; ++k)
                            {
                                if (authoring._attributes[k].name == area.attributeName)
                                {
                                    areaIndex.attributeIndex = k;

                                    break;
                                }
                            }

                            if (areaIndex.attributeIndex == -1)
                                Debug.LogError(
                                    $"Attribute {area.attributeName} of spawner {source.name} can not been found!");
                        }

                        areaIndex.layerMask = area.layerMask.value;
                    }
                    
                    if(source.prefab == null)
                        Debug.LogError($"Spawner {source.name} can not been found!");

                    if (!prefabEntities.TryGetValue(source.prefab, out prefabLoaderIndex))
                    {
                        prefabLoaderIndex = prefabLoaders.Length;
                        
                        prefabEntities[source.prefab] = prefabLoaderIndex;

                        prefab.loader = CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false,
                            source.prefab.name);

                        requestEntityPrefabLoaded.Prefab = new EntityPrefabReference(source.prefab);
                        AddComponent(prefab.loader, requestEntityPrefabLoaded);

                        prefabLoaders.Add(prefab);
                    }

                    destination.loaderIndex = prefabLoaderIndex;
                    
                    destination.layerMask = source.layerMask.value;
                }

                instance.definition = builder.CreateBlobAssetReference<SpawnerDefinition>(Allocator.Persistent);
            }
            
            AddBlobAsset(ref instance.definition, out _);
            
            AddComponent(entity, instance);
            AddComponent<SpawnerStatus>(entity);

            SpawnerLayerMask layerMask;
            layerMask.value = authoring._layerMask.value;
            AddComponent(entity, layerMask);
            
            AddComponent<SpawnerLayerMaskOverride>(entity);

            //SpawnerLayerMaskInclude layerMaskInclude;
            //layerMaskInclude.value = authoring._layerMaskInclude.value;
            AddComponent<SpawnerLayerMaskInclude>(entity);
            
            AddComponent<SpawnerLayerMaskExclude>(entity);
        }
    }

    private void OnDrawGizmosSelected()
    {
        foreach (var area in _areas)
        {
            //Gizmos.color = area.boundColor;
            //Gizmos.DrawWireCube(area.bounds.center, area.bounds.size);
            
            Gizmos.color = area.color;
            Gizmos.DrawWireCube(area.position, new Vector3(area.width, area.height, area.length));
        }
    }

    private void OnValidate()
    {
        int numAreas = _areas == null ? 0 : _areas.Length;
        for (int i = 0; i < numAreas; ++i)
        {
            ref var area = ref _areas[i];
            if(area.from == default)
                area.from = Quaternion.identity;

            if (area.to == default)
                area.to = Quaternion.identity;
        }
    }
}
#endif