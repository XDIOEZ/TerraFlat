using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 建筑区块归属规则
    private static void RegisterBuildingStreamingOperations(Dictionary<string, Func<JObject, JObject>> result)
    {
        result.Add("building/chunk_residency", _ => CaptureBuildingChunkResidency());
        result.Add("building/hit_candidate_order", CaptureTileBuildingHitCandidateOrder);
    }

    // 只构造正式候选并采样公共排序，优先级与误差边界交给 Lua。
    private static JObject CaptureTileBuildingHitCandidateOrder(JObject args)
    {
        var candidate = new TileBuildingHitCandidate(
            new TileBuildingCell(null, new Vector2Int((int)Number(args, "x", 0), (int)Number(args, "y", 0))),
            Vector2.zero, (float)Number(args, "forward", 1), (float)Number(args, "lateral", 1),
            (float)Number(args, "distance", 1));
        var current = new TileBuildingHitCandidate(new TileBuildingCell(null, Vector2Int.zero),
            Vector2.zero, 1f, 1f, 1f);
        return new JObject { ["better"] = candidate.HasHigherHitPriorityThan(current) };
    }

    private static JObject CaptureBuildingChunkResidency()
    {
        var root = new GameObject("[LuaBuildingChunkResidency]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var registry = new RuntimeBuildingChunkRegistry();
        try
        {
            Item first = root.AddComponent<GameItem>();
            first.BindData(EntityProbeData("probe:chunk-building-a", 1f, 1f));
            var child = new GameObject("second");
            child.transform.SetParent(root.transform, false);
            Item second = child.AddComponent<GameItem>();
            second.BindData(EntityProbeData("probe:chunk-building-b", 1f, 1f));
            var a = new RuntimeWorldAddress("building-probe", new Int2(0, 0));
            var b = new RuntimeWorldAddress("building-probe", new Int2(16, 0));
            var foreign = new RuntimeWorldAddress("other-world", new Int2(0, 0));
            var items = new List<Item>();
            var sample = new JObject();
            registry.Track(first, a);
            registry.Track(first, a);
            registry.CopyTo(a, items);
            sample["duplicateCount"] = items.Count;
            registry.Track(second, foreign);
            registry.CopyTo(a, items);
            sample["isolatedCount"] = items.Count;
            registry.Track(first, b);
            registry.CopyTo(a, items);
            sample["oldChunkCount"] = items.Count;
            registry.CopyTo(b, items);
            sample["newChunkCount"] = items.Count;
            first.OnItemDestroy.Invoke(first);
            sample["destroyRemoved"] = !registry.HasItems(b);
            registry.Track(first, a);
            registry.CopyTo(a, items);
            sample["reusedCount"] = items.Count;
            registry.CopyTo(b, items);
            sample["copyClearsPrevious"] = items.Count == 0;
            registry.Clear();
            sample["cleared"] = !registry.HasItems(a) && !registry.HasItems(foreign);
            sample["scope"] = "production-registry-only";
            return sample;
        }
        finally
        {
            registry.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
    #endregion
}
