using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 扩展入口
    /// <summary>能力扩展的稳定接入点；决策前写提议，移动前消费自定义意图，结算后处理已提交结果。</summary>
    public enum AiecsStagePhase { BeforeDecision, BeforeMovement, BeforeAttack, BeforeSettlement, AfterDamage }

    /// <summary>少量批处理能力系统的调度契约；只在注册时创建对象，禁止每个实体注册托管状态机。</summary>
    public interface IAiecsSimulationStage
    {
        AiecsStagePhase Phase { get; }
        /// <summary>消费冻结帧并返回全部 Native/ECS 访问依赖，不能自行丢弃依赖或持有跨帧视图。</summary>
        JobHandle Schedule(EntityManager entities, EntityQuery query, AiecsFrame frame, JobHandle dependency);
    }

    /// <summary>本 tick 的只读资源契约，所有借用均在 Simulation 的生命周期内。</summary>
    public struct AiecsFrame
    {
        public CombatClock Clock;
        public AiecsSpatialView Spatial;
        public AiecsLosView Los;
        public FlowNavigationSnapshot Navigation;
        public NativeArray<AiecsDefinition> Definitions;
        public NativeArray<FixedString128Bytes> Factions;
        public NativeQueue<AiecsHitEvent>.ParallelWriter HitEvents; // 技能在实际 Pulse 通过同一结算入口发射命中。
    }

    /// <summary>冷路径创建模板；静态定义共享，生命和部位复制为实例运行态。</summary>
    public struct AiecsActorTemplate
    {
        public int Definition, Faction;
        public AiecsBody Body;
        public AiecsVital Vital;
        public AiecsDefense Defense;
        public AiecsAnatomy Anatomy;
        public byte HasBlood;
    }
    #endregion

    /// <summary>
    /// 正式 ECS 模拟的资源与调度所有者，玩法分别由独立批量 System 执行。
    /// 一个 World、一组空间表和少量共享 Goal 服务全部单位；主线程只协调阶段和外部事件，不逐 AI 决策。
    /// </summary>
    public sealed class AiecsSimulation : IDisposable
    {
        #region 资源
        private readonly World world;
        private readonly AiecsJobSchedulerSystem scheduler;
        private readonly EntityArchetype archetype;
        private readonly EntityQuery query;
        private readonly AiecsSpatialIndex spatial = new AiecsSpatialIndex();
        private readonly AiecsFlowCrowdScheduler movement = new AiecsFlowCrowdScheduler();
        private readonly List<IAiecsSimulationStage> stages = new List<IAiecsSimulationStage>();
        private NativeArray<AiecsDefinition> definitions;
        private NativeArray<AiecsBuffDefinition> buffs;
        private NativeArray<FixedString128Bytes> factions;
        private NativeArray<byte> relations;
        private NativeArray<FlowGoalHandle> goals;
        private NativeArray<AiecsHitEvent> attacks;
        private NativeArray<int> periodicCounts, totalHitCount;
        private NativeArray<AiecsDisplayRecord> display;
        private NativeArray<AiecsWorkCounters> work;
        private NativeArray<int> statistics;
        private NativeArray<float3> groups;
        private NativeArray<float2> groupAnchors;
        private NativeArray<float> groupNearest;
        private NativeParallelHashMap<int2, int> density;
        private NativeList<AiecsHitEvent> pendingInputs, inputs, hits, externalHits;
        private NativeList<AiecsDamageResult> results;
        private NativeList<AiecsDeathEvent> deaths;
        private NativeParallelHashMap<Entity, int2> hitRanges;
        private NativeQueue<AiecsHitEvent> abilityHits;
        private JobHandle pending;
        private bool disposed;
        private float maximumBodyExtent; // 单次世界生命周期内只扩张，覆盖动态玩家体型与偏心形状。
        public EntityManager Entities => world.EntityManager;
        public CombatClock Clock { get; private set; }
        public NativeArray<AiecsDisplayRecord> Display => display;
        public NativeArray<int> Statistics => statistics;
        public NativeArray<float3> GroupPositions => groups;
        public NativeArray<AiecsDamageResult> DamageResults => results.AsArray();
        public NativeArray<AiecsDeathEvent> DeathEvents => deaths.AsArray();
        public NativeArray<AiecsHitEvent> ExternalHits => externalHits.AsArray();
        public AiecsSpatialView Spatial { get { Complete(); return spatial.View; } }
        public CombatDifficulty Difficulty { get; set; }
        #endregion

        #region 创建与外部输入
        /// <summary>冻结当前内容编译结果；组数是战略位置数量，不能按单位数量扩展。</summary>
        public AiecsSimulation(AiecsDefinition[] actorDefinitions, AiecsBuffDefinition[] buffDefinitions,
            FixedString128Bytes[] factionIds, byte[] factionRelations, int groupCount, float bodyExtent, double startTime)
        {
            if (groupCount < 1 || groupCount > 32) throw new ArgumentOutOfRangeException(nameof(groupCount));
            if (factionRelations.Length != factionIds.Length * factionIds.Length) throw new ArgumentException("阵营矩阵尺寸不匹配");
            world = new World("AIECS 正式模拟");
            scheduler = world.GetOrCreateSystemManaged<AiecsJobSchedulerSystem>();
            var types = new ComponentType[] { typeof(AiecsIdentity), typeof(AiecsBody), typeof(AiecsVital), typeof(AiecsDefense),
                typeof(AiecsAnatomy), typeof(AiecsBrain), typeof(AiecsBehaviorProposal), typeof(AiecsBehaviorIntent),
                typeof(AiecsLocalMotion), typeof(AiecsAttackState), typeof(AiecsStatus), typeof(AiecsWorkCounters),
                typeof(AiecsFlowAgent), typeof(AiecsBuff), typeof(AiecsPeriodicHit) };
            archetype = Entities.CreateArchetype(types); query = Entities.CreateEntityQuery(types);
            definitions = new NativeArray<AiecsDefinition>(actorDefinitions, Allocator.Persistent);
            buffs = new NativeArray<AiecsBuffDefinition>(buffDefinitions, Allocator.Persistent);
            factions = new NativeArray<FixedString128Bytes>(factionIds, Allocator.Persistent);
            relations = new NativeArray<byte>(factionRelations, Allocator.Persistent);
            goals = new NativeArray<FlowGoalHandle>(groupCount, Allocator.Persistent);
            groups = new NativeArray<float3>(groupCount, Allocator.Persistent);
            groupAnchors = new NativeArray<float2>(groupCount, Allocator.Persistent);
            groupNearest = new NativeArray<float>(groupCount, Allocator.Persistent);
            statistics = new NativeArray<int>((int)AiecsStatistic.Count, Allocator.Persistent);
            totalHitCount = new NativeArray<int>(1, Allocator.Persistent);
            pendingInputs = new NativeList<AiecsHitEvent>(16, Allocator.Persistent);
            inputs = new NativeList<AiecsHitEvent>(16, Allocator.Persistent);
            hits = new NativeList<AiecsHitEvent>(16, Allocator.Persistent);
            externalHits = new NativeList<AiecsHitEvent>(16, Allocator.Persistent);
            results = new NativeList<AiecsDamageResult>(16, Allocator.Persistent);
            deaths = new NativeList<AiecsDeathEvent>(16, Allocator.Persistent);
            hitRanges = new NativeParallelHashMap<Entity, int2>(16, Allocator.Persistent);
            abilityHits = new NativeQueue<AiecsHitEvent>(Allocator.Persistent);
            density = new NativeParallelHashMap<int2, int>(16, Allocator.Persistent);
            maximumBodyExtent = bodyExtent;
            Clock = new CombatClock { Time = startTime };
        }

        /// <summary>显式注册一个批处理能力阶段；重复实例不会重复执行。</summary>
        public void RegisterStage(IAiecsSimulationStage stage) { if (stage != null && !stages.Contains(stage)) stages.Add(stage); }

        /// <summary>移除已注册能力前完成其任务，避免资源提前释放。</summary>
        public void UnregisterStage(IAiecsSimulationStage stage) { Complete(); stages.Remove(stage); }

        /// <summary>创建真实 Entity 并初始化各层实例数据；不创建任何 GameObject 代理。</summary>
        public Entity Spawn(AiecsActorTemplate template, CombatIdentity key, float2 position, int group, bool external = false, bool player = false)
        {
            Complete();
            if ((uint)group >= (uint)goals.Length || (uint)template.Definition >= (uint)definitions.Length) throw new ArgumentOutOfRangeException(nameof(group));
            IncludeBodyExtent(template.Body);
            Entity entity = Entities.CreateEntity(archetype);
            AiecsDefinition definition = definitions[template.Definition];
            uint seed = math.hash(new uint3((uint)key.Value, key.Generation, key.World)); if (seed == 0) seed = 1;
            var random = new Unity.Mathematics.Random(seed);
            Entities.SetComponentData(entity, new AiecsIdentity { Key = key, Definition = template.Definition, Faction = template.Faction,
                Group = group, External = (byte)(external ? 1 : 0), Player = (byte)(player ? 1 : 0), HasBlood = template.HasBlood, Targetable = 1 });
            Entities.SetComponentData(entity, template.Body); Entities.SetComponentData(entity, template.Vital);
            Entities.SetComponentData(entity, template.Defense); Entities.SetComponentData(entity, template.Anatomy);
            Entities.SetComponentData(entity, new AiecsStatus { MoveMultiplier = 1f, Water = 100f });
            Entities.SetComponentData(entity, new AiecsBrain { RandomState = random.state, Behavior = (int)AiecsBehavior.Idle,
                NextPerception = Clock.Time + random.NextFloat() * definition.PerceptionPeriod,
                NextDecision = Clock.Time + random.NextFloat() * definition.DecisionPeriod,
                BehaviorUntil = Clock.Time + definition.IdleSeconds * random.NextFloat(0.2f, 1f), EnteredAt = Clock.Time });
            Entities.SetComponentData(entity, new AiecsFlowAgent { Position = position, Radius = template.Body.Radius,
                Speed = definition.MoveSpeed, StopDistance = 0.15f, Mode = AiecsMoveMode.Hold });
            return entity;
        }

        /// <summary>每个外部对象每个模拟 Tick 刷新一次；核心系统不反向访问原对象。</summary>
        public void UpdateExternal(Entity entity, CombatIdentity key, float2 position, AiecsBody body, float hp, float maxHp, int faction, bool targetable = true)
        {
            Complete(); if (!Entities.Exists(entity)) return;
            var identity = Entities.GetComponentData<AiecsIdentity>(entity);
            if (identity.Key != key || identity.External == 0) return;
            IncludeBodyExtent(body);
            identity.Faction = faction; identity.Targetable = (byte)(targetable ? 1 : 0); Entities.SetComponentData(entity, identity);
            var agent = Entities.GetComponentData<AiecsFlowAgent>(entity); agent.Position = position; agent.Radius = body.Radius; agent.Mode = AiecsMoveMode.Hold;
            Entities.SetComponentData(entity, agent); Entities.SetComponentData(entity, body);
            var vital = Entities.GetComponentData<AiecsVital>(entity); vital.Hp = hp; vital.MaxHp = maxHp; vital.Dead = (byte)(hp <= 0f ? 1 : 0);
            Entities.SetComponentData(entity, vital);
        }

        /// <summary>粗筛扩张必须覆盖实际形状偏移与缩放，外部玩家变大时不能漏掉其中心所在桶。</summary>
        private void IncludeBodyExtent(AiecsBody body)
        {
            maximumBodyExtent = math.max(maximumBodyExtent, math.cmax(math.abs(body.Perception.Center) + body.Perception.Extents));
            maximumBodyExtent = math.max(maximumBodyExtent, math.cmax(math.abs(body.Hit.Center) + body.Hit.Extents));
        }

        /// <summary>移除外部代理或到期尸体；Entity 版本使旧命中和旧锁定自动失效。</summary>
        public void Despawn(Entity entity) { Complete(); if (Entities.Exists(entity)) Entities.DestroyEntity(entity); }

        /// <summary>同一批到期尸体一次性结构删除，避免逐对象重复更新查询结构。</summary>
        public void DespawnBatch(NativeArray<Entity> entities) { Complete(); if (entities.Length > 0) Entities.DestroyEntity(entities); }

        /// <summary>少量共享目标由玩家或编队中心更新，调用方不得为每个单位设置独立槽位。</summary>
        public void SetGroupGoal(int group, FlowGoalHandle handle) { Complete(); goals[group] = handle; }

        /// <summary>阵营规则改变时更新冻结矩阵，正常 Tick 不重建关系。</summary>
        public void SetRelations(byte[] matrix) { Complete(); if (matrix.Length != relations.Length) throw new ArgumentException("阵营矩阵尺寸不匹配"); relations.CopyFrom(matrix); }

        /// <summary>新阵营注册属于冷路径；保留已有索引顺序并整体更换小型关系矩阵。</summary>
        public void SetFactions(FixedString128Bytes[] ids, byte[] matrix)
        {
            Complete();
            if (matrix.Length != ids.Length * ids.Length) throw new ArgumentException("阵营矩阵尺寸不匹配");
            factions.Dispose(); relations.Dispose();
            factions = new NativeArray<FixedString128Bytes>(ids, Allocator.Persistent);
            relations = new NativeArray<byte>(matrix, Allocator.Persistent);
            spatial.SetRelations(relations, ids.Length);
        }

        /// <summary>仅武器实际窗口/Pulse 提交输入，未来时间的输入保留到对应模拟 Tick。</summary>
        public void SubmitHit(AiecsHitEvent hit) { Complete(); pendingInputs.Add(hit); }

        /// <summary>桥接前检查当前 Buff 是否完整编译，防止丢弃未迁移能力。</summary>
        public bool SupportsBuff(FixedString128Bytes id)
        {
            for (int i = 0; i < buffs.Length; i++) if (buffs[i].Id.Equals(id)) return true;
            return false;
        }
        #endregion

        #region 批次执行
        /// <summary>调度完整垂直切片；仅在阶段需要发布/重建空间表或交给 Bridge 时同步。</summary>
        public void Step(FlowNavigationCache navigation, AiecsLosView los, float deltaTime, double time)
        {
            Complete(); Clock = new CombatClock { Tick = Clock.Tick + 1, Time = time, DeltaTime = deltaTime };
            int count = query.CalculateEntityCount(); Resize(count);
            inputs.Clear();
            for (int i = pendingInputs.Length - 1; i >= 0; i--)
            {
                if (pendingInputs[i].Context.Clock.Time > time) continue;
                var input = pendingInputs[i]; input.Context.Clock.Tick = Clock.Tick; input.Context.Clock.DeltaTime = deltaTime;
                inputs.Add(input); pendingInputs.RemoveAtSwapBack(i);
            }
            int capacity = math.max(16, count * 2 + inputs.Length);
            if (hits.Capacity < capacity) hits.Capacity = capacity;
            if (results.Capacity < capacity) results.Capacity = capacity;
            if (externalHits.Capacity < capacity) externalHits.Capacity = capacity;
            if (hitRanges.Capacity < capacity) hitRanges.Capacity = capacity;
            results.Clear(); externalHits.Clear(); deaths.Clear();
            FlowNavigationSnapshot view = navigation.Read();
            pending = spatial.Build(scheduler, query, view.Domain, relations, factions.Length, maximumBodyExtent);
            var frame = new AiecsFrame { Clock = Clock, Spatial = spatial.View, Los = los, Navigation = view, Definitions = definitions,
                Factions = factions, HitEvents = abilityHits.AsParallelWriter() };
            pending = scheduler.ScheduleParallel(new AiecsPerceptionSystem { Definitions = definitions, Spatial = frame.Spatial,
                Los = los, Time = time, Tick = (uint)Clock.Tick }, query, pending);
            pending = ScheduleStages(AiecsStagePhase.BeforeDecision, frame, pending);
            pending = scheduler.ScheduleParallel(new AiecsDecisionSystem { Definitions = definitions, Spatial = frame.Spatial,
                Time = time }, query, pending);
            pending = scheduler.ScheduleParallel(new AiecsBehaviorSystem { Definitions = definitions, Spatial = frame.Spatial,
                Navigation = view, GroupGoals = goals, Time = time }, query, pending);
            pending = ScheduleStages(AiecsStagePhase.BeforeMovement, frame, pending);
            spatial.RegisterReader(pending); navigation.RegisterReader(pending);
            pending = movement.Schedule(scheduler, query, navigation, deltaTime, (uint)Clock.Tick, dependency: pending);
            // 命中必须读取本 Tick 移动后的坐标，不能使用感知开始前的旧目标位置。
            pending = spatial.Build(scheduler, query, view.Domain, relations, factions.Length, maximumBodyExtent, pending);
            frame.Spatial = spatial.View;
            pending = ScheduleStages(AiecsStagePhase.BeforeAttack, frame, pending);
            pending = scheduler.ScheduleParallel(new AiecsAttackSystem { Definitions = definitions, Factions = factions,
                Spatial = frame.Spatial, Los = los, Clock = Clock, Hits = attacks }, query, pending);
            pending = scheduler.ScheduleParallel(new AiecsBuffSystem { Definitions = buffs, Clock = Clock,
                HitCounts = periodicCounts }, query, pending);
            pending = ScheduleStages(AiecsStagePhase.BeforeSettlement, frame, pending);
            pending = new AiecsRouteHitsJob { Attacks = attacks, PeriodicCounts = periodicCounts, TotalCount = totalHitCount,
                External = inputs.AsArray(), Abilities = abilityHits, Hits = hits, Ranges = hitRanges }.Schedule(pending);
            // 按实际命中量预留并行输出容量；新增 Buff 不需要给每个 AI 预留最大 Buff 数的事件数组。
            pending.Complete();
            if (results.Capacity < totalHitCount[0]) results.Capacity = totalHitCount[0];
            if (externalHits.Capacity < hits.Length) externalHits.Capacity = hits.Length;
            pending = scheduler.ScheduleParallel(new AiecsDamageSettlementSystem { Hits = hits.AsArray(), Ranges = hitRanges,
                Factions = factions, Definitions = definitions, BuffDefinitions = buffs, Spatial = frame.Spatial, Difficulty = Difficulty,
                Results = results.AsParallelWriter(), ExternalHits = externalHits.AsParallelWriter() }, query, pending);
            pending = ScheduleStages(AiecsStagePhase.AfterDamage, frame, pending);
            pending = scheduler.ScheduleParallel(new AiecsDeathSystem { Clock = Clock, Events = deaths.AsParallelWriter() }, query, pending);
            pending = scheduler.ScheduleParallel(new AiecsCaptureStateJob { Clock = Clock, Display = display, Work = work }, query, pending);
            pending = new AiecsStatisticsJob { Display = display, Work = work, Statistics = statistics, Groups = groups,
                Anchors = groupAnchors, Nearest = groupNearest, Navigation = view, Density = density, Domain = view.Domain }.Schedule(pending);
            spatial.RegisterReader(pending); navigation.RegisterReader(pending);
            Complete();
        }

        /// <summary>依次接入少量能力系统，保留调用方的依赖链。</summary>
        private JobHandle ScheduleStages(AiecsStagePhase phase, AiecsFrame frame, JobHandle dependency)
        {
            for (int i = 0; i < stages.Count; i++) if (stages[i].Phase == phase) dependency = stages[i].Schedule(Entities, query, frame, dependency);
            return dependency;
        }

        /// <summary>只有结构变化时调整连续数组；普通 Tick 复用全部 Native 容器。</summary>
        private void Resize(int count)
        {
            if (display.IsCreated && display.Length == count) return;
            if (attacks.IsCreated) attacks.Dispose(); if (periodicCounts.IsCreated) periodicCounts.Dispose();
            if (display.IsCreated) display.Dispose(); if (work.IsCreated) work.Dispose();
            attacks = new NativeArray<AiecsHitEvent>(count, Allocator.Persistent);
            periodicCounts = new NativeArray<int>(count, Allocator.Persistent);
            display = new NativeArray<AiecsDisplayRecord>(count, Allocator.Persistent);
            work = new NativeArray<AiecsWorkCounters>(count, Allocator.Persistent);
            int capacity = math.max(16, count);
            if (deaths.Capacity < capacity) deaths.Capacity = capacity;
            if (hitRanges.Capacity < capacity) hitRanges.Capacity = capacity;
            if (density.Capacity < capacity) density.Capacity = capacity;
        }

        /// <summary>完成本世界全部任务，允许 Bridge 读取输出或更新外部代理。</summary>
        public void Complete() { pending.Complete(); spatial.Complete(); }

        /// <summary>先完成读取者再释放所有容器与 World，外部共享导航由其原所有者负责。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Complete(); movement.Dispose(); spatial.Dispose();
            // PlayMode/domain teardown can dispose custom Worlds before MonoBehaviour owners receive OnDestroy.
            // In that path the EntityQuery's manager is already gone, so disposing the stale query would dereference
            // Unity.Entities' destroyed query registry. Native resources below are owned by this simulation and still
            // need deterministic cleanup regardless of the World state.
            bool disposeWorld = world != null && world.IsCreated;
            if (disposeWorld) query.Dispose();
            definitions.Dispose(); buffs.Dispose(); factions.Dispose(); relations.Dispose(); goals.Dispose();
            groups.Dispose(); groupAnchors.Dispose(); groupNearest.Dispose(); statistics.Dispose(); totalHitCount.Dispose();
            if (attacks.IsCreated) attacks.Dispose(); if (periodicCounts.IsCreated) periodicCounts.Dispose();
            if (display.IsCreated) display.Dispose(); if (work.IsCreated) work.Dispose();
            pendingInputs.Dispose(); inputs.Dispose(); hits.Dispose(); externalHits.Dispose(); results.Dispose(); deaths.Dispose();
            hitRanges.Dispose(); abilityHits.Dispose(); density.Dispose();
            if (disposeWorld) world.Dispose();
        }
        #endregion
    }
}
