using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public static partial class GameplayTestSystemProbe
{
    #region 地块接触单轮查询

    // 独立世界只提供权威地形，寻址、表面覆盖与实时修改全部使用生产查询。
    private static JObject TerrainContactQueryContext(JObject args)
    {
        const string dimension = "probe:terrain-contact";
        bool wrapped = Flag(args, "wrapped", false);
        WorldTopologyDomain topology = wrapped
            ? new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true) : default;
        using var world = new WorldRuntime(dimension, 1);
        var profile = new ChunkGenerationProfileSnapshot(dimension, 1, 2, 2);
        ChunkRuntime Commit(int x, int y, int tileId)
        {
            var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(x, y));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(2, 2);
            for (int row = 0; row < 2; row++)
            for (int column = 0; column < 2; column++)
                buffer.SetCell(column, row, new TerrainCell(tileId, 0, 0, 1, 1, TerrainCellFlags.Walkable));
            using var result = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(result, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunk(address, out ChunkRuntime committed);
            return committed;
        }

        ChunkRuntime first = Commit(-4, -2, 11);
        Commit(-2, -2, 22);
        Commit(2, -2, 33);
        var query = new ChunkMgr.RuntimeTerrainPresenceQuery(world, topology, dimension, 2, 2);
        JObject Sample(Vector2 point)
        {
            bool found = query.TrySample(point, out RuntimeTerrainTileSample sample);
            return new JObject
            {
                ["found"] = found, ["x"] = sample.WorldCell.x, ["y"] = sample.WorldCell.y,
                ["localX"] = sample.LocalCell.x, ["localY"] = sample.LocalCell.y,
                ["top"] = found ? sample.TopTileId : 0,
                ["ground"] = found ? sample.Cell.GroundTileId : 0,
                ["depth"] = found ? sample.LiquidDepth : 0f,
                ["liquid"] = found ? sample.LiquidId : string.Empty
            };
        }

        JObject initial = Sample(new Vector2(-3.75f, -1.75f));
        int liquid = first.Terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        first.Terrain.SetLiquid(0, 0, liquid, 6);
        first.Terrain.SetCell(0, 0, new TerrainCell(44, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        JObject changed = Sample(new Vector2(-3.75f, -1.75f));
        TerrainSupportLayer.Set(first.Terrain, 0, 0, 90, 1);
        JObject covered = Sample(new Vector2(-3.75f, -1.75f));
        TerrainSupportLayer.Set(first.Terrain, 0, 0, 0, 1);
        JObject uncovered = Sample(new Vector2(-3.75f, -1.75f));
        JObject neighbour = Sample(new Vector2(-1.75f, -1.75f));
        JObject seam = Sample(new Vector2(4.25f, -1.75f));
        JObject negativeImage = Sample(new Vector2(-4.25f, -1.75f));
        JObject missing = Sample(new Vector2(0.25f, 0.25f));
        // 丢失与回收的块不成为固定负缓存，同轮和下一轮都读取最新生命周期。
        Commit(0, 0, 55);
        JObject loadedSameBatch = Sample(new Vector2(.25f, .25f));
        var nextQuery = new ChunkMgr.RuntimeTerrainPresenceQuery(world, topology, dimension, 2, 2);
        bool newlyLoaded = nextQuery.TrySample(new Vector2(.25f, .25f), out RuntimeTerrainTileSample loaded);
        Sample(new Vector2(-3.75f, -1.75f));
        world.EvictChunk(first.Address);
        JObject removed = Sample(new Vector2(-3.75f, -1.75f));
        Commit(-4, -2, 66);
        JObject replaced = Sample(new Vector2(-3.75f, -1.75f));
        using var otherWorld = new WorldRuntime("probe:other-contact-world", 1);
        bool sameWorld = query.IsForWorld(world), foreignWorld = query.IsForWorld(otherWorld);
        world.BeginNewEpoch(2);
        bool oldEpochAccepted = query.TrySample(new Vector2(-3.75f, -1.75f), out _);
        var newEpochQuery = new ChunkMgr.RuntimeTerrainPresenceQuery(world, topology, dimension, 2, 2);
        bool newEpochAccepted = newEpochQuery.TrySample(new Vector2(-3.75f, -1.75f), out _);
        world.Dispose();
        bool disposedAccepted = newEpochQuery.TrySample(new Vector2(-3.75f, -1.75f), out _);
        return new JObject
        {
            ["initial"] = initial, ["changed"] = changed, ["covered"] = covered,
            ["uncovered"] = uncovered, ["neighbour"] = neighbour, ["seam"] = seam,
            ["negativeImage"] = negativeImage, ["missing"] = missing, ["removed"] = removed,
            ["loadedSameBatch"] = loadedSameBatch, ["replaced"] = replaced,
            ["sameWorld"] = sameWorld, ["foreignWorld"] = foreignWorld,
            ["oldEpochAccepted"] = oldEpochAccepted, ["newEpochAccepted"] = newEpochAccepted,
            ["disposedAccepted"] = disposedAccepted,
            ["newlyLoaded"] = newlyLoaded, ["loadedTop"] = newlyLoaded ? loaded.TopTileId : 0
        };
    }

    #endregion

    #region 丢弃动作按需悬停查询

    private static JObject InventoryDiscardHoverQuery(JObject args)
    {
        var root = new GameObject("[LuaDiscardHoverQuery]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        EventSystem previous = EventSystem.current;
        Mouse temporaryMouse = Mouse.current == null ? UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>() : null;
        try
        {
            var eventObject = new GameObject("EventSystem");
            eventObject.transform.SetParent(root.transform, false);
            EventSystem eventSystem = eventObject.AddComponent<EventSystem>();
            eventSystem.enabled = false;
            GameplayTestHotPathRaycaster raycaster = eventObject.AddComponent<GameplayTestHotPathRaycaster>();
            var moduleObject = new GameObject("Discard");
            moduleObject.transform.SetParent(root.transform, false);
            moduleObject.SetActive(false);
            Mod_DiscardItem module = moduleObject.AddComponent<Mod_DiscardItem>();
            module.modData = new Ex_ModData { ID = ModText.ItemDorper };
            var blocker = new GameObject("BlockingUI");
            blocker.transform.SetParent(root.transform, false);
            ItemSlot_UI Slot(string name, int index)
            {
                var slotObject = new GameObject(name);
                slotObject.transform.SetParent(root.transform, false);
                slotObject.SetActive(false);
                ItemSlot_UI slot = slotObject.AddComponent<ItemSlot_UI>();
                slot.slotIndex = index;
                return slot;
            }
            ItemSlot_UI first = Slot("FirstSlot", 1), second = Slot("SecondSlot", 2);
            var reads = new JArray();
            first.GetSlotDataFunc = index => { reads.Add(index); return null; };
            second.GetSlotDataFunc = index => { reads.Add(index); return null; };
            raycaster.First = first.gameObject;
            root.SetActive(true);
            EventSystem.current = eventSystem;
            for (int i = 0; i < 60; i++) module.ModUpdate(1f / 60f);
            int idleQueries = raycaster.Calls;
            module.FastDropItem();
            int firstQueries = raycaster.Calls;
            raycaster.First = second.gameObject;
            module.FastDropStack();
            int switchedQueries = raycaster.Calls;
            raycaster.First = blocker;
            raycaster.Second = first.gameObject;
            module.FastDropItem();
            int blockedReads = reads.Count;
            module.HandleRepeatDrop();
            int repeatedQueries = raycaster.Calls;
            raycaster.Second = second.gameObject;
            module.HandleRepeatDrop();
            return new JObject
            {
                ["idleQueries"] = idleQueries, ["firstQueries"] = firstQueries,
                ["switchedQueries"] = switchedQueries, ["blockedReads"] = blockedReads,
                ["repeatedQueries"] = repeatedQueries, ["finalQueries"] = raycaster.Calls, ["reads"] = reads
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (temporaryMouse != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(temporaryMouse);
            EventSystem.current = previous;
        }
    }

    #endregion

    #region 交互查询拓扑与实时准入

    private static JObject InteractionQueryContext(JObject args)
    {
        var root = new GameObject("[LuaInteractionQueryContext]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        GameItem actor = root.AddComponent<GameItem>();
        actor.enabled = false;
        var targets = new List<GameplayTestHotPathInteractionTarget>();
        try
        {
            GameplayTestHotPathInteractionTarget Target(string name, Vector2 position)
            {
                var targetObject = new GameObject(name);
                targetObject.transform.SetParent(root.transform, false);
                targetObject.transform.position = position;
                GameplayTestHotPathInteractionTarget target = targetObject.AddComponent<GameplayTestHotPathInteractionTarget>();
                targets.Add(target);
                return target;
            }
            GameplayTestHotPathInteractionTarget first = Target("AcrossSeam", new Vector2(3.75f, 0));
            GameplayTestHotPathInteractionTarget duplicate = Target("DuplicateVisual", new Vector2(3.75f, 0));
            GameplayTestHotPathInteractionTarget far = Target("OutsideRadius", Vector2.zero);
            root.transform.position = new Vector2(-3.75f, 0);
            first.transform.position = duplicate.transform.position = new Vector2(3.75f, 0);
            far.transform.position = Vector3.zero;
            root.SetActive(true);
            WorldTopologyDomain topology = new(new int2(-4, -4), new int2(8, 8), true);
            SpatialInteractionRegistry.Register(first, .3f);
            SpatialInteractionRegistry.Register(duplicate, .3f, first);
            SpatialInteractionRegistry.Register(far, .3f);
            var results = new List<IInteractable>();
            int Query(Vector2? pointer, WorldTopologyDomain domain)
            {
                results.Clear();
                SpatialInteractionRegistry.QueryRegisteredTargets(actor, 1f, pointer, results, domain);
                return results.Count;
            }
            int wrapped = Query(null, topology);
            int infinite = Query(null, default);
            int pointerHit = Query(new Vector2(3.75f, 0), topology);
            int pointerMiss = Query(new Vector2(3.2f, 0), topology);
            first.Available = false;
            int denied = Query(null, topology);
            first.Available = true;
            first.gameObject.SetActive(false);
            duplicate.gameObject.SetActive(false);
            int inactive = Query(null, topology);
            first.gameObject.SetActive(true);
            duplicate.gameObject.SetActive(true);
            first.transform.position = duplicate.transform.position = Vector3.zero;
            int moved = Query(null, topology);
            first.transform.position = duplicate.transform.position = new Vector2(3.75f, 0);
            SpatialInteractionRegistry.Unregister(first);
            SpatialInteractionRegistry.Unregister(duplicate);
            int removed = Query(null, topology);
            return new JObject
            {
                ["wrapped"] = wrapped, ["infinite"] = infinite, ["pointerHit"] = pointerHit,
                ["pointerMiss"] = pointerMiss, ["denied"] = denied, ["inactive"] = inactive,
                ["moved"] = moved, ["removed"] = removed
            };
        }
        finally
        {
            foreach (GameplayTestHotPathInteractionTarget target in targets)
                SpatialInteractionRegistry.Unregister(target);
            try { actor.PrepareForDespawn(saveData: false); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}

internal sealed class GameplayTestHotPathRaycaster : BaseRaycaster
{
    #region 独立 UI 射线采样
    public int Calls;
    public GameObject First;
    public GameObject Second;
    public override Camera eventCamera => null;
    public override int sortOrderPriority => int.MaxValue;
    public override int renderOrderPriority => int.MaxValue;
    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        Calls++;
        if (First != null) resultAppendList.Add(new RaycastResult { gameObject = First, module = this, index = 0 });
        if (Second != null) resultAppendList.Add(new RaycastResult { gameObject = Second, module = this, index = 1 });
    }
    #endregion
}

internal sealed class GameplayTestHotPathInteractionTarget : MonoBehaviour, IInteractable
{
    #region 独立交互准入采样
    public bool Available = true;
    public bool CanInteract(Item interactor) => Available;
    public void OnInteractStart(Item interactor) { }
    public void OnInteractCancel(Item interactor) { }
    #endregion
}
