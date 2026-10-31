using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 自然实体可燃性发布
        private static void PublishNaturalCombustibility(Record record)
        {
            var profile = record.Profile.Definition.FireFuel;
            if (!profile.IsCombustible)
            { WorldCombustibilitySystem.RemoveCombustibilitySource(record.WorldKey, record); return; }
            Vector2Int root = Vector2Int.FloorToInt(record.Snapshot.transform.position);
            var cells = new HashSet<Vector2Int>();
            foreach (var offset in record.Profile.Definition.WorldGridOccupancy)
                cells.Add(root + new Vector2Int(offset.X, offset.Y));
            if (cells.Count == 0) cells.Add(root);
            WorldCombustibilitySystem.ReplaceCombustibilitySource(record.WorldKey, record, cells,
                profile.Flammability, profile.BurnDurationSeconds);
        }
        #endregion
    }
}
