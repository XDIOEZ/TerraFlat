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
        var merged = new ChunkMergedObstacleRectangles();
        var centers = new List<Vector2>();
        var sizes = new List<Vector2>();
        var ids = new List<int>();
        JObject clip = args["clip"] as JObject;
        int sourceId = 0;
        foreach (JObject box in input)
        {
            sourceId++;
            Vector2 size = new((float)Number(box, "width", 1), (float)Number(box, "height", 1));
            Vector2 center = new Vector2((float)Number(box, "x", 0), (float)Number(box, "y", 0)) + size * .5f;
            if (clip != null)
            {
                center -= new Vector2((float)Number(clip, "originX", 0), (float)Number(clip, "originY", 0));
                if (!ChunkCollisionRenderer.TryClipLocalObstacleRectangle(center, size * .5f,
                    new Vector2((float)Number(clip, "width", 32), (float)Number(clip, "height", 32)),
                    out Vector2 clippedCenter, out Vector2 clippedSize)) continue;
                center = clippedCenter; size = clippedSize;
            }
            centers.Add(center); sizes.Add(size); ids.Add(sourceId);
            merged.SetSourceRectangle(sourceId, center, size);
        }
        bool rebuilt = merged.RebuildMergedRectangles();
        bool unchangedSourceChanged = false;
        for (int i = 0; i < centers.Count; i++)
            unchangedSourceChanged |= merged.SetSourceRectangle(ids[i], centers[i], sizes[i]);
        bool unchangedRebuilt = merged.RebuildMergedRectangles();
        var boxes = new JArray();
        foreach (ChunkMergedObstacleRectangles.MergedObstacleRectangle box in merged.Rectangles)
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
                ["eligible"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(new MachineEntity { Definition = definition }) });
        }
        MachineEntity Storage(string id, string kind = "tank", bool combine = true, bool blocking = true)
            => new() { Definition = new MachineDefinition { Id = id, PlacementLayer = 0, BlocksMovement = blocking,
                Fluid = new FluidMachineDefinition { Kind = kind, CombineAdjacent = combine } } };
        bool futureStorageStrategy;
        using (MachineLogicRegistry.RegisterFluidDevice("probe:combined-collision-storage", new FluidStorageBehavior()))
            futureStorageStrategy = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(
                Storage("FutureChemicalReservoir", "probe:combined-collision-storage"));
        return new JObject
        {
            ["builtInTanks"] = tanks, ["futureTank"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(Storage("FutureFluidReservoir")),
            ["futureStorageStrategy"] = futureStorageStrategy,
            ["disabledCombination"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(Storage("disabled", combine: false)),
            ["nonBlocking"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(Storage("passable", blocking: false)),
            ["pipe"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(Storage("pipe", "pipe")),
            ["ordinaryMachine"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(
                new MachineEntity { Definition = new MachineDefinition { Id = "ordinary", PlacementLayer = 0 } }),
            ["missingNode"] = ChunkCollisionRenderer.CanMergeAdjacentStorageCollision(null)
        };
    }

    #endregion

    #region 原生碰撞与镜像生命周期采样

    // 独立停用根实走正式碰撞容器，清理时同时撤销物理归属，不借用或修改用户世界。
    private static JObject CaptureCombinedStorageObstacleLifecycle(JObject args)
    {
        var sourceRoot = new GameObject("Combined storage collision source probe");
        var mirrorRoot = new GameObject("Combined storage collision mirror probe");
        sourceRoot.SetActive(false); mirrorRoot.SetActive(false);
        sourceRoot.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        mirrorRoot.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        BoxCollider2D template = sourceRoot.AddComponent<BoxCollider2D>();
        template.enabled = false;
        var source = new ChunkObstacleColliderSet(sourceRoot.transform);
        var mirror = new ChunkObstacleColliderSet(mirrorRoot.transform);
        var merged = new ChunkMergedObstacleRectangles();
        var retainedIds = new HashSet<int>();
        int Publish()
        {
            retainedIds.Clear();
            int changed = 0;
            foreach (ChunkMergedObstacleRectangles.MergedObstacleRectangle box in merged.Rectangles)
            {
                retainedIds.Add(box.Id);
                if (source.Set(ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.MergedStorageKind, box.Id),
                    box.Center, box.Size, template)) changed++;
            }
            return changed + source.RemoveExcept(ChunkObstacleColliderSet.MergedStorageKind, retainedIds);
        }
        float ActiveArea(Transform root)
        {
            float area = 0;
            foreach (BoxCollider2D box in root.GetComponentsInChildren<BoxCollider2D>(true))
                if (box.enabled) area += box.size.x * box.size.y;
            return area;
        }
        bool IsBlocked(Transform root, Vector2 point)
        {
            foreach (BoxCollider2D box in root.GetComponentsInChildren<BoxCollider2D>(true))
                if (box.enabled && new Rect(box.offset - box.size * .5f, box.size).Contains(point)) return true;
            return false;
        }
        bool MirrorSourcesValid()
        {
            foreach (BoxCollider2D box in mirrorRoot.GetComponentsInChildren<BoxCollider2D>(true))
            {
                if (!box.enabled) continue;
                ColliderSource2D marker = box.GetComponent<ColliderSource2D>();
                if (marker == null || marker.SourceCollider == null || !marker.SourceCollider.enabled ||
                    marker.SourceCollider.transform.parent != sourceRoot.transform || marker.ImageOffset != new Vector2(64, 0) ||
                    ColliderSource2D.Resolve(box) != marker.SourceCollider) return false;
            }
            return true;
        }
        try
        {
            for (int i = 0; i < 3; i++) merged.SetSourceRectangle(i + 1, new Vector2(i + .5f, .5f), Vector2.one);
            merged.RebuildMergedRectangles(); Publish();
            int initialCount = source.Count;
            float initialArea = ActiveArea(sourceRoot.transform);
            int unchangedChanges = Publish();
            source.CopyTo(mirror, new Vector2(64, 0));
            int mirrorInitialCount = mirror.Count;
            BoxCollider2D initialMirror = null;
            foreach (BoxCollider2D box in mirrorRoot.GetComponentsInChildren<BoxCollider2D>(true))
                if (box.enabled) initialMirror = box;
            source.CopyTo(mirror, new Vector2(64, 0));
            bool mirrorStableIdentity = initialMirror != null && initialMirror.enabled && mirror.Count == mirrorInitialCount;
            bool mirrorSourcesValid = MirrorSourcesValid();

            merged.RemoveSourceRectangle(2); merged.RebuildMergedRectangles(); Publish();
            int splitCount = source.Count;
            float splitArea = ActiveArea(sourceRoot.transform);
            bool middleStillBlocked = IsBlocked(sourceRoot.transform, new Vector2(1.5f, .5f));
            source.CopyTo(mirror, new Vector2(64, 0));
            int mirrorSplitCount = mirror.Count;
            mirrorSourcesValid &= MirrorSourcesValid();

            long independentMachine = ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.MachineKind, 20);
            long independentNatural = ChunkObstacleColliderSet.Key(ChunkObstacleColliderSet.NaturalKind, 20);
            source.Set(independentMachine, new Vector2(9.5f, .5f), Vector2.one, template);
            source.Set(independentNatural, new Vector2(10.5f, .5f), Vector2.one, template);
            merged.Clear(); merged.RebuildMergedRectangles(); Publish();
            bool independentRetained = source.Contains(independentMachine) && source.Contains(independentNatural);
            int emptyMergedCount = source.Count - 2;
            source.Clear(); source.CopyTo(mirror, new Vector2(64, 0));
            int mirrorEmptyCount = mirror.Count;
            int clearedCount = source.Count;

            var clipped = new ChunkMergedObstacleRectangles();
            if (ChunkCollisionRenderer.TryClipLocalObstacleRectangle(new Vector2(-.25f, .5f), new Vector2(.75f, .5f),
                new Vector2(2, 2), out Vector2 firstCenter, out Vector2 firstSize))
                clipped.SetSourceRectangle(10, firstCenter, firstSize);
            if (ChunkCollisionRenderer.TryClipLocalObstacleRectangle(new Vector2(1.25f, .5f), new Vector2(.75f, .5f),
                new Vector2(2, 2), out Vector2 secondCenter, out Vector2 secondSize))
                clipped.SetSourceRectangle(11, secondCenter, secondSize);
            clipped.RebuildMergedRectangles();
            float clippedArea = 0;
            foreach (ChunkMergedObstacleRectangles.MergedObstacleRectangle box in clipped.Rectangles)
                clippedArea += box.Size.x * box.Size.y;
            int clippedCount = clipped.Rectangles.Count;
            bool outsideAccepted = ChunkCollisionRenderer.TryClipLocalObstacleRectangle(new Vector2(4, .5f), Vector2.one * .5f,
                new Vector2(2, 2), out _, out _);
            int removedByRetain = clipped.RemoveSourcesExcept(new HashSet<int> { 10 });
            return new JObject
            {
                ["initialCount"] = initialCount, ["initialArea"] = initialArea, ["unchangedChanges"] = unchangedChanges,
                ["splitCount"] = splitCount, ["splitArea"] = splitArea, ["middleStillBlocked"] = middleStillBlocked,
                ["mirrorInitialCount"] = mirrorInitialCount, ["mirrorSplitCount"] = mirrorSplitCount,
                ["mirrorSourcesValid"] = mirrorSourcesValid, ["mirrorStableIdentity"] = mirrorStableIdentity,
                ["mirrorEmptyCount"] = mirrorEmptyCount, ["independentRetained"] = independentRetained,
                ["emptyMergedCount"] = emptyMergedCount, ["clearedCount"] = clearedCount,
                ["clippedCount"] = clippedCount, ["clippedArea"] = clippedArea, ["outsideAccepted"] = outsideAccepted,
                ["removedByRetain"] = removedByRetain, ["retainedSource"] = clipped.ContainsSource(10),
                ["removedSource"] = clipped.ContainsSource(11)
            };
        }
        finally
        {
            source.Clear(); mirror.Clear();
            UnityEngine.Object.DestroyImmediate(sourceRoot);
            UnityEngine.Object.DestroyImmediate(mirrorRoot);
        }
    }

    #endregion
}
