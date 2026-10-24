using FlatWorld.WorldModel;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

static class Program
{
    private static int passed;
    private static readonly WorldAddress A = new("surface", new(0, 0));
    private static readonly WorldAddress B = new("surface", new(16, 0));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed++;
        Console.WriteLine("PASS " + message);
    }
    private static SaveDataMgr Reset()
    {
        ChunkMgr.ExistingInstance = new ChunkMgr();
        ItemMgr.Instance = new ItemMgr();
        SpaceSession.Current = new SpaceSession();
        GameNetwork.HasStateAuthority = true;
        GameplayWorldRegistry.CurrentScene = "surface";
        return new SaveDataMgr();
    }
    private static Item Spawn(SaveDataMgr save, int guid, float x = 1)
    {
        var item = new Item { itemData = new ItemData { Guid = guid }, OnSaved = save.Record };
        item.transform.position = new Vector3(x, 1);
        ItemMgr.Instance.Items.Add(guid, item);
        save.Record(item);
        return item;
    }
    public static void Main()
    {
        var registry = new RuntimeBuildingChunkRegistry();
        var item = new Item();
        var list = new List<Item>();
        registry.Track(item, A); registry.Track(item, A); registry.CopyTo(A, list);
        Check(list.Count == 1 && item.OnItemDestroy.GetInvocationList().Length == 1, "duplicate registration is idempotent");
        registry.Track(item, B); registry.CopyTo(A, list);
        Check(list.Count == 0 && registry.HasItems(B), "moving ownership releases old chunk");
        var foreign = new WorldAddress("other-world", A.ChunkOrigin);
        var second = new Item(); registry.Track(second, foreign);
        item.OnItemDestroy.Invoke(item);
        Check(!registry.HasItems(B) && registry.HasItems(foreign), "despawn removes only its own world/chunk");
        registry.Track(item, A); registry.Clear();
        Check(item.OnItemDestroy == null && second.OnItemDestroy == null, "world release removes all event subscriptions");
        item.IsInPool = true; registry.Track(item, A);
        Check(!registry.HasItems(A), "pooled shells cannot become residents");

        var save = Reset();
        var floor = Spawn(save, 101); var wall = Spawn(save, 102, 2); var neighbor = Spawn(save, 103, 17);
        save.MarkRestored(A);
        Check(save.TryUnloadRuntimeBuildingsForChunk(A), "ground ship chunk can unload");
        Check(ItemMgr.Instance.Items.Count == 1 && ItemMgr.Instance.Items.ContainsKey(103), "only owning chunk is recycled");
        Check(!save.IsRestored(A) && !save.IsDirty(A), "restore latch and stale dirty marks reset");
        var saved = save.Records[SaveDataMgr.Key(A)];
        Check(saved.Count == 2 && saved[0].Hp == 83 && saved[0].InventoryCount == 17 && saved[0].ModuleState == "door-open", "health inventory and module state captured");
        floor.itemData.InventoryCount = 0; wall.itemData.Hp = 0;
        Check(saved.All(x => x.InventoryCount == 17 && x.Hp == 83), "pooled shell mutation cannot corrupt snapshot");
        save.AutoSave(A); save.AutoSave(A);
        Check(saved.Count == 2, "autosave with no live items preserves cold ship");
        save.AutoSave(A, new ItemData { Guid = 101, Hp = 61 });
        Check(saved.Count == 2 && saved.Single(x => x.Guid == 101).Hp == 61, "same identity upsert does not duplicate cold snapshot");
        save.RemoveRecord(A, 101); save.AutoSave(A);
        Check(saved.Count == 1 && saved[0].Guid == 102, "takeoff or dismantle cannot resurrect removed identity");
        for (int iteration = 0; iteration < 3; iteration++)
        {
            var restored = Spawn(save, 102, 2);
            restored.itemData = saved.Single(x => x.Guid == 102).Clone();
            save.MarkRestored(A);
            Check(save.TryUnloadRuntimeBuildingsForChunk(A) && !save.IsRestored(A) && saved.Count == 1,
                "repeated reload/unload preserves one identity cycle " + (iteration + 1));
        }

        save = Reset(); floor = Spawn(save, 201); wall = Spawn(save, 202, 2); wall.FailSave = true;
        save.MarkRestored(A);
        Check(!save.TryUnloadRuntimeBuildingsForChunk(A), "snapshot failure refuses chunk release");
        Check(ItemMgr.Instance.Items.Count == 2 && ItemMgr.Instance.DespawnCalls == 0 && save.IsRestored(A), "capture failure leaves entire ship alive");
        wall.FailSave = false;
        Check(save.TryUnloadRuntimeBuildingsForChunk(A), "capture failure can recover on retry");

        save = Reset(); floor = Spawn(save, 301); SpaceSession.Current.Pieces.Add(301);
        Check(save.TryUnloadRuntimeBuildingsForChunk(A) && ItemMgr.Instance.Items.ContainsKey(301), "flight-owned ship is not chunk recycled");
        save = Reset(); floor = Spawn(save, 302); floor.PersistenceOwner = new object();
        Check(save.TryUnloadRuntimeBuildingsForChunk(A) && ItemMgr.Instance.Items.ContainsKey(302), "other persistence owner is not stolen");
        save = Reset(); floor = Spawn(save, 303); floor.transform.position = new Vector3(17, 1);
        Check(save.TryUnloadRuntimeBuildingsForChunk(A) && ChunkMgr.ExistingInstance.HasRuntimeBuildings(B) && ItemMgr.Instance.Items.Count == 1,
            "moved building reindexed rather than unloaded with old chunk");
        save = Reset(); floor = Spawn(save, 304); floor.gameObject.scene = "other-world";
        Check(save.TryUnloadRuntimeBuildingsForChunk(A) && ItemMgr.Instance.Items.Count == 1, "foreign scene is never recycled");
        save = Reset(); floor = Spawn(save, 305); floor.ThrowAfterDespawn = true;
        Check(save.TryUnloadRuntimeBuildingsForChunk(A) && save.Records[SaveDataMgr.Key(A)].Count == 1,
            "teardown observer failure still retains committed snapshot");
        save = Reset(); floor = Spawn(save, 306); GameNetwork.HasStateAuthority = false;
        Check(save.TryUnloadRuntimeBuildingsForChunk(A) && ItemMgr.Instance.Items.Count == 1, "client never destroys authority state");
        Console.WriteLine($"Building streaming: {passed} assertions passed. Unity scene/graphics/network replay not covered.");
    }
}
