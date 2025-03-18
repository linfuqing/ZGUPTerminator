using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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

    internal sealed class Disposable : MonoBehaviour
    {
        private struct Instance
        {
            public double time;
            public GameObject gameObject;
            public GameObject prefab;
        }

        private List<Instance> __instances;

        private static Disposable __instance;

        public static void Destroy(float time, GameObject gameObject, GameObject prefab)
        {
            if (time > Mathf.Epsilon)
            {
                if (__instance == null)
                {
                    var temp = new GameObject();
                    temp.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(temp);

                    __instance = temp.AddComponent<Disposable>();
                }

                Instance instance;
                instance.time = Time.timeAsDouble + time;
                instance.gameObject = gameObject;
                instance.prefab = prefab;

                if (__instance.__instances == null)
                    __instance.__instances = new List<Instance>();

                __instance.__instances.Add(instance);
            }
            else
                __Destroy(gameObject, prefab);
        }

        void Update()
        {
            int numInstances = __instances == null ? 0 : __instances.Count;
            if (numInstances < 1)
                return;
            
            double time = Time.timeAsDouble;
            Instance instance;
            for(int i = 0; i < numInstances; ++i)
            {
                instance = __instances[i];
                if(instance.time > time)
                    continue;
                
                __Destroy(instance.gameObject, instance.prefab);
                
                __instances.RemoveAtSwapBack(i--);

                --numInstances;
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
            new NativeArray<Entity>(entities, Allocator.Persistent),
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

        if (numGameObjects > 0)
        {
            if (numGameObjects > 1)
            {
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
                yield return null;
            }
        }
        else
            //Wait For LocalToWorld
            yield return null;

        Entity entity;
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
            
            transform = gameObject.transform;

            entity = entities[i];
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
            _onAcitveCount.Invoke(activeCount.ToString());
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
            Disposable.Destroy(instance.destroyTime, gameObject, instance.prefab);
        }
        else
            __Destroy(gameObject, instance.prefab);
    }

    private static void __Destroy(GameObject gameObject, GameObject prefab)
    {
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
