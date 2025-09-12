using System;
using Unity.Mathematics;
using Unity.Animation;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Transforms;
using AnimationCurve = Unity.Animation.AnimationCurve;
using Object = UnityEngine.Object;

public enum AnimationCurveRotationType
{
    Quaternion, 
    EulerAngles,
    EulerAnglesRaw
}

public struct AnimationCurveBoolDefinition
{
    public struct KeyFrame : IComparable<KeyFrame>
    {
        public bool value;

        public int index;
        
        public float time;

        public int CompareTo(KeyFrame other)
        {
            int result = time.CompareTo(other.time);
            if (result == 0)
            {
                result = index.CompareTo(other.index);
                if (result == 0)
                    result = value.CompareTo(other.value);
            }

            return result;
        }
    }

    public BlobArray<KeyFrame> keyFrames;

    public int StartKeyFrameIndexOf(float time)
    {
        int numKeyFrames = keyFrames.Length, count = numKeyFrames, index = -1, middle;
        while (count > 0)
        {
            middle = (count + 1) >> 1;
            if (keyFrames[index + middle].time < time)
            {
                index += middle;

                count -= middle;
            }
            else
            {
                if (middle < 2)
                    break;

                count = middle;
            }
        }

        return index == -1 ? 0 : index;
    }
}

public struct AnimationCurve3
{
    public AnimationCurve x;
    public AnimationCurve y;
    public AnimationCurve z;

    public bool3 Evaluate(float time, ref float3 value)
    {
        bool3 result = math.bool3(x.IsCreated, y.IsCreated, z.IsCreated);
        if (result.x)
            value.x = AnimationCurveEvaluator.Evaluate(time, ref x);
        
        if (result.y)
            value.y = AnimationCurveEvaluator.Evaluate(time, ref y);
        
        if (result.z)
            value.z = AnimationCurveEvaluator.Evaluate(time, ref z);

        return result;
    }
}

public struct AnimationCurve4
{
    public AnimationCurve x;
    public AnimationCurve y;
    public AnimationCurve z;
    public AnimationCurve w;

    public bool3 Evaluate(float time, ref float3 value)
    {
        bool3 result = math.bool3(x.IsCreated, y.IsCreated, z.IsCreated);
        
        if (result.x)
            value.x = AnimationCurveEvaluator.Evaluate(time, ref x);
        
        if (result.y)
            value.y = AnimationCurveEvaluator.Evaluate(time, ref y);
        
        if (result.z)
            value.z = AnimationCurveEvaluator.Evaluate(time, ref z);

        return result;
    }
    
    public bool4 Evaluate(float time, ref float4 value)
    {
        float3 xyz = value.xyz;
        bool4 result = math.bool4(Evaluate(time, ref xyz), w.IsCreated);
        value.xyz = xyz;
        
        if (result.w)
            value.w = AnimationCurveEvaluator.Evaluate(time, ref w);

        return result;
    }
}

public struct AnimationCurveRotation
{
    public AnimationCurveRotationType type;
    public AnimationCurve4 value;

    public void Evaluate(float time, ref quaternion rotation)
    {
        switch (type)
        {
            case AnimationCurveRotationType.Quaternion:
                if (!math.all(value.Evaluate(time, ref rotation.value)))
                    rotation = math.normalizesafe(rotation);
                break;
            case AnimationCurveRotationType.EulerAngles:
            case AnimationCurveRotationType.EulerAnglesRaw:
                float3 eulerAngles = math.degrees(math.Euler(rotation));
                value.Evaluate(time, ref eulerAngles);
                rotation = quaternion.Euler(math.radians(eulerAngles));
                break;
        }
    }
}

public struct AnimationCurveTransform : IComponentData
{
    public AnimationCurve scale;
    public AnimationCurve3 position;
    public AnimationCurveRotation rotation;

    public void Evaluate(float time, ref LocalTransform localTransform)
    {
        if(scale.IsCreated)
            localTransform.Scale = AnimationCurveEvaluator.Evaluate(time, ref scale);
        
        position.Evaluate(time, ref localTransform.Position);
        rotation.Evaluate(time, ref localTransform.Rotation);
    }

    public bool Evaluate(
        in Entity entity,
        in ComponentLookup<Parent> parents,
        in ComponentLookup<AnimationCurveTime> times,
        ref LocalTransform localTransform)
    {
        Parent parent;
        Entity parentEntity = entity;
        AnimationCurveTime time;
        while (!times.TryGetComponent(parentEntity, out time))
        {
            if (parents.TryGetComponent(parentEntity, out parent))
                parentEntity = parent.Value;
            else
                return false;
        }
        
        Evaluate(time.value, ref localTransform);

        return true;
    }
}

public struct AnimationCurveTime : IComponentData
{
    public float value;
}

public struct AnimationCurveDelta : IComponentData
{
    public double elapsed;
    public double start;

    public float Update(double time)
    {
        if (elapsed > 0.0)
            return (float)(time - elapsed);

        start = time;

        return 0.0f;
    }
}

public struct AnimationCurveSpeed : IComponentData
{
    public float value;
}

public struct AnimationCurveRoot : IComponentData
{
    public float length;
}

public struct AnimationCurveActive : IComponentData
{
    public BlobAssetReference<AnimationCurveBoolDefinition> definition;

    public void Init(
        in DynamicBuffer<AnimationCurveEntity> entities, 
        in BufferLookup<AnimationCurveChild> children, 
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        using (var entityIndices = new UnsafeHashSet<int>(1, Allocator.Temp))
        {
            ref var definition = ref this.definition.Value;
            int numKeyFrames = definition.keyFrames.Length;
            for (int i = 0; i < numKeyFrames; ++i)
            {
                ref var keyFrame = ref definition.keyFrames[i];
                if (!entityIndices.Add(keyFrame.index))
                    continue;

                __SetActive(keyFrame.value, entities[keyFrame.index].value, children, ref entityManager);
            }
        }
    }

    public void Evaluate(
        float length, 
        float time, 
        float deltaTime, 
        in DynamicBuffer<AnimationCurveEntity> entities, 
        in BufferLookup<AnimationCurveChild> children, 
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        ref var definition = ref this.definition.Value;
        int startFrameIndex = definition.StartKeyFrameIndexOf(time), numKeyFrames = definition.keyFrames.Length;
        if (startFrameIndex < numKeyFrames)
        {
            int i;
            for (i = startFrameIndex; i < numKeyFrames; ++i)
            {
                ref var keyFrame = ref definition.keyFrames[i];
                if (keyFrame.time - time > deltaTime)
                    break;
                
                __SetActive(keyFrame.value, entities[keyFrame.index].value, children, ref entityManager);
            }

            if (length > math.FLT_MIN_NORMAL)
            {
                time += deltaTime;
                if (time > length)
                {
                    time -= length;
                    
                    for (i = 0; i < numKeyFrames; ++i)
                    {
                        ref var keyFrame = ref definition.keyFrames[i];
                        if (keyFrame.time - time > 0.0f)
                            break;
                
                        __SetActive(keyFrame.value, entities[keyFrame.index].value, children, ref entityManager);
                    }
                }
            }
        }
    }

    private void __SetActive(
        bool value, 
        in Entity entity, 
        in BufferLookup<AnimationCurveChild> children, 
        ref EntityCommandBuffer.ParallelWriter entityManager)
    {
        if (children.TryGetBuffer(entity, out var childrenBuffer))
        {
            foreach (var child in childrenBuffer)
                __SetActive(value, child.entity, children, ref entityManager);
        }
        else
            entityManager.SetEnabled(0, entity, value);
    }
}

public struct AnimationCurveMessage : IBufferElementData
{
    public float time;
    public FixedString128Bytes messageName;
    public UnityObjectRef<Object> messageValue;
}

public struct AnimationCurveEntity : IBufferElementData
{
    public Entity value;
}

public struct AnimationCurveChild : IBufferElementData
{
    public Entity entity;
}


