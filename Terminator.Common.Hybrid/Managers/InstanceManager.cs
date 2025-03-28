using System;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Events;

public sealed class InstanceManager : MonoBehaviour
{
    private struct Instance
    {
        public float destroyTime;
        public string destroyMessageName;
        public UnityEngine.Object destroyMessageValue;
        public GameObject prefab;
        //public InstanceManager manager;
    }
    
    [Serializable]
    public struct Prefab
    {
        public string name;
        public string destroyMessageName;
        public UnityEngine.Object destroyMessageValue;
        public GameObject gameObject;
        public float destroyTime;
    }

    [Serializable]
    internal class StringEvent : UnityEvent<string>
    {
        
    }

    internal sealed class Factory : MonoBehaviour
    {
        private struct InstanceToDestroy
        {
            public double time;
            public GameObject gameObject;
            public GameObject prefab;
        }

        private struct InstancesToCreate
        {
            public int entityCount;
            public GameObject prefab;
            
            public int Submit(
                int maxEntityCount, 
                int startIndex, 
                in ComponentLookup<LocalToWorld> localToWorlds, 
                ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs, 
                ref NativeList<Entity> entities, 
                List<GameObject> results, 
                SystemBase system)
            {
                int entityCount = Mathf.Min(this.entityCount, maxEntityCount), index;
                Entity entity;
                var entityManager = system.EntityManager;
                for (int i = 0; i < entityCount; ++i)
                {
                    index = i + startIndex;
                    entity = entities[index];
                    if (entityManager.IsEnabled(entity) &&
                        entityManager.HasComponent<global::Instance>(entity) &&
                        //entityManager.IsComponentEnabled<global::Instance>(entity) && 
                        results[index] != null)
                        continue;

                    __Destroy(results[index], prefab);

                    results.RemoveAt(index);
                    
                    entities.RemoveAt(index);

                    --entityCount;

                    --this.entityCount;
                }

                if (entityCount < 1)
                    return 0;

                this.entityCount -= entityCount;

                system.EntityManager.AddComponent<CopyMatrixToTransformInstanceID>(
                    entities.AsArray().GetSubArray(startIndex, entityCount));

                instanceIDs.Update(system);
                localToWorlds.Update(system);
                CopyMatrixToTransformInstanceID instanceID;
                instanceID.isSendMessageOnDestroy = true;

                GameObject gameObject;
                Transform transform;
                LocalToWorld localToWorld;
                for (int i = 0; i < entityCount; ++i)
                {
                    gameObject = results[i];

                    UnityEngine.Assertions.Assert.IsTrue(gameObject.name.Contains(prefab.name));

                    transform = gameObject.transform;

                    entity = entities[i];

#if UNITY_EDITOR
                    entityManager.SetName(entity, $"{gameObject.name}({transform.GetInstanceID()})");
#endif

                    if (localToWorlds.TryGetComponent(entity, out localToWorld))
                    {
                        transform.localPosition = localToWorld.Position;
                        transform.localRotation = localToWorld.Rotation;
                    }

                    gameObject.SetActive(true);

                    instanceID.value = transform.GetInstanceID();

                    instanceIDs[entity] = instanceID;
                }
                
                results.RemoveRange(startIndex, entityCount);
                entities.RemoveRange(startIndex, entityCount);

                return entityCount;
            }
        }
        
        private class System
        {
            private ComponentLookup<LocalToWorld> __localToWorlds;
            private ComponentLookup<CopyMatrixToTransformInstanceID> __instanceIDs;
            private NativeList<Entity> __entities;
            private List<GameObject> __results; 
            private List<InstancesToCreate> __instances;

            public System( 
                in ComponentLookup<LocalToWorld> localToWorlds, 
                ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs)
            {
                __localToWorlds = localToWorlds;
                __instanceIDs = instanceIDs;
                __entities = new NativeList<Entity>(Allocator.Temp);
                __results = new List<GameObject>();
                __instances = new List<InstancesToCreate>();
            }

            public void Dispose()
            {
                __entities.Dispose();
            }

            public void Apply(
                in NativeArray<Entity> entities, 
                IEnumerable<GameObject> results, 
                GameObject prefab)
            {
                __entities.AddRange(entities);
                __results.AddRange(results);
                
                UnityEngine.Assertions.Assert.AreEqual(__results.Count, __entities.Length);
                
                InstancesToCreate instance;
                instance.entityCount = entities.Length;
                instance.prefab = prefab;
                
                __instances.Add(instance);
            }

            public int Submit(
                int maxEntityCount, 
                SystemBase system)
            {
                InstancesToCreate instance;
                int startIndex = 0, count, numInstances = __instances.Count;
                for (int i = 0; i < numInstances; ++i)
                {
                    instance = __instances[i];
                    count = instance.Submit(
                        maxEntityCount, 
                        startIndex, 
                        __localToWorlds, 
                        ref __instanceIDs, 
                        ref __entities,
                        __results, 
                        system);

                    if (instance.entityCount < 1)
                        __instances.RemoveAt(i);

                    startIndex += count;

                    if (startIndex == maxEntityCount)
                        break;
                }
                
                return startIndex;
            }
        }
        
        private List<InstanceToDestroy> __instancesToDestroy = new List<InstanceToDestroy>();
        
        private Dictionary<SystemBase, System> __systems = new Dictionary<SystemBase, System>();

        private static Factory __instance;

        public static Factory instance
        {
            get
            {
                if (__instance == null)
                {
                    var temp = new GameObject();
                    temp.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(temp);

                    __instance = temp.AddComponent<Factory>();
                }

                return __instance;
            }
        }

        public void Create(
            ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs, 
            in ComponentLookup<LocalToWorld> localToWorlds, 
            in NativeArray<Entity> entities, 
            IEnumerable<GameObject> results, 
            GameObject prefab, 
            SystemBase system)
        {
            if (!__systems.TryGetValue(system, out var instance))
            {
                instance = new System(localToWorlds, ref instanceIDs);

                __systems[system] = instance;
            }
            
            instance.Apply(entities, results, prefab);
        }
        
        public void Destroy(float time, GameObject gameObject, GameObject prefab)
        {
            if (time > Mathf.Epsilon)
            {
                InstanceToDestroy instance;
                instance.time = Time.timeAsDouble + time;
                instance.gameObject = gameObject;
                instance.prefab = prefab;

                if (__instancesToDestroy == null)
                    __instancesToDestroy = new List<InstanceToDestroy>();

                __instancesToDestroy.Add(instance);
            }
            else
                __Destroy(gameObject, prefab);
        }

        void Update()
        {
            float deltaTime = Time.maximumDeltaTime * 0.5f;
            long tick = DateTime.Now.Ticks;
            foreach (var system in __systems)
            {
                while (system.Value.Submit(1, system.Key) > 0)
                {
                    if ((DateTime.Now.Ticks - tick) * 1.0f / TimeSpan.TicksPerSecond > deltaTime)
                        return;
                }
            }
            
            int numInstancesToDestroy = __instancesToDestroy == null ? 0 : __instancesToDestroy.Count;
            if (numInstancesToDestroy > 0)
            {
                double time = Time.timeAsDouble;
                InstanceToDestroy instance;
                for (int i = 0; i < numInstancesToDestroy; ++i)
                {
                    instance = __instancesToDestroy[i];
                    if (instance.time > time)
                        continue;

                    __Destroy(instance.gameObject, instance.prefab);

                    __instancesToDestroy.RemoveAtSwapBack(i--);

                    --numInstancesToDestroy;
                }
            }
        }
    }

    [SerializeField] 
    [UnityEngine.Serialization.FormerlySerializedAs("_onCount")]
    internal StringEvent _onAcitveCount;

    //public UnityEngine.Object TEMP;
    [SerializeField]
    internal Prefab[] _prefabs;

    private static Dictionary<string, (InstanceManager, int)> __prefabIndices;

    private static Dictionary<GameObject, List<GameObject>> __gameObjects;

    private static Dictionary<int, InstanceManager> __instanceManagers;

    private Dictionary<int, Instance> __instances;

    private HashSet<AsyncInstantiateOperation<GameObject>> __results;

    public static int activeCount
    {
        get;

        private set;
    }

    public static void Destroy(int instanceID, bool isSendMessage)
    {
        if (__instanceManagers == null ||
            !__instanceManagers.TryGetValue(instanceID, out var instanceManager) ||
            instanceManager.__instances == null ||
            !instanceManager.__instances.TryGetValue(instanceID, out var instance))
        {
            Debug.LogWarning($"Destroy {instanceID} has been failed!", Resources.InstanceIDToObject(instanceID));
            
            return;
        }

        instanceManager.__Destroy(instanceID, instance, isSendMessage);
        //instanceManager.StartCoroutine(instanceManager.__Destroy(instanceID, instance, isSendMessage));
    }
    
    public static void Instantiate(
        string name, 
        SystemBase system, 
        in NativeArray<Entity> entities, 
        in ComponentLookup<LocalToWorld> localToWorlds, 
        ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs)
    {
        if (!__prefabIndices.TryGetValue(name, out var prefabIndex))
        {
            Debug.LogError($"The prefab {name} can not been found!");

            return;
        }

        var manager = prefabIndex.Item1;

        manager.StartCoroutine(manager.__Instantiate(
            manager._prefabs[prefabIndex.Item2],
            system,
            entities,
            instanceIDs,
            localToWorlds));
    }

    private IEnumerator __Instantiate(
        Prefab prefab, 
        SystemBase system, 
        NativeArray<Entity> entities, 
        ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs, 
        ComponentLookup<LocalToWorld> localToWorlds)
    {
        int numEntities = entities.Length, numGameObjects;
        List<GameObject> results = null;
        if (__gameObjects != null && __gameObjects.TryGetValue(prefab.gameObject, out var gameObjects))
        {
            numGameObjects = gameObjects == null ? 0 : gameObjects.Count;
            if (numGameObjects > 0)
            {
                results = new List<GameObject>(numEntities);

                if (numGameObjects > numEntities)
                {
                    int temp = numGameObjects - numEntities;
                    for(int i = numGameObjects - 1; i >= temp; --i)
                        results.Add(gameObjects[i]);
                    
                    gameObjects.RemoveRange(temp, numEntities);

                    numGameObjects = 0;
                }
                else
                {
                    numGameObjects = numEntities - numGameObjects;
                    
                    results.AddRange(gameObjects);
                    
                    gameObjects.Clear();
                }
            }
            else
                numGameObjects = numEntities;
        }
        else 
            numGameObjects = numEntities;

        bool isCreated;
        if (numGameObjects > 0)
        {
            isCreated = numGameObjects > 1;
            if (isCreated)
            {
                entities = new NativeArray<Entity>(entities, Allocator.Persistent);
                
                var result = InstantiateAsync(prefab.gameObject, numGameObjects, this.transform);

                if (__results == null)
                    __results = new HashSet<AsyncInstantiateOperation<GameObject>>();

                __results.Add(result);

                yield return result;

                __results.Remove(result);

                if (__instanceManagers == null)
                    __instanceManagers = new Dictionary<int, InstanceManager>();
                    
                if (__instances == null)
                    __instances = new Dictionary<int, Instance>();

                if (results == null)
                    results = new List<GameObject>(numGameObjects);

                Instance instance;
                instance.destroyTime = prefab.destroyTime;
                instance.destroyMessageName = prefab.destroyMessageName;
                instance.destroyMessageValue = prefab.destroyMessageValue;
                instance.prefab = prefab.gameObject;
                
                int transformInstanceID;
                foreach (var temp in result.Result)
                {
                    transformInstanceID = temp.transform.GetInstanceID();
                    
                    __instanceManagers.Add(transformInstanceID, this);

                    __instances.Add(transformInstanceID, instance);

                    results.Add(temp);
                }
            }
            else
            {
                Instance instance;
                instance.destroyTime = prefab.destroyTime;
                instance.destroyMessageName = prefab.destroyMessageName;
                instance.destroyMessageValue = prefab.destroyMessageValue;
                instance.prefab = prefab.gameObject;

                int transformInstanceID;
                GameObject result;
                for (int i = 0; i < numGameObjects; ++i)
                {
                    result = Instantiate(prefab.gameObject, this.transform);

                    transformInstanceID = result.transform.GetInstanceID();

                    if (__instanceManagers == null)
                        __instanceManagers = new Dictionary<int, InstanceManager>();
                    
                    __instanceManagers.Add(transformInstanceID, this);
                    
                    if (__instances == null)
                        __instances = new Dictionary<int, Instance>();

                    __instances.Add(transformInstanceID, instance);

                    if (results == null)
                        results = new List<GameObject>();

                    results.Add(result);
                }

                //Wait For LocalToWorld
                yield return new WaitForEndOfFrame();
            }
        }
        else
        {
            isCreated = false;
            
            //Wait For LocalToWorld
            yield return new WaitForEndOfFrame();
        }

        Factory.instance.Create(ref instanceIDs, localToWorlds, entities, results, prefab.gameObject, system);

        if (isCreated)
            entities.Dispose();

        /*Entity entity;
        var entityManager = system.EntityManager;
        for (int i = 0; i < numEntities; ++i)
        {
            entity = entities[i];
            if (entityManager.IsEnabled(entity) &&
                entityManager.HasComponent<global::Instance>(entity) &&
                //entityManager.IsComponentEnabled<global::Instance>(entity) &&
                results[i] != null)
                continue;

            __Destroy(results[i], prefab.gameObject);

            results.RemoveAtSwapBack(i);

            entities[i--] = entities[--numEntities];
        }

        if(numEntities < 1)
            yield break;

        system.EntityManager.AddComponent<CopyMatrixToTransformInstanceID>(entities.GetSubArray(0, numEntities));

        instanceIDs.Update(system);
        localToWorlds.Update(system);
        CopyMatrixToTransformInstanceID instanceID;
        instanceID.isSendMessageOnDestroy = true;

        GameObject gameObject;
        Transform transform;
        LocalToWorld localToWorld;
        for (int i = 0; i < numEntities; ++i)
        {
            gameObject = results[i];

            UnityEngine.Assertions.Assert.IsTrue(gameObject.name.Contains(prefab.gameObject.name));

            transform = gameObject.transform;

            entity = entities[i];

#if UNITY_EDITOR
            entityManager.SetName(entity, $"{gameObject.name}({transform.GetInstanceID()})");
#endif

            if (localToWorlds.TryGetComponent(entity, out localToWorld))
            {
                transform.localPosition = localToWorld.Position;
                transform.localRotation = localToWorld.Rotation;
            }

            gameObject.SetActive(true);

            instanceID.value = transform.GetInstanceID();

            instanceIDs[entity] = instanceID;
        }

        entities.Dispose();

        activeCount += numEntities;

        if(_onAcitveCount != null)
            _onAcitveCount.Invoke(activeCount.ToString());*/
    }

    private void __Destroy(int instanceID, Instance instance, bool isSendMessage)
    {
        --activeCount;

        if(_onAcitveCount != null)
            _onAcitveCount.Invoke(activeCount.ToString());
        
        var transform = Resources.InstanceIDToObject(instanceID) as Transform;
        var gameObject = transform == null ? null : transform.gameObject;
        if (gameObject == null)
            return;

        if (isSendMessage)
        {
            if (!string.IsNullOrEmpty(instance.destroyMessageName))
                gameObject.BroadcastMessage(instance.destroyMessageName, instance.destroyMessageValue);

            //yield return new WaitForSeconds(instance.destroyTime);
            Factory.instance.Destroy(instance.destroyTime, gameObject, instance.prefab);
        }
        else
            __Destroy(gameObject, instance.prefab);
    }

    private static void __Destroy(GameObject gameObject, GameObject prefab)
    {
        if (gameObject == null)
            return;
        
        if (__gameObjects == null)
            __gameObjects = new Dictionary<GameObject, List<GameObject>>();

        if (!__gameObjects.TryGetValue(prefab, out var gameObjects))
        {
            gameObjects = new List<GameObject>();

            __gameObjects[prefab] = gameObjects;
        }

        gameObject.SetActive(false);
        
        gameObjects.Add(gameObject);
    }
    
    void OnEnable()
    {
        if (__prefabIndices == null)
            __prefabIndices = new Dictionary<string, (InstanceManager, int)>(_prefabs.Length);
        
        int numPrefabs = _prefabs.Length;
        for (int i = 0; i < numPrefabs; ++i)
            __prefabIndices.Add(_prefabs[i].name, (this, i));
    }

    void OnDisable()
    {
        if (__prefabIndices != null)
        {
            int numPrefabs = _prefabs.Length;
            for (int i = 0; i < numPrefabs; i++)
                __prefabIndices.Remove(_prefabs[i].name);
        }
        
        if (__results != null)
        {
            foreach (var result in __results)
                result.Cancel();
            
            __results.Clear();
        }

        if (__instances != null)
        {
            int i, numGameObjects, instanceID;
            Transform transform;
            GameObject gameObject;
            List<GameObject> gameObjects;
            foreach (var pair in __instances)
            {
                instanceID = pair.Key;

                __instanceManagers.Remove(instanceID);

                gameObject = pair.Value.prefab;
                if (__gameObjects != null && __gameObjects.TryGetValue(gameObject, out gameObjects))
                {
                    numGameObjects = gameObjects.Count;
                    for (i = 0; i < numGameObjects; ++i)
                    {
                        gameObject = gameObjects[i];
                        if (gameObject == null || gameObject.transform.GetInstanceID() == instanceID)
                        {
                            gameObjects.RemoveAt(i);

                            if (numGameObjects == 1)
                                __gameObjects.Remove(gameObject);

                            break;
                        }
                    }
                }
                
                transform = Resources.InstanceIDToObject(instanceID) as Transform;
                if(transform != null)
                    Destroy(transform.gameObject);
            }
            
            __instances.Clear();
        }
    }
}
