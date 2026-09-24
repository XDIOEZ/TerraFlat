using System;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 冻结视图
    /// <summary>本批实体快照；AI 数据直接从 ECS 采集，外部代理只由 Bridge 更新。</summary>
    public struct AiecsTargetSample
    {
        public Entity Entity; // 当前 World 的实体引用。
        public AiecsIdentity Identity; // 稳定身份与阵营。
        public AiecsBody Body; // 局部几何。
        public float2 Position; // 冻结世界位置。
        public float Hp, MaxHp; // 冻结生命。
        public byte Dead; // 死亡立即取消锁定。

        /// <summary>把受击/感知形状放到观察者所在的最近循环镜像。</summary>
        public PerceptionShape2D ShapeNear(float2 observer, WorldTopologyDomain domain, bool hit)
        {
            PerceptionShape2D shape = hit ? Body.Hit : Body.Perception;
            shape.Center += observer + domain.ShortestDelta(observer, Position);
            return shape;
        }
    }

    /// <summary>稀疏空间哈希读视图；四格桶按阵营分区，密集友军不能耗光找敌预算。</summary>
    public struct AiecsSpatialView
    {
        public const int BucketSize = 4; // 与 16 格导航 Chunk 对齐。
        public WorldTopologyDomain Domain; // 冻结拓扑。
        [ReadOnly] public NativeArray<AiecsTargetSample> Samples;
        [ReadOnly] public NativeParallelHashMap<Entity, int> Lookup;
        [ReadOnly] public NativeParallelMultiHashMap<int3, int> Buckets;
        [ReadOnly] public NativeArray<byte> Hostile; // 行优先关系矩阵。
        public int FactionCount; // 矩阵边长。
        public float MaximumBodyExtent; // 粗筛扩张，包含形状中心偏移。

        /// <summary>同时校验 ECS 版本、后端代际、世界和存活状态。</summary>
        public bool TryTarget(Entity entity, CombatIdentity key, out AiecsTargetSample target)
        {
            target = default;
            if (!Lookup.TryGetValue(entity, out int index)) return false;
            target = Samples[index];
            return target.Identity.Key == key && target.Identity.Targetable != 0 && target.Dead == 0 && target.Hp > 0f;
        }

        /// <summary>关系表来自当前游戏阵营规则，Job 不解析字符串或访问服务。</summary>
        public bool IsHostile(int from, int to) => (uint)from < (uint)FactionCount &&
            (uint)to < (uint)FactionCount && Hostile[from * FactionCount + to] != 0;

        /// <summary>四格空间桶的规范化坐标。</summary>
        public int2 Bucket(float2 point) => (int2)math.floor((Domain.Normalize(point) -
            (Domain.IsWrapped ? (float2)Domain.Min : float2.zero)) / BucketSize);

        /// <summary>跨世界接缝仍查询同一规范化桶。</summary>
        public int2 NormalizeBucket(int2 bucket) => Bucket((Domain.IsWrapped ? (float2)Domain.Min : float2.zero) +
            (float2)bucket * BucketSize + 0.5f);

        /// <summary>限定循环域内每个桶最多访问一次，避免小世界大半径重复候选。</summary>
        public void BucketRange(float2 center, float2 extents, out int2 min, out int2 size)
        {
            float2 origin = Domain.IsWrapped ? (float2)Domain.Min : float2.zero;
            min = (int2)math.floor((center - extents - origin) / BucketSize);
            int2 max = (int2)math.floor((center + extents - origin) / BucketSize);
            size = max - min + 1;
            if (Domain.IsWrapped) size = math.min(size, Domain.Span / BucketSize);
        }
    }

    /// <summary>冻结地形与建筑遮挡；数组与导航 Chunk 排列一致，但遮挡规则独立于可走性。</summary>
    public struct AiecsLosView
    {
        public WorldTopologyDomain Domain;
        [ReadOnly] public NativeParallelHashMap<int2, int> Chunks;
        [ReadOnly] public NativeArray<byte> Cells;

        /// <summary>未知格视作遮挡，读取 Blocking 地形和建筑占地的合并位。</summary>
        public bool Blocks(int2 cell) => !Chunks.TryGetValue(FlowNavigationMath.ChunkOf(cell, Domain), out int chunk) ||
            Cells[chunk * 256 + FlowNavigationMath.LocalIndex(cell, Domain)] != 0;

        /// <summary>整数超覆盖射线同时检查斜线两侧墙角，不调用任何物理 API。</summary>
        public bool Visible(float2 from, float2 to)
        {
            int2 start = (int2)math.floor(from);
            int2 end = (int2)math.floor(from + Domain.ShortestDelta(from, to));
            int2 delta = math.abs(end - start), step = math.select(new int2(-1), new int2(1), end >= start);
            int error = delta.x - delta.y;
            int2 cell = start;
            while (!cell.Equals(end))
            {
                int twice = error * 2;
                bool x = twice > -delta.y, y = twice < delta.x;
                if (x && y && (Blocks(cell + new int2(step.x, 0)) || Blocks(cell + new int2(0, step.y)))) return false;
                if (x) { error -= delta.y; cell.x += step.x; }
                if (y) { error += delta.x; cell.y += step.y; }
                if (Blocks(cell)) return false;
            }
            return true;
        }
    }
    #endregion

    #region 批量索引
    /// <summary>每批 O(N) 并行冻结并建表，无每实体跨格事件或托管对象。</summary>
    [BurstCompile]
    internal partial struct AiecsBuildSpatialJob : IJobEntity
    {
        public WorldTopologyDomain Domain;
        [NativeDisableParallelForRestriction] public NativeArray<AiecsTargetSample> Output;
        public NativeParallelHashMap<Entity, int>.ParallelWriter Lookup;
        public NativeParallelMultiHashMap<int3, int>.ParallelWriter Buckets;

        /// <summary>一个查询索引只写一个快照；死者保留身份记录但不参与空间候选。</summary>
        private void Execute([EntityIndexInQuery] int index, Entity entity, in AiecsIdentity identity,
            in AiecsBody body, in AiecsVital vital, in AiecsFlowAgent position)
        {
            Output[index] = new AiecsTargetSample { Entity = entity, Identity = identity, Body = body,
                Position = position.Position, Hp = vital.Hp, MaxHp = vital.MaxHp, Dead = vital.Dead };
            Lookup.TryAdd(entity, index);
            int2 bucket = (int2)math.floor((Domain.Normalize(position.Position) -
                (Domain.IsWrapped ? (float2)Domain.Min : float2.zero)) / AiecsSpatialView.BucketSize);
            if (identity.Targetable != 0 && vital.Dead == 0 && vital.Hp > 0f) Buckets.Add(new int3(bucket, identity.Faction), index);
        }
    }

    /// <summary>空间表单一所有者；重建或扩容前完成读取者，数组容量只在实体数量改变时调整。</summary>
    public sealed class AiecsSpatialIndex : IDisposable
    {
        private NativeArray<AiecsTargetSample> samples;
        private NativeParallelHashMap<Entity, int> lookup;
        private NativeParallelMultiHashMap<int3, int> buckets;
        private JobHandle readers;
        public AiecsSpatialView View { get; private set; }

        /// <summary>登记所有读取者，防止下一次重建提前清空桶。</summary>
        public void RegisterReader(JobHandle handle) => readers = JobHandle.CombineDependencies(readers, handle);

        /// <summary>阵营表替换后同步借用视图，空间桶的稳定阵营索引保持不变。</summary>
        public void SetRelations(NativeArray<byte> relations, int factionCount)
        {
            Complete(); var view = View; view.Hostile = relations; view.FactionCount = factionCount; View = view;
        }

        /// <summary>批量建立同一时刻的索引，返回显式依赖。</summary>
        public JobHandle Build(AiecsJobSchedulerSystem scheduler, EntityQuery query, WorldTopologyDomain domain, NativeArray<byte> relations,
            int factionCount, float maximumBodyExtent, JobHandle dependency = default)
        {
            // 只等待本索引上一轮的读取者；当前批次 dependency 由 Build Job 自身承接，不能在主线程提前 Complete。
            readers.Complete();
            int count = query.CalculateEntityCount();
            if (!samples.IsCreated || samples.Length < count)
            {
                Dispose();
                int capacity = 1;
                while (capacity < count) capacity *= 2;
                samples = new NativeArray<AiecsTargetSample>(capacity, Allocator.Persistent);
                lookup = new NativeParallelHashMap<Entity, int>(capacity, Allocator.Persistent);
                buckets = new NativeParallelMultiHashMap<int3, int>(capacity, Allocator.Persistent);
            }
            lookup.Clear(); buckets.Clear();
            NativeArray<AiecsTargetSample> currentSamples = samples.GetSubArray(0, count);
            View = new AiecsSpatialView { Domain = domain, Samples = currentSamples, Lookup = lookup, Buckets = buckets,
                Hostile = relations, FactionCount = factionCount, MaximumBodyExtent = maximumBodyExtent };
            // Build Job 只携带数学字段，不能同时把同一容器作为只读 View 和 Writer 传入。
            readers = scheduler.ScheduleParallel(new AiecsBuildSpatialJob { Domain = domain, Output = currentSamples,
                Lookup = lookup.AsParallelWriter(), Buckets = buckets.AsParallelWriter() }, query, dependency);
            return readers;
        }

        /// <summary>主线程 Bridge 在武器实际 Pulse 查询前等待当前索引。</summary>
        public void Complete() => readers.Complete();

        /// <summary>释放本对象的全部 Native 容器。</summary>
        public void Dispose()
        {
            readers.Complete();
            if (samples.IsCreated) samples.Dispose();
            if (lookup.IsCreated) lookup.Dispose();
            if (buckets.IsCreated) buckets.Dispose();
            samples = default; lookup = default; buckets = default;
        }
    }
    #endregion
}
