using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using NaturalEntityHealth = FlatWorld.AIECS.AiecsVital;
using NaturalEntityGrowth = FlatWorld.AIECS.EntityGrowth;
using NaturalEntityClimate = FlatWorld.AIECS.EntityClimate;
using NaturalEntityHarvest = FlatWorld.AIECS.EntityHarvestRequirement;

namespace FlatWorld.NaturalEntities
{
    /// <summary>
    /// 自然物 ECS 与现有 Item/存档/导航之间的唯一桥。
    /// 热数据留在 Entities；完整 ItemData 只作为冷载荷，在升格、存档和降级边界读写。
    /// </summary>
    public static class NaturalEntityEcsService
    {
        private sealed class Record
        {
            public NaturalEntityHandle Handle;
            public NaturalEntityEcsProfile Profile;
            public ItemData Snapshot;
            public long ObstacleId;
            public bool NavigationRegistered;
            public WorldNavigationManager NavigationOwner;
            public float Precipitation;
            public bool Suspended;
        }

        private sealed class CompileCacheEntry
        {
            public NaturalEntityEcsProfile Profile;
            public string Reason;
            public bool Supported => Profile != null;
        }

        private static NaturalEntitySimulation simulation;
        private static readonly Dictionary<int, Record> records = new();
        private static readonly Dictionary<RuntimeItemDefinition, CompileCacheEntry> profiles = new();
        private static readonly List<Vector2Int> navigationCells = new(8);
        private const float SimulationInterval = 0.25f;
        private static int nextRuntimeId = 1;
        private static float pendingSimulationSeconds;
        private sealed class RuntimeModule : IWorldEntityRuntimeModule
        {
            public string RuntimeModuleId => "entity.resource-inputs";
            public void TickEntities(float deltaTime) => Tick(deltaTime);
            public void CompleteEntityJobs() => simulation?.Complete();
            public void ReleaseEntities() => ReleaseWorld();
        }
        private static readonly RuntimeModule runtimeModule = new();

        public static int Count => simulation?.Count ?? 0;

        #region 生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReleaseWorld();
            profiles.Clear();
            nextRuntimeId = 1;
        }

        /// <summary>只准备导航和环境输入，成长/耐候由 WorldEntityRuntime 的通用能力系统统一计算。</summary>
        public static void Tick(float deltaTime)
        {
            if (simulation == null || simulation.Count == 0)
                return;

            if (deltaTime <= 0f)
                return;
            pendingSimulationSeconds += deltaTime;
            if (pendingSimulationSeconds < SimulationInterval ||
                DayTimeSystem.Instance == null ||
                !DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock))
                return;

            pendingSimulationSeconds = 0f;
            foreach (Record record in records.Values)
            {
                if (record.Suspended) continue;
                if (record.NavigationOwner != WorldNavigationManager.ExistingInstance)
                    UnregisterNavigation(record);
                if (!record.NavigationRegistered) TryRegisterNavigation(record);
                FreezeEnvironment(record);
            }
        }

        public static void ReleaseWorld()
        {
            WorldEntityRuntime.Unregister(runtimeModule);
            foreach (Record record in records.Values)
                UnregisterNavigation(record);
            records.Clear();
            simulation?.Dispose();
            simulation = null;
            profiles.Clear();
            nextRuntimeId = 1;
            pendingSimulationSeconds = 0f;
        }

        #endregion

        #region 注册与升降级

        /// <summary>按现有模块组合尝试进入自然物 ECS；不支持的组合返回 false，由调用方保留完整 Item。</summary>
        public static bool TryRegister(RuntimeItemDefinition definition, int naturalGuid,
            Vector3 defaultPosition, float baselineCelsius, float precipitation, string dimensionId, ItemData persistedData,
            out NaturalEntityHandle handle)
        {
            handle = default;
            if (!TryGetProfile(definition, out NaturalEntityEcsProfile profile, out _))
                return false;
            if (profile.HasClimate && (float.IsNaN(baselineCelsius) || float.IsInfinity(baselineCelsius)))
                return false;

            ItemData snapshot = persistedData == null
                ? definition.CreateItemData()
                : ItemDefinitionRuntime.RebasePersistedData(
                    GameRes.ExistingInstance, FastCloner.FastCloner.DeepClone(persistedData));
            if (snapshot == null)
                return false;
            if (!SupportsInstance(profile, snapshot)) return false;
            snapshot.Guid = naturalGuid;
            snapshot.transform ??= new ItemTransform();
            if (persistedData?.transform == null)
            {
                snapshot.transform.position = defaultPosition;
                snapshot.transform.rotation = Quaternion.identity;
                snapshot.transform.scale = Vector3.one;
            }

            if (simulation == null)
            {
                var world = WorldEntityRuntime.GetOrCreate(dimensionId);
                simulation = new NaturalEntitySimulation(world);
                WorldEntityRuntime.Register(world, runtimeModule);
            }
            int runtimeId = AllocateRuntimeId();
            handle = new NaturalEntityHandle(runtimeId, WorldEntityRuntime.Generation);
            var record = new Record
            {
                Handle = handle,
                Profile = profile,
                Snapshot = snapshot,
                Precipitation = precipitation,
                ObstacleId = (1L << 32) | (uint)runtimeId // 与 Unity 有符号 32 位 InstanceID 分开。
            };

            try
            {
                BuildComponents(record, baselineCelsius,
                    out NaturalEntityBody body,
                    out NaturalEntityHealth? health,
                    out NaturalEntityGrowth? growth,
                    out NaturalEntityClimate? climate,
                    out NaturalEntityHarvest? harvest);
                simulation.Create(runtimeId, body, health, growth, climate, harvest);
                records.Add(runtimeId, record);
                FreezeEnvironment(record);
                TryRegisterNavigation(record);
                return true;
            }
            catch
            {
                simulation.Remove(runtimeId);
                records.Remove(runtimeId);
                UnregisterNavigation(record);
                handle = default;
                throw;
            }
        }

        public static bool Contains(NaturalEntityHandle handle)
            => handle.IsValid && handle.Generation == WorldEntityRuntime.Generation && records.ContainsKey(handle.Id) &&
               simulation?.Contains(handle.Id) == true;

        public static void Remove(NaturalEntityHandle handle)
        {
            if (!Contains(handle) || !records.TryGetValue(handle.Id, out Record record))
                return;
            UnregisterNavigation(record);
            records.Remove(handle.Id);
            simulation?.Remove(handle.Id);
        }

        /// <summary>升格时暂停 ECS 能力并交出导航占格；Item 注册后由旧桥接管。</summary>
        public static void SetSuspended(NaturalEntityHandle handle, bool suspended)
        {
            if (!Contains(handle))
                return;
            NaturalEntityBody body = simulation.GetBody(handle.Id);
            if ((body.Suspended != 0) == suspended)
                return;
            body.Suspended = (byte)(suspended ? 1 : 0);
            body.VisualVersion++;
            simulation.SetBody(handle.Id, body);

            Record record = records[handle.Id];
            record.Suspended = suspended;
            if (suspended)
                UnregisterNavigation(record);
            else
            {
                TryRegisterNavigation(record);
                FreezeEnvironment(record);
            }
        }

        /// <summary>降级时把完整 Item 快照重新压回 ECS 热组件，不丢其它尚未迁移的冷模块数据。</summary>
        public static void ApplySnapshot(NaturalEntityHandle handle, ItemData snapshot,
            float baselineCelsius)
        {
            if (!Contains(handle) || snapshot == null)
                return;
            Record record = records[handle.Id];
            UnregisterNavigation(record);
            if (record.Profile.HasClimate &&
                (float.IsNaN(baselineCelsius) || float.IsInfinity(baselineCelsius)) &&
                simulation.TryGet(handle.Id, out NaturalEntityClimate currentClimate))
            {
                baselineCelsius = currentClimate.BaselineCelsius;
            }
            record.Snapshot = ItemDefinitionRuntime.RebasePersistedData(
                GameRes.ExistingInstance, FastCloner.FastCloner.DeepClone(snapshot));

            BuildComponents(record, baselineCelsius,
                out NaturalEntityBody nextBody,
                out NaturalEntityHealth? health,
                out NaturalEntityGrowth? growth,
                out NaturalEntityClimate? climate,
                out NaturalEntityHarvest? harvest);
            NaturalEntityBody current = simulation.GetBody(handle.Id);
            nextBody.RuntimeId = current.RuntimeId;
            nextBody.Suspended = current.Suspended;
            nextBody.VisualVersion = current.VisualVersion + 1;
            simulation.SetBody(handle.Id, nextBody);
            if (health.HasValue) simulation.Set(handle.Id, health.Value);
            else simulation.RemoveComponent<NaturalEntityHealth>(handle.Id);
            if (growth.HasValue) simulation.Set(handle.Id, growth.Value);
            else simulation.RemoveComponent<NaturalEntityGrowth>(handle.Id);
            if (climate.HasValue) simulation.Set(handle.Id, climate.Value);
            else simulation.RemoveComponent<NaturalEntityClimate>(handle.Id);
            if (harvest.HasValue) simulation.Set(handle.Id, harvest.Value);
            else simulation.RemoveComponent<NaturalEntityHarvest>(handle.Id);

            if (nextBody.Suspended == 0)
            {
                TryRegisterNavigation(record);
                FreezeEnvironment(record);
            }
        }

        /// <summary>F5 只替换当前配置，保留已提交热状态；未迁移的新能力由调用方转为完整接口实体。</summary>
        public static bool TryRefreshDefinition(NaturalEntityHandle handle, RuntimeItemDefinition definition)
        {
            if (!Contains(handle)) return false;
            Record record = records[handle.Id];
            if (ReferenceEquals(record.Profile.Definition, definition)) return true;
            if (!TryGetProfile(definition, out NaturalEntityEcsProfile replacement, out _)) return false;
            profiles.Remove(record.Profile.Definition);
            if (record.Suspended) { record.Profile = replacement; return true; }
            if (!TryCapture(handle, out ItemData snapshot)) return false;
            float baseline = simulation.TryGet(handle.Id, out NaturalEntityClimate climate) ? climate.BaselineCelsius : 0f;
            record.Profile = replacement;
            ApplySnapshot(handle, snapshot, baseline);
            return true;
        }

        public static bool CanDemote(NaturalEntityHandle handle, ItemData snapshot) =>
            Contains(handle) && SupportsInstance(records[handle.Id].Profile, snapshot);

        private static bool SupportsInstance(NaturalEntityEcsProfile profile, ItemData snapshot)
        {
            // 耕地水肥尚未接入通用能力，不能把已有人工种植状态当作野生树木计算。
            return !profile.HasGrowth || !TryGetModuleData(snapshot, profile.GrowthModuleName, out ModuleData raw) ||
                raw is not Ex_ModData_MemoryPackable data || data.GetData<GrowData>()?.isCultivatedCrop != true;
        }

        #endregion

        #region 查询与持久化

        public static bool TryGetPresentation(NaturalEntityHandle handle,
            out NaturalEntityPresentationState state)
        {
            state = default;
            if (!Contains(handle))
                return false;
            NaturalEntityBody body = simulation.GetBody(handle.Id);
            byte growthStage = simulation.TryGet(handle.Id, out NaturalEntityGrowth growth)
                ? growth.Stage
                : (byte)0;
            state = new NaturalEntityPresentationState(
                body.Position, body.Scale, body.Rotation, body.VisualVersion,
                body.Dead != 0, body.Suspended != 0, growthStage);
            return true;
        }

        /// <summary>生成一份可交给 Item/生态差量存档的完整快照，并把 ECS 热数据回写到对应模块。</summary>
        public static bool TryCapture(NaturalEntityHandle handle, out ItemData snapshot)
        {
            snapshot = null;
            if (!Contains(handle))
                return false;
            Record record = records[handle.Id];
            WriteHotState(record, record.Snapshot);
            snapshot = FastCloner.FastCloner.DeepClone(record.Snapshot);
            return true;
        }

        public static bool TryCanHostCompanion(NaturalEntityHandle handle, out bool canHost)
        {
            canHost = false;
            if (!Contains(handle))
                return false;
            if (simulation.GetBody(handle.Id).Dead != 0) return true;
            Record record = records[handle.Id];
            if (!record.Profile.HasGrowth)
            {
                canHost = true;
                return true;
            }
            if (!simulation.TryGet(handle.Id, out NaturalEntityGrowth growth))
                return false;
            canHost = growth.Stage >= (byte)Mod_Grow.GrowState.发育;
            return true;
        }

        public static bool TryGetRenewalYear(NaturalEntityHandle handle, out int year)
        {
            year = 0;
            if (!Contains(handle) || !records[handle.Id].Profile.HasGrowth ||
                !DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season))
                return false;
            year = season.Year + 1;
            return true;
        }

        /// <summary>只有远距离仍会自行变化的能力需要周期写生态差量；纯矿点保持可由世界种子重建。</summary>
        public static bool RequiresRuntimePersistence(NaturalEntityHandle handle)
            => Contains(handle) &&
               (records[handle.Id].Profile.HasGrowth || records[handle.Id].Profile.HasClimate);

        public static bool TryGetUnsupportedReason(RuntimeItemDefinition definition, out string reason)
        {
            TryGetProfile(definition, out _, out reason);
            return !string.IsNullOrWhiteSpace(reason);
        }

        #endregion

        #region 组件装配

        private static void BuildComponents(Record record, float baselineCelsius,
            out NaturalEntityBody body,
            out NaturalEntityHealth? health,
            out NaturalEntityGrowth? growth,
            out NaturalEntityClimate? climate,
            out NaturalEntityHarvest? harvest)
        {
            NaturalEntityEcsProfile profile = record.Profile;
            ItemData snapshot = record.Snapshot;
            Vector3 position = snapshot.transform?.position ?? Vector3.zero;
            Vector3 scale = snapshot.transform?.scale ?? Vector3.one;
            float rotation = snapshot.transform?.rotation.eulerAngles.z ?? 0f;
            body = new NaturalEntityBody
            {
                RuntimeId = record.Handle.Id,
                NaturalGuid = snapshot.Guid,
                Position = new float2(position.x, position.y),
                Scale = new float2(scale.x == 0f ? 1f : scale.x, scale.y == 0f ? 1f : scale.y),
                Rotation = rotation,
                VisualVersion = 1
            };

            health = null;
            growth = null;
            climate = null;
            harvest = null;

            if ((profile.Capabilities & NaturalEntityCapability.Health) != 0)
            {
                DamageReceiver.DamageReceiver_SaveData state = ReadHealthState(record);
                health = new NaturalEntityHealth
                {
                    Hp = Mathf.Clamp(state.Hp, 0f, Mathf.Max(1f, state.MaxHp)),
                    MaxHp = Mathf.Max(1f, state.MaxHp),
                    Dead = (byte)(state.Hp <= 0f ? 1 : 0),
                    ReceivedMultiplier = 1f, DamageInterval = state.DamageInterval,
                    LastDamageTime = double.NegativeInfinity
                };
            }

            if ((profile.Capabilities & NaturalEntityCapability.Growth) != 0)
            {
                GrowData state = ReadGrowthState(record);
                Mod_Grow.InitializeNaturalGrowthData(state, snapshot.Guid, record.Precipitation);
                int stage = 0;
                for (int i = 0; i < state.growState_Value.Count; i++)
                    if (state.GrowProgress >= state.growState_Value[i]) stage = i;
                var component = new NaturalEntityGrowth
                {
                    Progress = Mathf.Clamp(state.GrowProgress, 0f, state.MaxGrowProgress),
                    MaxProgress = Mathf.Max(0.01f, state.MaxGrowProgress),
                    Speed = Mathf.Max(0f, state.GrowSpeed),
                    EnvironmentMultiplier = state.environmentGrowthMultiplier,
                    MatureMaxHealth = profile.MatureMaxHealth,
                    RainGrowthBonus = profile.RainGrowthBonus,
                    Stage = (byte)stage
                };
                FillFixedList(state.growState_Value, ref component.Thresholds);
                FillFixedList(state.growState_Scale, ref component.Scales);
                FillFixedList(profile.GrowthHealthRatios, ref component.HealthRatios);
                growth = component;

                if (component.Scales.Length > component.Stage)
                {
                    float stageScale = component.Scales[component.Stage];
                    body.Scale = new float2(stageScale, stageScale);
                }
                if (health.HasValue)
                {
                    NaturalEntityHealth vital = health.Value;
                    float fraction = vital.MaxHp > 0f ? Mathf.Clamp01(vital.Hp / vital.MaxHp) : 1f;
                    vital.MaxHp = Mathf.Max(1f, profile.MatureMaxHealth * component.HealthRatios[stage]);
                    vital.Hp = vital.MaxHp * fraction;
                    health = vital;
                }
            }

            if ((profile.Capabilities & NaturalEntityCapability.Climate) != 0)
            {
                PlantClimateState state = ReadClimateState(record);
                PlantClimateConfig config = profile.Climate;
                climate = new NaturalEntityClimate
                {
                    BaselineCelsius = baselineCelsius,
                    MinimumGrowthTemperature = config.MinimumGrowth,
                    MaximumGrowthTemperature = config.MaximumGrowth,
                    MinimumSurvivalTemperature = config.MinimumSurvival,
                    MaximumSurvivalTemperature = config.MaximumSurvival,
                    FatalExposureHours = config.FatalHours,
                    RecoveryRate = config.RecoveryRate,
                    ColdSeconds = Mathf.Max(0f, state.ColdSeconds),
                    HeatSeconds = Mathf.Max(0f, state.HeatSeconds),
                    LastSimulationTime = state.LastSimulationTime,
                    ClockInitialized = (byte)(state.ClockInitialized ? 1 : 0),
                    Dead = (byte)(state.Dead ? 1 : 0)
                };
                if (state.Dead)
                    body.Dead = 1;
            }

            if ((profile.Capabilities & NaturalEntityCapability.Harvest) != 0)
            {
                harvest = new NaturalEntityHarvest
                {
                    ToolKind = (int)profile.Harvest.Tool,
                    MinimumTier = profile.Harvest.MinimumTier
                };
            }
            if (body.Dead != 0 && health.HasValue)
            {
                NaturalEntityHealth vital = health.Value;
                vital.Hp = 0f; vital.Dead = 1;
                health = vital;
            }
        }

        private static DamageReceiver.DamageReceiver_SaveData ReadHealthState(Record record)
        {
            var result = FastCloner.FastCloner.DeepClone(record.Profile.HealthDefaults);
            if (TryGetModuleData(record.Snapshot, record.Profile.HealthModuleName, out ModuleData raw) &&
                raw is Ex_ModData data)
            {
                DamageReceiver.DamageReceiver_SaveData saved =
                    data.GetData<DamageReceiver.DamageReceiver_SaveData>();
                if (saved != null)
                {
                    result.Hp = saved.Hp;
                    result.MaxHp = saved.MaxHp;
                    result.AttackersUIDs = saved.AttackersUIDs;
                }
            }
            return result;
        }

        private static GrowData ReadGrowthState(Record record)
        {
            GrowData result = FastCloner.FastCloner.DeepClone(record.Profile.GrowthDefaults);
            if (TryGetModuleData(record.Snapshot, record.Profile.GrowthModuleName, out ModuleData raw) &&
                raw is Ex_ModData_MemoryPackable data)
            {
                GrowData saved = data.GetData<GrowData>();
                if (saved != null)
                {
                    result.GrowProgress = Mathf.Clamp(saved.GrowProgress, 0f, result.MaxGrowProgress);
                    result.isCultivatedCrop = saved.isCultivatedCrop;
                    result.isMature = result.GrowProgress >= result.MaxGrowProgress;
                    result.isHarvested = saved.isHarvested;
                    result.environmentInitialized = saved.environmentInitialized;
                    result.environmentGrowthMultiplier = saved.environmentGrowthMultiplier;
                    result.simulationInitialized = saved.simulationInitialized;
                    result.lastSimulatedTime = saved.lastSimulatedTime;
                    result.plantedTilePos = saved.plantedTilePos;
                }
            }
            return result;
        }

        private static PlantClimateState ReadClimateState(Record record)
        {
            if (TryGetModuleData(record.Snapshot, record.Profile.ClimateModuleName, out ModuleData raw) &&
                raw is Ex_ModData_MemoryPackable data)
            {
                PlantClimateState saved = data.GetData<PlantClimateState>();
                if (saved != null)
                    return saved;
            }
            var initial = new PlantClimateState();
            if (DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock))
            {
                SeasonSnapshot season = SeasonCalendar.Sample(clock);
                initial.ClockInitialized = true;
                initial.LastSimulationTime = Math.Max(0d,
                    clock.TotalDays * (double)clock.DayLength + clock.CurrentTime - season.ElapsedDays * clock.DayLength);
            }
            return initial;
        }

        /// <summary>局部热源和天气先冻结为纯数值，再交给共享并行能力；不把当前火源用于历史补算。</summary>
        private static void FreezeEnvironment(Record record)
        {
            if (!record.Profile.HasClimate || record.Suspended ||
                !simulation.TryGet(record.Handle.Id, out NaturalEntityClimate climate)) return;
            TemperatureMgr temperature = TemperatureMgr.Instance;
            Vector3 position = record.Snapshot.transform.position;
            climate.EnvironmentReady = 0;
            if (temperature != null && temperature.TryGetAmbientTemperature(position, out float ambient) &&
                temperature.TryGetClimateBaseline(position, out float baseline))
            {
                climate.AmbientCelsius = ambient;
                climate.BaselineCelsius = baseline;
                climate.EnvironmentReady = 1;
            }
            simulation.Set(record.Handle.Id, climate);
        }

        private static void WriteHotState(Record record, ItemData data)
        {
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            data.Guid = body.NaturalGuid;
            data.transform ??= new ItemTransform();
            data.transform.position = new Vector3(body.Position.x, body.Position.y, 0f);
            data.transform.rotation = Quaternion.Euler(0f, 0f, body.Rotation);
            data.transform.scale = new Vector3(body.Scale.x, body.Scale.y, 1f);

            if (simulation.TryGet(record.Handle.Id, out NaturalEntityHealth health) &&
                TryGetModuleData(data, record.Profile.HealthModuleName, out ModuleData healthRaw) &&
                healthRaw is Ex_ModData healthData)
            {
                DamageReceiver.DamageReceiver_SaveData state =
                    healthData.GetData<DamageReceiver.DamageReceiver_SaveData>() ??
                    FastCloner.FastCloner.DeepClone(record.Profile.HealthDefaults);
                state.Hp = health.Hp;
                state.MaxHp = health.MaxHp;
                healthData.WriteData(state);
            }

            if (simulation.TryGet(record.Handle.Id, out NaturalEntityGrowth growth) &&
                TryGetModuleData(data, record.Profile.GrowthModuleName, out ModuleData growthRaw) &&
                growthRaw is Ex_ModData_MemoryPackable growthData)
            {
                GrowData state = growthData.GetData<GrowData>() ??
                                 FastCloner.FastCloner.DeepClone(record.Profile.GrowthDefaults);
                state.GrowProgress = growth.Progress;
                state.MaxGrowProgress = growth.MaxProgress;
                state.GrowSpeed = growth.Speed;
                state.growState = (Mod_Grow.GrowState)growth.Stage;
                state.environmentInitialized = true;
                state.environmentGrowthMultiplier = growth.EnvironmentMultiplier;
                state.isMature = growth.Progress >= growth.MaxProgress || state.growState == Mod_Grow.GrowState.成熟;
                state.growthStatus = state.isMature ? Mod_Grow.GrowthStatus.Mature : Mod_Grow.GrowthStatus.Growing;
                state.growState_Value = new List<float>(record.Profile.GrowthDefaults.growState_Value);
                state.growState_Scale = new List<float>(record.Profile.GrowthDefaults.growState_Scale);
                growthData.WriteData(state);
            }

            if (simulation.TryGet(record.Handle.Id, out NaturalEntityClimate climate) &&
                TryGetModuleData(data, record.Profile.ClimateModuleName, out ModuleData climateRaw) &&
                climateRaw is Ex_ModData_MemoryPackable climateData)
            {
                PlantClimateState state = climateData.GetData<PlantClimateState>() ?? new PlantClimateState();
                state.ColdSeconds = climate.ColdSeconds;
                state.HeatSeconds = climate.HeatSeconds;
                state.Dead = climate.Dead != 0 || body.Dead != 0;
                state.ClockInitialized = climate.ClockInitialized != 0;
                state.LastSimulationTime = climate.LastSimulationTime;
                climateData.WriteData(state);
            }
        }

        private static bool TryGetModuleData(ItemData data, string stableName, out ModuleData module)
        {
            module = null;
            return data?.ModuleDataDic != null && !string.IsNullOrWhiteSpace(stableName) &&
                   data.ModuleDataDic.TryGetValue(stableName, out module) && module != null;
        }

        private static void FillFixedList(IReadOnlyList<float> values, ref FixedList64Bytes<float> target)
        {
            target.Clear();
            if (values == null)
                return;
            for (int i = 0; i < values.Count && i < target.Capacity; i++)
                target.Add(values[i]);
        }

        #endregion

        #region 导航占格

        private static void TryRegisterNavigation(Record record)
        {
            if (record == null || record.NavigationRegistered ||
                record.Profile.Definition.WorldGridOccupancy.Count == 0 ||
                !Contains(record.Handle))
                return;
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            if (body.Suspended != 0 || body.Dead != 0)
                return;

            WorldNavigationManager manager = WorldNavigationManager.ExistingInstance;
            if (manager == null)
                return;
            if (record.NavigationOwner != null && record.NavigationOwner != manager)
                UnregisterNavigation(record);

            Vector2Int anchor = WorldNavigationGrid.WorldToCell(
                new Vector2(body.Position.x, body.Position.y));
            navigationCells.Clear();
            IReadOnlyList<GridCellOffset> offsets = record.Profile.Definition.WorldGridOccupancy;
            for (int i = 0; i < offsets.Count; i++)
            {
                GridCellOffset offset = offsets[i];
                navigationCells.Add(new Vector2Int(anchor.x + offset.X, anchor.y + offset.Y));
            }
            manager.RegisterObstacle(record.ObstacleId, navigationCells);
            record.NavigationOwner = manager;
            record.NavigationRegistered = true;
        }

        private static void UnregisterNavigation(Record record)
        {
            if (record == null || !record.NavigationRegistered)
                return;
            if (record.NavigationOwner != null)
                record.NavigationOwner.UnregisterObstacle(record.ObstacleId);
            record.NavigationRegistered = false;
            record.NavigationOwner = null;
        }

        #endregion

        #region 定义缓存

        private static bool TryGetProfile(RuntimeItemDefinition definition,
            out NaturalEntityEcsProfile profile, out string reason)
        {
            profile = null;
            reason = null;
            if (definition == null)
                return false;
            if (!profiles.TryGetValue(definition, out CompileCacheEntry entry))
            {
                bool supported = NaturalEntityEcsProfileCompiler.TryCompile(
                    definition, out NaturalEntityEcsProfile compiled, out string failure);
                entry = new CompileCacheEntry
                {
                    Profile = supported ? compiled : null,
                    Reason = supported ? null : failure
                };
                profiles.Add(definition, entry);
            }
            profile = entry.Profile;
            reason = entry.Reason;
            return entry.Supported;
        }

        private static int AllocateRuntimeId()
        {
            while (nextRuntimeId == 0 || records.ContainsKey(nextRuntimeId))
                nextRuntimeId = nextRuntimeId == int.MaxValue ? 1 : nextRuntimeId + 1;
            int allocated = nextRuntimeId;
            nextRuntimeId = nextRuntimeId == int.MaxValue ? 1 : nextRuntimeId + 1;
            return allocated;
        }

        #endregion
    }
}
