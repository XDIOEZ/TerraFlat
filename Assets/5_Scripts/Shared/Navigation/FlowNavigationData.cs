using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace FlatWorld.Navigation
{
    #region 数据来源与共享记录
    /// <summary>共享导航格的纯数据快照；水体只描述有效表面，不改变可走性语义。</summary>
    public struct FlowNavigationCellData
    {
        public uint Penalty; // 零表示阻挡。
        public float WaterDepth; // 有效水面的 0~1 水深。
        public byte Water; // 水上平台等支撑面为 0。
    }

    /// <summary>主线程权威网格快照入口；不向 Job 暴露业务对象。</summary>
    public interface IFlowGridSource
    {
        // 当前世界的坐标数学规则。
        WorldTopologyDomain Domain { get; }
        /// <summary>读取已注册格的最终权重与有效表面；阻挡 penalty=0，未加载返回 false。</summary>
        bool TryGetCell(int2 cell, out FlowNavigationCellData data);
        /// <summary>首次绑定时列出已加载导航 Chunk，后续更新由脏格驱动。</summary>
        void CollectChunks(List<int2> chunks);
    }

    /// <summary>共享目标句柄；代际和世界序号防止目标槽位或世界复用。</summary>
    public struct FlowGoalHandle
    {
        // 槽位、复用代际和世界序号。
        public int Slot;
        public uint Generation;
        public uint Epoch;
    }

    /// <summary>一个 16×16 导航 Chunk 的 Native 表索引。</summary>
    public struct FlowChunkHeader
    {
        // 规范化 Chunk 坐标与连续出口范围。
        public int2 Coordinate;
        public int PortalStart;
        public int PortalCount;
    }

    /// <summary>真实边缘连续缺口的出口；Anchor 是该缺口确定的代表格，Pair 是对岸对应出口。</summary>
    public struct FlowPortal
    {
        // 所属 Chunk、本地锚点、方向和缺口闭区间。
        public int Chunk;
        public int Anchor;
        public int Side;
        public int First;
        public int Last;
        // 对岸出口索引，未加载邻块不建立出口。
        public int Pair;
    }

    /// <summary>少量共享目标的冻结状态，子格移动只改 Position。</summary>
    public struct FlowGoalData
    {
        // 目标所在 Chunk 和本地格索引；Chunk 为 -1 时不可用。
        public int Chunk;
        public int Cell;
        public float2 Position;
        public uint Generation;
        public uint Epoch;
    }

    /// <summary>采样状态区分缺少导航、不可达、移动和抵达，不能通过零速度猜测失败。</summary>
    public enum FlowSampleStatus : byte { Unavailable, Unreachable, Moving, Arrived }

    /// <summary>本帧纯数据移动意图，不包含逐单位路径或请求对象。</summary>
    public struct FlowSample
    {
        // 指向下一个安全格心或真实目标位置的位移。
        public float2 Delta;
        public FlowSampleStatus Status;
    }
    #endregion

    #region 网格数学
    /// <summary>网格坐标与步进代价的共享数学；16 是导航 Chunk 边长，不是固定出口数量。</summary>
    public static class FlowNavigationMath
    {
        // 固定分块尺寸、每块格数和保留加法余量的不可达哨兵。
        public const int ChunkSize = 16;
        public const int CellCount = ChunkSize * ChunkSize;
        public const int Infinity = int.MaxValue / 4;

        /// <summary>与既有网格相同的权重换算，保留 10/14 八邻接代价。</summary>
        public static int TerrainCost(uint penalty) => penalty >= 100000u ? 1000 : (int)(penalty / 100u);

        /// <summary>负数坐标也向下取整到 Chunk；循环世界以其 Min 为分块原点。</summary>
        public static int2 ChunkOf(int2 cell, WorldTopologyDomain domain)
        {
            int2 origin = domain.IsWrapped ? domain.Min : int2.zero;
            int2 offset = domain.Normalize(cell) - origin;
            return offset / ChunkSize - math.select(int2.zero, new int2(1), offset % ChunkSize < 0);
        }

        /// <summary>还原 Chunk 的世界格原点。</summary>
        public static int2 Origin(int2 chunk, WorldTopologyDomain domain) =>
            (domain.IsWrapped ? domain.Min : int2.zero) + chunk * ChunkSize;

        /// <summary>把规范化世界格转换为 Chunk 内索引。</summary>
        public static int LocalIndex(int2 cell, WorldTopologyDomain domain)
        {
            int2 local = domain.Normalize(cell) - Origin(ChunkOf(cell, domain), domain);
            return local.x + local.y * ChunkSize;
        }

        /// <summary>还原行优先索引的二维坐标。</summary>
        public static int2 LocalCell(int index) => new int2(index % ChunkSize, index / ChunkSize);

        /// <summary>稳定的四条边法向，成对编号便于定位对岸。</summary>
        public static int2 SideOffset(int side) => side == 0 ? new int2(1, 0) : side == 1 ? new int2(-1, 0) :
            side == 2 ? new int2(0, 1) : new int2(0, -1);

        /// <summary>取得某条边上第 offset 个本地格。</summary>
        public static int EdgeCell(int side, int offset) => side < 2 ? (side == 0 ? 15 : 0) + offset * 16 :
            offset + (side == 2 ? 15 : 0) * 16;

        /// <summary>解码八邻接方向；0 代表已到当前导向图的种子格。</summary>
        public static int2 Direction(byte direction)
        {
            switch (direction)
            {
                case 1: return new int2(1, 0); case 2: return new int2(-1, 0);
                case 3: return new int2(0, 1); case 4: return new int2(0, -1);
                case 5: return new int2(1, 1); case 6: return new int2(1, -1);
                case 7: return new int2(-1, 1); case 8: return new int2(-1, -1);
                default: return int2.zero;
            }
        }
    }
    #endregion

    #region 只读采样与移动约束
    /// <summary>
    /// 一致只读导航视图；所有数组由缓存所有者释放，调用方必须登记读取 Job 的依赖。
    /// 一个目标共享整套出口选择表，每个 AI 的导航采样只查表，不运行搜索。
    /// </summary>
    public struct FlowNavigationSnapshot
    {
        // 世界身份、稀疏块索引、出口图和少量目标的共享选择表；不持有单位级路径。
        public WorldTopologyDomain Domain;
        public uint Epoch;
        [ReadOnly] public NativeParallelHashMap<int2, int> ChunkLookup;
        [ReadOnly] public NativeArray<FlowChunkHeader> Chunks;
        [ReadOnly] public NativeArray<int> Cells;
        [ReadOnly] public NativeArray<byte> Water;
        [ReadOnly] public NativeArray<float> WaterDepth;
        [ReadOnly] public NativeArray<FlowPortal> Portals;
        [ReadOnly] public NativeArray<byte> ExitDirections;
        [ReadOnly] public NativeArray<FlowGoalData> Goals;
        [ReadOnly] public NativeArray<int> TargetCosts;
        [ReadOnly] public NativeArray<byte> TargetDirections;
        [ReadOnly] public NativeArray<int> SelectedExits;

        /// <summary>读取共享目标导向；同目标 Chunk 内优先采用最新局部图。</summary>
        public FlowSample Sample(float2 position, FlowGoalHandle handle, float stopDistance)
        {
            if (handle.Epoch != Epoch || (uint)handle.Slot >= (uint)Goals.Length) return default;
            FlowGoalData goal = Goals[handle.Slot];
            if (goal.Generation != handle.Generation || goal.Chunk < 0) return default;
            int2 cell = Domain.Normalize((int2)math.floor(position));
            if (!ChunkLookup.TryGetValue(FlowNavigationMath.ChunkOf(cell, Domain), out int chunk)) return default;
            int local = FlowNavigationMath.LocalIndex(cell, Domain);
            if (Cells[chunk * 256 + local] < 0) return default;
            int targetIndex = handle.Slot * 256 + local;
            if (chunk == goal.Chunk && TargetCosts[targetIndex] < FlowNavigationMath.Infinity)
            {
                float2 delta = Domain.ShortestDelta(position, goal.Position);
                if (math.lengthsq(delta) <= stopDistance * stopDistance)
                    return new FlowSample { Status = FlowSampleStatus.Arrived };
                byte step = TargetDirections[targetIndex];
                return Towards(position, step == 0 ? goal.Position : (float2)(cell + FlowNavigationMath.Direction(step)) + 0.5f);
            }
            int selection = (handle.Slot * Chunks.Length + chunk) * 256 + local;
            int exit = SelectedExits[selection];
            if (exit < 0) return new FlowSample { Status = FlowSampleStatus.Unreachable };
            byte direction = ExitDirections[exit * 256 + local];
            if (direction != 0) return Towards(position, (float2)(cell + FlowNavigationMath.Direction(direction)) + 0.5f);
            // 出口格直接指向对岸格心；不能每帧拉回本格中心，否则越界前会反复折返。
            // 体型净空由移动层的扫掠和转弯格心对齐处理。
            float2 center = (float2)cell + 0.5f;
            return Towards(position, center + FlowNavigationMath.SideOffset(Portals[exit].Side));
        }

        /// <summary>把世界目标转换为循环世界最短位移。</summary>
        private FlowSample Towards(float2 position, float2 destination) =>
            new FlowSample { Delta = Domain.ShortestDelta(position, destination), Status = FlowSampleStatus.Moving };

        /// <summary>局部 steering 只走短直段，并保留地形代价上限；不是逐单位寻路。</summary>
        public bool CanSteer(float2 from, float2 to, float radius, float maximumDistance = 8f)
        {
            float2 delta = Domain.ShortestDelta(from, to);
            if (math.lengthsq(delta) > maximumDistance * maximumDistance) return false;
            int limit = math.max(CostAt(from), CostAt(to));
            if (limit < 0) return false;
            int steps = math.max(1, (int)math.ceil(math.length(delta) / 0.4f));
            float2 last = from;
            for (int i = 1; i <= steps; i++)
            {
                float2 next = from + delta * ((float)i / steps);
                int cost = CostAt(next);
                if (cost < 0 || cost > limit || !CanStepWithinCost(last, next, radius, limit)) return false;
                last = next;
            }
            return true;
        }

        /// <summary>读取局部位置的最终地形代价，未知或阻挡返回负值。</summary>
        public int CostAt(float2 position)
        {
            int2 cell = Domain.Normalize((int2)math.floor(position));
            return CostAtCell(cell);
        }

        /// <summary>读取指定格的最终地形代价，未知或阻挡返回负值。</summary>
        public int CostAtCell(int2 cell)
        {
            cell = Domain.Normalize(cell);
            return ChunkLookup.TryGetValue(FlowNavigationMath.ChunkOf(cell, Domain), out int chunk)
                ? Cells[chunk * 256 + FlowNavigationMath.LocalIndex(cell, Domain)] : -1;
        }

        /// <summary>读取当前位置的有效水面；水上平台已在权威网格中表现为非水表面。</summary>
        public bool TryGetWater(float2 position, out float depth)
        {
            depth = 0f;
            int2 cell = Domain.Normalize((int2)math.floor(position));
            if (!ChunkLookup.TryGetValue(FlowNavigationMath.ChunkOf(cell, Domain), out int chunk))
                return false;
            int index = chunk * 256 + FlowNavigationMath.LocalIndex(cell, Domain);
            if (Cells[index] < 0 || !Water.IsCreated || Water[index] == 0)
                return false;
            depth = WaterDepth.IsCreated ? math.saturate(WaterDepth[index]) : 0f;
            return true;
        }

        /// <summary>查询冻结网格的可走性；未知 Chunk 始终视为阻挡。</summary>
        public bool IsWalkable(int2 cell)
        {
            cell = Domain.Normalize(cell);
            return ChunkLookup.TryGetValue(FlowNavigationMath.ChunkOf(cell, Domain), out int chunk) &&
                Cells[chunk * 256 + FlowNavigationMath.LocalIndex(cell, Domain)] >= 0;
        }

        /// <summary>用圆与阻挡格 AABB 检查移动落点，无物理对象或查询。</summary>
        public bool CanOccupy(float2 position, float radius)
        {
            int2 min = (int2)math.floor(position - radius);
            int2 max = (int2)math.floor(position + radius);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                int2 cell = new int2(x, y);
                if (IsWalkable(cell)) continue;
                float2 closest = math.clamp(position, (float2)cell, (float2)cell + 1f);
                if (math.lengthsq(position - closest) < radius * radius || radius == 0f) return false;
            }
            return true;
        }

        /// <summary>扫掠圆形体型的小步路径，同时保留旧网格禁止对角切角的规则。</summary>
        public bool CanStep(float2 from, float2 to, float radius)
        {
            int2 start = Domain.Normalize((int2)math.floor(from));
            int2 end = Domain.Normalize((int2)math.floor(to));
            int2 delta = Domain.ShortestDelta(start, end);
            if (math.any(math.abs(delta) > 1)) return false;
            if (delta.x != 0 && delta.y != 0 &&
                (!IsWalkable(start + new int2(delta.x, 0)) || !IsWalkable(start + new int2(0, delta.y)))) return false;
            // 使用同一局部镜像做扫掠，避免循环边界被误认为横穿整个世界。
            to = from + Domain.ShortestDelta(from, to);
            int2 min = (int2)math.floor(math.min(from, to) - radius);
            int2 max = (int2)math.floor(math.max(from, to) + radius);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                int2 cell = new int2(x, y);
                if (IsWalkable(cell)) continue;
                float squared = FlowNavigationGeometry.SegmentAabbDistanceSquared(from, to, (float2)cell, (float2)cell + 1f);
                if (squared < radius * radius || (radius == 0f && squared == 0f)) return false;
            }
            return true;
        }

        /// <summary>
        /// 在正常体型扫掠基础上限制实体中心进入的最高地形代价。墙体/建筑仍按半径检查净空，
        /// 水等可走软地形只在中心真正跨格时生效，避免身体边缘擦到水格就被卡在锯齿岸线。
        /// </summary>
        public bool CanStepWithinCost(float2 from, float2 to, float radius, int maximumCost)
        {
            if (maximumCost < 0 || !CanStep(from, to, radius))
                return false;

            int destinationCost = CostAt(to);
            return destinationCost >= 0 && destinationCost <= maximumCost;
        }

        /// <summary>
        /// 取得一次合法主 Flow 小步中心轨迹的最高地形代价；用于约束 Crowd 偏移不能主动把中心推入
        /// 更昂贵地形。体型边缘接触水格不提升该上限，返回负值表示主步骤本身不可执行。
        /// </summary>
        public int StepMaximumCost(float2 from, float2 to, float radius)
        {
            if (!CanStep(from, to, radius))
                return -1;

            int startCost = CostAt(from);
            int destinationCost = CostAt(to);
            return startCost < 0 || destinationCost < 0
                ? -1
                : math.max(startCost, destinationCost);
        }
    }
    #endregion
}
