using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>
    /// 正式世界 AIECS 生态宿主：把既有 MonsterSpawnerManager 的生成请求转换为纯 Entity，
    /// 统一驱动 30Hz 模拟、批量表现、数量查询和远距离回收。该组件不重新实现生态调度规则。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("FlatWorld/AIECS/正式生态宿主")]
    public sealed class AiecsEcologyRuntimeHost : MonoBehaviour, IAiEcologyBackend
    {
        #region 配置与记录

        private const int MaxSimulationStepsPerFrame = 2; // 15 FPS 仍可维持 30Hz；更低帧率时优先保护渲染帧不被追帧拖垮。
        private const int MaxBacklogSteps = 3; // 只保留少量时间债务，避免一次卡顿演变成连续多帧追赶尖峰。
        private static AiecsEcologyRuntimeHost active;
        [SerializeField] private AiecsAnimationCatalog _catalog;
        [SerializeField, Range(10, 60)] private int _simulationHz = 30;

        private sealed class EcologyActor
        {
            public Entity Entity;
            public CombatIdentity Identity;
            public SpawnerConfig Config;
            public string SpeciesId;
            public float FarSince = -1f;
        }

        private readonly Dictionary<string, int> _templateBySpecies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SpawnerConfig> _configBySpecies = new(StringComparer.Ordinal);
        private readonly Dictionary<CombatIdentity, EcologyActor> _actors = new();
        private readonly List<CombatIdentity> _cleanup = new(64);
        private readonly List<string> _actorIds = new();
        private readonly List<string> _actorFactions = new();
        private readonly List<bool> _fleeFromHostiles = new();
        private readonly HashSet<string> _reportedUnsupported = new(StringComparer.Ordinal);

        private AiecsGameplayBridge _bridge;
        private AiecsWorldRenderer _renderer;
        private Player _player;
        private Camera _camera;
        private double _simulationTime;
        private bool _worldPrepared;
        private bool _startFailed;
        private bool _ownsBackendRegistration;

        public static AiecsEcologyRuntimeHost Active => active;
        public bool IsReady => _bridge != null;

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
            if (!AiRuntimeBackendService.UseEntities || !_worldPrepared)
                return;

            // GM 开发场景与正式生态共享同一套玩家/导航资源，但任何时刻只允许一个模拟真正运行。
            if (AiecsPlayground.Active?.HasActiveScenario == true)
            {
                if (_bridge != null)
                    DisposeSimulation();
                return;
            }

            if (_bridge == null)
                TryStartSimulation();
            if (_bridge == null || _player == null)
                return;

            if (!_bridge.IsCurrentWorld(_player))
            {
                DisposeSimulation();
                _startFailed = false;
                return;
            }

            float step = 1f / Mathf.Max(1, _simulationHz);
            double now = Time.timeAsDouble;
            double maxBacklog = step * MaxBacklogSteps;
            if (now - _simulationTime > maxBacklog)
                _simulationTime = now - maxBacklog;

            int budget = MaxSimulationStepsPerFrame;
            try
            {
                _bridge.EnsurePlayer(_player);
                while (_simulationTime + step <= now && budget-- > 0)
                {
                    _simulationTime += step;
                    _bridge.Step(step, _simulationTime);
                }

                PruneDestroyedActors();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DisposeSimulation();
                _startFailed = true;
            }
        }

        private void LateUpdate()
        {
            if (_bridge == null || _renderer == null)
                return;

            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;
            if (_camera == null)
                return;

            _renderer.Draw(_bridge.Simulation, _camera, _bridge.Navigation.Read().Domain);
        }

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
            _reportedUnsupported.Clear();

            if (_catalog == null || _catalog.Material == null)
            {
                Debug.LogError("[AIECS] 正式生态宿主缺少动画目录或批量材质。", this);
                _startFailed = true;
                return false;
            }

            if (configs == null)
                return false;

            for (int configIndex = 0; configIndex < configs.Count; configIndex++)
            {
                SpawnerConfig config = configs[configIndex];
                if (config?.SpawnEntries == null)
                    continue;

                for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
                {
                    SpawnerConfig.SpawnEntry entry = config.SpawnEntries[entryIndex];
                    string speciesId = entry?.PrefabName?.Trim();
                    if (string.IsNullOrEmpty(speciesId) ||
                        !AiRuntimeBackendService.UsesEntities(entry) ||
                        _templateBySpecies.ContainsKey(speciesId))
                        continue;

                    if (!CatalogContains(speciesId))
                    {
                        ReportUnsupported(speciesId, "动画目录没有该 Actor");
                        continue;
                    }

                    bool fleeFromHostiles = config.EcologyGroup == SpawnerEcologyGroup.Animals;
                    if (!AiecsDefinitionCompiler.TryValidate(speciesId, fleeFromHostiles, out string reason))
                    {
                        ReportUnsupported(speciesId, reason);
                        continue;
                    }

                    int templateIndex = _actorIds.Count;
                    _templateBySpecies.Add(speciesId, templateIndex);
                    _configBySpecies.Add(speciesId, config);
                    _actorIds.Add(speciesId);
                    _actorFactions.Add(ResolveFaction(config, speciesId));
                    _fleeFromHostiles.Add(fleeFromHostiles);
                }
            }

            if (_actorIds.Count == 0)
            {
                Debug.LogError("[AIECS] 当前生态目录没有可由正式 AIECS 基础切片运行的 Actor。", this);
                _startFailed = true;
                return false;
            }

            return true;
        }

        public void ResetWorld()
        {
            DisposeSimulation();
            _templateBySpecies.Clear();
            _configBySpecies.Clear();
            _actorIds.Clear();
            _actorFactions.Clear();
            _fleeFromHostiles.Clear();
            _reportedUnsupported.Clear();
            _worldPrepared = false;
            _startFailed = false;
            _player = null;
            _camera = null;
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
                _bridge = new AiecsGameplayBridge(
                    _player,
                    navigation.GetSharedNavigation(),
                    ids,
                    _actorFactions.ToArray(),
                    0f,
                    _fleeFromHostiles.ToArray());
                _renderer = new AiecsWorldRenderer(_catalog, ids, _player.gameObject.scene);
                _simulationTime = Time.timeAsDouble;
                Debug.Log($"[AIECS] 正式 ECS 生态已启动：{ids.Length} 个 Actor 定义；GameObject AI 保持并行运行。", this);
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
            _renderer?.Dispose();
            _renderer = null;
            _bridge?.Dispose();
            _bridge = null;
            _actors.Clear();
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
            DisposeSimulation();
            _startFailed = false;
        }

        #endregion

        #region 生态后端

        public bool SupportsSpecies(string speciesId)
        {
            return !string.IsNullOrWhiteSpace(speciesId) && _templateBySpecies.ContainsKey(speciesId);
        }

        public bool TrySpawn(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 position)
        {
            if (!AiRuntimeBackendService.UseEntities || !AiRuntimeBackendService.UsesEntities(entry) || config == null)
                return false;

            return TrySpawnSpecies(entry.PrefabName, config, position);
        }

        /// <summary>游戏事件同样创建纯 Entity，不实例化旧 Actor GameObject。</summary>
        public bool TrySpawnEvent(string speciesId, Vector3 position)
        {
            if (!AiRuntimeBackendService.UseEntities ||
                !AiRuntimeBackendService.UsesEntities(speciesId) ||
                string.IsNullOrWhiteSpace(speciesId) ||
                !_configBySpecies.TryGetValue(speciesId, out SpawnerConfig config))
            {
                return false;
            }

            return TrySpawnSpecies(speciesId, config, position);
        }

        public int GetGroupCount(SpawnerConfig config)
        {
            if (config == null || !PrepareRead())
                return 0;

            int count = 0;
            foreach (EcologyActor actor in _actors.Values)
            {
                if (ReferenceEquals(actor.Config, config) && IsAlive(actor, out _))
                    count++;
            }
            return count;
        }

        public int GetSpeciesCount(string speciesId)
        {
            if (string.IsNullOrWhiteSpace(speciesId) || !PrepareRead())
                return 0;

            int count = 0;
            foreach (EcologyActor actor in _actors.Values)
            {
                if (string.Equals(actor.SpeciesId, speciesId, StringComparison.Ordinal) && IsAlive(actor, out _))
                    count++;
            }
            return count;
        }

        public int PopulationLimitedCount
        {
            get
            {
                if (!PrepareRead())
                    return 0;

                int count = 0;
                foreach (EcologyActor actor in _actors.Values)
                {
                    SpawnerConfig config = actor.Config;
                    if (config != null && !config.UnboundedDailyGrowth && !config.IgnorePopulationLimits && IsAlive(actor, out _))
                        count++;
                }
                return count;
            }
        }

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

        /// <summary>远距离回收仍由生态配置决定，只把销毁操作改为 Entity 结构删除。</summary>
        public void RecycleDistantPopulation(IReadOnlyList<Vector3> playerPositions, float now)
        {
            if (playerPositions == null || playerPositions.Count == 0 || !PrepareRead())
                return;

            _cleanup.Clear();
            foreach (EcologyActor actor in _actors.Values)
            {
                SpawnerConfig config = actor.Config;
                if (config == null || config.RecycleDistance <= 0f || !IsAlive(actor, out float2 position))
                    continue;

                bool nearPlayer = false;
                float radiusSqr = config.RecycleDistance * config.RecycleDistance;
                Vector3 world = new Vector3(position.x, position.y);
                for (int i = 0; i < playerPositions.Count; i++)
                {
                    if (WorldTopologyRuntime.SqrDistance(world, playerPositions[i]) > radiusSqr)
                        continue;
                    nearPlayer = true;
                    break;
                }

                if (nearPlayer)
                {
                    actor.FarSince = -1f;
                    continue;
                }

                if (actor.FarSince < 0f)
                {
                    actor.FarSince = now;
                    continue;
                }

                if (now - actor.FarSince >= Mathf.Max(0f, config.RecycleGraceSeconds))
                    _cleanup.Add(actor.Identity);
            }

            for (int i = 0; i < _cleanup.Count; i++)
                Despawn(_cleanup[i]);
            _cleanup.Clear();
        }

        #endregion

        #region 查询辅助

        private bool TrySpawnSpecies(string speciesId, SpawnerConfig config, Vector3 position)
        {
            if (_bridge == null)
                TryStartSimulation();
            if (_bridge == null || !_templateBySpecies.TryGetValue(speciesId, out int templateIndex))
                return false;

            if (!_bridge.Spawn(templateIndex, (Vector2)position, 1f, out Entity entity, out CombatIdentity identity))
                return false;

            _actors[identity] = new EcologyActor
            {
                Entity = entity,
                Identity = identity,
                Config = config,
                SpeciesId = speciesId
            };
            return true;
        }

        private bool PrepareRead()
        {
            if (_bridge?.Simulation == null)
                return false;
            _bridge.Simulation.Complete();
            return true;
        }

        private bool IsAlive(EcologyActor actor, out float2 position)
        {
            position = default;
            if (_bridge?.Simulation == null || actor == null || !_bridge.Simulation.Entities.Exists(actor.Entity))
                return false;

            AiecsVital vital = _bridge.Simulation.Entities.GetComponentData<AiecsVital>(actor.Entity);
            if (vital.Dead != 0)
                return false;

            position = _bridge.Simulation.Entities.GetComponentData<AiecsFlowAgent>(actor.Entity).Position;
            return true;
        }

        private void Despawn(CombatIdentity identity)
        {
            if (!_actors.TryGetValue(identity, out EcologyActor actor))
                return;

            if (_bridge?.Simulation != null && _bridge.Simulation.Entities.Exists(actor.Entity))
                _bridge.Simulation.Despawn(actor.Entity);
            _actors.Remove(identity);
        }

        private void PruneDestroyedActors()
        {
            if (!PrepareRead() || _actors.Count == 0)
                return;

            _cleanup.Clear();
            foreach (KeyValuePair<CombatIdentity, EcologyActor> pair in _actors)
            {
                if (!_bridge.Simulation.Entities.Exists(pair.Value.Entity))
                    _cleanup.Add(pair.Key);
            }
            for (int i = 0; i < _cleanup.Count; i++)
                _actors.Remove(_cleanup[i]);
            _cleanup.Clear();
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

        private static string ResolveFaction(SpawnerConfig config, string speciesId)
        {
            if (GameRes.Instance != null && GameRes.Instance.TryGetItemDefinition(speciesId, out RuntimeItemDefinition definition))
            {
                string configured = definition.CreateItemData()?.FactionId?.Trim();
                if (!string.IsNullOrEmpty(configured))
                    return configured;
            }

            return config.EcologyGroup switch
            {
                SpawnerEcologyGroup.Animals => "aiecs.wildlife",
                SpawnerEcologyGroup.CommonEnemies => "aiecs.common_enemies",
                SpawnerEcologyGroup.NightEnemies => "aiecs.night_enemies",
                _ => "aiecs.ecology"
            };
        }

        private void ReportUnsupported(string speciesId, string reason)
        {
            if (!_reportedUnsupported.Add(speciesId))
                return;

            // 单个 Actor 尚未迁移属于已知能力边界：明确跳过即可，不能让正常 GameStart 启动持续产生 Warning。
            // 真正的后端缺失、目录完全不可用或运行时异常仍由调用链按 Error/Exception 报告。
            string detail = (reason ?? string.Empty).Trim().TrimEnd('。', '.', '！', '!', '？', '?');
            Debug.Log($"[AIECS] 已跳过未支持 Actor '{speciesId}'：{detail}；不会回退 BaseAI。", this);
        }

        #endregion
    }
}
