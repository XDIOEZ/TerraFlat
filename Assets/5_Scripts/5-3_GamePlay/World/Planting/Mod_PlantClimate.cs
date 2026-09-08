using System;
using MemoryPack;
using UnityEngine;

/// <summary>可替换的植物环境条件；成长模块不持有具体耐候数值或受害存档。</summary>
public interface IPlantEnvironmentCondition
{
    bool IsDead { get; }
    float Stress { get; }
    /// <summary>推进一段确定的环境输入，并返回本段成长倍率。</summary>
    float AdvanceEnvironment(float temperature, float seconds, float dayLength);
    /// <summary>新播种时清除前一株植物的环境状态。</summary>
    void ResetEnvironment();
}

/// <summary>自主环境模块向成长和采集提供只读限制，调用方不重复推进暴露时间。</summary>
public interface IPlantGrowthConstraint
{
    float GrowthMultiplier { get; }
    bool CanHarvest { get; }
}

/// <summary>独立保存冷热暴露进度；只有持续超过致害阈值才死亡。</summary>
[Serializable, MemoryPackable]
public partial class PlantClimateState
{
    public float ColdSeconds; // 连续受寒负担。
    public float HeatSeconds; // 连续受热负担。
    public bool Dead; // 死亡结算标记。
    public bool ClockInitialized; // 自主运行的树木耐候游标。
    public double LastSimulationTime; // 最后完成结算的世界秒。
}

/// <summary>植物耐候能力。默认适生 5～35℃，低于 0℃或高于 40℃累计 6 个游戏小时死亡，回到安全温度逐渐恢复。</summary>
public sealed class Mod_PlantClimate : Module, IPlantEnvironmentCondition, IPlantGrowthConstraint, INaturalResourceInitializer
{
    #region 定义与存档
    public Ex_ModData_MemoryPackable ModData = new(); // 独立暴露存档。
    public PlantClimateState Data = new(); // 当前冷热负担。
    public float minimumGrowthTemperature = 5f; // 适生下限。
    public float maximumGrowthTemperature = 35f; // 适生上限。
    public float minimumSurvivalTemperature; // 致害下限。
    public float maximumSurvivalTemperature = 40f; // 致害上限。
    [Min(0.01f)] public float fatalExposureHours = 6f; // 可承受游戏小时。
    [Min(0f)] public float recoveryRate = 0.5f; // 安全温度下恢复速度。
    public bool autonomous; // 树木独立驱动；Mod_Crop 组合时保持关闭以免重复结算。
    private IPlantEnvironmentCondition[] self; // 复用通用时间补算接口。
    private float fatalSeconds = 360f; // 当前日长折算后的阈值。
    private float lastGrowthMultiplier; // 最近一次环境结算给出的成长倍率。
    public float GrowthMultiplier => CanHarvest ? lastGrowthMultiplier : 0f;
    public bool CanHarvest => !Data.Dead && (!autonomous ||
        (Data.ClockInitialized && DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock) &&
        Math.Abs(clock.TotalDays * (double)clock.DayLength + clock.CurrentTime - Data.LastSimulationTime) <= 1.25d));
    public override string CanonicalModuleId => "Mod_PlantClimate";
    public override ModuleTickMode TickMode => autonomous ? ModuleTickMode.FixedInterval : ModuleTickMode.Disabled;
    public override float FixedTickInterval => 1f;
    public bool IsDead => Data.Dead;
    public float Stress => Mathf.Clamp01(Mathf.Max(Data.ColdSeconds, Data.HeatSeconds) / fatalSeconds);
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("植物耐候数据类型错误。");
    }

    /// <summary>装配时读取独立受害状态并校验温度区间。</summary>
    public override void Load()
    {
        ModData.ReadData(ref Data);
        lastGrowthMultiplier = 0f;
        self = new IPlantEnvironmentCondition[] { this };
        if (!(minimumSurvivalTemperature <= minimumGrowthTemperature &&
            minimumGrowthTemperature < maximumGrowthTemperature &&
            maximumGrowthTemperature <= maximumSurvivalTemperature && fatalExposureHours > 0f && recoveryRate >= 0f))
            throw new InvalidOperationException("植物耐候温度区间或暴露时长无效。");
    }

    /// <summary>写入冷热暴露和死亡状态，卸载区块不会清空受害进度。</summary>
    public override void Save() => ModData.WriteData(Data);

    /// <summary>播种创建全新的耐候状态。</summary>
    public void ResetEnvironment() => Data = new PlantClimateState();

    /// <summary>新发现的自然树木从当季起点补算，离开区块不清空既有受害。</summary>
    public void InitializeNaturalResource(uint deterministicRandomValue)
    {
        if (!autonomous || Data.ClockInitialized || !DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock)) return;
        SeasonSnapshot season = SeasonCalendar.Sample(clock);
        Data.LastSimulationTime = Math.Max(0d, clock.TotalDays * (double)clock.DayLength + clock.CurrentTime - season.ElapsedDays * clock.DayLength);
        Data.ClockInitialized = true;
    }
    /// <summary>非作物成长器的植物共用补算器，气候死亡不触发伐木战利品。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!autonomous || !FlatWorld.Networking.GameNetwork.HasStateAuthority ||
            !DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock)) return;
        double now = clock.TotalDays * (double)clock.DayLength + clock.CurrentTime;
        if (!Data.ClockInitialized || now < Data.LastSimulationTime)
        {
            Data.ClockInitialized = true; Data.LastSimulationTime = now;
        }
        if (!PlantClimateTimeline.Advance(item, clock, now, ref Data.LastSimulationTime, self, null, out _))
            item.DestroySelf();
    }
    #endregion

    /// <summary>累计冷热暴露、缓慢恢复，并把不适温度映射为停长或减速。</summary>
    public float AdvanceEnvironment(float temperature, float seconds, float dayLength)
    {
        fatalSeconds = Mathf.Max(0.01f, fatalExposureHours * dayLength / 24f);
        lastGrowthMultiplier = 0f;
        if (Data.Dead)
            return 0f;
        Data.ColdSeconds = temperature < minimumSurvivalTemperature
            ? Data.ColdSeconds + seconds : Mathf.Max(0f, Data.ColdSeconds - seconds * recoveryRate);
        Data.HeatSeconds = temperature > maximumSurvivalTemperature
            ? Data.HeatSeconds + seconds : Mathf.Max(0f, Data.HeatSeconds - seconds * recoveryRate);
        Data.Dead = Data.ColdSeconds >= fatalSeconds || Data.HeatSeconds >= fatalSeconds;
        if (Data.Dead || temperature < minimumSurvivalTemperature || temperature > maximumSurvivalTemperature)
            return 0f;
        lastGrowthMultiplier = temperature < minimumGrowthTemperature || temperature > maximumGrowthTemperature ? 0.5f : 1f;
        return lastGrowthMultiplier;
    }
}
