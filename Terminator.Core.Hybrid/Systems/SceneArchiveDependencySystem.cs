using Unity.Entities;
using Unity.Collections;
using ZG;

[UpdateInGroup(typeof(Unity.Scenes.SceneSystemGroup)),
UpdateBefore(typeof(ZG.PrefabLoaderSystem))]
public partial class SceneArchiveDependencySystem : SystemBase
{
    protected override void OnCreate()
    {
        base.OnCreate();
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            builder
                .WithAllRW<PrefabLoaderReferences>()
                .Build(this);
    }
    
    protected override void OnUpdate()
    {
        var references = SystemAPI.GetSingletonRW<PrefabLoaderReferences>().ValueRW;
    }
}
