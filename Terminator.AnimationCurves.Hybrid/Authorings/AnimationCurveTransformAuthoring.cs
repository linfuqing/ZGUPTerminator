using System;
using Unity.Mathematics;
using Unity.Animation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Transforms;
using UnityEngine;
using AnimationCurve = Unity.Animation.AnimationCurve;
using Hash128 = Unity.Entities.Hash128;
using LegacyCurve = UnityEngine.AnimationCurve;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Unity.Animation.Hybrid;
using UnityEditor;
using static AnimationCurveBakeUtility;

[Serializable]
public struct AnimationCurveBool
{
    public struct KeyFrame
    {
        public bool value;
        public float time;
    }

    public KeyFrame[] keyFrames;
}

[Serializable]
public struct AnimationCurveData3
{
    public LegacyCurve x;
    public LegacyCurve y;
    public LegacyCurve z;

    public float length => Mathf.Max(x.length, y.length, z.length);

    public AnimationCurve3 ToAsset(IBaker baker, out Hash128 hash)
    {
        AnimationCurve3 result;
        result.x = this.x == null || this.x.length < 1 ? default : this.x.ToDotsAnimationCurve();
        result.y = this.y == null || this.z.length < 1 ? default : this.y.ToDotsAnimationCurve();
        result.z = this.z == null || this.z.length < 1 ? default : this.z.ToDotsAnimationCurve();

        hash = default;
        if (result.x.IsCreated)
            AddBlobAsset(baker, ref result.x, ref hash);

        if (result.y.IsCreated)
            AddBlobAsset(baker, ref result.y, ref hash);

        if (result.z.IsCreated)
            AddBlobAsset(baker, ref result.z, ref hash);

        return result;
    }
}

[Serializable]
public struct AnimationCurveData4
{
    public LegacyCurve x;
    public LegacyCurve y;
    public LegacyCurve z;
    public LegacyCurve w;

    public float length => Mathf.Max(x.length, y.length, z.length);

    public AnimationCurve4 ToAsset(IBaker baker, out Hash128 hash)
    {
        AnimationCurve4 result;
        result.x = this.x == null || this.x.length < 1 ? default : this.x.ToDotsAnimationCurve();
        result.y = this.y == null || this.y.length < 1 ? default : this.y.ToDotsAnimationCurve();
        result.z = this.z == null || this.z.length < 1 ? default : this.z.ToDotsAnimationCurve();
        result.w = this.w == null || this.w.length < 1 ? default : this.w.ToDotsAnimationCurve();

        hash = default;
        if (result.x.IsCreated)
            AddBlobAsset(baker, ref result.x, ref hash);

        if (result.y.IsCreated)
            AddBlobAsset(baker, ref result.y, ref hash);

        if (result.z.IsCreated)
            AddBlobAsset(baker, ref result.z, ref hash);

        if (result.w.IsCreated)
            AddBlobAsset(baker, ref result.w, ref hash);

        return result;
    }
}

[Serializable]
public struct AnimationCurveRotationData
{
    public AnimationCurveRotationType type;
    public AnimationCurveData4 value;
}

[Serializable]
public struct AnimationCurveTransformData
{
    public AnimationCurveBool active;
    public LegacyCurve scale;
    public AnimationCurveData3 position;
    public AnimationCurveRotationData rotation;
    
    public AnimationCurveTransform ToAsset(
        IBaker baker, 
        List<AnimationCurveBoolDefinition.KeyFrame> keyFrames, 
        int entityIndex, 
        out Hash128 hash)
    {
        AnimationCurveBoolDefinition.KeyFrame keyFrame;
        keyFrame.index = entityIndex;

        if (active.keyFrames != null)
        {
            foreach (var activeKeyFrame in active.keyFrames)
            {
                keyFrame.value = activeKeyFrame.value;
                keyFrame.time = activeKeyFrame.time;

                keyFrames.Add(keyFrame);
            }
        }

        AnimationCurveTransform result;
        result.scale = this.scale == null ? default : this.scale.ToDotsAnimationCurve();

        Hash128 scale = default;
        if (result.scale.IsCreated)
            AddBlobAsset(baker, ref result.scale, ref scale);

        result.position = this.position.ToAsset(baker, out var position);
        result.rotation.type = this.rotation.type;
        result.rotation.value = this.rotation.value.ToAsset(baker, out var rotation);

        hash.Value = scale.Value ^ position.Value ^ rotation.Value;

        return result;
    }
}

public static class AnimationCurveBakeUtility
{
    public static void AddBlobAsset(IBaker baker, ref AnimationCurve curve, ref Hash128 hash)
    {
        var asset = curve.GetAnimationCurveBlobAssetRef();
        baker.AddBlobAsset(ref asset, out var temp);
        curve.SetAnimationCurveBlobAssetRef(asset);

        if (hash.IsValid)
            hash.Value ^= temp.Value;
        else
            hash.Value = temp.Value;
    }

    public static Dictionary<string, AnimationCurveTransformData> ToAnimationCurveTransforms(this AnimationClip clip)
    {
        AnimationCurveTransformData curveTransform;
        var transforms = new Dictionary<string, AnimationCurveTransformData>();
        var curveBindings = AnimationUtility.GetCurveBindings(clip);
        foreach (var curveBinding in curveBindings)
        {
            if (curveBinding.type == typeof(GameObject))
            {
                switch (curveBinding.propertyName)
                {
                    case "m_IsActive":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        AnimationCurveBool.KeyFrame keyFrame;
                        var active = AnimationUtility.GetEditorCurve(clip, curveBinding);
                        foreach (var key in active.keys)
                        {
                            keyFrame.value = !Mathf.Approximately(key.value, 0.0f);
                            keyFrame.time = key.time;

                            if (curveTransform.active.keyFrames == null)
                            {
                                curveTransform.active.keyFrames = new AnimationCurveBool.KeyFrame[1];
                                curveTransform.active.keyFrames[0] = keyFrame;
                            }
                            else
                                ArrayUtility.Add(ref curveTransform.active.keyFrames, keyFrame);
                        }

                        transforms[curveBinding.path] = curveTransform;
                        break;
                }
            }
            else if (curveBinding.type == typeof(Transform) || curveBinding.type.IsSubclassOf(typeof(Transform)))
            {
                switch (curveBinding.propertyName)
                {
                    case "m_LocalScale.x":
                        //case "m_LocalScale.y":
                        //case "m_LocalScale.z":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        curveTransform.scale = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;

                    case "m_LocalPosition.x":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        curveTransform.position.x = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    case "m_LocalPosition.y":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        curveTransform.position.y = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    case "m_LocalPosition.z":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        curveTransform.position.z = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    case "m_LocalRotation.x":
                    case "localEulerAngles.x":
                    case "localEulerAnglesRaw.x":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        switch (curveBinding.propertyName)
                        {
                            case "m_LocalRotation.x":
                                curveTransform.rotation.type = AnimationCurveRotationType.Quaternion;
                                break;
                            case "localEulerAngles.x":
                                curveTransform.rotation.type = AnimationCurveRotationType.EulerAngles;
                                break;
                            case "localEulerAnglesRaw.x":
                                curveTransform.rotation.type = AnimationCurveRotationType.EulerAnglesRaw;
                                break;
                        }

                        curveTransform.rotation.value.x = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    case "m_LocalRotation.y":
                    case "localEulerAngles.y":
                    case "localEulerAnglesRaw.y":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        switch (curveBinding.propertyName)
                        {
                            case "m_LocalRotation.y":
                                curveTransform.rotation.type = AnimationCurveRotationType.Quaternion;
                                break;
                            case "localEulerAngles.y":
                                curveTransform.rotation.type = AnimationCurveRotationType.EulerAngles;
                                break;
                            case "localEulerAnglesRaw.y":
                                curveTransform.rotation.type = AnimationCurveRotationType.EulerAnglesRaw;
                                break;
                        }

                        curveTransform.rotation.value.y = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    case "m_LocalRotation.z":
                    case "localEulerAngles.z":
                    case "localEulerAnglesRaw.z":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        switch (curveBinding.propertyName)
                        {
                            case "m_LocalRotation.z":
                                curveTransform.rotation.type = AnimationCurveRotationType.Quaternion;
                                break;
                            case "localEulerAngles.z":
                                curveTransform.rotation.type = AnimationCurveRotationType.EulerAngles;
                                break;
                            case "localEulerAnglesRaw.z":
                                curveTransform.rotation.type = AnimationCurveRotationType.EulerAnglesRaw;
                                break;
                        }

                        curveTransform.rotation.value.z = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    case "m_LocalRotation.w":
                        transforms.TryGetValue(curveBinding.path, out curveTransform);

                        curveTransform.rotation.type = AnimationCurveRotationType.Quaternion;

                        curveTransform.rotation.value.w = AnimationUtility.GetEditorCurve(clip, curveBinding);

                        transforms[curveBinding.path] = curveTransform;
                        break;
                    default:
                        Debug.LogError($"Animation curve {curveBinding.path}.{curveBinding.propertyName} of {curveBinding.type} can not been binding!");
                        break;
                }
            }
        }

        return transforms;
    }
    
    public static AnimationCurveMessage[] ToAnimationCurveMessages(this AnimationClip clip)
    {
        var animationEvents = AnimationUtility.GetAnimationEvents(clip);

        int numAnimationEvents = animationEvents == null ? 0 : animationEvents.Length;
        var messages = numAnimationEvents > 0 ? new AnimationCurveMessage[numAnimationEvents] : null;
        for (int i = 0; i < numAnimationEvents; ++i)
        {
            ref var source = ref animationEvents[i];
            ref var destination = ref messages[i];
            destination.time = source.time;
            destination.messageName = source.functionName;
            destination.messageValue = new WeakObjectReference<Object>(source.objectReferenceParameter);
        }

        return messages;
    }
}

public class AnimationCurveTransformAuthoring : MonoBehaviour
{
    [SerializeField] 
    internal float _speed = 1.0f;
    
    [SerializeField] 
    internal AnimationClip _clip;
    
    class Baker : Baker<AnimationCurveTransformAuthoring>
    {
        /*static readonly string k_LocalPosition       = "m_LocalPosition.x";
        static readonly string k_LocalRotation       = "m_LocalRotation.x";
        static readonly string k_LocalEulerAngles    = "localEulerAngles.x";
        static readonly string k_LocalEulerAnglesRaw = "localEulerAnglesRaw.x";
        static readonly string k_LocalScale          = "m_LocalScale.x";*/

        public override void Bake(AnimationCurveTransformAuthoring authoring)
        {
            if(authoring._clip == null)
                Debug.LogError($"Clip of authoring {authoring} is null", authoring);
            
            var transforms = authoring._clip.ToAnimationCurveTransforms();
            
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            var instances = AddBuffer<AnimationCurveTransformBakingData>(entity);
            var entities = AddBuffer<AnimationCurveEntity>(entity);
            Transform parentTransform, rootTransform = authoring.transform;
            string path;
            var stringBuilder = new StringBuilder();
            var keyFrames = new List<AnimationCurveBoolDefinition.KeyFrame>();
            if (transforms.TryGetValue(string.Empty, out var transform))
                __Bake(keyFrames, authoring.gameObject, entity, transform, ref entities, ref instances);
            
            foreach (var childGameObject in GetChildren(true))
            {
                path = __GetPath(stringBuilder, authoring.transform, childGameObject.transform);
                
                if (string.IsNullOrEmpty(path) || !transforms.TryGetValue(path, out transform))
                    continue;
                
                __Bake(
                    keyFrames, 
                    childGameObject, 
                    GetEntity(childGameObject, TransformUsageFlags.Dynamic), 
                    transform, 
                    ref entities, 
                    ref instances);
                
                parentTransform = childGameObject.transform.parent;
                while (parentTransform != rootTransform)
                {
                    GetEntity(parentTransform.gameObject, TransformUsageFlags.Dynamic);

                    parentTransform = parentTransform.parent;
                }
                
                //AddComponent(child, transform.ToAsset(this, out _));
            }

            int numKeyFrames = keyFrames.Count;
            if (numKeyFrames > 0)
            {
                keyFrames.Sort();
                AnimationCurveActive active;
                using (var builder = new BlobBuilder(Allocator.Temp))
                {
                    ref var definition = ref builder.ConstructRoot<AnimationCurveBoolDefinition>();

                    var definitionKeyFrames = builder.Allocate(ref definition.keyFrames, numKeyFrames);
                    for (int i = 0; i < numKeyFrames; ++i)
                        definitionKeyFrames[i] = keyFrames[i];

                    active.definition =
                        builder.CreateBlobAssetReference<AnimationCurveBoolDefinition>(Allocator.Persistent);
                }
                
                AddComponent(entity, active);
            }

            AnimationCurveRoot root;
            root.length = authoring._clip.isLooping ? authoring._clip.length : 0.0f;
            AddComponent(entity, root);

            AnimationCurveSpeed speed;
            speed.value = authoring._speed;
            AddComponent(entity, speed);
            
            AddComponent<AnimationCurveTime>(entity);

            var messages = AnimationCurveBakeUtility.ToAnimationCurveMessages(authoring._clip);
            if (messages != null)
                AddBuffer<AnimationCurveMessage>(entity).CopyFrom(messages);
        }

        private void __Bake(
            List<AnimationCurveBoolDefinition.KeyFrame> keyFrames, 
            GameObject gameObject, 
            in Entity entity, 
            in AnimationCurveTransformData transform, 
            ref DynamicBuffer<AnimationCurveEntity> entities,
            ref DynamicBuffer<AnimationCurveTransformBakingData> instances)
        {
            AnimationCurveTransformBakingData instance;
            instance.entity = entity;
            instance.transform = transform.ToAsset(this, keyFrames, entities.Length, out _);

            LocalTransform localTransform = LocalTransform.Identity;
            instance.transform.Evaluate(0, ref localTransform);
            
            AnimationCurveEntity temp;
            temp.value = instance.entity;
            entities.Add(temp);
                
            instances.Add(instance);

            foreach (var leafGameObject in GetChildren(gameObject, true))
            {
                if(PrefabAssetType.NotAPrefab != PrefabUtility.GetPrefabAssetType(leafGameObject))
                    RegisterPrefabForBaking(leafGameObject);
                    
                GetEntity(leafGameObject, TransformUsageFlags.Dynamic);
            }
        }
    }

    private static string __GetPath(StringBuilder stringBuilder, Transform root, Transform child)
    {
        stringBuilder.Clear();
        while (child != null && child != root)
        {
            if (stringBuilder.Length > 0)
                stringBuilder.Insert(0, '/');
            
            stringBuilder.Insert(0, child.name);

            child = child.parent;
        }

        return stringBuilder.ToString();
    }
}

[BakingType]
public struct AnimationCurveTransformBakingData : IBufferElementData
{
    public Entity entity;

    public AnimationCurveTransform transform;
}

[BurstCompile, WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
public partial struct AnimationCurveTransformBakingSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        using (var ecb = new EntityCommandBuffer(Allocator.Temp))
        {
            foreach (var instances in 
                     SystemAPI.Query<DynamicBuffer<AnimationCurveTransformBakingData>>()
                         .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab))
            {
                foreach (var instance in instances)
                    ecb.AddComponent(instance.entity, instance.transform);
            }
            
            ecb.Playback(state.EntityManager);
        }
    }
}
#endif