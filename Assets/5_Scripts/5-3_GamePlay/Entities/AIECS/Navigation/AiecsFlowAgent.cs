using System;
using FlatWorld.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    /// <summary>默认共享目标保持旧入口兼容；局部目标仅用于短距离行为差异，不创建导向图。</summary>
    public enum AiecsMoveMode : byte { SharedGoal, Local, Hold }

    #region 实体状态与空间快照
    /// <summary>纯 ECS 导航状态；多个实体共享 Goal，实体自身不持有路径、请求或 GameObject 代理。</summary>
    public struct AiecsFlowAgent : IComponentData
    {
        // 当前世界规范化位置和本帧移动速度。
        public float2 Position;
        public float2 Velocity;
        // 共享目标与显式采样结果。
        public FlowGoalHandle Goal;
        public FlowSampleStatus Status;
        // 当前点格导航支持半径小于半格的圆体；大体型须先实现独立通行配置缓存。
        public float Radius;
        public float Speed;
        public float StopDistance;
        public AiecsMoveMode Mode; // 导向图、局部 steering 或停车。
        public float2 LocalDestination; // 只有局部模式消费。
    }

    /// <summary>避让快照中的一个实体，按空间桶和 Entity 代际稳定排序。</summary>
    public struct AiecsCrowdSample : IComparable<AiecsCrowdSample>
    {
        // 所属桶、冻结位置、体型和当前 World 内的实体身份。
        public int2 Bucket;
        public float2 Position;
        public float Radius;
        public Entity Entity;

        /// <summary>空间桶相同则使用当前 World 内的实体身份保持稳定顺序。</summary>
        public int CompareTo(AiecsCrowdSample other)
        {
            int order = Bucket.y.CompareTo(other.Bucket.y);
            if (order == 0) order = Bucket.x.CompareTo(other.Bucket.x);
            if (order == 0) order = Entity.Index.CompareTo(other.Entity.Index);
            return order != 0 ? order : Entity.Version.CompareTo(other.Entity.Version);
        }

        /// <summary>两格宽的避让桶与循环世界对齐，半径小于半格时只需访问附近九桶。</summary>
        public static int2 BucketOf(float2 position, WorldTopologyDomain domain) =>
            (int2)math.floor((domain.Normalize(position) - (domain.IsWrapped ? (float2)domain.Min : float2.zero)) / 2f);

        /// <summary>规范化邻桶，使接缝两侧实体也参与局部避让。</summary>
        public static int2 NormalizeBucket(int2 bucket, WorldTopologyDomain domain) =>
            BucketOf((domain.IsWrapped ? (float2)domain.Min : float2.zero) + (float2)bucket * 2f + 1f, domain);
    }

    /// <summary>一次移动批次的格子预约计划；候选只包含当前格周围仍朝目标推进的可走格。</summary>
    internal struct AiecsCellMovePlan
    {
        public int2 CurrentCell;
        public int2 Candidate0;
        public int2 Candidate1;
        public int2 Candidate2;
        public int2 Candidate3;
        public int2 Candidate4;
        public int2 PrimaryCell;
        public int2 GrantedCell;
        public byte CandidateCount;
        public byte NeedsReservation;
        public byte HasReservation;

        /// <summary>按优先级读取可用候选格；原始前进格另存于 PrimaryCell。</summary>
        public int2 Candidate(int index)
        {
            switch (index)
            {
                case 0: return Candidate0;
                case 1: return Candidate1;
                case 2: return Candidate2;
                case 3: return Candidate3;
                default: return Candidate4;
            }
        }

        /// <summary>追加不重复候选，最多保留原始方向、两个前侧格和两个纯侧格。</summary>
        public void AddCandidate(int2 cell)
        {
            for (int i = 0; i < CandidateCount; i++)
                if (Candidate(i).Equals(cell)) return;
            switch (CandidateCount)
            {
                case 0: Candidate0 = cell; break;
                case 1: Candidate1 = cell; break;
                case 2: Candidate2 = cell; break;
                case 3: Candidate3 = cell; break;
                case 4: Candidate4 = cell; break;
                default: return;
            }
            CandidateCount++;
        }
    }
    #endregion

    #region 空间索引与移动任务
    /// <summary>冻结 ECS 位置，整个批次只读取同一时刻的邻居。</summary>
    [BurstCompile]
    internal partial struct AiecsGatherCrowdJob : IJobEntity
    {
        public WorldTopologyDomain Domain;
        [NativeDisableParallelForRestriction] public NativeArray<AiecsCrowdSample> Samples;

        /// <summary>按查询中唯一索引写出本体快照。</summary>
        private void Execute([EntityIndexInQuery] int index, Entity entity, in AiecsFlowAgent actor)
        {
            Samples[index] = new AiecsCrowdSample { Bucket = AiecsCrowdSample.BucketOf(actor.Position, Domain),
                Position = Domain.Normalize(actor.Position), Radius = actor.Radius, Entity = entity };
        }
    }

    /// <summary>为排序后的 ECS 快照建立连续桶范围，密集桶可轮转采样而不用扫描全部邻居。</summary>
    [BurstCompile]
    internal struct AiecsCrowdRangesJob : IJob
    {
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        public NativeParallelHashMap<int2, int2> Ranges;

        /// <summary>线性登记每个桶的起点和成员数量。</summary>
        public void Execute()
        {
            Ranges.Clear();
            int start = 0;
            while (start < Samples.Length)
            {
                int end = start + 1;
                while (end < Samples.Length && Samples[end].Bucket.Equals(Samples[start].Bucket)) end++;
                Ranges.Add(Samples[start].Bucket, new int2(start, end - start)); start = end;
            }
        }
    }

    /// <summary>从本 Tick 冻结位置建立每格当前占用数；不写正式导航网格，也不触发流场重建。</summary>
    [BurstCompile]
    internal struct AiecsCellOccupancyJob : IJob
    {
        public WorldTopologyDomain Domain;
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        public NativeParallelHashMap<int2, int> Occupancy;

        public void Execute()
        {
            Occupancy.Clear();
            for (int i = 0; i < Samples.Length; i++)
            {
                int2 cell = Domain.Normalize((int2)math.floor(Samples[i].Position));
                if (Occupancy.TryGetValue(cell, out int count)) Occupancy[cell] = count + 1;
                else Occupancy.TryAdd(cell, 1);
            }
        }
    }

    /// <summary>并行生成跨格候选；前格优先，被占时只尝试仍朝目标推进且不增加地形代价的侧格。</summary>
    [BurstCompile]
    internal partial struct AiecsBuildCellMovePlansJob : IJobEntity
    {
        [ReadOnly] public FlowNavigationSnapshot Navigation;
        [NativeDisableParallelForRestriction] public NativeArray<AiecsCellMovePlan> Plans;
        public float DeltaTime;
        public uint Tick;

        private void Execute([EntityIndexInQuery] int index, Entity entity, in AiecsFlowAgent actor)
        {
            AiecsCellMovePlan plan = new AiecsCellMovePlan
            {
                CurrentCell = Navigation.Domain.Normalize((int2)math.floor(actor.Position))
            };
            if (DeltaTime <= 0f)
            {
                Plans[index] = plan;
                return;
            }

            FlowSample sample = Sample(actor);
            if (sample.Status != FlowSampleStatus.Moving || math.lengthsq(sample.Delta) <= 0.000001f)
            {
                Plans[index] = plan;
                return;
            }

            float distance = math.length(sample.Delta);
            float2 desired = math.normalizesafe(sample.Delta);
            float moveLength = math.min(actor.Speed * DeltaTime, distance);
            int2 directCell = Navigation.Domain.Normalize((int2)math.floor(actor.Position + desired * moveLength));
            if (directCell.Equals(plan.CurrentCell))
            {
                Plans[index] = plan;
                return;
            }

            plan.NeedsReservation = 1;
            int2 offset = Navigation.Domain.ShortestDelta(plan.CurrentCell, directCell);
            offset = math.clamp(offset, new int2(-1), new int2(1));
            if (offset.Equals(int2.zero))
                offset = DirectionOffset(desired);
            int2 primary = Navigation.Domain.Normalize(plan.CurrentCell + offset);
            plan.PrimaryCell = primary;
            int currentCost = Navigation.CostAt(actor.Position);
            int primaryCost = Navigation.CostAt((float2)primary + 0.5f);
            int costLimit = math.max(currentCost, primaryCost);
            TryAdd(ref plan, actor, primary, costLimit);

            // 同方向冲突时优先前侧绕行；左右顺序按实体与 Tick 轮换，避免固定一侧长期饥饿。
            bool reverse = (math.hash(new uint3((uint)entity.Index, (uint)entity.Version, Tick)) & 1u) != 0u;
            if (offset.x != 0 && offset.y != 0)
            {
                int2 first = reverse ? new int2(0, offset.y) : new int2(offset.x, 0);
                int2 second = reverse ? new int2(offset.x, 0) : new int2(0, offset.y);
                TryAdd(ref plan, actor, Navigation.Domain.Normalize(plan.CurrentCell + first), costLimit);
                TryAdd(ref plan, actor, Navigation.Domain.Normalize(plan.CurrentCell + second), costLimit);
            }
            else
            {
                int2 right = new int2(offset.y, -offset.x);
                int2 firstSide = reverse ? -right : right;
                int2 secondSide = -firstSide;
                TryAdd(ref plan, actor, Navigation.Domain.Normalize(plan.CurrentCell + offset + firstSide), costLimit);
                TryAdd(ref plan, actor, Navigation.Domain.Normalize(plan.CurrentCell + offset + secondSide), costLimit);
                TryAdd(ref plan, actor, Navigation.Domain.Normalize(plan.CurrentCell + firstSide), costLimit);
                TryAdd(ref plan, actor, Navigation.Domain.Normalize(plan.CurrentCell + secondSide), costLimit);
            }
            Plans[index] = plan;
        }

        private FlowSample Sample(AiecsFlowAgent actor)
        {
            if (actor.Mode == AiecsMoveMode.Hold)
                return new FlowSample { Status = FlowSampleStatus.Arrived };
            if (actor.Mode == AiecsMoveMode.Local)
            {
                float2 delta = Navigation.Domain.ShortestDelta(actor.Position, actor.LocalDestination);
                return new FlowSample { Delta = delta,
                    Status = math.lengthsq(delta) <= actor.StopDistance * actor.StopDistance
                        ? FlowSampleStatus.Arrived : FlowSampleStatus.Moving };
            }
            return Navigation.Sample(actor.Position, actor.Goal, actor.StopDistance);
        }

        private static int2 DirectionOffset(float2 direction)
        {
            float ax = math.abs(direction.x), ay = math.abs(direction.y);
            if (ax > ay * 1.75f) return new int2(direction.x >= 0f ? 1 : -1, 0);
            if (ay > ax * 1.75f) return new int2(0, direction.y >= 0f ? 1 : -1);
            return new int2(direction.x >= 0f ? 1 : -1, direction.y >= 0f ? 1 : -1);
        }

        private void TryAdd(ref AiecsCellMovePlan plan, AiecsFlowAgent actor, int2 cell, int costLimit)
        {
            if (cell.Equals(plan.CurrentCell)) return;
            float2 center = (float2)cell + 0.5f;
            int cost = Navigation.CostAt(center);
            if (cost < 0 || (costLimit >= 0 && cost > costLimit) || !Navigation.CanStep(actor.Position, center, actor.Radius))
                return;
            plan.AddCandidate(cell);
        }
    }

    /// <summary>串行解决少量哈希计数冲突，保证每格“现有占用 + 本 Tick 预约”不超过可配置容量。</summary>
    [BurstCompile]
    internal struct AiecsResolveCellReservationsJob : IJob
    {
        [ReadOnly] public NativeParallelHashMap<int2, int> Occupancy;
        public NativeParallelHashMap<int2, int> Reservations;
        public NativeArray<AiecsCellMovePlan> Plans;
        public int CellCapacity;
        public uint Tick;

        public void Execute()
        {
            Reservations.Clear();
            int count = Plans.Length;
            if (count == 0) return;
            int start = (int)(Tick % (uint)count);
            for (int order = 0; order < count; order++)
            {
                int index = (start + order) % count;
                AiecsCellMovePlan plan = Plans[index];
                if (plan.NeedsReservation == 0 || plan.CandidateCount == 0) continue;
                for (int candidateIndex = 0; candidateIndex < plan.CandidateCount; candidateIndex++)
                {
                    int2 cell = plan.Candidate(candidateIndex);
                    Occupancy.TryGetValue(cell, out int occupied);
                    Reservations.TryGetValue(cell, out int reserved);
                    if (occupied + reserved >= CellCapacity) continue;
                    if (reserved > 0) Reservations[cell] = reserved + 1;
                    else Reservations.TryAdd(cell, 1);
                    plan.GrantedCell = cell;
                    plan.HasReservation = 1;
                    break;
                }
                Plans[index] = plan;
            }
        }
    }

    /// <summary>批量查表、简单避让和纯几何移动；不创建导航 Agent，不触碰 Physics2D。</summary>
    [BurstCompile]
    internal partial struct AiecsFlowMoveJob : IJobEntity
    {
        [ReadOnly] public FlowNavigationSnapshot Navigation;
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        [ReadOnly] public NativeParallelHashMap<int2, int2> Ranges;
        [ReadOnly] public NativeArray<AiecsCellMovePlan> Plans;
        public float DeltaTime;
        public uint Tick;
        // 每桶最多检查此数量；固定九桶使密集单格工作量有界，并逐 tick 轮换成员。
        public int NeighboursPerBucket;
        public float SeparationWeight;

        /// <summary>从共享图采样方向，再在一致邻居快照上避让并按小步约束地形。</summary>
        private void Execute([EntityIndexInQuery] int index, Entity entity, ref AiecsFlowAgent actor)
        {
            FlowSample sample = actor.Mode == AiecsMoveMode.Hold
                ? new FlowSample { Status = FlowSampleStatus.Arrived }
                : actor.Mode == AiecsMoveMode.Local
                    ? new FlowSample { Delta = Navigation.Domain.ShortestDelta(actor.Position, actor.LocalDestination),
                        Status = math.lengthsq(Navigation.Domain.ShortestDelta(actor.Position, actor.LocalDestination)) <= actor.StopDistance * actor.StopDistance
                            ? FlowSampleStatus.Arrived : FlowSampleStatus.Moving }
                    : Navigation.Sample(actor.Position, actor.Goal, actor.StopDistance);
            actor.Status = sample.Status; actor.Velocity = float2.zero;
            if (sample.Status != FlowSampleStatus.Moving || DeltaTime <= 0f) return;
            AiecsCellMovePlan plan = Plans[index];
            // 有合法候选但本 Tick 容量全部被占/预约时原地等待；没有合法候选则保留旧格内对齐逻辑。
            if (plan.NeedsReservation != 0 && plan.CandidateCount > 0 && plan.HasReservation == 0) return;
            float distance = math.length(sample.Delta);
            float2 desired = math.normalizesafe(sample.Delta);
            if (plan.HasReservation != 0 && !plan.GrantedCell.Equals(plan.PrimaryCell))
            {
                float2 sideDelta = Navigation.Domain.ShortestDelta(actor.Position, (float2)plan.GrantedCell + 0.5f);
                distance = math.length(sideDelta);
                desired = math.normalizesafe(sideDelta, desired);
            }
            float2 separation = Separate(entity, actor, desired);
            float2 direction = math.normalizesafe(desired + separation * SeparationWeight, desired);
            float moveLength = math.min(actor.Speed * DeltaTime, distance);
            float2 movement = direction * moveLength;
            int steps = math.max(1, (int)math.ceil(moveLength / 0.2f));
            float2 step = movement / steps;
            float2 start = actor.Position;
            for (int i = 0; i < steps; i++)
            {
                float2 next = actor.Position + step;
                if (!CanEnterPlannedCell(next, plan) || !Navigation.CanStep(actor.Position, next, actor.Radius))
                {
                    // 避让偏转被地形阻断时先回到导向图方向，仍只面向相邻路点。
                    next = actor.Position + desired * (moveLength / steps);
                    if (!CanEnterPlannedCell(next, plan) || !Navigation.CanStep(actor.Position, next, actor.Radius))
                    {
                        // 尚未到格心就转弯可能扫到墙角；沿本格回到可保证体型净空的路点再转向。
                        float2 center = (float2)(int2)math.floor(actor.Position) + 0.5f;
                        float2 alignment = Navigation.Domain.ShortestDelta(actor.Position, center);
                        if (math.lengthsq(alignment) == 0f) break;
                        next = actor.Position + math.normalizesafe(alignment) * math.min(math.length(alignment), moveLength / steps);
                        if (!CanEnterPlannedCell(next, plan) || !Navigation.CanStep(actor.Position, next, actor.Radius)) break;
                    }
                }
                actor.Position = Navigation.Domain.Normalize(next);
            }
            actor.Velocity = Navigation.Domain.ShortestDelta(start, actor.Position) / DeltaTime;
        }

        /// <summary>本 Tick 只能留在原格或进入自己成功预约的一个相邻格，禁止避让偏转偷跑到第三格。</summary>
        private bool CanEnterPlannedCell(float2 position, AiecsCellMovePlan plan)
        {
            int2 cell = Navigation.Domain.Normalize((int2)math.floor(position));
            return cell.Equals(plan.CurrentCell) || (plan.HasReservation != 0 && cell.Equals(plan.GrantedCell));
        }

        /// <summary>对密集桶做轮转采样；相向单位增加稳定右侧偏转，避免完全对称停滞。</summary>
        private float2 Separate(Entity entity, AiecsFlowAgent actor, float2 desired)
        {
            float2 result = float2.zero;
            int2 bucket = AiecsCrowdSample.BucketOf(actor.Position, Navigation.Domain);
            uint seed = math.hash(new uint3((uint)entity.Index, (uint)entity.Version, Tick));
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                int2 key = AiecsCrowdSample.NormalizeBucket(bucket + new int2(x, y), Navigation.Domain);
                if (!Ranges.TryGetValue(key, out int2 range)) continue;
                int count = math.min(range.y, NeighboursPerBucket);
                int offset = (int)(seed % (uint)range.y);
                for (int i = 0; i < count; i++)
                {
                    AiecsCrowdSample other = Samples[range.x + (offset + i) % range.y];
                    if (other.Entity == entity) continue;
                    float2 delta = Navigation.Domain.ShortestDelta(other.Position, actor.Position);
                    float safeDistance = actor.Radius + other.Radius + 0.2f;
                    float squared = math.lengthsq(delta);
                    if (squared >= safeDistance * safeDistance) continue;
                    float length = math.sqrt(squared);
                    float2 away = length > 0.0001f ? delta / length : (entity.Index < other.Entity.Index ? new float2(1, 0) : new float2(-1, 0));
                    float strength = 1f - length / safeDistance;
                    result += away * strength;
                    if (math.dot(away, desired) < -0.5f) result += new float2(desired.y, -desired.x) * strength * 0.5f;
                }
            }
            return math.normalizesafe(result) * math.min(1f, math.length(result));
        }
    }
    #endregion

    #region 调度与容器所有权
    /// <summary>一个 ECS 导航批次的可复用调度器；实际实体、Native 快照和空间桶均按需扩容并释放。</summary>
    public sealed class AiecsFlowCrowdScheduler : IDisposable
    {
        private NativeArray<AiecsCrowdSample> samples;
        private NativeArray<AiecsCellMovePlan> plans;
        private NativeParallelHashMap<int2, int2> ranges;
        private NativeParallelHashMap<int2, int> occupancy;
        private NativeParallelHashMap<int2, int> reservations;
        private JobHandle pending;
        public int SampleCount => samples.IsCreated ? samples.Length : 0;

        /// <summary>对查询中的全部 ECS 导航实体调度同一流水线；调用方传入并接回本系统的实体访问依赖。</summary>
        public JobHandle Schedule(AiecsJobSchedulerSystem scheduler, EntityQuery query, FlowNavigationCache cache, float deltaTime, uint tick,
            int neighboursPerBucket = 8, float separationWeight = 0.8f, int cellCapacity = 1, JobHandle dependency = default)
        {
            pending.Complete();
            int count = query.CalculateEntityCount();
            if (!samples.IsCreated || samples.Length != count)
            {
                if (samples.IsCreated) samples.Dispose();
                if (plans.IsCreated) plans.Dispose();
                if (ranges.IsCreated) ranges.Dispose();
                if (occupancy.IsCreated) occupancy.Dispose();
                if (reservations.IsCreated) reservations.Dispose();
                samples = new NativeArray<AiecsCrowdSample>(count, Allocator.Persistent);
                plans = new NativeArray<AiecsCellMovePlan>(count, Allocator.Persistent);
                ranges = new NativeParallelHashMap<int2, int2>(math.max(1, count), Allocator.Persistent);
                occupancy = new NativeParallelHashMap<int2, int>(math.max(1, count), Allocator.Persistent);
                reservations = new NativeParallelHashMap<int2, int>(math.max(1, count), Allocator.Persistent);
            }
            if (count == 0) return dependency;
            FlowNavigationSnapshot view = cache.Read();
            pending = scheduler.ScheduleParallel(new AiecsGatherCrowdJob { Domain = view.Domain, Samples = samples }, query, dependency);
            pending = samples.SortJob().Schedule(pending);
            pending = new AiecsCrowdRangesJob { Samples = samples, Ranges = ranges }.Schedule(pending);
            pending = new AiecsCellOccupancyJob { Domain = view.Domain, Samples = samples, Occupancy = occupancy }.Schedule(pending);
            pending = scheduler.ScheduleParallel(new AiecsBuildCellMovePlansJob { Navigation = view, Plans = plans,
                DeltaTime = deltaTime, Tick = tick }, query, pending);
            pending = new AiecsResolveCellReservationsJob { Occupancy = occupancy, Reservations = reservations, Plans = plans,
                CellCapacity = math.max(1, cellCapacity), Tick = tick }.Schedule(pending);
            pending = scheduler.ScheduleParallel(new AiecsFlowMoveJob { Navigation = view, Samples = samples, Ranges = ranges, Plans = plans,
                DeltaTime = deltaTime, Tick = tick, NeighboursPerBucket = math.max(1, neighboursPerBucket),
                SeparationWeight = math.max(0f, separationWeight) }, query, pending);
            cache.RegisterReader(pending);
            return pending;
        }

        /// <summary>只在本批任务完成后读取调试位置；该数据是本次移动前的一致快照。</summary>
        public float2 GetDebugPosition(int index) { pending.Complete(); return samples[index].Position; }

        /// <summary>完成本调度器任务并释放缓存，不释放外部 World 或共享导航。</summary>
        public void Dispose()
        {
            pending.Complete();
            if (samples.IsCreated) samples.Dispose();
            if (plans.IsCreated) plans.Dispose();
            if (ranges.IsCreated) ranges.Dispose();
            if (occupancy.IsCreated) occupancy.Dispose();
            if (reservations.IsCreated) reservations.Dispose();
        }
    }
    #endregion
}
