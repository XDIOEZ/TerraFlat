using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 植物与资源模块数据

    public enum EntityPlantGrowthStatus : byte { Growing, MissingSoil, NeedsWater, NeedsFertility, Mature, Harvested, TemperatureStress }

    /// <summary>植物生命周期只保存实例状态，生长与耐候仍使用共享能力组件。</summary>
    public struct EntityPlantLifecycle : IComponentData
    {
        public double LastWorldTime;
        public int2 SoilCell;
        public float ClimateStress;
        public byte Initialized, Cultivated, GrowWild, ScaleByStage, Harvested;
        public EntityPlantGrowthStatus Status;
    }

    /// <summary>主线程冻结耕地输入，Job 只输出本次消耗，提交时不覆盖其它系统的土壤修改。</summary>
    public struct EntityPlantSoil : IComponentData
    {
        public float Water, MaxWater, Fertility, MaxFertility;
        public float MinimumWaterMultiplier, MinimumFertilityMultiplier;
        public float WaterPerSecond, FertilityPerSecond, RainWaterPerSecond;
        public float UsedWater, UsedFertility, AddedWater;
        public byte Available;
    }

    /// <summary>可采集资源库存独立于植物，任何实体均可组合相同库存和生产模块。</summary>
    public struct EntityResourceStock : IComponentData
    {
        public FixedString64Bytes ItemId;
        public int Count, Capacity, InitialMinimum, InitialMaximum;
        public uint Seed;
        public byte Initialized;
    }

    [InternalBufferCapacity(1)]
    public struct EntityStockProduction : IBufferElementData
    {
        public FixedString64Bytes ItemId;
        public float Progress, Duration, IntervalDays, Speed, Probability;
        public float2 InitialProgressRange;
        public int MinimumAmount, MaximumAmount, Limit, Completed, GeneVariantIndex;
        public uint RandomState;
        public byte Initialized, UseGrowthDifficulty;
    }

    #endregion

    #region 共享植物数值规则

    public static class EntityPlantRules
    {
        /// <summary>独立耐候与植物生命周期共用冷热暴露数值核。</summary>
        public static float AdvanceClimate(ref EntityClimate climate, float temperature, float seconds, float dayLength)
        {
            float fatal = math.max(0.01f, climate.FatalExposureHours * dayLength / 24f);
            climate.ColdSeconds = temperature < climate.MinimumSurvivalTemperature
                ? climate.ColdSeconds + seconds : math.max(0f, climate.ColdSeconds - seconds * climate.RecoveryRate);
            climate.HeatSeconds = temperature > climate.MaximumSurvivalTemperature
                ? climate.HeatSeconds + seconds : math.max(0f, climate.HeatSeconds - seconds * climate.RecoveryRate);
            if (climate.ColdSeconds >= fatal || climate.HeatSeconds >= fatal) climate.Dead = 1;
            climate.GrowthMultiplier = climate.Dead != 0 || temperature < climate.MinimumSurvivalTemperature ||
                temperature > climate.MaximumSurvivalTemperature ? 0f :
                temperature < climate.MinimumGrowthTemperature || temperature > climate.MaximumGrowthTemperature ? 0.5f : 1f;
            return climate.GrowthMultiplier;
        }

        /// <summary>成长换阶段保持生命百分比，不通过重建生命模块刷新血量。</summary>
        public static void ApplyStage(ref EntityGrowth growth, ref EntityModuleAppearance appearance,
            ref AiecsVital vital, bool scaleBody)
        {
            int stage = 0;
            for (int i = 0; i < growth.Thresholds.Length; i++)
                if (growth.Progress >= growth.Thresholds[i]) stage = i;
            if (stage == growth.Stage) return;
            growth.Stage = (byte)stage;
            appearance.Revision++;
            if (!scaleBody) return;
            if (stage < growth.Scales.Length) appearance.Scale = new float2(growth.Scales[stage]);
            if (stage >= growth.HealthRatios.Length || growth.MatureMaxHealth <= 0f) return;
            float fraction = vital.MaxHp > 0f ? math.saturate(vital.Hp / vital.MaxHp) : 1f;
            vital.MaxHp = math.max(1f, growth.MatureMaxHealth * growth.HealthRatios[stage]);
            vital.Hp = vital.MaxHp * fraction;
        }
    }

    #endregion

    #region 植物与库存并行调度

    /// <summary>借用统一 World；没有逐植物 MonoBehaviour、Collider 或独立世界。</summary>
    [DisableAutoCreation]
    public partial class EntityPlantModuleSystem : SystemBase
    {
        public double GameTime;
        public float StepSeconds, DayLength, RainIntensity, RainGrowthIntensity, WeatherMultiplier, GrowthDifficulty;
        public bool Seasonal;
        private NativeArray<EntitySeasonPeriod> seasons;
        private EntityQuery plants, stocks;

        protected override void OnCreate()
        {
            plants = GetEntityQuery(ComponentType.ReadWrite<EntityPlantLifecycle>(), ComponentType.ReadWrite<EntityPlantSoil>(),
                ComponentType.ReadWrite<EntityGrowth>(), ComponentType.ReadWrite<AiecsVital>(),
                ComponentType.ReadWrite<EntityModuleAppearance>(), ComponentType.ReadOnly<EntityModuleActive>());
            stocks = GetEntityQuery(ComponentType.ReadWrite<EntityResourceStock>(), ComponentType.ReadWrite<EntityStockProduction>(),
                ComponentType.ReadOnly<AiecsVital>(), ComponentType.ReadWrite<EntityModuleAppearance>(),
                ComponentType.ReadOnly<EntityModuleActive>());
        }

        public void SetSeasons(EntitySeasonPeriod[] values)
        {
            Complete();
            if (!seasons.IsCreated || seasons.Length != values.Length)
            {
                if (seasons.IsCreated) seasons.Dispose();
                seasons = new NativeArray<EntitySeasonPeriod>(values.Length, Allocator.Persistent);
            }
            seasons.CopyFrom(values);
        }

        protected override void OnUpdate()
        {
            // 在 OnUpdate 内合并本轮输入，避免 SystemBase 的前置更新把新任务提前 Complete。
            Dependency = Unity.Jobs.JobHandle.CombineDependencies(Dependency, inputDependency);
            inputDependency = default;
            if (!plants.IsEmptyIgnoreFilter)
                Dependency = new PlantJob
                {
                    GameTime = GameTime, Delta = StepSeconds, DayLength = math.max(0.01f, DayLength),
                    Rain = RainIntensity, RainGrowth = RainGrowthIntensity, Weather = WeatherMultiplier,
                    Difficulty = GrowthDifficulty, Seasonal = Seasonal, Seasons = seasons,
                    Climates = GetComponentLookup<EntityClimate>()
                }.ScheduleParallel(plants, Dependency);
            if (!stocks.IsEmptyIgnoreFilter)
                Dependency = new StockJob
                {
                    Delta = StepSeconds, DayLength = math.max(0.01f, DayLength), Difficulty = GrowthDifficulty,
                    Growth = GetComponentLookup<EntityGrowth>(true), Climates = GetComponentLookup<EntityClimate>(true)
                }.ScheduleParallel(stocks, Dependency);
        }

        private Unity.Jobs.JobHandle inputDependency;
        public void Complete() => Unity.Jobs.JobHandle.CombineDependencies(Dependency, inputDependency).Complete();
        public void DependOn(Unity.Jobs.JobHandle jobs) =>
            inputDependency = Unity.Jobs.JobHandle.CombineDependencies(inputDependency, jobs);
        protected override void OnDestroy() { Complete(); if (seasons.IsCreated) seasons.Dispose(); }

        [BurstCompile]
        private partial struct PlantJob : IJobEntity
        {
            public double GameTime;
            public float Delta, DayLength, Rain, RainGrowth, Weather, Difficulty;
            public bool Seasonal;
            [ReadOnly] public NativeArray<EntitySeasonPeriod> Seasons;
            // 每次 Execute 只写自身 Entity 的耐候组件，不跨实体写入。
            [NativeDisableParallelForRestriction] public ComponentLookup<EntityClimate> Climates;

            private void Execute(Entity entity, ref EntityPlantLifecycle plant, ref EntityPlantSoil soil,
                ref EntityGrowth growth, ref AiecsVital vital, ref EntityModuleAppearance appearance)
            {
                if (vital.Dead != 0 || plant.Harvested != 0) return;
                bool hasClimate = Climates.HasComponent(entity);
                EntityClimate climate = hasClimate ? Climates[entity] : default;
                if (hasClimate && climate.EnvironmentReady == 0) return;
                if (plant.Initialized == 0 || GameTime < plant.LastWorldTime)
                { plant.Initialized = 1; plant.LastWorldTime = GameTime; }
                bool historical = GameTime - plant.LastWorldTime > 1d;
                float before = growth.Progress, oldStress = plant.ClimateStress;
                soil.UsedWater = soil.UsedFertility = soil.AddedWater = 0f;
                if (hasClimate) climate.CaughtUp = 0;
                double maxStep = math.min(30d, DayLength / 96d);
                for (int step = 0; step < 2048 && GameTime - plant.LastWorldTime > 0.00001d; step++)
                {
                    float seconds = (float)math.min(maxStep, GameTime - plant.LastWorldTime);
                    double midpointDay = plant.LastWorldTime + seconds * 0.5d;
                    float temperature = historical ? climate.BaselineCelsius +
                        (Seasonal ? SampleSeason(
                            midpointDay, climate.BaselineCelsius, climate.SeasonPoleProximity) : 0f) :
                        climate.AmbientCelsius;
                    float suitability = hasClimate ? EntityPlantRules.AdvanceClimate(ref climate, temperature, seconds, DayLength) : 1f;
                    plant.LastWorldTime += seconds;
                    if (hasClimate && climate.Dead != 0) { vital.Dead = 1; vital.Hp = 0f; break; }
                    if (plant.Cultivated == 0 || growth.Progress >= growth.MaxProgress) continue;
                    if (soil.Available == 0) { plant.Status = EntityPlantGrowthStatus.MissingSoil; continue; }
                    if (!historical)
                    {
                        float rainWater = math.min(math.max(0f, soil.MaxWater - soil.Water), soil.RainWaterPerSecond * Rain * seconds);
                        soil.Water += rainWater; soil.AddedWater += rainWater;
                    }
                    if (suitability <= 0f) { plant.Status = EntityPlantGrowthStatus.TemperatureStress; continue; }
                    if (soil.Water <= 0f) { plant.Status = EntityPlantGrowthStatus.NeedsWater; continue; }
                    if (soil.Fertility <= 0f) { plant.Status = EntityPlantGrowthStatus.NeedsFertility; continue; }
                    float multiplier = math.lerp(soil.MinimumWaterMultiplier, 1f, math.saturate(soil.Water / math.max(0.01f, soil.MaxWater))) *
                        math.lerp(soil.MinimumFertilityMultiplier, 1f, math.saturate(soil.Fertility / math.max(0.01f, soil.MaxFertility)));
                    float weather = historical ? 1f : Weather * (1f + RainGrowth * growth.RainGrowthBonus);
                    float increment = seconds * growth.Speed * multiplier * suitability * weather * math.max(0f, Difficulty);
                    if (increment <= 0f) continue;
                    growth.Progress = math.min(growth.MaxProgress, growth.Progress + increment);
                    float usedWater = math.min(soil.Water, soil.WaterPerSecond * seconds);
                    float usedFertility = math.min(soil.Fertility, soil.FertilityPerSecond * seconds);
                    soil.Water -= usedWater; soil.UsedWater += usedWater;
                    soil.Fertility -= usedFertility; soil.UsedFertility += usedFertility;
                    plant.Status = EntityPlantGrowthStatus.Growing;
                }
                bool caughtUp = GameTime - plant.LastWorldTime <= 0.00001d;
                if (hasClimate)
                {
                    climate.ClockInitialized = 1; climate.LastSimulationTime = plant.LastWorldTime;
                    climate.CaughtUp = (byte)(caughtUp ? 1 : 0);
                    if (caughtUp && !historical) EntityPlantRules.AdvanceClimate(ref climate, climate.AmbientCelsius, 0f, DayLength);
                    plant.ClimateStress = math.saturate(math.max(climate.ColdSeconds, climate.HeatSeconds) /
                        math.max(0.01f, climate.FatalExposureHours * DayLength / 24f));
                    Climates[entity] = climate;
                }
                if (vital.Dead == 0 && caughtUp && plant.Cultivated == 0 && plant.GrowWild != 0)
                {
                    float suitability = hasClimate ? climate.GrowthMultiplier : 1f;
                    growth.Progress = math.min(growth.MaxProgress, growth.Progress + Delta * growth.Speed *
                        math.max(0f, growth.EnvironmentMultiplier) * suitability * Weather *
                        (1f + RainGrowth * growth.RainGrowthBonus) * math.max(0f, Difficulty));
                }
                if (growth.Progress >= growth.MaxProgress) plant.Status = EntityPlantGrowthStatus.Mature;
                EntityPlantRules.ApplyStage(ref growth, ref appearance, ref vital, plant.ScaleByStage != 0);
                if ((plant.ScaleByStage == 0 && growth.Progress != before) ||
                    (int)(oldStress * 64f) != (int)(plant.ClimateStress * 64f) || vital.Dead != 0) appearance.Revision++;
            }

            private float SampleSeason(double day, float baselineCelsius, float poleProximity)
            {
                for (int i = 0; i < Seasons.Length; i++)
                    if (day < Seasons[i].EndTimeSeconds)
                        return Seasons[i].SamplePhysicalOffset(day, baselineCelsius, poleProximity);
                return 0f;
            }
        }

        [BurstCompile]
        private partial struct StockJob : IJobEntity
        {
            public float Delta, DayLength, Difficulty;
            [ReadOnly] public ComponentLookup<EntityGrowth> Growth;
            [ReadOnly] public ComponentLookup<EntityClimate> Climates;

            private void Execute(Entity entity, in AiecsVital vital, ref EntityResourceStock stock,
                ref EntityModuleAppearance appearance, DynamicBuffer<EntityStockProduction> production)
            {
                if (vital.Dead != 0) return;
                if (Growth.HasComponent(entity) && Growth[entity].Progress < Growth[entity].MaxProgress) return;
                if (Climates.HasComponent(entity) && (Climates[entity].CaughtUp == 0 || Climates[entity].Dead != 0)) return;
                int oldCount = stock.Count;
                if (stock.Initialized == 0)
                {
                    stock.Count = stock.InitialMinimum + (int)(stock.Seed % (uint)(stock.InitialMaximum - stock.InitialMinimum + 1));
                    stock.Initialized = 1;
                }
                for (int i = 0; i < production.Length; i++)
                {
                    EntityStockProduction rule = production[i];
                    if (!stock.ItemId.Equals(rule.ItemId) || (rule.Limit >= 0 && rule.Completed >= rule.Limit)) continue;
                    var random = new Random(rule.RandomState == 0 ? 1u : rule.RandomState);
                    if (rule.Initialized == 0)
                    {
                        rule.Progress = random.NextFloat(rule.InitialProgressRange.x, rule.InitialProgressRange.y);
                        rule.Initialized = 1;
                    }
                    bool calendarInterval = rule.IntervalDays > 0f;
                    float duration = calendarInterval ? math.max(0.01f, rule.IntervalDays * DayLength) : rule.Duration;
                    float rate = calendarInterval ? 1f : rule.Speed * (rule.UseGrowthDifficulty != 0 ? math.max(0f, Difficulty) : 1f);
                    rule.Progress += Delta * rate;
                    for (int work = 0; work < 64 && rule.Progress >= duration; work++)
                    {
                        if (random.NextFloat() < rule.Probability)
                        {
                            int amount = random.NextInt(rule.MinimumAmount, rule.MaximumAmount + 1);
                            if (amount > 0 && stock.Count < stock.Capacity)
                                stock.Count = math.min(stock.Capacity, stock.Count + amount);
                        }
                        rule.Progress -= duration;
                        rule.Completed++;
                        if (rule.Limit >= 0 && rule.Completed >= rule.Limit) break;
                    }
                    rule.RandomState = random.state;
                    production[i] = rule;
                }
                if (stock.Count != oldCount) appearance.Revision++;
            }
        }
    }

    #endregion
}
