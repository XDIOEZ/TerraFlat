using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Navigation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>
    /// 正式世界 AIECS 生态宿主：把既有 MonsterSpawnerManager 的生成请求转换为纯 Entity，
    /// 统一驱动 60Hz 模拟、批量表现和增量数量统计；每帧最多检查 256 个死亡居民。
    /// 该组件不重新实现生态调度规则。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("FlatWorld/AIECS/正式生态宿主")]
    public sealed partial class AiecsEcologyRuntimeHost : MonoBehaviour, IAiEcologyBackend, IWorldEntityRuntimeModule
    {
        #region 配置与记录

        private const int BaseSimulationHz = 60; // 一级范围的权威模拟频率。
        private const int MaxSimulationStepsPerFrame = 2; // 低帧率时只有限追帧，优先保护渲染帧。
        private const int MaxBacklogSteps = 3; // 只保留少量时间债务，避免一次卡顿演变成连续多帧追赶尖峰。
        private const int MaxActorSweepPerFrame = 256; // 死亡清理每帧最多检查的 ECS 居民数。
        private static AiecsEcologyRuntimeHost active;
        [SerializeField] private AiecsAnimationCatalog _catalog;

        private sealed class EcologyActor
        {
            public Entity Entity;
            public CombatIdentity Identity;
            public SpawnerConfig Config;
            public string SpeciesId;
            public int ActorGuid;
            public int AdvanceTargetGuid;
            public AiecsActorMirrorProxy Proxy;
            public bool CountedAlive;
        }

        private readonly Dictionary<string, int> _templateBySpecies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SpawnerConfig> _configBySpecies = new(StringComparer.Ordinal);
        private readonly Dictionary<CombatIdentity, EcologyActor> _actors = new();
        private readonly Dictionary<int, EcologyActor> _actorsByGuid = new();
        private sealed class AdvanceGoal
        {
            public FlowGoalHandle Handle;
            public Vector2 Position;
            public int References;
        }
        private readonly Dictionary<int, AdvanceGoal> _advanceGoals = new();
        private readonly Dictionary<SpawnerConfig, int> _aliveByGroup = new();
        private readonly Dictionary<string, int> _aliveBySpecies = new(StringComparer.Ordinal);
        private int _populationLimitedCount;
        private readonly List<EcologyActor> _actorSweep = new(256);
        private int _actorSweepCursor;
        private readonly List<string> _actorIds = new();
        private readonly List<string> _actorFactions = new();
        private readonly List<bool> _fleeFromHostiles = new();

        private AiecsGameplayBridge _bridge;
        private AiecsWorldRenderer _renderer;
        private Player _player;
        private Camera _camera;
        private double _simulationTime;
        private bool _worldPrepared;
        private bool _startFailed;
        private bool _ownsBackendRegistration;
        private bool _disposingSimulation;

        public static AiecsEcologyRuntimeHost Active => active;
        public bool IsReady => !_disposingSimulation && _bridge?.Simulation?.IsCreated == true;
        public string RuntimeModuleId => "entity.ai";

        #endregion

        #region 生命周期

        private void Awake()
        {
            // WorldManager 跨场景常驻；回主菜单时场景副本会在同一帧短暂 Awake。
            // 副本只保持禁用，不能抢占仍存活的正式生态后端，也不能制造退出流程异常。
            _ownsBackendRegistration = AiRuntimeBackendService.TryRegisterEcology(this);
            if (!_ownsBackendRegistration)
            {
                enabled = false;
                return;
            }

            active = this;
        }

        private void Update()
        {
            if (!AiRuntimeBackendService.UseEntities)
            {
                // 停用只保存并释放 AI 自身的状态与绘制，不关闭树木和作物共用的 World。
                if (_bridge != null)
                {
                    CaptureResidents(SaveDataMgr.Instance?.SaveData?.MonsterSpawnerData);
                    DisposeSimulation();
                }
                return;
            }
            if (!_worldPrepared)
                return;

            // GM 开发场景与正式生态共享同一套玩家/导航资源，但任何时刻只允许一个模拟真正运行。
            if (AiecsPlayground.Active?.HasActiveScenario == true)
            {
                WorldEntityRuntime.DevelopmentSuspended = true;
                if (_bridge != null)
                    DisposeSimulation();
                return;
            }
            WorldEntityRuntime.DevelopmentSuspended = false;

            if (_bridge == null)
                TryStartSimulation();
            else if (_player == null)
            {
                Player currentPlayer = ItemMgr.Instance?.User_Player;
                if (currentPlayer == null)
                    return;
                if (!_bridge.IsCurrentWorld(currentPlayer))
                {
                    DisposeSimulation();
                    _startFailed = false;
                    return;
                }
                _player = currentPlayer;
            }
            if (_bridge == null || _player == null)
                return;

            if (!_bridge.IsCurrentWorld(_player))
            {
                DisposeSimulation();
                _startFailed = false;
                return;
            }

        }

        /// <summary>动物能力由统一实体入口驱动；Update 只准备依赖，不再另起一套玩法 Tick。</summary>
        public void TickEntities(float deltaTime)
        {
            if (!AiRuntimeBackendService.UseEntities || deltaTime <= 0f ||
                !_worldPrepared || !IsReady || _player == null ||
                AiecsPlayground.Active?.HasActiveScenario == true || !_bridge.IsCurrentWorld(_player))
                return;

            float step = 1f / BaseSimulationHz;
            double now = Time.timeAsDouble;
            double maxBacklog = step * MaxBacklogSteps;
            if (now - _simulationTime > maxBacklog)
                _simulationTime = now - maxBacklog;

            int budget = MaxSimulationStepsPerFrame;
            try
            {
                _bridge.EnsurePlayer(_player);
                _bridge.Simulation.Complete();
                RefreshAdvanceGoals();
                while (_simulationTime + step <= now && budget-- > 0)
                {
                    _simulationTime += step;
                    Vector3 playerPosition = _player.transform.position;
                    int near = SimulationRangePreferences.NearRadius;
                    int middle = SimulationRangePreferences.MiddleRadius;
                    int far = SimulationRangePreferences.FarRadius;
                    _bridge.Step(step, _simulationTime, new AiecsSimulationRange
                    {
                        Enabled = 1,
                        PlayerPosition = new float2(playerPosition.x, playerPosition.y),
                        NearSquared = near * near,
                        MiddleSquared = middle * middle,
                        FarSquared = far * far
                    });
                }

                PruneDestroyedActors();
                RefreshMirrors();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DisposeSimulation();
                _startFailed = true;
            }
        }

        public void CompleteEntityJobs() => _bridge?.Simulation?.Complete();
        public void ReleaseEntities() => DisposeSimulation();

        private void LateUpdate()
        {
            if (!AiRuntimeBackendService.UseEntities || !IsReady || _renderer == null || _player == null)
                return;

            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;
            if (_camera == null)
                return;

            ActorShadowManager shadowManager = ActorShadowManager.GetInstance();
            _renderer.ShadowOpacity = shadowManager != null ? shadowManager.GetShadowOpacity(_player.gameObject.scene) : 0.4f;
            _renderer.Draw(_bridge.Simulation, _camera, _bridge.Navigation.Read().Domain,
                IsRuntimeEntityPresentationReady);
        }

        /// <summary>由 Gameplay 层查询 ChunkMgr，避免 Presentation 程序集反向依赖 GamePlay。</summary>
        private static bool IsRuntimeEntityPresentationReady(Vector2 position)
        {
            ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
            return chunkManager == null || chunkManager.IsRuntimeEntityPresentationReady(position);
        }

        /// <summary>脚本重载不保证调用 OnDestroy，停用阶段就释放自有模拟与原生绘制资源。</summary>
        private void OnDisable() => DisposeSimulation();

        private void OnDestroy()
        {
            ResetWorld();
            if (ReferenceEquals(active, this))
                active = null;
            if (_ownsBackendRegistration)
                AiRuntimeBackendService.UnregisterEcology(this);
        }

        #endregion

        #region 世界准备

        /// <summary>冻结本世界会用到的 Actor 目录；不支持的定义明确拒绝，不回退旧 AI。</summary>
        public bool PrepareWorld(IReadOnlyList<SpawnerConfig> configs)
        {
            ResetWorld();
            _worldPrepared = true;

            if (_catalog == null || _catalog.Material == null)
            {
                Debug.LogError("[AIECS] 正式生态宿主缺少动画目录或批量材质。", this);
                _startFailed = true;
                return false;
            }

            if (configs == null || GameRes.Instance == null)
                return false;
            var ownerBySpecies = new Dictionary<string, SpawnerConfig>(StringComparer.OrdinalIgnoreCase);
            for (int configIndex = 0; configIndex < configs.Count; configIndex++)
            {
                SpawnerConfig config = configs[configIndex];
                if (config?.SpawnEntries == null)
                    continue;
                for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
                {
                    SpawnerConfig.SpawnEntry entry = config.SpawnEntries[entryIndex];
                    string speciesId = entry?.PrefabName?.Trim();
                    if (!string.IsNullOrEmpty(speciesId) && AiRuntimeBackendService.UsesEntities(entry))
                        ownerBySpecies.TryAdd(speciesId, config);
                }
            }

            var species = new List<string>(GameRes.Instance.ActorDefinitions.Keys);
            species.Sort(StringComparer.Ordinal);
            foreach (string speciesId in species)
            {
                if (!AiRuntimeBackendService.UsesEntities(speciesId)) continue;
                ownerBySpecies.TryGetValue(speciesId, out SpawnerConfig config);
                ItemData definitionData = GameRes.Instance.ActorDefinitions[speciesId].CreateItemData();
                bool fleeFromHostiles = config?.EcologyGroup == SpawnerEcologyGroup.Animals ||
                    definitionData.Tags?.Contains("Prey") == true;
                if (!CatalogContains(speciesId))
                {
                    Debug.LogError($"[AIECS] 物种 {speciesId} 缺少动画目录，正式世界不能只迁移部分生物。", this);
                    _startFailed = true;
                    return false;
                }
                if (!AiecsDefinitionCompiler.TryValidate(speciesId, fleeFromHostiles, out string reason))
                {
                    Debug.LogError($"[AIECS] 物种 {speciesId} 无法编译：{reason}", this);
                    _startFailed = true;
                    return false;
                }
                int templateIndex = _actorIds.Count;
                _templateBySpecies.Add(speciesId, templateIndex);
                _configBySpecies.Add(speciesId, config);
                _actorIds.Add(speciesId);
                _actorFactions.Add(ResolveFaction(config, speciesId));
                _fleeFromHostiles.Add(fleeFromHostiles);
            }

            if (_actorIds.Count == 0)
            {
                Debug.LogError("[AIECS] 当前生态目录没有可由正式 AIECS 基础切片运行的 Actor。", this);
                _startFailed = true;
                return false;
            }

            LoadResidentSnapshots();
            return true;
        }

        public void ResetWorld()
        {
            DisposeSimulation();
            _templateBySpecies.Clear();
            _configBySpecies.Clear();
            _actorsByGuid.Clear();
            _actorIds.Clear();
            _actorFactions.Clear();
            _fleeFromHostiles.Clear();
            _pendingRestores.Clear();
            _worldPrepared = false;
            _startFailed = false;
            _player = null;
            _camera = null;
        }

        /// <summary>玩家离开当前运行实例时先撤销战斗代理；新玩家会在下一次 Update 重新绑定。</summary>
        public void ReleasePlayer(Player player)
        {
            if (!ReferenceEquals(_player, player))
                return;
            _bridge?.ReleasePlayer(player);
            ReleaseAllMirrors();
            _player = null;
        }

        private void TryStartSimulation()
        {
            // 空闲的 GM 调试入口不阻断正式生态；只有正在运行的开发场景才独占模拟。
            if (_startFailed || !_worldPrepared || AiecsPlayground.Active?.HasActiveScenario == true)
                return;

            _player = ItemMgr.Instance?.User_Player;
            WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
            if (_player == null || navigation == null || !navigation.IsNavigationReady)
                return;

            try
            {
                string[] ids = _actorIds.ToArray();
                World entityWorld = WorldEntityRuntime.GetOrCreate(
                    ChunkMgr.ExistingInstance.ResolveWorldAddress(_player.transform.position).DimensionId);
                _bridge = new AiecsGameplayBridge(
                    _player,
                    navigation.GetSharedNavigation(),
                    ids,
                    _actorFactions.ToArray(),
                    0f,
                    _fleeFromHostiles.ToArray(), sharedWorld: entityWorld);
                _renderer = new AiecsWorldRenderer(_catalog, ids, _player.gameObject.scene,
                    AiecsWorldSortingResolver.Resolve());
                RestoreResidentSnapshots();
                _simulationTime = Time.timeAsDouble;
                WorldEntityRuntime.Register(entityWorld, this);
                Debug.Log($"[AIECS] 正式 ECS 生态已启动：{ids.Length} 个 Actor 定义。", this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DisposeSimulation();
                _startFailed = true;
            }
        }

        private void DisposeSimulation()
        {
            if (_disposingSimulation) return;
            _disposingSimulation = true;
            try
            {
                WorldEntityRuntime.Unregister(this);
                if (_bridge != null)
                {
                    TryReleaseSimulationResource(() =>
                    {
                        var snapshots = new List<AiecsResidentSaveData>(_pendingRestores);
                        if (!TryCaptureActiveResidents(snapshots)) return;
                        _pendingRestores.Clear();
                        _pendingRestores.AddRange(snapshots);
                    });
                }

                // 先断开可重入引用，各资源独立清理，快照失败不能让 BRG 和 Native 内存泄漏。
                AiecsGameplayBridge bridge = _bridge;
                AiecsWorldRenderer renderer = _renderer;
                _bridge = null;
                _renderer = null;
                TryReleaseSimulationResource(() => ReleaseAllAdvanceGoals(bridge?.Navigation));
                TryReleaseSimulationResource(DisposeMirrors);
                if (renderer != null) TryReleaseSimulationResource(renderer.Dispose);
                if (bridge != null) TryReleaseSimulationResource(bridge.Dispose);
            }
            finally
            {
                _advanceGoals.Clear();
                _actors.Clear();
                _actorsByGuid.Clear();
                _actorSweep.Clear();
                _actorSweepCursor = 0;
                _aliveByGroup.Clear();
                _aliveBySpecies.Clear();
                _populationLimitedCount = 0;
                _disposingSimulation = false;
            }
        }

        /// <summary>只隔离退出清理步骤，仍记录真实异常，不影响其它资源继续释放。</summary>
        private void TryReleaseSimulationResource(Action release)
        {
            try { release(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        /// <summary>为正常 GameStart 世界提供惰性 GM 调试入口；只创建宿主，不立即创建第二套 ECS 模拟。</summary>
        public AiecsPlayground EnsureDebugPlayground()
        {
            if (AiecsPlayground.Active != null)
                return AiecsPlayground.Active;
            if (!_ownsBackendRegistration || _catalog == null)
                return null;
            return AiecsPlayground.CreateRuntimeGmEntry(_catalog);
        }

        /// <summary>开发场景启动前同步释放正式生态模拟，禁止一帧内同时推进两套 AIECS World。</summary>
        internal void SuspendForDevelopmentScenario(AiecsPlayground owner)
        {
            if (owner == null || AiecsPlayground.Active != owner)
                return;
            WorldEntityRuntime.DevelopmentSuspended = true;
            DisposeSimulation();
            _startFailed = false;
        }

        #endregion

        #region 生态后端

        public bool SupportsSpecies(string speciesId)
        {
            return _worldPrepared && !_startFailed && !string.IsNullOrWhiteSpace(speciesId) &&
                _templateBySpecies.ContainsKey(speciesId);
        }

        public bool TrySpawn(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 position)
        {
            if (!AiRuntimeBackendService.UseEntities || !AiRuntimeBackendService.UsesEntities(entry) || config == null)
                return false;

            if (!TrySpawnSpecies(entry.PrefabName, config, position, 0, out int actorGuid))
                return false;
            ApplyEntryInitialization(actorGuid, entry);
            return true;
        }

        /// <summary>生成规则只给新 Entity 注入初始值，不再触发旧物种 Module.Load。</summary>
        private void ApplyEntryInitialization(int actorGuid, SpawnerConfig.SpawnEntry entry)
        {
            SpawnerConfig.SpawnerNutritionInitialization config = entry?.Initialization?.Nutrition;
            if (config?.Enabled != true || !_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) ||
                !PrepareRead())
                return;
            EntityManager manager = _bridge.Simulation.Entities;
            if (!manager.Exists(actor.Entity) || !manager.HasComponent<AiecsNutrition>(actor.Entity))
                return;
            AiecsNutrition nutrition = manager.GetComponentData<AiecsNutrition>(actor.Entity);
            float minimum = Mathf.Clamp01(config.MinFoodRate);
            float maximum = Mathf.Max(minimum, Mathf.Clamp01(config.MaxFoodRate));
            nutrition.Current = nutrition.Maximum * UnityEngine.Random.Range(minimum, maximum);
            manager.SetComponentData(actor.Entity, nutrition);
        }

        /// <summary>游戏事件同样创建纯 Entity，不实例化旧 Actor GameObject。</summary>
        public bool TrySpawnEvent(string speciesId, Vector3 position)
        {
            return TrySpawnEvent(speciesId, position, out _);
        }

        public bool TrySpawnEvent(string speciesId, Vector3 position, out int actorGuid)
        {
            actorGuid = 0;
            if (!AiRuntimeBackendService.UseEntities ||
                !AiRuntimeBackendService.UsesEntities(speciesId) ||
                string.IsNullOrWhiteSpace(speciesId) ||
                !_configBySpecies.TryGetValue(speciesId, out SpawnerConfig config))
            {
                return false;
            }

            return TrySpawnSpecies(speciesId, config, position, 0, out actorGuid);
        }

        /// <summary>蜂巢等非生态生成器直接创建同一类 ECS Actor，并保留指定的存档 GUID。</summary>
        public bool TrySpawnDirect(string speciesId, Vector3 position, int requestedGuid, out int actorGuid)
        {
            actorGuid = 0;
            if (!AiRuntimeBackendService.UseEntities || !AiRuntimeBackendService.UsesEntities(speciesId) ||
                !_configBySpecies.TryGetValue(speciesId, out SpawnerConfig config))
                return false;
            return TrySpawnSpecies(speciesId, config, position, requestedGuid, out actorGuid);
        }

        public bool TryGetActor(int actorGuid, out Vector3 position, out bool alive)
        {
            position = default;
            alive = false;
            if (!_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) || !PrepareRead())
                return false;
            if (!_bridge.Simulation.Entities.Exists(actor.Entity)) return false;
            float2 value = _bridge.Simulation.Entities.GetComponentData<AiecsFlowAgent>(actor.Entity).Position;
            position = new Vector3(value.x, value.y, 0f);
            alive = _bridge.Simulation.Entities.GetComponentData<AiecsVital>(actor.Entity).Dead == 0;
            return true;
        }

        public bool TryDespawnActor(int actorGuid)
        {
            if (!_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) || _bridge?.Simulation == null)
                return false;
            ReleaseAdvanceGoal(actor);
            for (int i = _boundMirrors.Count - 1; i >= 0; i--)
                if (_boundMirrors[i] == actor.Proxy)
                    ReleaseMirrorAt(i);
            _bridge.Simulation.Despawn(actor.Entity);
            if (actor.CountedAlive)
                AdjustAliveCounts(actor.Config, actor.SpeciesId, -1);
            _actors.Remove(actor.Identity);
            _actorsByGuid.Remove(actorGuid);
            int index = _actorSweep.IndexOf(actor);
            if (index >= 0)
            {
                _actorSweep.RemoveAt(index);
                _actorSweepCursor = Mathf.Min(_actorSweepCursor, _actorSweep.Count);
            }
            return true;
        }

        #region 事件推进命令

        /// <summary>同一事件目标只建立一个共享 Flow 目标，生物只保存纯值命令。</summary>
        public bool TrySetAdvanceCommand(int actorGuid, AIAdvanceCommand command)
        {
            if (command.TargetItemGuid == 0 ||
                !math.all(math.isfinite(new float2(command.TargetPosition.x, command.TargetPosition.y))) ||
                !_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) || !PrepareRead() ||
                !IsAlive(actor, out _) || _bridge.Navigation == null)
                return false;

            if (!_advanceGoals.TryGetValue(command.TargetItemGuid, out AdvanceGoal goal))
            {
                try
                {
                    Vector2 position = command.TargetPosition;
                    goal = new AdvanceGoal
                    {
                        Handle = _bridge.Navigation.CreateGoal(new float2(position.x, position.y)),
                        Position = position
                    };
                    _advanceGoals.Add(command.TargetItemGuid, goal);
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogWarning($"[AIECS] 无法建立事件共享导航目标：{exception.Message}", this);
                    return false;
                }
            }
            if (!_bridge.Navigation.IsValid(goal.Handle))
                return false;
            goal.Position = command.TargetPosition;
            _bridge.Navigation.UpdateGoal(goal.Handle,
                new float2(goal.Position.x, goal.Position.y));
            if (actor.AdvanceTargetGuid != 0 && actor.AdvanceTargetGuid != command.TargetItemGuid)
                ReleaseAdvanceGoal(actor);
            if (actor.AdvanceTargetGuid == 0 || goal.References == 0)
                goal.References++;
            actor.AdvanceTargetGuid = command.TargetItemGuid;
            EntityManager manager = _bridge.Simulation.Entities;
            manager.SetComponentData(actor.Entity, new AiecsAdvanceDirective
            {
                Goal = goal.Handle,
                ArrivalDistance = Mathf.Max(0.05f, command.ArrivalDistance),
                AttackActorsOnRoute = (byte)(command.AttackActorsOnRoute ? 1 : 0),
                Active = 1
            });
            AiecsBrain brain = manager.GetComponentData<AiecsBrain>(actor.Entity);
            brain.NextDecision = 0;
            manager.SetComponentData(actor.Entity, brain);
            return true;
        }

        /// <summary>外部命令取消后立即释放共享目标，让生物恢复能力规则。</summary>
        public bool TryClearAdvanceCommand(int actorGuid)
        {
            if (!_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) || !PrepareRead() ||
                !_bridge.Simulation.Entities.Exists(actor.Entity))
                return false;
            EntityManager manager = _bridge.Simulation.Entities;
            AiecsAdvanceDirective advance = manager.GetComponentData<AiecsAdvanceDirective>(actor.Entity);
            advance.Active = 0;
            manager.SetComponentData(actor.Entity, advance);
            AiecsBrain brain = manager.GetComponentData<AiecsBrain>(actor.Entity);
            brain.NextDecision = 0;
            manager.SetComponentData(actor.Entity, brain);
            ReleaseAdvanceGoal(actor);
            return true;
        }

        private void RefreshAdvanceGoals()
        {
            FlowNavigationCache navigation = _bridge?.Navigation;
            if (navigation == null) return;
            foreach (KeyValuePair<int, AdvanceGoal> pair in _advanceGoals)
            {
                Item target = ItemMgr.Instance?.GetItemByGuid(pair.Key);
                if (target != null && !target.DestructionHandled)
                    pair.Value.Position = target.transform.position;
                if (navigation.IsValid(pair.Value.Handle))
                    navigation.UpdateGoal(pair.Value.Handle,
                        new float2(pair.Value.Position.x, pair.Value.Position.y));
            }
        }

        private void ReleaseAdvanceGoal(EcologyActor actor)
        {
            int targetGuid = actor.AdvanceTargetGuid;
            actor.AdvanceTargetGuid = 0;
            if (targetGuid == 0 || !_advanceGoals.TryGetValue(targetGuid, out AdvanceGoal goal))
                return;
            if (--goal.References > 0) return;
            _bridge?.Navigation?.RemoveGoal(goal.Handle);
            _advanceGoals.Remove(targetGuid);
        }

        private void ReleaseAllAdvanceGoals(FlowNavigationCache navigation)
        {
            foreach (AdvanceGoal goal in _advanceGoals.Values)
                navigation?.RemoveGoal(goal.Handle);
            _advanceGoals.Clear();
        }

        #endregion

        #region 蜂巢成员模块

        /// <summary>蜂巢只注入归属和存档值，后续行动由共享 ECS 能力查询驱动。</summary>
        public bool TryBindHiveActor(int actorGuid, int hiveGuid, Vector2 home, float patrolRadius, AiHiveActorState state)
        {
            if (!TryGetHiveComponents(actorGuid, out Entity entity, out AiecsHiveMember hive, out AiecsNutrition nutrition))
                return false;
            hive.HomeGuid = hiveGuid;
            hive.Home = new float2(home.x, home.y);
            hive.PatrolRadius = Mathf.Max(0f, patrolRadius);
            hive.Alert = Mathf.Clamp01(state.Anger / 10f);
            hive.CarryingHoney = (byte)(state.ReturningHome ? 1 : 0);
            hive.ReturnHome = 0;
            hive.Orphaned = (byte)(state.Orphaned ? 1 : 0);
            nutrition.Current = Mathf.Clamp(state.Satiety, 0f, nutrition.Maximum);
            _bridge.Simulation.Entities.SetComponentData(entity, hive);
            _bridge.Simulation.Entities.SetComponentData(entity, nutrition);
            return true;
        }

        public bool TryGetHiveActorState(int actorGuid, out AiHiveActorState state)
        {
            state = default;
            if (!TryGetHiveComponents(actorGuid, out _, out AiecsHiveMember hive, out AiecsNutrition nutrition))
                return false;
            state = new AiHiveActorState
            {
                Satiety = nutrition.Current,
                Anger = hive.Alert * 10f,
                Angry = hive.Alert >= 1f,
                ReturningHome = hive.CarryingHoney != 0,
                Orphaned = hive.Orphaned != 0
            };
            return true;
        }

        public bool TrySetHiveActorDirective(int actorGuid, bool returnHome, float alert,
            Vector2 defensePosition, bool hasDefenseTarget)
        {
            if (!TryGetHiveComponents(actorGuid, out Entity entity, out AiecsHiveMember hive, out _))
                return false;
            hive.ReturnHome = (byte)(returnHome ? 1 : 0);
            if (hasDefenseTarget)
            {
                hive.Alert = 1f;
                hive.HasDefenseTarget = 1;
                hive.DefenseRemaining = 10f;
                hive.DefensePosition = new float2(defensePosition.x, defensePosition.y);
            }
            else if (hive.HasDefenseTarget == 0)
                hive.Alert = Mathf.Clamp01(alert);
            _bridge.Simulation.Entities.SetComponentData(entity, hive);
            return true;
        }

        public bool TryReleaseHiveActor(int actorGuid, Vector2 formerHome,
            Vector2 defensePosition, bool hasDefenseTarget)
        {
            if (!TryGetHiveComponents(actorGuid, out Entity entity, out AiecsHiveMember hive, out _))
                return false;
            hive.HomeGuid = 0;
            hive.Home = new float2(formerHome.x, formerHome.y);
            hive.Orphaned = 1;
            hive.ReturnHome = 0;
            hive.HasDefenseTarget = (byte)(hasDefenseTarget ? 1 : 0);
            hive.DefenseRemaining = hasDefenseTarget ? 10f : 0f;
            hive.DefensePosition = new float2(defensePosition.x, defensePosition.y);
            _bridge.Simulation.Entities.SetComponentData(entity, hive);
            return true;
        }

        public bool TryCollectHiveHoney(int actorGuid, Vector2 home, float arrivalRadius,
            float minimumSatiety, float contributionCost)
        {
            if (!TryGetHiveComponents(actorGuid, out Entity entity, out AiecsHiveMember hive,
                    out AiecsNutrition nutrition) || hive.CarryingHoney == 0 ||
                !_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) ||
                !IsAlive(actor, out float2 position) ||
                WorldTopologyRuntime.SqrDistance(new Vector2(position.x, position.y), home) >
                    arrivalRadius * arrivalRadius)
                return false;
            hive.CarryingHoney = 0;
            _bridge.Simulation.Entities.SetComponentData(entity, hive);
            if (nutrition.Current <= minimumSatiety || nutrition.Current < contributionCost)
                return false;
            nutrition.Current -= contributionCost;
            _bridge.Simulation.Entities.SetComponentData(entity, nutrition);
            return true;
        }

        public bool TryGiveHiveHoney(int actorGuid, Vector2 home, float arrivalRadius,
            float feedBelow, float mealGain)
        {
            if (!TryGetHiveComponents(actorGuid, out Entity entity, out _, out AiecsNutrition nutrition) ||
                nutrition.Current >= feedBelow ||
                !_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) ||
                !IsAlive(actor, out float2 position) ||
                WorldTopologyRuntime.SqrDistance(new Vector2(position.x, position.y), home) >
                    arrivalRadius * arrivalRadius)
                return false;
            nutrition.Current = Mathf.Min(nutrition.Maximum, nutrition.Current + mealGain);
            _bridge.Simulation.Entities.SetComponentData(entity, nutrition);
            return true;
        }

        private bool TryGetHiveComponents(int actorGuid, out Entity entity, out AiecsHiveMember hive,
            out AiecsNutrition nutrition)
        {
            entity = default;
            hive = default;
            nutrition = default;
            if (!_actorsByGuid.TryGetValue(actorGuid, out EcologyActor actor) || !PrepareRead())
                return false;
            entity = actor.Entity;
            EntityManager manager = _bridge.Simulation.Entities;
            if (!manager.Exists(entity) || !manager.HasComponent<AiecsHiveMember>(entity) ||
                !manager.HasComponent<AiecsNutrition>(entity) ||
                manager.GetComponentData<AiecsVital>(entity).Dead != 0)
                return false;
            hive = manager.GetComponentData<AiecsHiveMember>(entity);
            nutrition = manager.GetComponentData<AiecsNutrition>(entity);
            return true;
        }

        #endregion

        public int GetGroupCount(SpawnerConfig config)
        {
            if (config == null)
                return 0;
            return _aliveByGroup.TryGetValue(config, out int count) ? count : 0;
        }

        /// <summary>当前装载的 ECS 居民数量；出生硬预算读取不触发全表扫描。</summary>
        public int ResidentCount => _actors.Count;

        public int GetSpeciesCount(string speciesId)
        {
            if (string.IsNullOrWhiteSpace(speciesId))
                return 0;
            return _aliveBySpecies.TryGetValue(speciesId, out int count) ? count : 0;
        }

        public int PopulationLimitedCount => _populationLimitedCount;

        public int CountGroupWithinRadius(SpawnerConfig config, Vector3 center, float radiusSqr)
        {
            if (config == null || !PrepareRead())
                return 0;

            int count = 0;
            foreach (EcologyActor actor in _actors.Values)
            {
                if (!ReferenceEquals(actor.Config, config) || !IsAlive(actor, out float2 position))
                    continue;

                if (WorldTopologyRuntime.SqrDistance(new Vector3(position.x, position.y), center) <= radiusSqr)
                    count++;
            }
            return count;
        }


        #endregion

        #region 查询辅助

        private bool TrySpawnSpecies(string speciesId, SpawnerConfig config, Vector3 position)
        {
            return TrySpawnSpecies(speciesId, config, position, 0, out _);
        }

        private bool TrySpawnSpecies(string speciesId, SpawnerConfig config, Vector3 position,
            int requestedGuid, out int actorGuid)
        {
            actorGuid = 0;
            if (_bridge == null)
                TryStartSimulation();
            if (_bridge == null || !_templateBySpecies.TryGetValue(speciesId, out int templateIndex))
                return false;

            if (requestedGuid != 0 && (_actorsByGuid.ContainsKey(requestedGuid) ||
                ItemMgr.Instance?.GetItemByGuid(requestedGuid) != null))
                return false;

            if (!_bridge.Spawn(templateIndex, (Vector2)position, 1f, out Entity entity, out CombatIdentity identity))
                return false;

            actorGuid = requestedGuid;
            if (actorGuid == 0)
            {
                do { actorGuid = ItemMgr.Instance?.GenerateGuid() ?? Guid.NewGuid().GetHashCode(); }
                while (actorGuid == 0 || _actorsByGuid.ContainsKey(actorGuid) ||
                    ItemMgr.Instance?.GetItemByGuid(actorGuid) != null);
            }

            var actor = new EcologyActor
            {
                Entity = entity,
                Identity = identity,
                Config = config,
                SpeciesId = speciesId,
                ActorGuid = actorGuid,
                CountedAlive = true
            };
            _actors[identity] = actor;
            _actorsByGuid[actorGuid] = actor;
            _actorSweep.Add(actor);
            AdjustAliveCounts(config, speciesId, 1);
            return true;
        }

        private bool PrepareRead()
        {
            if (!IsReady)
                return false;
            _bridge.Simulation.Complete();
            return true;
        }

        private bool IsAlive(EcologyActor actor, out float2 position)
        {
            position = default;
            if (!IsReady || actor == null || !_bridge.Simulation.Entities.Exists(actor.Entity))
                return false;

            AiecsVital vital = _bridge.Simulation.Entities.GetComponentData<AiecsVital>(actor.Entity);
            if (vital.Dead != 0)
                return false;

            position = _bridge.Simulation.Entities.GetComponentData<AiecsFlowAgent>(actor.Entity).Position;
            return true;
        }

        private void PruneDestroyedActors()
        {
            if (_actorSweep.Count == 0 || !PrepareRead())
                return;

            int checks = Mathf.Min(MaxActorSweepPerFrame, _actorSweep.Count);
            for (int i = 0; i < checks && _actorSweep.Count > 0; i++)
            {
                if (_actorSweepCursor >= _actorSweep.Count)
                    _actorSweepCursor = 0;
                EcologyActor actor = _actorSweep[_actorSweepCursor];
                if (!_bridge.Simulation.Entities.Exists(actor.Entity))
                {
                    ReleaseAdvanceGoal(actor);
                    if (actor.CountedAlive)
                        AdjustAliveCounts(actor.Config, actor.SpeciesId, -1);
                    _actors.Remove(actor.Identity);
                    _actorsByGuid.Remove(actor.ActorGuid);
                    int lastIndex = _actorSweep.Count - 1;
                    _actorSweep[_actorSweepCursor] = _actorSweep[lastIndex];
                    _actorSweep.RemoveAt(lastIndex);
                    continue;
                }
                if (actor.CountedAlive &&
                    _bridge.Simulation.Entities.GetComponentData<AiecsVital>(actor.Entity).Dead != 0)
                {
                    ReleaseAdvanceGoal(actor);
                    actor.CountedAlive = false;
                    AdjustAliveCounts(actor.Config, actor.SpeciesId, -1);
                }
                if (actor.AdvanceTargetGuid != 0 &&
                    _bridge.Simulation.Entities.GetComponentData<AiecsAdvanceDirective>(actor.Entity).Active == 0)
                    ReleaseAdvanceGoal(actor);
                _actorSweepCursor++;
            }
        }

        /// <summary>出生和死亡在同一注册表维护数量，生态查询不再完成 Job 后重扫全体 Entity。</summary>
        private void AdjustAliveCounts(SpawnerConfig config, string speciesId, int delta)
        {
            if (config != null)
            {
                _aliveByGroup.TryGetValue(config, out int groupCount);
                _aliveByGroup[config] = groupCount + delta;
            }
            _aliveBySpecies.TryGetValue(speciesId, out int speciesCount);
            _aliveBySpecies[speciesId] = speciesCount + delta;
            if (config != null && !config.UnboundedDailyGrowth && !config.IgnorePopulationLimits)
                _populationLimitedCount += delta;
        }

        private bool CatalogContains(string speciesId)
        {
            if (_catalog?.Actors == null)
                return false;
            for (int i = 0; i < _catalog.Actors.Length; i++)
            {
                if (string.Equals(_catalog.Actors[i]?.Id, speciesId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>GM 目录从正式 BRG 动画图集读取物种静止帧，避免借用旧外壳的图片。</summary>
        public bool TryGetCatalogIcon(string speciesId, out Texture texture, out Rect uv)
        {
            texture = _catalog?.Material?.mainTexture;
            uv = default;
            if (texture == null || _catalog?.Actors == null || _catalog.Sprites == null ||
                string.IsNullOrWhiteSpace(speciesId))
                return false;

            for (int actorIndex = 0; actorIndex < _catalog.Actors.Length; actorIndex++)
            {
                AiecsActorVisual actor = _catalog.Actors[actorIndex];
                if (actor == null || !string.Equals(actor.Id, speciesId, StringComparison.OrdinalIgnoreCase) ||
                    actor.Clips == null)
                    continue;

                AiecsAnimationClip iconClip = null;
                for (int clipIndex = 0; clipIndex < actor.Clips.Length; clipIndex++)
                {
                    AiecsAnimationClip clip = actor.Clips[clipIndex];
                    if (clip?.Frames == null || clip.Frames.Length == 0)
                        continue;
                    iconClip ??= clip;
                    if (clip.State != null &&
                        (clip.State.EndsWith(".Stand", StringComparison.OrdinalIgnoreCase) ||
                         clip.State.EndsWith(".Idle", StringComparison.OrdinalIgnoreCase) ||
                         clip.State.Equals("Stand", StringComparison.OrdinalIgnoreCase) ||
                         clip.State.Equals("Idle", StringComparison.OrdinalIgnoreCase)))
                    {
                        iconClip = clip;
                        break;
                    }
                }

                if (iconClip == null)
                    return false;
                int spriteIndex = iconClip.Frames[0].Sprite;
                if ((uint)spriteIndex >= (uint)_catalog.Sprites.Length)
                    return false;
                uv = _catalog.Sprites[spriteIndex].AtlasRect;
                return uv.width > 0f && uv.height > 0f;
            }

            return false;
        }

        private static string ResolveFaction(SpawnerConfig config, string speciesId)
        {
            if (GameRes.Instance != null && GameRes.Instance.TryGetItemDefinition(speciesId, out RuntimeItemDefinition definition))
            {
                string configured = definition.CreateItemData()?.FactionId?.Trim();
                if (!string.IsNullOrEmpty(configured))
                    return configured;
            }

            return config?.EcologyGroup switch
            {
                SpawnerEcologyGroup.Animals => "aiecs.wildlife",
                SpawnerEcologyGroup.CommonEnemies => "aiecs.common_enemies",
                SpawnerEcologyGroup.NightEnemies => "aiecs.night_enemies",
                _ => "aiecs.ecology"
            };
        }

        #endregion
    }
}
