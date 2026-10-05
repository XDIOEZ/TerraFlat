using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 通用能力组件

    /// <summary>能力执行开关；接口壳接管时关闭，不销毁组件或把实体塞入 AI 查询。</summary>
    public struct EntityModuleActive : IComponentData, IEnableableComponent { }

    /// <summary>通用实例表现参数；只在状态改变时发布版本，不持有 Renderer。</summary>
    public struct EntityModuleAppearance : IComponentData
    {
        public float2 Scale;
        public float Rotation;
        public uint Revision;
    }

    /// <summary>成长能力可装配到任意具有生命和阶段表现的实体，没有树种分支。</summary>
    public struct EntityGrowth : IComponentData
    {
        public float Progress, MaxProgress, Speed, EnvironmentMultiplier;
        public float MatureMaxHealth, RainGrowthBonus;
        public byte Stage;
        public FixedList64Bytes<float> Thresholds, Scales, HealthRatios;
    }

    /// <summary>环境输入由主线程批量冻结；冷热负担和世界时间游标由能力 Job 持有。</summary>
    public struct EntityClimate : IComponentData
    {
        public float BaselineCelsius, AmbientCelsius;
        public float MinimumGrowthTemperature, MaximumGrowthTemperature;
        public float MinimumSurvivalTemperature, MaximumSurvivalTemperature;
        public float FatalExposureHours, RecoveryRate, ColdSeconds, HeatSeconds;
        public float GrowthMultiplier;
        public double LastSimulationTime;
        public byte ClockInitialized, Dead, EnvironmentReady, CaughtUp;
    }

    /// <summary>工具条件属于可复用能力数据，不以矿物名称或伤害大小猜开采资格。</summary>
    public struct EntityHarvestRequirement : IComponentData
    {
        public int ToolKind, MinimumTier;
    }

    #endregion

    #region 冻结季节输入

    /// <summary>一段季节配置历史的纯值副本；最后一段 EndDay 为正无穷。</summary>
    public struct EntitySeasonPeriod
    {
        public double EndDay, OffsetDays;
        public float4 Days, Temperatures;

        /// <summary>日历与批量耐候共用同一数值核，修改季长不会重写历史气候。</summary>
        public float Sample(double absoluteDay, out int index, out double completedYears, out float progress)
        {
            double elapsed = math.max(0d, absoluteDay + OffsetDays);
            double yearDays = (double)Days.x + Days.y + Days.z + Days.w;
            completedYears = math.floor(elapsed / yearDays);
            double remaining = elapsed - completedYears * yearDays;
            index = 0;
            while (index < 3 && remaining >= Days[index]) remaining -= Days[index++];
            progress = (float)(remaining / Days[index]);
            float center = Temperatures[index];
            float previous = completedYears == 0d && index == 0 ? center : Temperatures[(index + 3) % 4];
            float next = Temperatures[(index + 1) % 4];
            float t = math.saturate(progress < 0.5f ? progress * 2f : (progress - 0.5f) * 2f);
            t = t * t * (3f - 2f * t);
            return progress < 0.5f
                ? math.lerp((previous + center) * 0.5f, center, t)
                : math.lerp(center, (center + next) * 0.5f, t);
        }
    }

    #endregion

    #region 通用并行系统

    /// <summary>共享 World 内的可组合能力系统；查询只要求相应组件，不判断实体是动物还是自然物。</summary>
    [DisableAutoCreation]
    public partial class EntityCapabilitySystem : SystemBase
    {
        public float StepSeconds, DayLength, RainIntensity, DifficultyGrowthMultiplier, WeatherMultiplier;
        public double GameTime;
        public bool Seasonal;
        private NativeArray<EntitySeasonPeriod> seasons;
        private EntityQuery climateQuery, growthQuery;

        protected override void OnCreate()
        {
            // 带植物生命周期的实体需要按同一历史时间段同时结算气候与土壤，不能重复推进。
            climateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<EntityClimate>(), ComponentType.ReadWrite<AiecsVital>(), ComponentType.ReadOnly<EntityModuleActive>() },
                None = new[] { ComponentType.ReadOnly<EntityPlantLifecycle>() }
            });
            growthQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<EntityGrowth>(), ComponentType.ReadWrite<EntityModuleAppearance>(),
                    ComponentType.ReadWrite<AiecsVital>(), ComponentType.ReadOnly<EntityModuleActive>() },
                None = new[] { ComponentType.ReadOnly<EntityPlantLifecycle>() }
            });
        }

        /// <summary>历史快照只在内容改变时替换，普通批次复用 Native 内存。</summary>
        public void SetSeasons(EntitySeasonPeriod[] values)
        {
            Complete();
            if (values == null) return;
            if (!seasons.IsCreated || seasons.Length != values.Length)
            {
                if (seasons.IsCreated) seasons.Dispose();
                seasons = new NativeArray<EntitySeasonPeriod>(values.Length, Allocator.Persistent);
            }
            seasons.CopyFrom(values);
        }

        protected override void OnUpdate()
        {
            if (!climateQuery.IsEmptyIgnoreFilter)
                Dependency = new ClimateJob
                {
                    GameTime = GameTime, DayLength = math.max(0.01f, DayLength),
                    Seasonal = Seasonal, Seasons = seasons
                }.ScheduleParallel(climateQuery, Dependency);
            if (!growthQuery.IsEmptyIgnoreFilter)
                Dependency = new GrowthJob
                {
                    DeltaTime = StepSeconds, RainIntensity = RainIntensity,
                    WeatherMultiplier = WeatherMultiplier,
                    DifficultyMultiplier = DifficultyGrowthMultiplier,
                    Climates = GetComponentLookup<EntityClimate>(true)
                }.ScheduleParallel(growthQuery, Dependency);
        }

        public void Complete() => Dependency.Complete();

        protected override void OnDestroy()
        {
            Complete();
            if (seasons.IsCreated) seasons.Dispose();
        }

        [BurstCompile]
        private partial struct ClimateJob : IJobEntity
        {
            public double GameTime;
            public float DayLength;
            public bool Seasonal;
            [ReadOnly] public NativeArray<EntitySeasonPeriod> Seasons;

            private void Execute(ref EntityClimate climate, ref AiecsVital vital)
            {
                if (vital.Dead != 0 || climate.Dead != 0 || climate.EnvironmentReady == 0) return;
                if (climate.ClockInitialized == 0 || GameTime < climate.LastSimulationTime)
                {
                    climate.ClockInitialized = 1;
                    climate.LastSimulationTime = GameTime;
                }
                bool historical = GameTime - climate.LastSimulationTime > 1d;
                double maxStep = math.min(30d, DayLength / 96d);
                climate.CaughtUp = 0;
                float fatalSeconds = math.max(0.01f, climate.FatalExposureHours * DayLength / 24f);
                for (int segment = 0; segment < 2048 && GameTime - climate.LastSimulationTime > 0.00001d; segment++)
                {
                    float seconds = (float)math.min(maxStep, GameTime - climate.LastSimulationTime);
                    double midpoint = (climate.LastSimulationTime + seconds * 0.5d) / DayLength;
                    float temperature = historical ? climate.BaselineCelsius +
                        (Seasonal ? SampleSeason(midpoint) : 0f) : climate.AmbientCelsius;
                    climate.ColdSeconds = temperature < climate.MinimumSurvivalTemperature
                        ? climate.ColdSeconds + seconds : math.max(0f, climate.ColdSeconds - seconds * climate.RecoveryRate);
                    climate.HeatSeconds = temperature > climate.MaximumSurvivalTemperature
                        ? climate.HeatSeconds + seconds : math.max(0f, climate.HeatSeconds - seconds * climate.RecoveryRate);
                    climate.LastSimulationTime += seconds;
                    if (climate.ColdSeconds >= fatalSeconds || climate.HeatSeconds >= fatalSeconds)
                    {
                        climate.Dead = 1;
                        climate.GrowthMultiplier = 0f;
                        vital.Dead = 1;
                        vital.Hp = 0f;
                        return;
                    }
                    climate.GrowthMultiplier = GrowthAt(temperature, climate);
                }
                if (GameTime - climate.LastSimulationTime <= 0.00001d)
                {
                    climate.CaughtUp = 1;
                    // 零时差也要发布实际温度的限制，不给首次加载额外一次无条件成长。
                    if (!historical) climate.GrowthMultiplier = GrowthAt(climate.AmbientCelsius, climate);
                }
            }

            private float SampleSeason(double day)
            {
                for (int i = 0; i < Seasons.Length; i++)
                    if (day < Seasons[i].EndDay) return Seasons[i].Sample(day, out _, out _, out _);
                return 0f;
            }

            private static float GrowthAt(float temperature, EntityClimate climate) =>
                temperature < climate.MinimumSurvivalTemperature || temperature > climate.MaximumSurvivalTemperature
                    ? 0f : temperature < climate.MinimumGrowthTemperature || temperature > climate.MaximumGrowthTemperature
                        ? 0.5f : 1f;
        }

        [BurstCompile]
        private partial struct GrowthJob : IJobEntity
        {
            public float DeltaTime, RainIntensity, DifficultyMultiplier, WeatherMultiplier;
            [ReadOnly] public ComponentLookup<EntityClimate> Climates;

            private void Execute(Entity entity, ref EntityGrowth growth,
                ref EntityModuleAppearance appearance, ref AiecsVital vital)
            {
                if (vital.Dead != 0 || DeltaTime <= 0f || growth.Progress >= growth.MaxProgress) return;
                float climateMultiplier = 1f;
                if (Climates.HasComponent(entity))
                {
                    EntityClimate climate = Climates[entity];
                    if (climate.CaughtUp == 0 || climate.EnvironmentReady == 0 || climate.Dead != 0) return;
                    climateMultiplier = climate.GrowthMultiplier;
                }
                float multiplier = math.max(0f, growth.EnvironmentMultiplier) * math.max(0f, climateMultiplier) *
                    (1f + math.saturate(RainIntensity) * math.max(0f, growth.RainGrowthBonus)) *
                    math.max(0f, WeatherMultiplier) * math.max(0f, DifficultyMultiplier);
                growth.Progress = math.min(growth.MaxProgress, growth.Progress + math.max(0f, growth.Speed) * DeltaTime * multiplier);
                int stage = 0;
                for (int i = 0; i < growth.Thresholds.Length; i++)
                    if (growth.Progress >= growth.Thresholds[i]) stage = i;
                stage = math.min(stage, math.max(0, growth.Scales.Length - 1));
                if (stage == growth.Stage) return;
                float ratio = vital.MaxHp > 0f ? math.saturate(vital.Hp / vital.MaxHp) : 1f;
                growth.Stage = (byte)stage;
                appearance.Scale = new float2(growth.Scales[stage]);
                appearance.Revision++;
                if (stage < growth.HealthRatios.Length)
                {
                    vital.MaxHp = math.max(1f, growth.MatureMaxHealth * math.max(0.01f, growth.HealthRatios[stage]));
                    vital.Hp = vital.MaxHp * ratio;
                }
            }
        }
    }

    #endregion
}
