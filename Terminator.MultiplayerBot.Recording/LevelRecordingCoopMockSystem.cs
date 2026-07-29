using Unity.Entities;
using ZG;

/// <summary>
/// Login coop mock: activate on <see cref="LoginManager.Status.Start"/>, keep Remote Joined during client __Start wait.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct LevelRecordingCoopMockSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        LevelRecordingHarnessOps.TickCoopMock();
    }
}
