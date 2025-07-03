using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.CharacterController;
using Unity.Physics;
using Unity.Physics.Authoring;

[Serializable]
public struct ThirdPersonCharacterComponent : IComponentData
{
    public float RotationSharpness;
    public float GroundMaxSpeed;
    public float GroundedMovementSharpness;
    public float SprintSpeedMultiplier; 
    public float AirAcceleration;
    public float AirMaxSpeed;
    public float AirDrag;
    public float JumpSpeed;
    public int MaxAirJumps;
    public float3 Gravity;
    public bool PreventAirAccelerationAgainstUngroundedHits;
    public BasicStepAndSlopeHandlingParameters StepAndSlopeHandling;
    public CustomPhysicsBodyTags IgnoredPhysicsTags;
    
    [UnityEngine.HideInInspector] // we don't want this field to appear in the inspector
    public int CurrentAirJumps;
    
    public static ThirdPersonCharacterComponent GetDefault()
    {
        return new ThirdPersonCharacterComponent
        {
            RotationSharpness = 25f,
            GroundMaxSpeed = 10f,
            GroundedMovementSharpness = 15f,
            AirAcceleration = 50f,
            AirMaxSpeed = 10f,
            AirDrag = 0f,
            JumpSpeed = 10f,
            Gravity = math.up() * -30f,
            PreventAirAccelerationAgainstUngroundedHits = true,
            StepAndSlopeHandling = BasicStepAndSlopeHandlingParameters.GetDefault(),
        };
    }
}

public struct ThirdPersionCharacterGravityFactor : IComponentData
{
    public float value;
}

public struct ThirdPersonCharacterLookAt : IComponentData
{
    public quaternion direction;
}

public struct ThirdPersonCharacterControl : IComponentData
{
    public float3 MoveVector;
    public bool Jump;
    public bool Sprint;
}

public struct ThirdPersonCharacterStandTime : IBufferElementData
{
    public double time;
    
    public float duration;

    public static bool IsStand(double time, DynamicBuffer<ThirdPersonCharacterStandTime> standTimes)
    {
        int numStandTimes = standTimes.Length;
        for (int i = 0; i < numStandTimes; ++i)
        {
            ref var standTime = ref standTimes.ElementAt(i);
            if(standTime.time > time)
                continue;

            if (standTime.time + standTime.duration < time)
            {
                standTimes.RemoveAtSwapBack(i--);
                
                --numStandTimes;
            }
            else
                return true;
        }

        return false;
    }
}
