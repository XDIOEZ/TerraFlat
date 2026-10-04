using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Networking;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    /// <summary>复杂结果时间线保存在 Entity 的托管组件中，沿用可补算的事件规则，不创建树果 GameObject。</summary>
    public sealed class EntityCanopyFruitModule : IComponentData
    {
        public CanopyFruitState State = new();
    }

    public static partial class NaturalEntityEcsService
    {
        #region 实体运行边界

        private sealed partial class Record
        {
            public string DimensionId;
            public Action<NaturalEntityHandle> Removed;
            public bool Busy, DeathByDamage, Highlighted;
            public float FlashUntil;
            public uint IndexedRevision;
            public int RenderPartCount;
            public bool CanopyWasLive;
        }

        private static readonly List<int> endedPlants = new();
        private static readonly Dictionary<Vector2Int, int> cultivatedCells = new();

        public static void BindRemovalHandler(NaturalEntityHandle handle, Action<NaturalEntityHandle> removed)
        {
            if (!Contains(handle)) throw new InvalidOperationException("植物实体已失效。");
            records[handle.Id].Removed = removed;
        }

        /// <summary>MOD 可在主线程通过真实 Entity 组合额外组件，不需要任何植物外壳。</summary>
        public static bool TryGetEntity(NaturalEntityHandle handle, out Entity entity)
        {
            entity = Contains(handle) ? simulation.GetEntity(handle.Id) : Entity.Null;
            return entity != Entity.Null;
        }

        public static bool IsCultivatedCellOccupied(Vector2Int cell) =>
            cultivatedCells.ContainsKey(WorldTopologyRuntime.NormalizeCell(cell));

        private static bool ReadClock(out TimeData clock, out double now)
        {
            now = 0d;
            if (!DayTimeSystem.Instance.TryGetActiveTimeData(out clock) || clock.DayLength <= 0f) return false;
            now = clock.TotalDays * (double)clock.DayLength + clock.CurrentTime;
            return true;
        }

        #endregion

        #region 纯数据植物装配与存档

        private static CropRuntimeData ReadCropState(Record record)
        {
            CropRuntimeData state = FastCloner.FastCloner.DeepClone(record.Profile.CropDefaults);
            if (TryGetModuleData(record.Snapshot, record.Profile.CropModuleName, out ModuleData module) &&
                module is Ex_ModData_MemoryPackable data && data.GetData<CropRuntimeData>() is { } saved)
                state = saved;
            state ??= new CropRuntimeData();
            state.normalizedGrowth = state.stage == CropStage.Mature ? 1f : Mathf.Clamp01(state.normalizedGrowth);
            return state;
        }

        private static void InitializePlantedSnapshot(Record record, Vector2Int? plantedCell)
        {
            if (!plantedCell.HasValue) return;
            if (!record.Profile.HasGrowth) throw new InvalidOperationException("播种目标必须具备生长能力。");
            Vector2Int cell = WorldTopologyRuntime.NormalizeCell(plantedCell.Value);
            if (IsCultivatedCellOccupied(cell)) throw new InvalidOperationException("种植格已经有实体植株。");
            if (!ReadClock(out _, out double now)) throw new InvalidOperationException("植物世界时钟尚未就绪，不能提交播种。");
            record.Snapshot.transform.position = new Vector3(cell.x + 0.5f, cell.y + 0.5f);
            if (record.Profile.HasCrop)
            {
                var state = new CropRuntimeData { isPlanted = true, plantedTilePosition = cell,
                    simulationInitialized = true, lastSimulatedTime = now };
                ((Ex_ModData_MemoryPackable)record.Snapshot.ModuleDataDic[record.Profile.CropModuleName]).WriteData(state);
            }
            else
            {
                GrowData state = FastCloner.FastCloner.DeepClone(record.Profile.GrowthDefaults);
                state.GrowProgress = 0f; state.growState = Mod_Grow.GrowState.幼苗;
                state.isMature = false; state.isHarvested = false; state.isCultivatedCrop = true;
                state.plantedTilePos = cell; state.environmentInitialized = true; state.environmentGrowthMultiplier = 1f;
                state.simulationInitialized = true; state.lastSimulatedTime = now;
                ((Ex_ModData_MemoryPackable)record.Snapshot.ModuleDataDic[record.Profile.GrowthModuleName]).WriteData(state);
            }
            if (record.Profile.HasClimate)
                ((Ex_ModData_MemoryPackable)record.Snapshot.ModuleDataDic[record.Profile.ClimateModuleName]).WriteData(
                    new PlantClimateState { ClockInitialized = true, LastSimulationTime = now });
            if (TryGetModuleData(record.Snapshot, record.Profile.StockModuleName, out ModuleData stock) && stock is CollectableModuleData inventory)
            { inventory.CurrentStock = 0; inventory.IsInitialized = false; }
        }

        private static void InstallPlantModules(Record record, ItemData persistedData, bool freshlyPlanted)
        {
            int id = record.Handle.Id;
            NaturalEntityEcsProfile profile = record.Profile;
            if (profile.HasGrowth)
            {
                GrowData state = ReadGrowthState(record);
                if (!profile.HasCrop) Mod_Grow.InitializeNaturalGrowthData(state, record.Snapshot.Guid, record.Precipitation);
                ReadClock(out TimeData clock, out double now);
                double cursor = state.lastSimulatedTime;
                if (!state.simulationInitialized)
                    cursor = clock == null ? now : Math.Max(0d, now - SeasonCalendar.Sample(clock).ElapsedDays * clock.DayLength);
                if (simulation.TryGet(id, out EntityClimate climate) && climate.ClockInitialized != 0)
                    cursor = Math.Min(cursor, climate.LastSimulationTime);
                var plant = new EntityPlantLifecycle
                {
                    Initialized = 1, LastWorldTime = cursor,
                    Cultivated = (byte)(state.isCultivatedCrop ? 1 : 0), SoilCell = new int2(state.plantedTilePos.x, state.plantedTilePos.y),
                    GrowWild = (byte)(profile.HasCrop ? 0 : 1), ScaleByStage = (byte)(profile.HasCrop ? 0 : 1),
                    Harvested = (byte)(state.isHarvested ? 1 : 0),
                    Status = state.GrowProgress >= state.MaxGrowProgress ? EntityPlantGrowthStatus.Mature : EntityPlantGrowthStatus.Growing
                };
                if (profile.HasCrop) plant.Harvested = (byte)(ReadCropState(record).isHarvested ? 1 : 0);
                simulation.Set(id, plant);
                simulation.Set(id, profile.Soil);
                if (plant.Cultivated != 0)
                {
                    Vector2Int cell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(plant.SoilCell.x, plant.SoilCell.y));
                    if (cultivatedCells.TryGetValue(cell, out int existing) && existing != id)
                        throw new InvalidOperationException("一个耕地格不能拥有两个植物权威。");
                    cultivatedCells[cell] = id;
                }
            }
            else
            {
                simulation.RemoveComponent<EntityPlantLifecycle>(id);
                simulation.RemoveComponent<EntityPlantSoil>(id);
            }
            if (profile.Collection is { } collection)
            {
                var stock = new EntityResourceStock
                {
                    ItemId = collection.CollectItemId, Capacity = collection.MaxStock,
                    InitialMinimum = collection.NaturalInitialStockMin, InitialMaximum = collection.NaturalInitialStockMax,
                    Seed = unchecked((uint)record.Snapshot.Guid)
                };
                if (!freshlyPlanted && TryGetModuleData(record.Snapshot, profile.StockModuleName, out ModuleData raw) &&
                    raw is CollectableModuleData savedStock)
                { stock.Count = Mathf.Clamp(savedStock.CurrentStock, 0, stock.Capacity); stock.Initialized = (byte)(savedStock.IsInitialized ? 1 : 0); }
                simulation.Set(id, stock);
                List<Mod_Production.ItemProductionData> savedRules = null;
                if (!freshlyPlanted && TryGetModuleData(persistedData, profile.ProductionModuleName, out ModuleData oldRules) &&
                    oldRules is Ex_ModData_MemoryPackable oldProduction)
                    savedRules = oldProduction.GetData<List<Mod_Production.ItemProductionData>>();
                DynamicBuffer<EntityStockProduction> buffer = simulation.Buffer<EntityStockProduction>(id);
                buffer.Clear();
                if (profile.Production != null)
                {
                    var claimedSavedRules = new HashSet<int>();
                    for (int i = 0; i < profile.Production.Count; i++)
                    {
                        var config = profile.Production[i];
                        Mod_Production.ItemProductionData saved = null;
                        if (savedRules != null)
                            for (int old = 0; old < savedRules.Count; old++)
                                if (!claimedSavedRules.Contains(old) && savedRules[old]?.itemName == config.itemName)
                                { claimedSavedRules.Add(old); saved = savedRules[old]; break; }
                        uint randomSeed = math.hash(new uint2(stock.Seed, (uint)i + 1u)) | 1u;
                        config.ResolveYieldGene(randomSeed ^ 0xA511E9B3u, saved?.YieldGeneVariantIndex ?? -1,
                            out int geneVariantIndex, out int minimumAmount, out int maximumAmount);
                        buffer.Add(new EntityStockProduction
                        {
                            ItemId = config.itemName, Duration = config.MaxProductionTime, IntervalDays = config.ProductionIntervalDays,
                            Speed = profile.ProductionSpeed,
                            Probability = config.SpawnProbability, InitialProgressRange = config.Random_ProductionTime,
                            MinimumAmount = minimumAmount, MaximumAmount = maximumAmount, GeneVariantIndex = geneVariantIndex,
                            Limit = config.MaxProductionCount, Progress = Mathf.Max(0f, saved?.ProductionTime ?? config.ProductionTime),
                            Completed = saved?.CurrentProductionCount ?? 0, Initialized = (byte)(saved?.IsInitialized == true ? 1 : 0),
                            RandomState = saved?.EntityRandomState is > 0 ? saved.EntityRandomState : randomSeed,
                            UseGrowthDifficulty = (byte)(profile.ProductionUsesGrowthDifficulty ? 1 : 0)
                        });
                    }
                }
            }
            else
            {
                simulation.RemoveComponent<EntityResourceStock>(id);
                Entity entity = simulation.GetEntity(id);
                if (simulation.Manager.HasComponent<EntityStockProduction>(entity))
                    simulation.Manager.RemoveComponent<EntityStockProduction>(entity);
            }
            if (profile.Canopy != null)
            {
                CanopyFruitState state = null;
                if (TryGetModuleData(record.Snapshot, profile.CanopyModuleName, out ModuleData raw) && raw is Ex_ModData save)
                    state = save.GetData<CanopyFruitState>();
                Entity entity = simulation.GetEntity(id);
                var component = new EntityCanopyFruitModule { State = state ?? new CanopyFruitState() };
                if (simulation.Manager.HasComponent<EntityCanopyFruitModule>(entity))
                    simulation.Manager.GetComponentObject<EntityCanopyFruitModule>(entity).State = component.State;
                else simulation.Manager.AddComponentObject(entity, component);
                record.CanopyWasLive = false;
            }
            else
            {
                Entity entity = simulation.GetEntity(id);
                if (simulation.Manager.HasComponent<EntityCanopyFruitModule>(entity))
                    simulation.Manager.RemoveComponent<EntityCanopyFruitModule>(entity);
            }
        }

        private static void WritePlantState(Record record, ItemData snapshot)
        {
            int id = record.Handle.Id;
            if (simulation.TryGet(id, out EntityPlantLifecycle plant) && simulation.TryGet(id, out EntityGrowth growth))
            {
                if (record.Profile.HasCrop)
                {
                    CropRuntimeData state = ReadCropState(record);
                    state.normalizedGrowth = growth.Progress / growth.MaxProgress;
                    state.stage = growth.Progress >= growth.MaxProgress ? CropStage.Mature : CropStage.Seedling;
                    state.isHarvested = plant.Harvested != 0; state.isPlanted = plant.Cultivated != 0;
                    state.plantedTilePosition = new Vector2Int(plant.SoilCell.x, plant.SoilCell.y);
                    state.lastSimulatedTime = plant.LastWorldTime; state.simulationInitialized = plant.Initialized != 0;
                    ((Ex_ModData_MemoryPackable)snapshot.ModuleDataDic[record.Profile.CropModuleName]).WriteData(state);
                }
                else if (TryGetModuleData(snapshot, record.Profile.GrowthModuleName, out ModuleData raw) && raw is Ex_ModData_MemoryPackable data)
                {
                    GrowData state = data.GetData<GrowData>();
                    state.lastSimulatedTime = plant.LastWorldTime; state.simulationInitialized = plant.Initialized != 0;
                    state.isCultivatedCrop = plant.Cultivated != 0; state.isHarvested = plant.Harvested != 0;
                    state.plantedTilePos = new Vector2Int(plant.SoilCell.x, plant.SoilCell.y);
                    data.WriteData(state);
                }
            }
            if (record.Profile.Collection != null && simulation.TryGet(id, out EntityResourceStock stock))
            {
                var data = (CollectableModuleData)snapshot.ModuleDataDic[record.Profile.StockModuleName];
                data.CurrentStock = stock.Count; data.IsInitialized = stock.Initialized != 0;
                if (record.Profile.Production != null)
                {
                    var state = FastCloner.FastCloner.DeepClone(record.Profile.Production);
                    DynamicBuffer<EntityStockProduction> rules = simulation.Buffer<EntityStockProduction>(id);
                    for (int i = 0; i < state.Count && i < rules.Length; i++)
                    {
                        var rule = rules[i];
                        state[i].ProductionTime = rule.Progress; state[i].CurrentProductionCount = rule.Completed;
                        state[i].IsInitialized = rule.Initialized != 0; state[i].EntityRandomState = rule.RandomState;
                        state[i].YieldGeneVariantIndex = rule.GeneVariantIndex;
                    }
                    ((Ex_ModData_MemoryPackable)snapshot.ModuleDataDic[record.Profile.ProductionModuleName]).WriteData(state);
                }
            }
            if (record.Profile.Canopy != null)
                ((Ex_ModData)snapshot.ModuleDataDic[record.Profile.CanopyModuleName]).WriteData(GetCanopy(record));
        }

        #endregion

        #region 世界输入及副作用提交

        private static void FreezeSoil(Record record)
        {
            if (!simulation.TryGet(record.Handle.Id, out EntityPlantLifecycle plant) || plant.Cultivated == 0) return;
            EntityPlantSoil soil = record.Profile.Soil;
            if (FarmlandSystem.TryReadSoil(new Vector2Int(plant.SoilCell.x, plant.SoilCell.y), out TileData_Farmland source))
            {
                soil.Available = 1; soil.Water = source.waterValue; soil.MaxWater = source.maxWater;
                soil.Fertility = source.Fertility; soil.MaxFertility = source.maxFertility;
            }
            simulation.Set(record.Handle.Id, soil);
        }

        private static void CommitSoil(Record record)
        {
            if (!simulation.TryGet(record.Handle.Id, out EntityPlantSoil soil) ||
                (soil.AddedWater == 0f && soil.UsedWater == 0f && soil.UsedFertility == 0f)) return;
            if (simulation.TryGet(record.Handle.Id, out EntityPlantLifecycle plant) &&
                FarmlandSystem.TryReadSoil(new Vector2Int(plant.SoilCell.x, plant.SoilCell.y), out TileData_Farmland target))
            {
                target.AddWater(soil.AddedWater); target.ConsumeWater(soil.UsedWater);
                target.ConsumeFertility(soil.UsedFertility); FarmlandSystem.CommitSoil(target);
            }
            soil.AddedWater = soil.UsedWater = soil.UsedFertility = 0f;
            simulation.Set(record.Handle.Id, soil);
        }

        private static void PublishEntities(float deltaTime)
        {
            if (simulation == null || !GameNetwork.HasStateAuthority) return;
            bool hasClock = ReadClock(out _, out double now);
            endedPlants.Clear();
            foreach (Record record in records.Values)
            {
                CommitSoil(record);
                NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
                if (body.Dead != 0) { endedPlants.Add(record.Handle.Id); continue; }
                if (hasClock && record.Profile.Canopy != null) AdvanceCanopy(record, now);
                if (body.VisualVersion != record.PresentedRevision)
                    MarkPresentationDirty(record);
                if (body.VisualVersion != record.IndexedRevision)
                {
                    // 外观、果实和受击版本仍刷新空间索引，但相同阻挡矩形不通知物理层。
                    Bounds previousBounds = record.BodyBounds;
                    bool previouslyBlocking = record.BlocksMovement;
                    UnregisterSpatial(record, PhysicsBodyChangeReason.VisualRevision, notifyPhysics: false);
                    RegisterSpatial(record, PhysicsBodyChangeReason.VisualRevision, notifyPhysics: false);
                    if (previouslyBlocking != record.BlocksMovement || !previousBounds.Equals(record.BodyBounds))
                    {
                        if (previouslyBlocking)
                        {
                            PhysicsBodyChanged?.Invoke(previousBounds);
                            PhysicsBodyChangedWithReason?.Invoke(previousBounds, PhysicsBodyChangeReason.VisualRevision,
                                record.Snapshot.Guid, record.Profile.Definition.Id);
                            BlockingBodyChanged?.Invoke(new BlockingBodySnapshot(record.Handle.Id, record.Snapshot.Guid,
                                record.Profile.Definition.Id, previousBounds), false, PhysicsBodyChangeReason.VisualRevision);
                        }
                        if (record.BlocksMovement) PublishPhysicsBodyChanged(record, PhysicsBodyChangeReason.VisualRevision, true);
                    }
                }
            }
            foreach (int id in endedPlants)
            {
                if (!records.TryGetValue(id, out Record record)) continue;
                PublishDeath(record);
            }
            endedPlants.Clear();
        }

        private static void ValidateOutputDefinitions(NaturalEntityEcsProfile profile)
        {
            void Require(string itemId)
            {
                if (string.IsNullOrWhiteSpace(itemId)) return;
                if (!GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
                    throw new InvalidOperationException($"植物 {profile.Definition.Id} 引用了不存在的产物 {itemId}。");
                if (definition.IsActor || definition.UsesResourceEntities)
                    throw new InvalidOperationException($"植物资源产出 {itemId} 必须是可交付物品，生物或植株生成应使用独立能力。");
                if ((itemId == profile.Collection?.CollectItemId || itemId == profile.Canopy?.FruitItemId ||
                    itemId == profile.Canopy?.SplitItemId) && (definition.Sprite == null || definition.Material == null))
                    throw new InvalidOperationException($"植物 {profile.Definition.Id} 的果实 {itemId} 缺少共享表现资源。");
            }
            Require(profile.Collection?.CollectItemId);
            Require(profile.Canopy?.FruitItemId); Require(profile.Canopy?.SplitItemId);
            Require(profile.TreeFoodId); Require(profile.TreeSeedId); Require(profile.TemperatureYield?.OutputItemId);
            if (profile.HarvestOutputs != null) foreach (CropYieldEntry entry in profile.HarvestOutputs) Require(entry.itemId);
        }

        private static void ClearResourceQueries()
        {
            spatialCells.Clear(); cultivatedCells.Clear(); endedPlants.Clear();
            queryLists.Clear(); spatialQuerySequence = 0;
            canopyHits.Clear(); canopyReceivers.Clear();
        }

        #endregion
    }
}
