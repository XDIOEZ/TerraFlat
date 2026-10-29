using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 组合储罐碰撞回归登记

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterCombinedStorageObstacleCollisionRules()
    {
        Register("industrial_fluids", "combined_tank_collision_geometry", CaptureCombinedStorageObstacleGeometry);
        Register("industrial_fluids", "combined_tank_collision_capabilities", CaptureCombinedStorageObstacleCapabilities);
        Register("industrial_fluids", "combined_tank_collision_lifecycle", CaptureCombinedStorageObstacleLifecycle);
    }

    #endregion

    #region 正式合并器与储存能力采样

    // 探针只交给正式合并器处理，面积、空洞和数量由Lua独立断言。
    private static JObject CaptureCombinedStorageObstacleGeometry(JObject args)
    {
        if (args["boxes"] is not JArray input) throw new ArgumentException("组合罐碰撞采样缺少矩形列表。");
        var merged = new WorldPhysicsRectangleMerger2D();
        var centers = new List<Vector2>();
        var sizes = new List<Vector2>();
        var ids = new List<int>();
        int sourceId = 0;
        foreach (JObject box in input)
        {
            sourceId++;
            Vector2 size = new((float)Number(box, "width", 1), (float)Number(box, "height", 1));
            Vector2 center = new Vector2((float)Number(box, "x", 0), (float)Number(box, "y", 0)) + size * .5f;
            centers.Add(center); sizes.Add(size); ids.Add(sourceId);
            merged.SetSourceRectangle(sourceId, center, size);
        }
        bool rebuilt = merged.RebuildMergedRectangles();
        bool unchangedSourceChanged = false;
        for (int i = 0; i < centers.Count; i++)
            unchangedSourceChanged |= merged.SetSourceRectangle(ids[i], centers[i], sizes[i]);
        bool unchangedRebuilt = merged.RebuildMergedRectangles();
        var boxes = new JArray();
        foreach (WorldPhysicsRectangleMerger2D.MergedObstacleRectangle box in merged.Rectangles)
            boxes.Add(new JObject { ["id"] = box.Id, ["x"] = box.Bounds.xMin, ["y"] = box.Bounds.yMin,
                ["width"] = box.Size.x, ["height"] = box.Size.y });
        return new JObject { ["inputCount"] = input.Count, ["boxes"] = boxes, ["rebuilt"] = rebuilt,
            ["unchangedSourceChanged"] = unchangedSourceChanged, ["unchangedRebuilt"] = unchangedRebuilt };
    }

    private static JObject CaptureCombinedStorageObstacleCapabilities(JObject args)
    {
        var catalog = JsonConvert.DeserializeObject<MachineCatalogDocument>(
            Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog").text);
        var tanks = new JArray();
        foreach (string id in new[] { "GasTankBlock_Iron", "GasTankBlock_Steel", "LiquidTankBlock_Iron", "LiquidTankBlock_Steel" })
        {
            MachineDefinition definition = catalog.Nodes.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少组合储罐定义：" + id);
            tanks.Add(new JObject { ["id"] = id,
                ["eligible"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(new MachineEntity { Definition = definition }) });
        }
        MachineEntity Storage(string id, string kind = "tank", bool combine = true, bool blocking = true)
            => new() { Definition = new MachineDefinition { Id = id, PlacementLayer = 0, BlocksMovement = blocking,
                Fluid = new FluidMachineDefinition { Kind = kind, CombineAdjacent = combine } } };
        bool futureStorageStrategy;
        using (MachineLogicRegistry.RegisterFluidDevice("probe:combined-collision-storage", new FluidStorageBehavior()))
            futureStorageStrategy = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(
                Storage("FutureChemicalReservoir", "probe:combined-collision-storage"));
        return new JObject
        {
            ["builtInTanks"] = tanks, ["futureTank"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(Storage("FutureFluidReservoir")),
            ["futureStorageStrategy"] = futureStorageStrategy,
            ["disabledCombination"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(Storage("disabled", combine: false)),
            ["nonBlocking"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(Storage("passable", blocking: false)),
            ["pipe"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(Storage("pipe", "pipe")),
            ["ordinaryMachine"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(
                new MachineEntity { Definition = new MachineDefinition { Id = "ordinary", PlacementLayer = 0 } }),
            ["missingNode"] = WorldMachinePhysicsSource2D.CanMergeAdjacentStorageCollision(null)
        };
    }

    #endregion

    #region 原生碰撞与镜像生命周期采样

    // 同一通用来源实走原生几何、镜像和回收，不借用用户世界。
    private static JObject CaptureCombinedStorageObstacleLifecycle(JObject args)
    {
        using var physics = new WorldPhysicsService2D("probe:fixed-geometry",
            WorldPhysicsScene2D.CreateIsolated("probe:fixed-geometry"));
        using var source = physics.CreateFixedRectangleSource(new object(), Vector2.zero);
        var topology = new WorldTopologyDomain(new Unity.Mathematics.int2(0, 0), new Unity.Mathematics.int2(64, 64), true);
        float Area(WorldPhysicsFixedRectangleSource2D group)
        {
            float area = 0;
            if (group.Proxy != null)
                foreach (Collider2D shape in group.Proxy.Colliders)
                    if (shape is BoxCollider2D box && box.enabled) area += box.size.x * box.size.y;
            return area;
        }
        int Count(WorldPhysicsFixedRectangleSource2D group) => group.Proxy?.Colliders.Count ?? 0;
        int Images(WorldPhysicsFixedRectangleSource2D group)
        {
            int count = 0;
            if (group.Proxy != null)
                foreach (Collider2D shape in group.Proxy.Shell.ImageColliders)
                    if (shape != null && shape.enabled && shape.GetComponent<ColliderSource2D>().ImageOffset == new Vector2(64, 0)) count++;
            return count;
        }
        bool ValidImages(WorldPhysicsFixedRectangleSource2D group)
        {
            if (group.Proxy == null) return false;
            foreach (Collider2D shape in group.Proxy.Shell.ImageColliders)
            {
                ColliderSource2D marker = shape.GetComponent<ColliderSource2D>();
                if (marker?.SourceCollider == null || !marker.SourceCollider.enabled ||
                    !WorldPhysicsService2D.TryResolveCollider(shape, out var binding) || binding.World != physics) return false;
            }
            return group.Proxy.Shell.ImageColliders.Count > 0;
        }
        var result = new JObject();
        for (int i = 0; i < 3; i++) source.SetRectangle(i + 1, new Vector2(i + .5f, .5f), Vector2.one);
        physics.FlushPendingGeometry();
        result["initialCount"] = Count(source); result["initialArea"] = Area(source);
        Collider2D initial = source.Proxy.Colliders[0];
        source.SetRectangle(2, new Vector2(1.5f, .5f), Vector2.one);
        result["unchangedChanges"] = physics.PendingGeometryUpdateCount;
        result["genericProxyRegistry"] = physics.TryGetEntityProxy(source.Identity, out var registered) && registered == source.Proxy;
        physics.RefreshFixedProxyBoundaryImages(source.Proxy, topology);
        result["mirrorInitialCount"] = Images(source);
        Collider2D initialMirror = source.Proxy.Shell.ImageColliders[0];
        physics.RefreshFixedProxyBoundaryImages(source.Proxy, topology);
        result["mirrorStableIdentity"] = initialMirror == source.Proxy.Shell.ImageColliders[0] && initial == source.Proxy.Colliders[0];
        bool valid = ValidImages(source);
        source.RemoveRectangle(2); physics.FlushPendingGeometry(); Physics2D.SyncTransforms();
        result["splitCount"] = Count(source); result["splitArea"] = Area(source);
        bool middleBlocked = false;
        foreach (Collider2D collider in source.Proxy.Colliders) middleBlocked |= collider.OverlapPoint(new Vector2(1.5f, .5f));
        result["middleStillBlocked"] = middleBlocked;
        physics.RefreshFixedProxyBoundaryImages(source.Proxy, topology);
        result["mirrorSplitCount"] = Images(source); result["mirrorSourcesValid"] = valid && ValidImages(source);

        using var first = physics.CreateFixedRectangleSource(new object(), new Vector2(9, 0));
        using var second = physics.CreateFixedRectangleSource(new object(), new Vector2(10, 0));
        first.SetRectangle(20, Vector2.one * .5f, Vector2.one); second.SetRectangle(20, Vector2.one * .5f, Vector2.one);
        source.RemoveRectangle(1); source.RemoveRectangle(3); physics.FlushPendingGeometry();
        result["independentRetained"] = first.Proxy?.IsValid == true && second.Proxy?.IsValid == true &&
            first.Proxy != second.Proxy && first.Proxy.Body == second.Proxy.Body;
        result["emptyMergedCount"] = Count(source); result["clearedCount"] = Count(source);
        result["mirrorEmptyCount"] = Images(source);

        using var crossing = physics.CreateFixedRectangleSource(new object(), new Vector2(31, 0));
        crossing.SetRectangle(1, new Vector2(1f, .5f), new Vector2(2, 1));
        physics.FlushPendingGeometry(); Physics2D.SyncTransforms();
        Collider2D cross = crossing.Proxy.Colliders[0];
        result["batchBoundaryKeepsWholeShape"] = cross.OverlapPoint(new Vector2(31.25f, .5f)) &&
            cross.OverlapPoint(new Vector2(32.75f, .5f)) && !cross.OverlapPoint(new Vector2(33.25f, .5f));
        result["crossingArea"] = Area(crossing);
        return result;
    }
    #endregion
}
