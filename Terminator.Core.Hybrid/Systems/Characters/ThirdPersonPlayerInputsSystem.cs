using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class ThirdPersonPlayerInputsSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<FixedTickSystem.Singleton>();
        RequireForUpdate(SystemAPI.QueryBuilder().WithAll<ThirdPersonPlayer, ThirdPersonPlayerInputs>().Build());
    }
    
    protected override void OnUpdate()
    {
        uint tick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick;

        foreach (var (playerInputs, player) in SystemAPI.Query<RefRW<ThirdPersonPlayerInputs>, ThirdPersonPlayer>())
        {
            playerInputs.ValueRW.MoveInput = Joystick.axis;/*new float2
            {
                x = (Input.GetKey(KeyCode.D) ? 1f : 0f) + (Input.GetKey(KeyCode.A) ? -1f : 0f),
                y = (Input.GetKey(KeyCode.W) ? 1f : 0f) + (Input.GetKey(KeyCode.S) ? -1f : 0f),
            }*/;

            //playerInputs.ValueRW.CameraLookInput = new float2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            //playerInputs.ValueRW.CameraZoomInput = -Input.mouseScrollDelta.y;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                playerInputs.ValueRW.JumpPressed.Set(tick);
            }
            
            playerInputs.ValueRW.SprintHeld = Input.GetKey(KeyCode.LeftShift);
        }
    }
}

