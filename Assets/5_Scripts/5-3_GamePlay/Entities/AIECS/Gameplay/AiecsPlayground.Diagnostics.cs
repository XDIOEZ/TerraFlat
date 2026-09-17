using System.Collections.Generic;
using Unity.Mathematics;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>主角附近最多八个真实 ECS 目标的只读坐标，供正常瞄准/移动动作验收使用。</summary>
    public sealed class AiecsDebugUnit
    {
        public float X, Y, Hp, DistanceSquared;
        public int Group;
        public string Behavior;
    }

    /// <summary>开发模拟的只读快照；明确区分请求、累计生成、当前存活与当前实际提交绘制的数量。</summary>
    public sealed class AiecsDebugSnapshot
    {
        public bool Active;
        public bool LocalAvoidanceEnabled;
        public string Mode, Status;
        public int RequestedPerWave, Spawned, Alive, Visible, Batches;
        public int Targets, Chase, Wander, Flee, Attack, Deaths, Drops, PlayerHits, WeaponHits;
        public ulong Tick;
        public double BacklogSeconds;
        public int InvalidPositions, DenseCells, MaximumCellDensity;
        public uint PositionHash;
        public bool PositionsChecked;
        public int ActorLootSpawns, PendingDropRecords;
        public readonly List<AiecsDebugUnit> NearbyUnits = new();
    }

    public sealed partial class AiecsPlayground
    {
        #region 只读压力诊断

        /// <summary>轻量计数供采样器消费，不执行生成、推进模拟或导航重建。</summary>
        public int AliveUnitCount => bridge == null ? 0 : bridge.Simulation.Statistics[(int)AiecsStatistic.Alive];

        /// <summary>默认 O(1) 读数；需要观察连续 Crowd 密度时显式扫描当前已完成的只读表现快照。</summary>
        public AiecsDebugSnapshot CaptureDebugSnapshot(bool checkPositions = false)
        {
            var snapshot = new AiecsDebugSnapshot
            {
                Active = bridge != null, LocalAvoidanceEnabled = LocalAvoidanceEnabled,
                Mode = mode.ToString(), Status = Status,
                RequestedPerWave = ReinforcementUnitCount, Spawned = spawned, Alive = AliveUnitCount,
                Visible = VisibleUnitCount, Batches = VisibleBatchCount,
                Tick = CompletedSimulationTicks, BacklogSeconds = SimulationBacklogSeconds
            };
            if (bridge == null) return snapshot;
            var stats = bridge.Simulation.Statistics;
            snapshot.Targets = stats[(int)AiecsStatistic.Targets];
            snapshot.Chase = stats[(int)AiecsStatistic.Chase];
            snapshot.Wander = stats[(int)AiecsStatistic.Wander];
            snapshot.Flee = stats[(int)AiecsStatistic.Flee];
            snapshot.Attack = stats[(int)AiecsStatistic.Attack];
            snapshot.Deaths = bridge.Deaths; snapshot.Drops = bridge.Drops;
            snapshot.ActorLootSpawns = bridge.ActorLootSpawns; snapshot.PendingDropRecords = bridge.PendingDropRecords;
            snapshot.PlayerHits = bridge.PlayerHits; snapshot.WeaponHits = bridge.PlayerToEcsHits;
            if (!checkPositions) return snapshot;

            snapshot.PositionsChecked = true;
            var cells = new Dictionary<int2, int>();
            var topology = WorldTopologyRuntime.GetActiveDomain();
            float2 origin = player != null ? (UnityEngine.Vector2)player.transform.position : default;
            foreach (var record in bridge.Simulation.Display)
            {
                if (record.External != 0 || record.Dead != 0) continue;
                if (!math.all(math.isfinite(record.Position))) { snapshot.InvalidPositions++; continue; }
                int2 cell = (int2)math.floor(record.Position);
                cells.TryGetValue(cell, out int count);
                cells[cell] = ++count;
                snapshot.MaximumCellDensity = math.max(snapshot.MaximumCellDensity, count);
                snapshot.PositionHash = math.hash(new uint2(snapshot.PositionHash, math.hash(record.Position)));
                float distance = math.lengthsq(topology.ShortestDelta(origin, record.Position));
                if (snapshot.NearbyUnits.Count < 8 || distance < snapshot.NearbyUnits[7].DistanceSquared)
                {
                    snapshot.NearbyUnits.Add(new AiecsDebugUnit { X = record.Position.x, Y = record.Position.y,
                        Hp = record.Hp, Group = record.Group, DistanceSquared = distance,
                        Behavior = ((AiecsBehavior)record.Behavior).ToString() });
                    snapshot.NearbyUnits.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
                    if (snapshot.NearbyUnits.Count > 8) snapshot.NearbyUnits.RemoveAt(8);
                }
            }
            foreach (int count in cells.Values)
                if (count > 1) snapshot.DenseCells++;
            return snapshot;
        }

        #endregion
    }
}
