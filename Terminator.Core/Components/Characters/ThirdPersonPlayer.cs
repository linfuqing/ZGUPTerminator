using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct ThirdPersonPlayer : IComponentData
{
    public Entity ControlledCharacter;
    public Entity ControlledCamera;
}

public struct ThirdPersonPlayerInputs : IComponentData
{
    public float2 MoveInput;
    public float2 CameraLookInput;
    public float CameraZoomInput;
    public FixedInputEvent JumpPressed;
    public bool SprintHeld;
}
