using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;
using Unity.CharacterController;
using UnityEngine;

public struct ThirdPersonCharacterSimulationEventResult
{
    public Entity entity;

    public SimulationEvent value;
}

[Serializable]
public struct CharacterFrictionSurface : IComponentData
{
    public float VelocityFactor;
} 

public struct ThirdPersonCharacterUpdateContext
{
    // Here, you may add additional global data for your character updates, such as ComponentLookups, Singletons, NativeCollections, etc...
    // The data you add here will be accessible in your character updates and all of your character "callbacks".
    [ReadOnly]
    public ComponentLookup<CharacterFrictionSurface> characterFrictionSurfaceLookup;

    [ReadOnly]
    public BufferLookup<SimulationEvent> simulationEvents;

    public BufferTypeHandle<SimulationEvent> simulationEventType;
    
    // This is called by systems that schedule jobs that update the character aspect, in their OnCreate().
    // Here, you can get the component lookups.
    public void OnSystemCreate(ref SystemState state)
    {
        characterFrictionSurfaceLookup = state.GetComponentLookup<CharacterFrictionSurface>(true);
        simulationEvents = state.GetBufferLookup<SimulationEvent>();
        simulationEventType = state.GetBufferTypeHandle<SimulationEvent>();
    }
    
    // This is called by systems that schedule jobs that update the character aspect, in their OnUpdate()
    // Here, you can update the component lookups.
    public void OnSystemUpdate(ref SystemState state)
    {
        characterFrictionSurfaceLookup.Update(ref state);
        simulationEvents.Update(ref state);
        simulationEventType.Update(ref state);
    }
}

public readonly partial struct ThirdPersonCharacterAspect : IAspect, IKinematicCharacterProcessor<ThirdPersonCharacterUpdateContext>
{
    public readonly KinematicCharacterAspect CharacterAspect;
    public readonly RefRW<ThirdPersonCharacterComponent> CharacterComponent;
    public readonly RefRW<ThirdPersonCharacterControl> CharacterControl;
    [Optional]
    public readonly RefRO<ThirdPersonCharacterLookAt> CharacterLookAt;
    [Optional]
    public readonly RefRO<ThirdPersionCharacterGravityFactor> GravityFactor;

    public void PhysicsUpdate(
        in Entity entity, 
        ref ThirdPersonCharacterUpdateContext context, 
        ref KinematicCharacterUpdateContext baseContext, 
        ref DynamicBuffer<SimulationEvent> simulationEvents, 
        ref NativeQueue<ThirdPersonCharacterSimulationEventResult>.ParallelWriter simulationEventResults)
    {
        ref ThirdPersonCharacterComponent characterComponent = ref CharacterComponent.ValueRW;
        ref KinematicCharacterBody characterBody = ref CharacterAspect.CharacterBody.ValueRW;
        ref float3 characterPosition = ref CharacterAspect.LocalTransform.ValueRW.Position;

        // First phase of default character update
        CharacterAspect.Update_Initialize(in this, ref context, ref baseContext, ref characterBody, baseContext.Time.DeltaTime);
        CharacterAspect.Update_ParentMovement(in this, ref context, ref baseContext, ref characterBody, ref characterPosition, characterBody.WasGroundedBeforeCharacterUpdate);
        CharacterAspect.Update_Grounding(in this, ref context, ref baseContext, ref characterBody, ref characterPosition);
        
        // Update desired character velocity after grounding was detected, but before doing additional processing that depends on velocity
        HandleVelocityControl(ref context, ref baseContext);

        // Second phase of default character update
        CharacterAspect.Update_PreventGroundingFromFutureSlopeChange(in this, ref context, ref baseContext, ref characterBody, in characterComponent.StepAndSlopeHandling);
        float gravityFactor = GravityFactor.IsValid ? GravityFactor.ValueRO.value : 1.0f;
        CharacterAspect.Update_GroundPushing(in this, ref context, ref baseContext, characterComponent.Gravity * gravityFactor);
        CharacterAspect.Update_MovementAndDecollisions(in this, ref context, ref baseContext, ref characterBody, ref characterPosition);
        CharacterAspect.Update_MovingPlatformDetection(ref baseContext, ref characterBody); 
        CharacterAspect.Update_ParentMomentum(ref baseContext, ref characterBody);
        CharacterAspect.Update_ProcessStatefulCharacterHits();
        
        ThirdPersonCharacterSimulationEventResult simulationEventResult;
        if (simulationEvents.IsCreated)
        {
            foreach (var characterHit in CharacterAspect.CharacterHitsBuffer)
            {
                simulationEventResult.value.entity = characterHit.Entity;
                simulationEventResult.value.colliderKey = characterHit.ColliderKey;
                SimulationEvent.Append(simulationEvents, simulationEventResult.value);
            }
        }
        
        simulationEventResult.value.entity = entity;
        foreach (var characterHit in CharacterAspect.CharacterHitsBuffer)
        {
            if(!context.simulationEvents.HasBuffer(characterHit.Entity))
                continue;
            
            simulationEventResult.value.colliderKey = characterHit.ColliderKey;
            simulationEventResult.entity = characterHit.Entity;

            simulationEventResults.Enqueue(simulationEventResult);
        }
    }

    private void HandleVelocityControl(ref ThirdPersonCharacterUpdateContext context, ref KinematicCharacterUpdateContext baseContext)
    {
        float deltaTime = baseContext.Time.DeltaTime;
        ref KinematicCharacterBody characterBody = ref CharacterAspect.CharacterBody.ValueRW;
        ref ThirdPersonCharacterComponent characterComponent = ref CharacterComponent.ValueRW;
        ref ThirdPersonCharacterControl characterControl = ref CharacterControl.ValueRW;

        // Rotate move input and velocity to take into account parent rotation
        if(characterBody.ParentEntity != Entity.Null)
        {
            characterControl.MoveVector = math.rotate(characterBody.RotationFromParent, characterControl.MoveVector);
            characterBody.RelativeVelocity = math.rotate(characterBody.RotationFromParent, characterBody.RelativeVelocity);
        }

        if (characterBody.IsGrounded)
        {
            // Move on ground
            float3 targetVelocity = characterControl.MoveVector * characterComponent.GroundMaxSpeed;
            
            // Sprint
            if (characterControl.Sprint)
            {
                targetVelocity *= characterComponent.SprintSpeedMultiplier;
            }
            
            // Friction surfaces
            if (context.characterFrictionSurfaceLookup.TryGetComponent(characterBody.GroundHit.Entity, out CharacterFrictionSurface frictionSurface))
            {
                targetVelocity *= frictionSurface.VelocityFactor;
            }
            
            CharacterControlUtilities.StandardGroundMove_Interpolated(ref characterBody.RelativeVelocity, targetVelocity, characterComponent.GroundedMovementSharpness, deltaTime, characterBody.GroundingUp, characterBody.GroundHit.Normal);
            
            // Jump
            if (characterControl.Jump)
            {
                CharacterControlUtilities.StandardJump(ref characterBody, characterBody.GroundingUp * characterComponent.JumpSpeed, true, characterBody.GroundingUp);
            }

            // Reset air jumps when grounded
            characterComponent.CurrentAirJumps = 0;
        }
        else
        {
            // Move in air
            float3 airAcceleration = characterControl.MoveVector * characterComponent.AirAcceleration;
            if (math.lengthsq(airAcceleration) > 0f)
            {
                float3 tmpVelocity = characterBody.RelativeVelocity;
                CharacterControlUtilities.StandardAirMove(ref characterBody.RelativeVelocity, airAcceleration, characterComponent.AirMaxSpeed, characterBody.GroundingUp, deltaTime, false);

                // Cancel air acceleration from input if we would hit a non-grounded surface (prevents air-climbing slopes at high air accelerations)
                if (characterComponent.PreventAirAccelerationAgainstUngroundedHits && CharacterAspect.MovementWouldHitNonGroundedObstruction(in this, ref context, ref baseContext, characterBody.RelativeVelocity * deltaTime, out ColliderCastHit hit))
                {
                    characterBody.RelativeVelocity = tmpVelocity;
                }
            }

            // Air Jumps
            if (characterControl.Jump && characterComponent.CurrentAirJumps < characterComponent.MaxAirJumps)
            {
                CharacterControlUtilities.StandardJump(ref characterBody, characterBody.GroundingUp * characterComponent.JumpSpeed, true, characterBody.GroundingUp);
                characterComponent.CurrentAirJumps++;
            }
            
            // Gravity
            float gravityFactor = GravityFactor.IsValid ? GravityFactor.ValueRO.value : 1.0f;
            CharacterControlUtilities.AccelerateVelocity(ref characterBody.RelativeVelocity, characterComponent.Gravity * gravityFactor, deltaTime);

            // Drag
            CharacterControlUtilities.ApplyDragToVelocity(ref characterBody.RelativeVelocity, deltaTime, characterComponent.AirDrag);
        }
    }

    public void VariableUpdate(ref ThirdPersonCharacterUpdateContext context, ref KinematicCharacterUpdateContext baseContext)
    {
        ref KinematicCharacterBody characterBody = ref CharacterAspect.CharacterBody.ValueRW;
        ref ThirdPersonCharacterComponent characterComponent = ref CharacterComponent.ValueRW;
        ref ThirdPersonCharacterControl characterControl = ref CharacterControl.ValueRW;
        ref var characterTransform = ref CharacterAspect.LocalTransform.ValueRW;

        // Add rotation from parent body to the character rotation
        // (this is for allowing a rotating moving platform to rotate your character as well, and handle interpolation properly)
        KinematicCharacterUtilities.AddVariableRateRotationFromFixedRateRotation(ref characterTransform.Rotation, characterBody.RotationFromParent, baseContext.Time.DeltaTime, characterBody.LastPhysicsUpdateDeltaTime);

        float3 direction = characterControl.MoveVector;
        if (CharacterLookAt.IsValid && math.lengthsq(CharacterLookAt.ValueRO.direction) > math.FLT_MIN_NORMAL)
            direction = math.forward(CharacterLookAt.ValueRO.direction);
        else if (math.lengthsq(direction) > math.FLT_MIN_NORMAL)
            direction = math.normalize(direction);
        else
            return;

        // Rotate towards move direction
        CharacterControlUtilities.SlerpRotationTowardsDirectionAroundUp(ref characterTransform.Rotation, baseContext.Time.DeltaTime, direction, MathUtilities.GetUpFromRotation(characterTransform.Rotation), characterComponent.RotationSharpness);
    }
    
    #region Character Processor Callbacks
    public void UpdateGroundingUp(
        ref ThirdPersonCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext)
    {
        ref KinematicCharacterBody characterBody = ref CharacterAspect.CharacterBody.ValueRW;
        
        CharacterAspect.Default_UpdateGroundingUp(ref characterBody);
    }
    
    public bool CanCollideWithHit(
        ref ThirdPersonCharacterUpdateContext context, 
        ref KinematicCharacterUpdateContext baseContext,
        in BasicHit hit)
    {
        ThirdPersonCharacterComponent characterComponent = CharacterComponent.ValueRO;

        // First, see if we'd have to ignore based on the default implementation
        if (!PhysicsUtilities.IsCollidable(hit.Material))
        {
            return false;
        }

        // if not, check for the ignored tag
        if (PhysicsUtilities.HasPhysicsTag(in baseContext.PhysicsWorld, hit.RigidBodyIndex, characterComponent.IgnoredPhysicsTags))
        {
            return false;
        }

        return true;
    }

    public bool IsGroundedOnHit(
        ref ThirdPersonCharacterUpdateContext context, 
        ref KinematicCharacterUpdateContext baseContext,
        in BasicHit hit, 
        int groundingEvaluationType)
    {
        ThirdPersonCharacterComponent characterComponent = CharacterComponent.ValueRO;
        
        return CharacterAspect.Default_IsGroundedOnHit(
            in this,
            ref context,
            ref baseContext,
            in hit,
            in characterComponent.StepAndSlopeHandling,
            groundingEvaluationType);
    }

    public void OnMovementHit(
            ref ThirdPersonCharacterUpdateContext context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterHit hit,
            ref float3 remainingMovementDirection,
            ref float remainingMovementLength,
            float3 originalVelocityDirection,
            float hitDistance)
    {
        ref KinematicCharacterBody characterBody = ref CharacterAspect.CharacterBody.ValueRW;
        ref float3 characterPosition = ref CharacterAspect.LocalTransform.ValueRW.Position;
        ThirdPersonCharacterComponent characterComponent = CharacterComponent.ValueRO;
        
        CharacterAspect.Default_OnMovementHit(
            in this,
            ref context,
            ref baseContext,
            ref characterBody,
            ref characterPosition,
            ref hit,
            ref remainingMovementDirection,
            ref remainingMovementLength,
            originalVelocityDirection,
            hitDistance,
            characterComponent.StepAndSlopeHandling.StepHandling,
            characterComponent.StepAndSlopeHandling.MaxStepHeight,
            characterComponent.StepAndSlopeHandling.CharacterWidthForStepGroundingCheck);
    }

    public void OverrideDynamicHitMasses(
        ref ThirdPersonCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext,
        ref PhysicsMass characterMass,
        ref PhysicsMass otherMass,
        BasicHit hit)
    {
    }

    public void ProjectVelocityOnHits(
        ref ThirdPersonCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext,
        ref float3 velocity,
        ref bool characterIsGrounded,
        ref BasicHit characterGroundHit,
        in DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits,
        float3 originalVelocityDirection)
    {
        ThirdPersonCharacterComponent characterComponent = CharacterComponent.ValueRO;
        
        CharacterAspect.Default_ProjectVelocityOnHits(
            ref velocity,
            ref characterIsGrounded,
            ref characterGroundHit,
            in velocityProjectionHits,
            originalVelocityDirection,
            characterComponent.StepAndSlopeHandling.ConstrainVelocityToGroundPlane);
    }
    #endregion
}
