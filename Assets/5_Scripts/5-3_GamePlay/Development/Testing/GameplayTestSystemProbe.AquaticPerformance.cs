using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 水生线路查询去重

    // 夹具只提供逐格可游泳结果，路径采样与环绕仍调用正式 AquaticHabitat。
    private static JObject EntityAquaticTraversal(JObject args)
    {
        Vector2 origin = new((float)Number(args, "originX", 0.1d), (float)Number(args, "originY", 0.1d));
        Vector2 target = new((float)Number(args, "targetX", 2.9d), (float)Number(args, "targetY", 0.1d));
        WorldTopologyDomain topology = Flag(args, "wrapped", false)
            ? new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true) : default;
        var blocked = new HashSet<Vector2Int>();
        if (args["blockedCells"] is JArray cells)
            foreach (JObject cell in cells)
                blocked.Add(new Vector2Int((int)Number(cell, "x", 0), (int)Number(cell, "y", 0)));
        var visits = new JArray();
        bool CanSwim(Vector2 position)
        {
            Vector2Int cell = Vector2Int.FloorToInt(position);
            visits.Add(new JObject { ["x"] = cell.x, ["y"] = cell.y });
            return !blocked.Contains(cell);
        }
        bool accepted = AquaticHabitat.CanTraverse(origin, target, topology, CanSwim);
        int queries = visits.Count;
        var firstVisits = (JArray)visits.DeepClone();
        float2 normalizedTarget = topology.Normalize(new float2(target.x, target.y));
        blocked.Add(Vector2Int.FloorToInt(new Vector2(normalizedTarget.x, normalizedTarget.y)));
        visits.Clear();
        bool acceptedAfterChange = AquaticHabitat.CanTraverse(origin, target, topology, CanSwim);
        return new JObject
        {
            ["accepted"] = accepted, ["queries"] = queries, ["visits"] = firstVisits,
            ["acceptedAfterChange"] = acceptedAfterChange, ["queriesAfterChange"] = visits.Count
        };
    }

    #endregion
}
