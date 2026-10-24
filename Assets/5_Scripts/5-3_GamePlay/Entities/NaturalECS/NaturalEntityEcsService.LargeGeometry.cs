using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 多格资源接触点
        private sealed partial class Record
        {
            internal Vector2 LastAccessibleLootPoint;
            internal bool HasAccessibleLootPoint;
        }

        /// <summary>大资源按真实占地阻止种植，不只检查根节点所在的一格。</summary>
        private static bool OccupiesCell(Record record, Vector2Int root, Vector2Int cell)
            => OccupiesCell(record.Profile.Definition.WorldGridOccupancy, root, cell);

        // 显式偏移占地可以不含根格；只有未声明占地时才按根格兜底。
        internal static bool OccupiesCell(IReadOnlyList<GridCellOffset> offsets, Vector2Int root, Vector2Int cell)
        {
            if (offsets == null || offsets.Count == 0) return root == cell;
            foreach (var offset in offsets)
                if (WorldTopologyRuntime.NormalizeCell(new Vector2Int(root.x + offset.X, root.y + offset.Y)) == cell)
                    return true;
            return false;
        }

        /// <summary>采掘奖励落在攻击侧的矿山外沿，不埋进仍然阻挡移动的岩体中心。</summary>
        internal static Vector2 AccessibleResourceDrop(Bounds body, Vector2 source)
        {
            Vector2 local = WorldTopologyRuntime.ShortestDelta(body.center, source);
            Vector2 half = body.extents;
            Vector2 point = new(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y));
            if (Mathf.Abs(local.x) / Mathf.Max(.01f, half.x) >= Mathf.Abs(local.y) / Mathf.Max(.01f, half.y))
                point.x = (local.x < 0f ? -1f : 1f) * (half.x + .75f);
            else point.y = (local.y < 0f ? -1f : 1f) * (half.y + .75f);
            return WorldTopologyRuntime.NormalizePosition((Vector2)body.center + point);
        }

        private static void RememberResourceHit(Record record, Vector2 origin, float damage)
        {
            if (damage <= 0f || record.BodyBounds.size.x <= 2f && record.BodyBounds.size.y <= 2f) return;
            record.LastAccessibleLootPoint = AccessibleResourceDrop(record.BodyBounds, origin);
            record.HasAccessibleLootPoint = true;
        }

        private static Vector3 ResourceLootPosition(Record record) => record.HasAccessibleLootPoint
            ? (Vector3)record.LastAccessibleLootPoint : record.Snapshot.transform.position;
        #endregion
    }
}
