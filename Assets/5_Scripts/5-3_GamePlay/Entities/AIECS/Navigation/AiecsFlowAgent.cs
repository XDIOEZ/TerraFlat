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
    /// <summary>
    /// 纯 ECS 导航状态。位置始终是连续世界坐标；导航格只提供地形代价和共享流向，
    /// 不再承担“一个格子只能一个 AI”的占位语义。
    /// </summary>
    public struct AiecsFlowAgent : IComponentData
    {
        public float2 Position; // 当前连续世界位置。
        public float2 Velocity; // 上一模拟 Tick 的实际速度，供预测避让和朝向使用。
        public FlowGoalHandle Goal; // 少量共享目标句柄。
        public FlowSampleStatus Status; // 本 Tick 的流场采样状态。
        public float Radius; // 连续移动体半径，当前共享通行配置要求小于半格。
        public float Speed; // 当前基础速度，Buff 后由行为层写入。
        public float StopDistance; // 到局部/共享目标的停止距离。
        public AiecsMoveMode Mode; // 导向图、局部 steering 或停车。
        public float2 LocalDestination; // 只有局部模式消费。
    }

    /// <summary>移动批次的一致邻居快照；按两格宽空间桶排序后供所有单位共享。</summary>
    public struct AiecsCrowdSample : IComparable<AiecsCrowdSample>
    {
        public int2 Bucket; // 两格宽的局部 Steering 桶。
        public float2 Position, Velocity; // 冻结位置与上一 Tick 速度。
        public float Radius; // 连续体型半径。
        public Entity Entity; // 当前 World 内的实体身份。

        /// <summary>空间桶相同则使用当前 World 内的实体身份保持稳定顺序。</summary>
        public int CompareTo(AiecsCrowdSample other)
        {
            int order = Bucket.y.CompareTo(other.Bucket.y);
            if (order == 0) order = Bucket.x.CompareTo(other.Bucket.x);
            if (order == 0) order = Entity.Index.CompareTo(other.Entity.Index);
            return order != 0 ? order : Entity.Version.CompareTo(other.Entity.Version);
        }

        /// <summary>两格宽的 Steering 桶与循环世界对齐。</summary>
        public static int2 BucketOf(float2 position, WorldTopologyDomain domain) =>
            (int2)math.floor((domain.Normalize(position) - (domain.IsWrapped ? (float2)domain.Min : float2.zero)) / 2f);

        /// <summary>规范化邻桶，使接缝两侧实体也参与局部 Steering。</summary>
        public static int2 NormalizeBucket(int2 bucket, WorldTopologyDomain domain) =>
            BucketOf((domain.IsWrapped ? (float2)domain.Min : float2.zero) + (float2)bucket * 2f + 1f, domain);
    }
    #endregion

    #region 空间索引、密度场与连续移动
    /// <summary>冻结全部导航实体，后续 Steering 与密度都只读取这一时刻的数据。</summary>
    [BurstCompile]
    internal partial struct AiecsGatherCrowdJob : IJobEntity
    {
        public WorldTopologyDomain Domain;
        [NativeDisableParallelForRestriction] public NativeArray<AiecsCrowdSample> Samples;

        private void Execute([EntityIndexInQuery] int index, Entity entity, in AiecsFlowAgent actor)
        {
            Samples[index] = new AiecsCrowdSample
            {
                Bucket = AiecsCrowdSample.BucketOf(actor.Position, Domain),
                Position = Domain.Normalize(actor.Position),
                Velocity = actor.Velocity,
                Radius = actor.Radius,
                Entity = entity
            };
        }
    }

    /// <summary>为排序后的邻居快照建立连续桶范围；密集人群也只在有界样本内做个体 Steering。</summary>
    [BurstCompile]
    internal struct AiecsCrowdRangesJob : IJob
    {
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        public NativeParallelHashMap<int2, int2> Ranges;

        public void Execute()
        {
            Ranges.Clear();
            int start = 0;
            while (start < Samples.Length)
            {
                int end = start + 1;
                while (end < Samples.Length && Samples[end].Bucket.Equals(Samples[start].Bucket)) end++;
                Ranges.Add(Samples[start].Bucket, new int2(start, end - start));
                start = end;
            }
        }
    }

    /// <summary>
    /// 一格一值的动态人群密度场。它只表示拥堵压力，不拥有格子、不会拒绝进入、也不会污染正式导航缓存。
    /// </summary>
    [BurstCompile]
    internal struct AiecsCrowdDensityJob : IJob
    {
        public WorldTopologyDomain Domain;
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        public NativeParallelHashMap<int2, int> Density;

        public void Execute()
        {
            Density.Clear();
            for (int i = 0; i < Samples.Length; i++)
            {
                int2 cell = Domain.Normalize((int2)math.floor(Samples[i].Position));
                if (Density.TryGetValue(cell, out int count)) Density[cell] = count + 1;
                else Density.TryAdd(cell, 1);
            }
        }
    }

    /// <summary>
    /// 连续 Crowd 移动：共享 Flow Field 决定主方向，有限邻居 Steering 处理局部冲突，
    /// 动态密度场让高拥堵区域自然减速并向低密度侧分流。单位之间没有硬格子所有权。
    /// </summary>
    [BurstCompile]
    internal partial struct AiecsFlowMoveJob : IJobEntity
    {
        [ReadOnly] public FlowNavigationSnapshot Navigation;
        [ReadOnly] public NativeArray<AiecsCrowdSample> Samples;
        [ReadOnly] public NativeParallelHashMap<int2, int2> Ranges;
        [ReadOnly] public NativeParallelHashMap<int2, int> Density;
        public float DeltaTime;
        public int NeighbourLimit; // 单位级 Steering 的固定总预算，不随人群密度平方增长。
        public float SeparationWeight;
        public float DensityWeight;

        private void Execute(Entity entity, ref AiecsFlowAgent actor)
        {
            FlowSample sample = Sample(actor);
            actor.Status = sample.Status;
            if (sample.Status != FlowSampleStatus.Moving || DeltaTime <= 0f)
            {
                actor.Velocity = float2.zero;
                return;
            }

            float distance = math.length(sample.Delta);
            float2 desired = math.normalizesafe(sample.Delta);
            float2 direction = desired;
            if (SeparationWeight > 0f)
                direction += CrowdSteering(entity, actor, desired) * SeparationWeight;

            float densitySpeed = 1f;
            if (DensityWeight > 0f)
                direction += DensitySteering(actor, desired, out densitySpeed) * DensityWeight;

            // 少量速度惯性抑制密集区域中左右瞬时翻转，不改变真正的 Flow/地形权威。
            if (math.lengthsq(actor.Velocity) > 0.0001f)
                direction += math.normalizesafe(actor.Velocity) * 0.15f;
            direction = math.normalizesafe(direction, desired);

            float moveLength = math.min(actor.Speed * densitySpeed * DeltaTime, distance);
            float2 movement = direction * moveLength;
            int steps = math.max(1, (int)math.ceil(moveLength / 0.2f));
            float2 step = movement / steps;
            float2 start = actor.Position;

            for (int i = 0; i < steps; i++)
            {
                float2 next = actor.Position + step;
                if (!Navigation.CanStep(actor.Position, next, actor.Radius))
                {
                    // 局部 Steering 被墙角挡住时回退到 Flow 主方向，避免人群逻辑穿墙。
                    next = actor.Position + desired * (moveLength / steps);
                    if (!Navigation.CanStep(actor.Position, next, actor.Radius))
                    {
                        // 转弯净空不足时先回当前格中心再重新采样方向；这里只处理地形，不占有格子。
                        float2 center = (float2)(int2)math.floor(actor.Position) + 0.5f;
                        float2 alignment = Navigation.Domain.ShortestDelta(actor.Position, center);
                        if (math.lengthsq(alignment) <= 0.000001f) break;
                        next = actor.Position + math.normalizesafe(alignment) *
                            math.min(math.length(alignment), moveLength / steps);
                        if (!Navigation.CanStep(actor.Position, next, actor.Radius)) break;
                    }
                }
                actor.Position = Navigation.Domain.Normalize(next);
            }

            actor.Velocity = Navigation.Domain.ShortestDelta(start, actor.Position) / DeltaTime;
        }

        /// <summary>局部目标与共享 Flow 使用同一连续移动入口。</summary>
        private FlowSample Sample(AiecsFlowAgent actor)
        {
            if (actor.Mode == AiecsMoveMode.Hold)
                return new FlowSample { Status = FlowSampleStatus.Arrived };
            if (actor.Mode == AiecsMoveMode.Local)
            {
                float2 delta = Navigation.Domain.ShortestDelta(actor.Position, actor.LocalDestination);
                return new FlowSample
                {
                    Delta = delta,
                    Status = math.lengthsq(delta) <= actor.StopDistance * actor.StopDistance
                        ? FlowSampleStatus.Arrived : FlowSampleStatus.Moving
                };
            }
            return Navigation.Sample(actor.Position, actor.Goal, actor.StopDistance);
        }

        /// <summary>
        /// 只采样固定数量邻居：重叠时直接分离，未来短时间会相撞时提前侧移。
        /// 这是为大规模 ECS 定制的轻量 reciprocal-style steering，不求严格 ORCA 约束解。
        /// </summary>
        private float2 CrowdSteering(Entity entity, AiecsFlowAgent actor, float2 desired)
        {
            float2 result = float2.zero;
            int visited = 0;
            int2 bucket = AiecsCrowdSample.BucketOf(actor.Position, Navigation.Domain);
            uint seed = math.hash(new uint2((uint)entity.Index, (uint)entity.Version));
            float2 right = new float2(desired.y, -desired.x);

            for (int y = -1; y <= 1 && visited < NeighbourLimit; y++)
            for (int x = -1; x <= 1 && visited < NeighbourLimit; x++)
            {
                int2 key = AiecsCrowdSample.NormalizeBucket(bucket + new int2(x, y), Navigation.Domain);
                if (!Ranges.TryGetValue(key, out int2 range) || range.y <= 0) continue;
                int offset = (int)(math.hash(new uint3(seed, (uint)(x + 2), (uint)(y + 2))) % (uint)range.y);
                int inspect = math.min(range.y, NeighbourLimit - visited);
                for (int i = 0; i < inspect && visited < NeighbourLimit; i++)
                {
                    AiecsCrowdSample other = Samples[range.x + (offset + i) % range.y];
                    if (other.Entity == entity) continue;
                    visited++;

                    float2 toOther = Navigation.Domain.ShortestDelta(actor.Position, other.Position);
                    float squared = math.lengthsq(toOther);
                    if (squared > 9f) continue; // 三格之外交给共享 Flow 与密度场，不做单位级配对。

                    float safeDistance = actor.Radius + other.Radius + 0.08f;
                    float safeSquared = safeDistance * safeDistance;
                    if (squared < safeSquared)
                    {
                        float length = math.sqrt(math.max(squared, 0.000001f));
                        float2 away = squared > 0.000001f
                            ? -toOther / length
                            : (((seed ^ (uint)other.Entity.Index) & 1u) == 0u ? right : -right);
                        result += away * (1f - math.saturate(length / safeDistance));
                    }

                    float2 relativeVelocity = other.Velocity - actor.Velocity;
                    float relativeSpeedSquared = math.lengthsq(relativeVelocity);
                    if (relativeSpeedSquared <= 0.0001f) continue;
                    float time = -math.dot(toOther, relativeVelocity) / relativeSpeedSquared;
                    if (time <= 0f || time >= 0.65f) continue;
                    float2 predicted = toOther + relativeVelocity * time;
                    float predictedDistance = math.length(predicted);
                    float predictiveRadius = safeDistance * 1.25f;
                    if (predictedDistance >= predictiveRadius) continue;

                    float2 fallback = (((seed ^ (uint)other.Entity.Version) & 1u) == 0u ? right : -right);
                    float2 awayPredicted = -math.normalizesafe(predicted, fallback);
                    float strength = (1f - math.saturate(predictedDistance / predictiveRadius)) *
                                     (1f - time / 0.65f);
                    result += awayPredicted * strength * 0.8f;
                }
            }

            float magnitude = math.length(result);
            return magnitude > 1f ? result / magnitude : result;
        }

        /// <summary>读取前方三点密度梯度；拥堵越高越慢，并向更空的一侧偏移。</summary>
        private float2 DensitySteering(AiecsFlowAgent actor, float2 desired, out float speedScale)
        {
            float2 right = new float2(desired.y, -desired.x);
            float2 forward = actor.Position + desired * 0.9f;
            int forwardDensity = DensityAt(forward);
            int leftDensity = DensityAt(forward - right * 0.75f);
            int rightDensity = DensityAt(forward + right * 0.75f);

            float lateral = math.clamp((leftDensity - rightDensity) * 0.18f, -1f, 1f);
            float crowding = math.max(0f, forwardDensity - 3f);
            speedScale = math.clamp(1f / (1f + crowding * 0.12f), 0.4f, 1f);
            return right * lateral;
        }

        private int DensityAt(float2 position)
        {
            int2 cell = Navigation.Domain.Normalize((int2)math.floor(position));
            return Density.TryGetValue(cell, out int count) ? count : 0;
        }
    }
    #endregion

    #region 调度与容器所有权
    /// <summary>
    /// 一个 ECS Crowd 移动批次的资源所有者。每 Tick 只建立邻居桶和密度场，
    /// 不再建立单格占用、预约或候选格计划。
    /// </summary>
    public sealed class AiecsFlowCrowdScheduler : IDisposable
    {
        private NativeArray<AiecsCrowdSample> samples;
        private NativeParallelHashMap<int2, int2> ranges;
        private NativeParallelHashMap<int2, int> density;
        private JobHandle pending;
        public int SampleCount => samples.IsCreated ? samples.Length : 0;

        public JobHandle Schedule(AiecsJobSchedulerSystem scheduler, EntityQuery query, FlowNavigationCache cache,
            float deltaTime, int neighbourLimit = 12, float separationWeight = 1f, float densityWeight = 0.45f,
            JobHandle dependency = default)
        {
            pending.Complete();
            int count = query.CalculateEntityCount();
            if (!samples.IsCreated || samples.Length != count)
            {
                if (samples.IsCreated) samples.Dispose();
                if (ranges.IsCreated) ranges.Dispose();
                if (density.IsCreated) density.Dispose();
                samples = new NativeArray<AiecsCrowdSample>(count, Allocator.Persistent);
                ranges = new NativeParallelHashMap<int2, int2>(math.max(1, count), Allocator.Persistent);
                density = new NativeParallelHashMap<int2, int>(math.max(1, count), Allocator.Persistent);
            }
            if (count == 0) return dependency;

            FlowNavigationSnapshot view = cache.Read();
            pending = scheduler.ScheduleParallel(new AiecsGatherCrowdJob { Domain = view.Domain, Samples = samples }, query, dependency);
            pending = samples.SortJob().Schedule(pending);
            pending = new AiecsCrowdRangesJob { Samples = samples, Ranges = ranges }.Schedule(pending);
            pending = new AiecsCrowdDensityJob { Domain = view.Domain, Samples = samples, Density = density }.Schedule(pending);
            pending = scheduler.ScheduleParallel(new AiecsFlowMoveJob
            {
                Navigation = view,
                Samples = samples,
                Ranges = ranges,
                Density = density,
                DeltaTime = deltaTime,
                NeighbourLimit = math.max(1, neighbourLimit),
                SeparationWeight = math.max(0f, separationWeight),
                DensityWeight = math.max(0f, densityWeight)
            }, query, pending);
            cache.RegisterReader(pending);
            return pending;
        }

        /// <summary>只在批次完成后读取调试位置；该数组是移动前排序后的 Crowd 快照。</summary>
        public float2 GetDebugPosition(int index) { pending.Complete(); return samples[index].Position; }

        public void Dispose()
        {
            pending.Complete();
            if (samples.IsCreated) samples.Dispose();
            if (ranges.IsCreated) ranges.Dispose();
            if (density.IsCreated) density.Dispose();
        }
    }
    #endregion
}
