using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static GameplayWorldRuntime World(string key, Scene scene)
    {
        var world = new GameplayWorldRuntime { Key = key, Scene = scene, Chunks = new ChunkMgr { WorldRuntime = new WorldRuntime(key) } };
        GameplayWorldRegistry.Add(world);
        return world;
    }

    private static bool Indexed(ItemMgr manager, string world, Item item, out WorldAddress address)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world);
        return manager.TryGetRuntimeEntityAddress(item, out address);
    }

    private static bool Dirty(ItemMgr manager, string world, WorldAddress address)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world);
        var dirty = new HashSet<WorldAddress>();
        manager.CopyRuntimeAiSaveAddresses(dirty);
        return dirty.Contains(address);
    }

    public static void Main()
    {
        var scene = new Scene { handle = 7, name = "shared-scene", isLoaded = true };
        World("world-a", scene);
        World("world-b", scene);
        var itemObject = new GameObject { scene = scene };
        var item = new Item { itemData = new ItemData { Guid = 42, IDName = "test-ai" } };
        GameplayWorldRegistry.AssignItemToWorld(item, "world-a");
        itemObject.Add(item);
        itemObject.Add(new AiMarker());
        var manager = new ItemMgr();
        using (GameplayWorldRegistry.UseWorld("world-a"))
            Check(manager.TestRegister(item), "initial registration in world A");
        Check(Indexed(manager, "world-a", item, out WorldAddress addressA), "A owns registered address");
        Check(!Indexed(manager, "world-b", item, out _), "same-scene B must not contain item");
        GameplayWorldRegistry.AssignItemToWorld(item, "world-b");
        using (GameplayWorldRegistry.UseWorld("world-a")) manager.TestMove(item);
        Check(!Indexed(manager, "world-a", item, out _), "same-scene migration removed A address");
        Check(Dirty(manager, "world-a", addressA), "old A address saved as dirty");
        Check(Indexed(manager, "world-b", item, out WorldAddress addressB), "migration registered B address");

        GameplayWorldRegistry.AssignItemToWorld(item, "world-not-ready");
        using (GameplayWorldRegistry.UseWorld("world-a")) manager.TestMove(item);
        Check(!Indexed(manager, "world-a", item, out _), "unavailable target cannot fall back to current world");
        Check(!Indexed(manager, "world-b", item, out _), "unavailable target removes prior index");
        Check(Dirty(manager, "world-b", addressB), "unavailable target dirties B old address");
        Check(!GameplayWorldRegistry.TryGet("world-not-ready", out _), "migration must not create target world");
        var target = new GameplayWorldRuntime { Key = "world-not-ready", Scene = scene };
        GameplayWorldRegistry.Add(target);
        using (GameplayWorldRegistry.UseWorld("world-a")) manager.TestMove(item);
        Check(!Indexed(manager, "world-a", item, out _) && !Indexed(manager, "world-b", item, out _),
            "registered world without chunks cannot fall back to another world");
        target.Chunks = new ChunkMgr { WorldRuntime = new WorldRuntime("world-not-ready") };
        using (GameplayWorldRegistry.UseWorld("world-a")) manager.TestMove(item);
        Check(Indexed(manager, "world-not-ready", item, out _), "ready target acquires pending item");
        Check(!Indexed(manager, "world-a", item, out _) && !Indexed(manager, "world-b", item, out _), "only one owner after readiness");

        GameplayWorldRegistry.Remove("world-not-ready");
        using (GameplayWorldRegistry.UseWorld("world-a")) manager.TestUnregister(item);
        World("world-not-ready", scene);
        Check(!Indexed(manager, "world-not-ready", item, out _), "unregister during target unload clears old owner");
        Check(!GameplayWorldRegistry.TryGet("world-never-created", out _), "unregister does not create unrelated world");

        GameplayWorldRegistry.AssignItemToWorld(item, "world-b");
        using (GameplayWorldRegistry.UseWorld("world-a")) Check(manager.TestRegister(item), "re-register in B");
        using (GameplayWorldRegistry.UseWorld("world-a"))
        {
            var otherScene = new Scene { handle = 8, name = "presentation", isLoaded = true };
            Check(manager.GetRuntimeEntityRoot(otherScene).gameObject.scene == otherScene,
                "entity root prefers requested item scene over execution scene");
        }
        item.Destroyed = true;
        using (GameplayWorldRegistry.UseWorld("world-a")) manager.TestCleanup();
        item.Destroyed = false;
        Check(!Indexed(manager, "world-b", item, out _), "cleanup removes pseudo-null owner from other world");

        var ambiguous = new HashSet<WorldAddress>();
        manager.CopyRuntimeAiSaveAddresses(ambiguous);
        Check(ambiguous.Count == 0, "missing execution identity never borrows another world's indexes");
        Console.WriteLine($"RuntimeAiWorldOwnership passed: {checks} checks");
    }
}
