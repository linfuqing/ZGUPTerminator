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
    internal struct Instance
    {
        public int instanceID;
        public float destroyTime;
        public string destroyMessageName;
        public UnityEngine.Object destroyMessageValue;
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
        [Flags]
        public enum InstancesFlag
        {
            New = 0x01
        }

        private struct InstanceToDestroy
        {
            public int instanceID;
            public double time;
            public GameObject gameObject;
        }

        private struct InstancesToCreate
        {
            public InstancesFlag flag;
            public int entityCount;
            public Instance instance;
            public AsyncInstantiateOperation<GameObject> asyncInstantiateOperation;
            public InstanceManager instanceManager;

            public int Submit(
                int maxEntityCount, 
                int startIndex, 
                ref NativeList<Entity> entities, 
                List<GameObject> gameObjects, 
                SystemBase system)
            {
                if ((flag & InstancesFlag.New) == InstancesFlag.New)
                {
                    flag &= ~InstancesFlag.New;
                    
                    return 0;
                }

                if (asyncInstantiateOperation != null && asyncInstantiateOperation.isDone)
                {
                    bool isDone = asyncInstantiateOperation.isDone;
                    if (!isDone && maxEntityCount == int.MaxValue)
                    {
                        isDone = true;
                        
                        UnityEngine.Profiling.Profiler.BeginSample("WaitForCompletion");

                        asyncInstantiateOperation.WaitForCompletion();
                        
                        UnityEngine.Profiling.Profiler.EndSample();
                    }

                    if (isDone)
                    {
                        UnityEngine.Profiling.Profiler.BeginSample("AsyncInstantiateOperation Done");
                        
                        var results = asyncInstantiateOperation.Result;
                        int numResults = results == null ? 0 : results.Length;
                        if (numResults > 0)
                        {
                            //maxEntityCount = Mathf.Max(maxEntityCount, numResults);
                            
                            GameObject result;
                            int entityIndex = startIndex + this.entityCount - 1;
                            for (int i = 0; i < numResults; ++i)
                                gameObjects[entityIndex - i] = results[i];

                            if (instanceManager != null)
                            {
                                if (__instanceManagers == null)
                                    __instanceManagers = new Dictionary<int, InstanceManager>();

                                if (instanceManager.__instances == null)
                                    instanceManager.__instances = new Dictionary<int, Instance>();

                                int transformInstanceID;
                                for (int i = 0; i < numResults; ++i)
                                {
                                    result = results[i];
                                    if (result == null)
                                        continue;

                                    transformInstanceID = result.transform.GetInstanceID();

                                    __instanceManagers.Add(transformInstanceID, instanceManager);

                                    instanceManager.__instances.Add(transformInstanceID, instance);
                                }
                            }
                        }

                        asyncInstantiateOperation = null;
                        
                        UnityEngine.Profiling.Profiler.EndSample();
                    }
                }
                
                UnityEngine.Profiling.Profiler.BeginSample("Check");

                int index, entityCount = Mathf.Min(this.entityCount, maxEntityCount);
                Entity entity;
                var entityManager = system.EntityManager;
                for (int i = 0; i < entityCount; ++i)
                {
                    index = i + startIndex;
                    if (gameObjects[index] == null && asyncInstantiateOperation != null)
                    {
                        entityCount = i;

                        break;
                    }
                    
                    entity = entities[index];
                    if (entityManager.IsEnabled(entity) &&
                        entityManager.HasComponent<global::Instance>(entity) &&
                        //entityManager.IsComponentEnabled<global::Instance>(entity) && 
                        gameObjects[index] != null)
                        continue;

                    __Destroy(gameObjects[index], instance.instanceID);

                    gameObjects.RemoveAt(index);
                    
                    entities.RemoveAt(index);

                    --i;

                    --entityCount;

                    --this.entityCount;
                }

                UnityEngine.Profiling.Profiler.EndSample();
                
                if (entityCount < 1)
                    return 0;

                this.entityCount -= entityCount;

                UnityEngine.Profiling.Profiler.BeginSample("AddComponent");

                system.EntityManager.AddComponent<CopyMatrixToTransformInstanceID>(
                    entities.AsArray().GetSubArray(startIndex, entityCount));

                UnityEngine.Profiling.Profiler.EndSample();

                UnityEngine.Profiling.Profiler.BeginSample("GetComponentLookup");

                var instanceIDs = system.GetComponentLookup<CopyMatrixToTransformInstanceID>();
                var localToWorlds = system.GetComponentLookup<LocalToWorld>(true);
                
                UnityEngine.Profiling.Profiler.EndSample();

                UnityEngine.Profiling.Profiler.BeginSample("Apply");

                CopyMatrixToTransformInstanceID instanceID;
                instanceID.isSendMessageOnDestroy = true;

                GameObject gameObject;
                Transform transform;
                LocalToWorld localToWorld;
                for (int i = 0; i < entityCount; ++i)
                {
                    index = i + startIndex;
                    
                    gameObject = gameObjects[index];

                    transform = gameObject.transform;

                    entity = entities[index];

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
                
                UnityEngine.Profiling.Profiler.EndSample();

                UnityEngine.Profiling.Profiler.BeginSample("RemoveRange");

                gameObjects.RemoveRange(startIndex, entityCount);
                entities.RemoveRange(startIndex, entityCount);

                UnityEngine.Profiling.Profiler.EndSample();

                return entityCount;
            }
        }
        
        private class System
        {
            private NativeList<Entity> __entities;
            private List<GameObject> __results; 
            private List<InstancesToCreate> __instances;

            public System()
            {
                __entities = new NativeList<Entity>(Allocator.Persistent);
                __results = new List<GameObject>();
                __instances = new List<InstancesToCreate>();
            }

            public void Dispose()
            {
                __entities.Dispose();

                foreach (var instance in __instances)
                {
                    if(instance.asyncInstantiateOperation == null)
                        continue;

                    if (instance.asyncInstantiateOperation.isDone)
                    {
                        foreach (var gameObject in instance.asyncInstantiateOperation.Result)
                            DestroyImmediate(gameObject);
                    }
                    else
                        instance.asyncInstantiateOperation.Cancel();
                }
            }

            public void Apply(
                in Instance instance, 
                in NativeArray<Entity> entities, 
                IEnumerable<GameObject> results, 
                AsyncInstantiateOperation<GameObject> asyncInstantiateOperation, 
                InstanceManager instanceManager)
            {
                __entities.AddRange(entities);
                
                int numResults = __results.Count;
                if(results != null)
                    __results.AddRange(results);
                
                InstancesToCreate result;
                result.entityCount = entities.Length;
                for(int i = __results.Count - numResults; i < result.entityCount; ++i)
                    __results.Add(null);
                
                UnityEngine.Assertions.Assert.AreEqual(__results.Count, __entities.Length);
                
                result.flag = InstancesFlag.New;
                result.instance = instance;
                result.asyncInstantiateOperation = asyncInstantiateOperation;
                result.instanceManager = instanceManager;
                
                __instances.Add(result);
            }

            public int Submit(
                int maxEntityCount, 
                SystemBase system)
            {
                UnityEngine.Profiling.Profiler.BeginSample("System Submit");
                
                InstancesToCreate instance;
                int count = 0, startIndex = 0, numInstances = __instances.Count;
                for (int i = 0; i < numInstances; ++i)
                {
                    instance = __instances[i];
                    count += instance.Submit(
                        maxEntityCount == int.MaxValue ? maxEntityCount : maxEntityCount - count, 
                        startIndex, 
                        ref __entities,
                        __results, 
                        system);

                    startIndex += instance.entityCount;

                    if (instance.entityCount < 1)
                    {
                        __instances.RemoveAt(i--);

                        --numInstances;
                    }
                    else
                        __instances[i] = instance;

                    if (count == maxEntityCount)
                        break;
                }
                
                UnityEngine.Profiling.Profiler.EndSample();
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

        public void WaitForCompletion()
        {
            foreach (var system in __systems)
                system.Value.Submit(int.MaxValue, system.Key);
            
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

                    __Destroy(instance.gameObject, instance.instanceID);

                    __instancesToDestroy.RemoveAtSwapBack(i--);

                    --numInstancesToDestroy;
                }
            }
        }

        public void Create(
            in Instance instance,
            in NativeArray<Entity> entities, 
            IEnumerable<GameObject> results, 
            AsyncInstantiateOperation<GameObject> asyncInstantiateOperation, 
            InstanceManager instanceManager, 
            SystemBase system)
        {
            if (!__systems.TryGetValue(system, out var value))
            {
                value = new System();

                __systems[system] = value;
            }
            
            value.Apply(instance, entities, results, asyncInstantiateOperation, instanceManager);
        }
        
        public void Destroy(int instanceID, float time, GameObject gameObject)
        {
            if (time > Mathf.Epsilon)
            {
                InstanceToDestroy instance;
                instance.instanceID = instanceID;
                instance.time = Time.timeAsDouble + time;
                instance.gameObject = gameObject;

                if (__instancesToDestroy == null)
                    __instancesToDestroy = new List<InstanceToDestroy>();

                __instancesToDestroy.Add(instance);
            }
            else
                __Destroy(gameObject, instanceID);
        }

        void OnDestroy()
        {
            foreach (var system in __systems.Values)
                system.Dispose();
        }

        void Update()
        {
            float deltaTime = Time.maximumDeltaTime * 0.5f;
            long tick = DateTime.Now.Ticks;
            foreach (var system in __systems)
            {
                while (system.Value.Submit(4, system.Key) > 0)
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

                    __Destroy(instance.gameObject, instance.instanceID);

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

    private static Dictionary<int, List<GameObject>> __gameObjects;

    private static Dictionary<int, InstanceManager> __instanceManagers;

    private Dictionary<int, Instance> __instances;

    //private HashSet<AsyncInstantiateOperation<GameObject>> __results;

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
        in NativeArray<Entity> entities//, 
        //in ComponentLookup<LocalToWorld> localToWorlds, 
        //ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs
        )
    {
        if (!__prefabIndices.TryGetValue(name, out var prefabIndex))
        {
            Debug.LogError($"The prefab {name} can not been found!");

            return;
        }

        var manager = prefabIndex.Item1;

        //manager.StartCoroutine(
            manager.__Instantiate(
            manager._prefabs[prefabIndex.Item2],
            system,
            entities//,
            //instanceIDs,
            //localToWorlds
            );
            //);
    }

    private void __Instantiate(
        Prefab prefab, 
        SystemBase system, 
        NativeArray<Entity> entities 
        //ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs, 
        //ComponentLookup<LocalToWorld> localToWorlds
        )
    {
        int numGameObjects, numEntities = entities.Length, instanceID = prefab.gameObject.GetInstanceID();
        List<GameObject> results = null;
        if (__gameObjects != null && __gameObjects.TryGetValue(instanceID, out var gameObjects))
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

        Instance instance;
        instance.instanceID = prefab.gameObject.GetInstanceID();
        instance.destroyTime = prefab.destroyTime;
        instance.destroyMessageName = prefab.destroyMessageName;
        instance.destroyMessageValue = prefab.destroyMessageValue;

        AsyncInstantiateOperation<GameObject> asyncInstantiateOperation = null;
        if (numGameObjects > 0)
        {
            if (numGameObjects > 1)
            {
                asyncInstantiateOperation = InstantiateAsync(prefab.gameObject, numGameObjects, this.transform);

                /*if (__results == null)
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

                int transformInstanceID;
                foreach (var temp in result.Result)
                {
                    transformInstanceID = temp.transform.GetInstanceID();
                    
                    __instanceManagers.Add(transformInstanceID, this);

                    __instances.Add(transformInstanceID, instance);

                    results.Add(temp);
                }*/
            }
            else
            {
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
                //yield return new WaitForEndOfFrame();
            }
        }
        //else
            //Wait For LocalToWorld
            //yield return new WaitForEndOfFrame();

        Factory.instance.Create(
            instance, 
            entities, 
            results, 
            asyncInstantiateOperation, 
            this, 
            system);

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
            Factory.instance.Destroy(instance.instanceID, instance.destroyTime, gameObject);
        }
        else
            __Destroy(gameObject, instance.instanceID);
    }

    private static void __Destroy(GameObject gameObject, int instanceID)
    {
        if (gameObject == null)
            return;
        
        if (__gameObjects == null)
            __gameObjects = new Dictionary<int, List<GameObject>>();

        if (!__gameObjects.TryGetValue(instanceID, out var gameObjects))
        {
            gameObjects = new List<GameObject>();

            __gameObjects[instanceID] = gameObjects;
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
        
        Factory.instance.WaitForCompletion();
        
        /*if (__results != null)
        {
            foreach (var result in __results)
                result.Cancel();
            
            __results.Clear();
        }*/

        if (__instances != null)
        {
            int i, numGameObjects, gameObjectInstanceID, transformInstanceID;
            Transform transform;
            GameObject gameObject;
            List<GameObject> gameObjects;
            foreach (var pair in __instances)
            {
                transformInstanceID = pair.Key;

                __instanceManagers.Remove(transformInstanceID);

                gameObjectInstanceID = pair.Value.instanceID;
                if (__gameObjects != null && __gameObjects.TryGetValue(gameObjectInstanceID, out gameObjects))
                {
                    numGameObjects = gameObjects.Count;
                    for (i = 0; i < numGameObjects; ++i)
                    {
                        gameObject = gameObjects[i];
                        if (gameObject == null || gameObject.transform.GetInstanceID() == transformInstanceID)
                        {
                            gameObjects.RemoveAt(i);

                            if (numGameObjects == 1)
                                __gameObjects.Remove(gameObjectInstanceID);

                            break;
                        }
                    }
                }
                
                transform = Resources.InstanceIDToObject(transformInstanceID) as Transform;
                if(transform != null)
                    Destroy(transform.gameObject);
            }
            
            __instances.Clear();
        }
    }
}
