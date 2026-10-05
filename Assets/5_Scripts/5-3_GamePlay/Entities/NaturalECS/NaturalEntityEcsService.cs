using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Newtonsoft.Json.Linq;
using NaturalEntityHealth = FlatWorld.AIECS.AiecsVital;
using NaturalEntityGrowth = FlatWorld.AIECS.EntityGrowth;
using NaturalEntityClimate = FlatWorld.AIECS.EntityClimate;
using NaturalEntityHarvest = FlatWorld.AIECS.EntityHarvestRequirement;

namespace FlatWorld.NaturalEntities
{
    /// <summary>
    /// 自然物 ECS 与现有 Item/存档/导航之间的唯一桥。
    /// 热数据留在 Entities；ItemData 仅用于定义和存档，树木排序外壳不承载资源玩法。
    /// </summary>
    public static partial class NaturalEntityEcsService
    {
        private sealed partial class Record
        {
            public NaturalEntityHandle Handle;
            public NaturalEntityEcsProfile Profile;
            public ItemData Snapshot;
            public long ObstacleId;
            public bool NavigationRegistered;
            public WorldNavigationManager NavigationOwner;
            public float Precipitation;
        }

        private sealed class CompileCacheEntry
        {
            public NaturalEntityEcsProfile Profile;
            public string Reason;
            public ulong RegistryRevision;
            public bool Supported => Profile != null;
        }

        private static NaturalEntitySimulation simulation;
        private static readonly Dictionary<int, Record> records = new();
        private static readonly Dictionary<RuntimeItemDefinition, CompileCacheEntry> profiles = new();
        private static readonly List<Vector2Int> navigationCells = new(8);
        private const float SimulationInterval = 0.25f;
        private static int nextRuntimeId = 1;
        private static float pendingSimulationSeconds;
        private sealed class RuntimeModule : IWorldEntityRuntimeModule, IWorldEntityPostSimulationModule, IGameplayCombatBridge
        {
            public string RuntimeModuleId => "entity.resource-inputs";
            public void TickEntities(float deltaTime) => Tick(deltaTime);
            // 输入准备只使用同步 EntityManager API，没有独立 Job 需要等待。
            public void CompleteEntityJobs() { }
            public void ReleaseEntities() => ReleaseWorld();
            public void AfterEntitySimulation(float deltaTime)
            {
                PublishEntities(deltaTime);
                TickContactDamage(deltaTime);
            }
            public bool TryGetIdentity(Item item, out CombatIdentity identity) { identity = default; return false; }
            public void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context) => QueryWeapon(weapon, shape, context);
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
                if (record.NavigationOwner != WorldNavigationManager.ExistingInstance)
                    UnregisterNavigation(record);
                if (!record.NavigationRegistered) TryRegisterNavigation(record);
                FreezeEnvironment(record);
                FreezeSoil(record);
            }
        }

        public static void ReleaseWorld()
        {
            WorldEntityRuntime.Unregister(runtimeModule);
            GameplayCombatBridge.Unregister(runtimeModule);
            foreach (Record record in records.Values)
            {
                ReleaseExtensionModules(record);
                UnregisterNavigation(record);
                ReleasePresentation(record);
            }
            records.Clear();
            ClearContactDamageSources();
            pendingPublications.Clear();
            publicationBatch.Clear();
            canopySources.Clear();
            canopyBatch.Clear();
            lastCapabilityVersion = ulong.MaxValue;
            simulation?.Dispose();
            simulation = null;
            profiles.Clear();
            nextRuntimeId = 1;
            pendingSimulationSeconds = 0f;
            ClearResourceQueries();
            PhysicsBodiesReset?.Invoke();
            ReleasePresentationRuntime();
        }

        #endregion

        #region 实体创建与配置刷新

        /// <summary>已声明 Entity 的资源只能走当前入口；模块缺失必须报错，不能降回 Item。</summary>
        public static bool TryRegister(RuntimeItemDefinition definition, int naturalGuid,
            Vector3 defaultPosition, float baselineCelsius, float precipitation, string dimensionId, ItemData persistedData,
            out NaturalEntityHandle handle, Vector2Int? plantedCell = null)
        {
            handle = default;
            if (definition?.UsesResourceEntities != true) return false;
            if (!TryGetProfile(definition, out NaturalEntityEcsProfile profile, out string failure))
                throw new InvalidOperationException($"资源实体 {definition.Id} 编译失败：{failure}");
            if (profile.HasClimate && (float.IsNaN(baselineCelsius) || float.IsInfinity(baselineCelsius)))
                return false;

            ItemData snapshot = persistedData == null
                ? definition.CreateItemData()
                : ItemDefinitionRuntime.RebasePersistedData(
                    GameRes.ExistingInstance, FastCloner.FastCloner.DeepClone(persistedData));
            if (snapshot == null)
                return false;
            snapshot.Guid = naturalGuid;
            snapshot.transform ??= new ItemTransform();
            if (persistedData?.transform == null)
            {
                snapshot.transform.position = defaultPosition;
                snapshot.transform.rotation = Quaternion.identity;
                snapshot.transform.scale = Vector3.one;
            }
            snapshot.transform.position = WorldTopologyRuntime.NormalizePosition(snapshot.transform.position);
            if (simulation != null && (!ReferenceEquals(simulation.World, WorldEntityRuntime.Current) ||
                !string.Equals(WorldEntityRuntime.DimensionId, dimensionId, StringComparison.Ordinal)))
                throw new InvalidOperationException("资源实体不能写入另一维度或过期的共享世界。");

            if (simulation == null)
            {
                var world = WorldEntityRuntime.GetOrCreate(dimensionId);
                simulation = new NaturalEntitySimulation(world);
                simulation.Changed += QueuePublication;
                WorldEntityRuntime.Register(world, runtimeModule);
                GameplayCombatBridge.Register(runtimeModule);
            }
            int runtimeId = AllocateRuntimeId();
            handle = new NaturalEntityHandle(runtimeId, WorldEntityRuntime.Generation);
            var record = new Record
            {
                Handle = handle,
                Profile = profile,
                Snapshot = snapshot,
                Precipitation = precipitation,
                DimensionId = dimensionId,
                ObstacleId = (1L << 32) | (uint)runtimeId // 与 Unity 有符号 32 位 InstanceID 分开。
            };

            try
            {
                InitializePlantedSnapshot(record, plantedCell);
                ValidateOutputDefinitions(profile);
                BuildComponents(record, baselineCelsius,
                    out NaturalEntityBody body,
                    out NaturalEntityHealth? health,
                    out NaturalEntityGrowth? growth,
                    out NaturalEntityClimate? climate,
                    out NaturalEntityHarvest? harvest);
                body.Suspended = (byte)(GameNetwork.HasStateAuthority ? 0 : 1);
                simulation.Create(profile, runtimeId, body, health, growth, climate, harvest);
                records.Add(runtimeId, record);
                InstallPlantModules(record, persistedData, plantedCell.HasValue);
                InstallExtensionModules(record, plantedCell.HasValue);
                FreezeEnvironment(record);
                FreezeSoil(record);
                TryRegisterNavigation(record);
                RegisterSpatial(record);
                QueuePublication(runtimeId);
                return true;
            }
            catch
            {
                if (records.ContainsKey(runtimeId)) Remove(handle);
                else simulation.Remove(runtimeId);
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
            ReleaseExtensionModules(record);
            UnregisterNavigation(record);
            UnregisterSpatial(record);
            RemoveContactDamageSource(record);
            canopySources.Remove(record);
            pendingPublications.Remove(handle.Id);
            ReleasePresentation(record);
            if (simulation.TryGet(handle.Id, out EntityPlantLifecycle plant) && plant.Cultivated != 0)
            {
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(plant.SoilCell.x, plant.SoilCell.y));
                if (cultivatedCells.TryGetValue(cell, out int occupant) && occupant == handle.Id) cultivatedCells.Remove(cell);
            }
            records.Remove(handle.Id);
            simulation?.Remove(handle.Id);
        }

        /// <summary>配置刷新时将保存的运行态恢复到原 Entity，不切换玩法后端。</summary>
        public static void ApplySnapshot(NaturalEntityHandle handle, ItemData snapshot,
            float baselineCelsius, bool rebaseCurrentDefinition = true)
        {
            if (!Contains(handle) || snapshot == null)
                return;
            Record record = records[handle.Id];
            if (record.Snapshot.Guid != snapshot.Guid || !string.Equals(record.Profile.Definition.Id, snapshot.IDName, StringComparison.Ordinal))
                throw new InvalidOperationException("配置刷新不能替换植物的实体身份。");
            AiecsVital previousVital = default;
            bool hasPreviousVital = simulation.TryGet(handle.Id, out previousVital);
            if (simulation.TryGet(handle.Id, out EntityPlantLifecycle previousPlant) && previousPlant.Cultivated != 0)
                cultivatedCells.Remove(WorldTopologyRuntime.NormalizeCell(new Vector2Int(previousPlant.SoilCell.x, previousPlant.SoilCell.y)));
            UnregisterNavigation(record);
            UnregisterSpatial(record, PhysicsBodyChangeReason.SnapshotRefresh);
            if (record.Profile.HasClimate &&
                (float.IsNaN(baselineCelsius) || float.IsInfinity(baselineCelsius)) &&
                simulation.TryGet(handle.Id, out NaturalEntityClimate currentClimate))
            {
                baselineCelsius = currentClimate.BaselineCelsius;
            }
            record.Snapshot = rebaseCurrentDefinition
                ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, FastCloner.FastCloner.DeepClone(snapshot))
                : FastCloner.FastCloner.DeepClone(snapshot);

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
            if (health.HasValue && hasPreviousVital)
            {
                AiecsVital next = health.Value;
                next.LastDamageTime = previousVital.LastDamageTime;
                next.DeathPublished = previousVital.DeathPublished;
                next.ReceivedMultiplier = previousVital.ReceivedMultiplier;
                health = next;
            }
            if (health.HasValue) simulation.Set(handle.Id, health.Value);
            else simulation.RemoveComponent<NaturalEntityHealth>(handle.Id);
            if (growth.HasValue) simulation.Set(handle.Id, growth.Value);
            else simulation.RemoveComponent<NaturalEntityGrowth>(handle.Id);
            if (climate.HasValue) simulation.Set(handle.Id, climate.Value);
            else simulation.RemoveComponent<NaturalEntityClimate>(handle.Id);
            if (harvest.HasValue) simulation.Set(handle.Id, harvest.Value);
            else simulation.RemoveComponent<NaturalEntityHarvest>(handle.Id);
            InstallPlantModules(record, snapshot, false);
            InstallExtensionModules(record, false);
            MarkPresentationDirty(record);

            if (nextBody.Suspended == 0)
            {
                TryRegisterNavigation(record);
                FreezeEnvironment(record);
                FreezeSoil(record);
                RegisterSpatial(record, PhysicsBodyChangeReason.SnapshotRefresh);
            }
        }

        /// <summary>F5 只替换配置并保留原 Entity，未知能力保留原状态并报告错误。</summary>
        public static bool TryRefreshDefinition(NaturalEntityHandle handle, RuntimeItemDefinition definition)
        {
            if (!Contains(handle)) return false;
            Record record = records[handle.Id];
            if (ReferenceEquals(record.Profile.Definition, definition) &&
                record.Profile.RegistryRevision == ResourceEntityCapabilityRegistry.Revision) return true;
            if (!TryGetProfile(definition, out NaturalEntityEcsProfile replacement, out _)) return false;
            ValidateOutputDefinitions(replacement);
            if (!TryCapture(handle, out ItemData snapshot)) return false;
            float baseline = simulation.TryGet(handle.Id, out NaturalEntityClimate climate) ? climate.BaselineCelsius : 0f;
            NaturalEntityEcsProfile previous = record.Profile;
            record.Profile = replacement;
            try
            {
                ApplySnapshot(handle, snapshot, baseline);
                profiles.Remove(previous.Definition);
                return true;
            }
            catch (Exception exception)
            {
                record.Profile = previous;
                ApplySnapshot(handle, snapshot, baseline, rebaseCurrentDefinition: false);
                Debug.LogError($"[NaturalEntities] {definition.Id} 刷新失败，已恢复原实体状态：{exception}");
                return false;
            }
        }

        #endregion

        #region 查询与持久化

        public static bool TryGetDefinition(NaturalEntityHandle handle, out RuntimeItemDefinition definition)
        {
            definition = Contains(handle) ? records[handle.Id].Profile.Definition : null;
            return definition != null;
        }

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
            WritePlantState(record, record.Snapshot);
            CaptureExtensionModules(record);
            snapshot = FastCloner.FastCloner.DeepClone(record.Snapshot);
            return true;
        }

        /// <summary>终端解绑直接移交内部快照所有权，避免卸载区块时再深拷贝一份 ItemData。</summary>
        public static bool TryTakeSnapshotAndRemove(NaturalEntityHandle handle, out ItemData snapshot)
        {
            snapshot = null;
            if (!Contains(handle))
                return false;

            PrepareForCapture(handle);
            if (!Contains(handle))
                return false;

            Record record = records[handle.Id];
            WriteHotState(record, record.Snapshot);
            WritePlantState(record, record.Snapshot);
            CaptureExtensionModules(record);
            snapshot = record.Snapshot;
            Remove(handle);
            return snapshot != null;
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
            if (simulation.TryGet(handle.Id, out EntityPlantLifecycle plant) && plant.Cultivated != 0) return false;
            year = season.Year + 1;
            return true;
        }

        /// <summary>只有远距离仍会自行变化的能力需要周期写生态差量；纯矿点保持可由世界种子重建。</summary>
        public static bool RequiresRuntimePersistence(NaturalEntityHandle handle)
            => Contains(handle);

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

            if (profile.HealthDefaults != null)
            {
                Mod_DamageReceiver.DamageReceiver_SaveData state = ReadHealthState(record);
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
                if (!profile.HasCrop) Mod_Grow.InitializeNaturalGrowthData(state, snapshot.Guid, record.Precipitation);
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

                if (!profile.HasCrop && component.Scales.Length > component.Stage)
                {
                    float stageScale = component.Scales[component.Stage];
                    body.Scale = new float2(stageScale, stageScale);
                }
                if (!profile.HasCrop && health.HasValue)
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

        private static Mod_DamageReceiver.DamageReceiver_SaveData ReadHealthState(Record record)
        {
            var result = FastCloner.FastCloner.DeepClone(record.Profile.HealthDefaults);
            if (TryGetModuleData(record.Snapshot, record.Profile.HealthModuleName, out ModuleData raw) &&
                raw is Ex_ModData data)
            {
                Mod_DamageReceiver.DamageReceiver_SaveData saved =
                    data.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>();
                if (saved != null)
                {
                    Mod_DamageReceiver.MigrateCombatBalance(saved, record.Profile.HealthDefaults);
                    result.Hp = saved.Hp;
                    result.MaxHp = saved.MaxHp;
                    result.AttackersUIDs = saved.AttackersUIDs;
                }
                if (!string.IsNullOrWhiteSpace(data.BitData))
                    record.DeathByDamage = JObject.Parse(data.BitData)["entityResource"]?.Value<bool?>("deathByDamage") == true;
            }
            return result;
        }

        private static GrowData ReadGrowthState(Record record)
        {
            GrowData result = FastCloner.FastCloner.DeepClone(record.Profile.GrowthDefaults);
            if (record.Profile.HasCrop)
            {
                CropRuntimeData crop = ReadCropState(record);
                result.GrowProgress = crop.stage == CropStage.Mature ? 1f : Mathf.Clamp01(crop.normalizedGrowth);
                result.isCultivatedCrop = crop.isPlanted;
                result.environmentInitialized = true;
                result.environmentGrowthMultiplier = 1f;
                result.simulationInitialized = crop.simulationInitialized;
                result.lastSimulatedTime = crop.lastSimulatedTime;
                result.plantedTilePos = crop.plantedTilePosition;
                return result;
            }
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
            if (!record.Profile.HasClimate ||
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
            simulation.Set(record.Handle.Id, climate, notifyChanged: false);
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
                Mod_DamageReceiver.DamageReceiver_SaveData state =
                    healthData.GetData<Mod_DamageReceiver.DamageReceiver_SaveData>() ??
                    FastCloner.FastCloner.DeepClone(record.Profile.HealthDefaults);
                state.Hp = health.Hp;
                state.MaxHp = health.MaxHp;
                healthData.WriteData(state);
                JObject payload = JObject.Parse(healthData.BitData);
                payload["entityResource"] = new JObject { ["deathByDamage"] = record.DeathByDamage };
                healthData.BitData = payload.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (simulation.TryGet(record.Handle.Id, out NaturalEntityGrowth growth) &&
                !record.Profile.HasCrop &&
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
            if (!profiles.TryGetValue(definition, out CompileCacheEntry entry) ||
                entry.RegistryRevision != ResourceEntityCapabilityRegistry.Revision)
            {
                bool supported = NaturalEntityEcsProfileCompiler.TryCompile(
                    definition, out NaturalEntityEcsProfile compiled, out string failure);
                entry = new CompileCacheEntry
                {
                    Profile = supported ? compiled : null,
                    Reason = supported ? null : failure,
                    RegistryRevision = ResourceEntityCapabilityRegistry.Revision
                };
                profiles[definition] = entry;
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
