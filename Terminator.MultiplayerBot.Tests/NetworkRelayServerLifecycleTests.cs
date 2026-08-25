using System.Reflection;

using NUnit.Framework;

using Unity.Entities;
using UnityEngine;

using ZG;

/// <summary>
/// Guards relay bootstrap against copying the large NetworkRelayServer component through
/// EntityManager/SystemAPI value APIs. Such copies fail on Mono before the component is attached.
/// </summary>
public class NetworkRelayServerLifecycleTests
{
    [Test]
    [Category("Regression")]
    public void Manager_LargeServerComponentInitializesInPlaceAndReportsStatus()
    {
        var previousWorld = World.DefaultGameObjectInjectionWorld;
        var world = new World("NetworkRelayServerLifecycleTests");
        GameObject gameObject = null;
        NetworkRelayServerManager manager = null;
        try
        {
            world.GetOrCreateSystem<NetworkRelayServerSystem>();
            World.DefaultGameObjectInjectionWorld = world;

            Assert.IsFalse(NetworkRelayServerManager.IsServerReady());
            Assert.IsFalse(NetworkRelayServerManager.GetServerStatus(out _, out _, out _));

            gameObject = new GameObject("NetworkRelayServerManagerTest");
            manager = gameObject.AddComponent<NetworkRelayServerManager>();
            InvokeLifecycle(manager, "Awake");

            Assert.IsTrue(
                NetworkRelayServerManager.IsServerReady(),
                "NetworkRelayServer must be initialized in place without a large by-value component copy.");
            Assert.IsTrue(
                NetworkRelayServerManager.GetServerStatus(
                    out int connectionCount,
                    out int channelCount,
                    out int matchCount),
                "The non-throwing status path must resolve the live server component.");
            Assert.GreaterOrEqual(connectionCount, 0);
            Assert.GreaterOrEqual(channelCount, 0);
            Assert.GreaterOrEqual(matchCount, 0);

            ref var server = ref NetworkRelayServerManager.server;
            Assert.IsTrue(server.isCreated);
        }
        finally
        {
            if (manager != null)
                InvokeLifecycle(manager, "OnDestroy");

            if (gameObject != null)
                Object.DestroyImmediate(gameObject);

            World.DefaultGameObjectInjectionWorld = previousWorld;
            if (world.IsCreated)
                world.Dispose();
        }
    }

    private static void InvokeLifecycle(NetworkRelayServerManager manager, string methodName)
    {
        var method = typeof(NetworkRelayServerManager).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method, methodName);
        method.Invoke(manager, null);
    }
}
