using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.AIECS;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        private sealed partial class Record
        {
            public bool DeathByFire;
        }

        #region 纯数据可燃物适配
        private sealed class FireTargetBuffer
        {
            public FireTargetBuffer() { }
            public readonly List<Record> Records = new();
            public readonly HashSet<int> Seen = new();
        }
        private static readonly ConditionalWeakTable<WorldFireSimulationContext, FireTargetBuffer> fireTargets = new();
        internal static void CollectWorldFireFuel(WorldFireSimulationContext context)
        {
            FireTargetBuffer targets = fireTargets.GetOrCreateValue(context);
            targets.Records.Clear(); targets.Seen.Clear();
            if (simulation == null) return;
            foreach (Vector2Int cell in context.Indices.Keys)
            {
                if (!spatialCells.TryGetValue(cell, out var bucket)) continue;
                foreach (int id in bucket)
                {
                    if (!targets.Seen.Add(id) || !records.TryGetValue(id, out Record record)) continue;
                    WorldFireFuelProfile profile = record.Profile.Definition.FireFuel;
                    if (!profile.IsCombustible || record.WorldKey != context.WorldKey || !record.IsValid) continue;
                    if (context.AddFuel(record.Snapshot.transform.position, record.Profile.Definition.WorldGridOccupancy, profile.Flammability))
                        targets.Records.Add(record);
                }
            }
        }

        internal static void ApplyWorldFireExposure(WorldFireSimulationContext context)
        {
            if (!fireTargets.TryGetValue(context, out FireTargetBuffer targets)) return;
            foreach (Record record in targets.Records)
            {
                if (!record.IsValid || !simulation.TryGet(record.Handle.Id, out AiecsVital vital) || vital.Dead != 0) continue;
                float exposure = context.ExposureAt(record.Snapshot.transform.position, record.Profile.Definition.WorldGridOccupancy);
                if (exposure <= 0f) continue;
                vital.Hp = Mathf.Max(0f, vital.Hp - vital.MaxHp * exposure / record.Profile.Definition.FireFuel.BurnDurationSeconds);
                if (vital.Hp <= 0f)
                {
                    vital.Dead = 1; vital.DeathTime = Time.timeAsDouble;
                    record.DeathByFire = true;
                }
                simulation.Set(record.Handle.Id, vital);
                if (vital.Dead != 0) PublishDeath(record);
            }
        }
        #endregion
    }
}
