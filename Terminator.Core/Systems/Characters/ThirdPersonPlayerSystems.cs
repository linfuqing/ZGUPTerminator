using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.CharacterController;
using Unity.Collections;

/// <summary>
/// Apply inputs that need to be read at a variable rate
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
[BurstCompile]
public partial struct ThirdPersonPlayerVariableStepControlSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<ThirdPersonPlayer, ThirdPersonPlayerInputs>().Build());
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (playerInputs, player) in SystemAPI.Query<ThirdPersonPlayerInputs, ThirdPersonPlayer>().WithAll<Simulate>())
        {
            if (SystemAPI.HasComponent<OrbitCameraControl>(player.ControlledCamera))
            {
                OrbitCameraControl cameraControl = SystemAPI.GetComponent<OrbitCameraControl>(player.ControlledCamera);
                
                cameraControl.FollowedCharacterEntity = player.ControlledCharacter;
                cameraControl.LookDegreesDelta = playerInputs.CameraLookInput;
                cameraControl.ZoomDelta = playerInputs.CameraZoomInput;
                
                SystemAPI.SetComponent(player.ControlledCamera, cameraControl);
            }
        }
    }
}

/// <summary>
/// Apply inputs that need to be read at a fixed rate.
/// It is necessary to handle this as part of the fixed step group, in case your framerate is lower than the fixed step rate.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct ThirdPersonPlayerFixedStepControlSystem : ISystem
{
    [BurstCompile]
    private partial struct Apply : IJobEntity
    {
        public uint tick;
        public quaternion cameraRotation;
        
        [ReadOnly]
        public ComponentLookup<LocalTransform> localTransforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<CameraRotation> cameraRotations;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ThirdPersonCharacterControl> characterControls;
        
        public void Execute(ref ThirdPersonPlayerInputs playerInputs, [ReadOnly]in ThirdPersonPlayer player)
        {
            if (cameraRotations.HasComponent(player.ControlledCharacter))
            {
                CameraRotation cameraRotation;
                cameraRotation.value = this.cameraRotation;
                cameraRotations[player.ControlledCharacter] = cameraRotation;
            }
            
            if (characterControls.TryGetComponent(player.ControlledCharacter, out var characterControl))
            {
                float3 characterUp = localTransforms.TryGetComponent(player.ControlledCharacter, out var localTransform)
                    ? MathUtilities.GetUpFromRotation(localTransform.Rotation)
                    : math.up();

                float3 cameraForwardOnUpPlane =
                    math.normalizesafe(MathUtilities.ProjectOnPlane(
                        MathUtilities.GetForwardFromRotation(cameraRotation),
                        characterUp));
                float3 cameraRight = MathUtilities.GetRightFromRotation(cameraRotation);

                // Move
                characterControl.MoveVector = (playerInputs.MoveInput.y * cameraForwardOnUpPlane) +
                                              (playerInputs.MoveInput.x * cameraRight);
                characterControl.MoveVector = MathUtilities.ClampToMaxLength(characterControl.MoveVector, 1f);

                // Jump
                // We use the "FixedInputEvent" helper struct here to detect if the event needs to be processed.
                // This is part of a strategy for proper handling of button press events that are consumed during the fixed update group.
                characterControl.Jump = playerInputs.JumpPressed.IsSet(tick);

                // Sprint
                characterControl.Sprint = playerInputs.SprintHeld;

                characterControls[player.ControlledCharacter] = characterControl;
            }
        }
    }
    
    private ComponentLookup<LocalTransform> __localTransforms;
    private ComponentLookup<CameraRotation> __cameraRotations;
    private ComponentLookup<ThirdPersonCharacterControl> __characterControls;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __localTransforms = state.GetComponentLookup<LocalTransform>(true);
        __cameraRotations = state.GetComponentLookup<CameraRotation>();
        __characterControls = state.GetComponentLookup<ThirdPersonCharacterControl>();
        
        state.RequireForUpdate<FixedTickSystem.Singleton>();
        state.RequireForUpdate<MainCameraTransform>();
        state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<ThirdPersonPlayer, ThirdPersonPlayerInputs>().Build());
    }

    public void OnDestroy(ref SystemState state)
    { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __localTransforms.Update(ref state);
        __cameraRotations.Update(ref state);
        __characterControls.Update(ref state);

        Apply apply = new Apply()
        {
            tick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick, 
            // Get camera rotation, since our movement is relative to it.
            cameraRotation = SystemAPI.GetSingleton<MainCameraTransform>().rotation, //quaternion.identity;
            localTransforms = __localTransforms,
            cameraRotations = __cameraRotations,
            characterControls = __characterControls
        };
        
        apply.ScheduleParallelByRef();
    }
}