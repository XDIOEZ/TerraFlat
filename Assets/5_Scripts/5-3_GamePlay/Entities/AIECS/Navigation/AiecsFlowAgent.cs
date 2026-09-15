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

    /// <summary>批量查表、简单避让和纯几何移动；不创建导航 Agent，不触碰 Physics2D。</summary>
    [BurstCompile]
    internal partial struct AiecsFlowMoveJob : IJobEntity
    {
        [ReadOnly] public FlowNavigationSnapshot Navigation;
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        [ReadOnly] public NativeParallelHashMap<int2, int2> Ranges;
        public float DeltaTime;
        public uint Tick;
        // 每桶最多检查此数量；固定九桶使密集单格工作量有界，并逐 tick 轮换成员。
        public int NeighboursPerBucket;
        public float SeparationWeight;

        /// <summary>从共享图采样方向，再在一致邻居快照上避让并按小步约束地形。</summary>
        private void Execute(Entity entity, ref AiecsFlowAgent actor)
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
            float distance = math.length(sample.Delta);
            float2 desired = math.normalizesafe(sample.Delta);
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
                if (!Navigation.CanStep(actor.Position, next, actor.Radius))
                {
                    // 避让偏转被地形阻断时先回到导向图方向，仍只面向相邻路点。
                    next = actor.Position + desired * (moveLength / steps);
                    if (!Navigation.CanStep(actor.Position, next, actor.Radius))
                    {
                        // 尚未到格心就转弯可能扫到墙角；沿本格回到可保证体型净空的路点再转向。
                        float2 center = (float2)(int2)math.floor(actor.Position) + 0.5f;
                        float2 alignment = Navigation.Domain.ShortestDelta(actor.Position, center);
                        if (math.lengthsq(alignment) == 0f) break;
                        next = actor.Position + math.normalizesafe(alignment) * math.min(math.length(alignment), moveLength / steps);
                        if (!Navigation.CanStep(actor.Position, next, actor.Radius)) break;
                    }
                }
                actor.Position = Navigation.Domain.Normalize(next);
            }
            actor.Velocity = Navigation.Domain.ShortestDelta(start, actor.Position) / DeltaTime;
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
        private NativeParallelHashMap<int2, int2> ranges;
        private JobHandle pending;
        public int SampleCount => samples.IsCreated ? samples.Length : 0;

        /// <summary>对查询中的全部 ECS 导航实体调度同一流水线；调用方传入并接回本系统的实体访问依赖。</summary>
        public JobHandle Schedule(EntityQuery query, FlowNavigationCache cache, float deltaTime, uint tick,
            int neighboursPerBucket = 8, float separationWeight = 0.8f, JobHandle dependency = default)
        {
            pending.Complete();
            int count = query.CalculateEntityCount();
            if (!samples.IsCreated || samples.Length != count)
            {
                if (samples.IsCreated) samples.Dispose();
                if (ranges.IsCreated) ranges.Dispose();
                samples = new NativeArray<AiecsCrowdSample>(count, Allocator.Persistent);
                ranges = new NativeParallelHashMap<int2, int2>(math.max(1, count), Allocator.Persistent);
            }
            if (count == 0) return dependency;
            FlowNavigationSnapshot view = cache.Read();
            pending = new AiecsGatherCrowdJob { Domain = view.Domain, Samples = samples }.ScheduleParallel(query, dependency);
            pending = samples.SortJob().Schedule(pending);
            pending = new AiecsCrowdRangesJob { Samples = samples, Ranges = ranges }.Schedule(pending);
            pending = new AiecsFlowMoveJob { Navigation = view, Samples = samples, Ranges = ranges,
                DeltaTime = deltaTime, Tick = tick, NeighboursPerBucket = math.max(1, neighboursPerBucket),
                SeparationWeight = math.max(0f, separationWeight) }.ScheduleParallel(query, pending);
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
            if (ranges.IsCreated) ranges.Dispose();
        }
    }
    #endregion
}
