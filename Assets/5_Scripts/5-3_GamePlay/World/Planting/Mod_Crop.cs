using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>
/// 农作物的权威状态容器：只管理幼苗、成熟、耕地成长与一次性收获调度。
/// 具体产物、任务、特效等收获结果通过 ICropHarvestAction 独立注册，避免继续堆入成长模块。
/// 满水满肥时的基础成熟时间由 growthDurationSeconds 决定，环境与通用成长倍率只结算一次。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class CropRuntimeData
{
    [Header("当前阶段")]
    public CropStage stage = CropStage.Seedling;

    [Header("标准化成长进度")]
    public float normalizedGrowth;

    [Header("是否由玩家种植")]
    public bool isPlanted;

    [Header("种植地块坐标")]
    public Vector2Int plantedTilePosition;

    [Header("是否已经收获")]
    public bool isHarvested;

    [Header("成长反馈状态")]
    public CropGrowthStatus growthStatus = CropGrowthStatus.Growing;

    public bool simulationInitialized; // 是否已经建立世界时间游标。
    public double lastSimulatedTime; // 最后完成结算的绝对游戏秒。
    public bool waterSimulationInitialized; // 耗水独立于生长状态。
    public double lastWaterSimulationTime; // 成熟后仍推进耗水游标。
}

public sealed class Mod_Crop : Module, IInteractable, IPlantableCrop, IWorldTimePlant, IItemModuleDependencyBinder, INaturalResourceInitializer, INaturalRenewalPolicy
{
    private readonly List<IPlantEnvironmentCondition> environmentConditions = new(); // 独立植物环境能力。
    public float ClimateStress { get; private set; } // 表现读取的最严重受害比例。

    /// <summary>仅自然植物在来年春季恢复；玩家农田不会借助生态刷新免费补种。</summary>
    public bool TryGetRenewalYear(out int year)
    {
        year = 0;
        if (Data.isPlanted || !DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season)) return false;
        year = season.Year + 1;
        return true;
    }

    /// <summary>在所有模块注册后解析可替换的环境条件。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        environmentConditions.Clear();
        foreach (Module module in modules.Mods.Values)
            if (module is IPlantEnvironmentCondition condition)
                environmentConditions.Add(condition);
    }

    /// <summary>首次发现野生植物时从当前季节起点补算，避免冬季新开区块凭空产生丰收。</summary>
    public void InitializeNaturalResource(uint deterministicRandomValue)
    {
        if (Data.simulationInitialized || !PlantGrowthEnvironment.TryReadWorldClock(out TimeData clock, out double now))
            return;
        SeasonSnapshot season = SeasonCalendar.Sample(clock);
        Data.lastSimulatedTime = Math.Max(0d, now - season.ElapsedDays * clock.DayLength);
        Data.simulationInitialized = true;
    }
    #region 模块数据

    public Ex_ModData_MemoryPackable ModData = new();

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
            throw new ArgumentException("[Mod_Crop] 模块数据类型错误。", nameof(value));
    }

    public override string CanonicalModuleId => ModText.Crop;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;

    [Header("权威运行时状态")]
    public CropRuntimeData Data = new();

    #endregion

    #region 成长配置

    [Header("基础成熟时间")]
    [SerializeField, Min(0.01f)]
    private float growthDurationSeconds = 500f;

    [Header("耕地成长倍率下限")]
    [SerializeField, Range(0f, 1f)]
    private float minimumWaterGrowthMultiplier = 0.5f;

    [SerializeField, Range(0f, 1f)]
    private float minimumFertilityGrowthMultiplier = 0.5f;

    [Header("耕地消耗")]
    [SerializeField, Min(0f)]
    private float waterConsumptionMultiplier = 1f;

    [SerializeField, Range(0f, 1f)]
    private float minimumLightIntensity = FlatWorld.AIECS.EntityPlantRules.DefaultMinimumLightIntensity;

    [SerializeField, Min(0f)]
    private float fertilityConsumePerSecond = 0.00035f;

    [Header("天气成长")]
    [SerializeField, Range(0f, 1f)]
    private float rainGrowthBonus = 0.15f;

    #endregion

    #region 公共状态与事件

    public CropStage Stage => Data?.stage ?? CropStage.Seedling;
    public float NormalizedGrowth => Data != null ? Mathf.Clamp01(Data.normalizedGrowth) : 0f;
    public CropGrowthStatus GrowthStatus => Data?.growthStatus ?? CropGrowthStatus.Growing;

    public event Action<Mod_Crop, float> GrowthChanged;
    public event Action<Mod_Crop, CropStage> StageChanged;
    public event Action<Mod_Crop> Matured;
    public event Action<CropHarvestContext> Harvested;

    #endregion

    #region 运行时缓存

    private ICropHarvestAction[] harvestActions = Array.Empty<ICropHarvestAction>();
    private ICropHarvestPolicy harvestPolicy;
    private bool harvestInProgress;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModText.Crop;
        base.Awake();
    }

    protected override void OnLoad()
    {
        manualHarvestProgress = 0;
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_Crop] 未找到所属 Item。");

        ValidateConfiguration();
        Data ??= new CropRuntimeData();
        ModData.ReadData(ref Data);
        NormalizeRuntimeState();

        ICropHarvestAction[] discoveredActions = item.GetComponentsInChildren<ICropHarvestAction>(true);
        var activeActions = new List<ICropHarvestAction>(discoveredActions.Length);
        ICropHarvestPolicy resolvedPolicy = null;
        foreach (ICropHarvestAction harvestAction in discoveredActions)
        {
            if (harvestAction is Module module && module._Data != null && !module._Data.isRunning)
                continue;

            activeActions.Add(harvestAction);
            if (harvestAction is not ICropHarvestPolicy policy)
                continue;
            if (resolvedPolicy != null)
                throw new InvalidOperationException($"[Mod_Crop] 作物 {item.itemData?.IDName} 配置了多个 ICropHarvestPolicy，收获生命周期不明确。");
            resolvedPolicy = policy;
        }

        harvestActions = activeActions.ToArray();
        harvestPolicy = resolvedPolicy;
        Mod_DamageReceiver health = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health != null) { health.DeathStarted -= HarvestAfterDamage; health.DeathStarted += HarvestAfterDamage; }
        if (harvestActions.Length == 0)
            throw new MissingComponentException($"[Mod_Crop] 作物 {item.itemData?.IDName} 缺少 ICropHarvestAction。");

        GrowthChanged?.Invoke(this, NormalizedGrowth);
    }

    protected override void OnSave()
    {
        Data ??= new CropRuntimeData();
        NormalizeRuntimeState();
        ModData.WriteData(Data);
    }

    protected override void OnUnload()
    {
        Mod_DamageReceiver health = item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health != null) health.DeathStarted -= HarvestAfterDamage;
        environmentConditions.Clear();
        ClimateStress = 0f;
        harvestActions = Array.Empty<ICropHarvestAction>();
        harvestPolicy = null;
        harvestInProgress = false;
        GrowthChanged = null;
        StageChanged = null;
        Matured = null;
        Harvested = null;
    }

    public override void ModUpdate(float deltaTime)
    {
        CatchUpToWorldTime();
    }

    /// <summary>区块重新绑定和常规 Tick 共用世界时间补算入口。</summary>
    public void CatchUpToWorldTime() => AdvanceWorldState();

    #endregion

    #region 种植与阶段控制

    /// <summary>种子成功消耗前，把新生成的作物初始化为指定耕地上的幼苗。</summary>
    public void InitializePlantedCrop(Vector2Int tilePosition)
    {
        Data ??= new CropRuntimeData();
        Data.isPlanted = true;
        Data.plantedTilePosition = tilePosition;
        Data.isHarvested = false;
        Data.normalizedGrowth = 0f;
        foreach (IPlantEnvironmentCondition condition in environmentConditions)
            condition.ResetEnvironment();
        Data.simulationInitialized = PlantGrowthEnvironment.TryReadWorldClock(out _, out double now);
        Data.lastSimulatedTime = now;
        Data.waterSimulationInitialized = Data.simulationInitialized;
        Data.lastWaterSimulationTime = now;
        SetStage(CropStage.Seedling);
        SetGrowthStatus(CropGrowthStatus.Growing, false);
        GrowthChanged?.Invoke(this, 0f);
    }

    /// <summary>供后续农业机制或调试工具显式切换权威阶段。</summary>
    public void SetStage(CropStage stage)
    {
        Data ??= new CropRuntimeData();
        CropStage previous = Data.stage;
        Data.stage = stage;

        if (stage == CropStage.Mature)
        {
            Data.normalizedGrowth = 1f;
            SetGrowthStatus(CropGrowthStatus.Mature, false);
        }
        else
        {
            Data.normalizedGrowth = 0f;
            Data.isHarvested = false;
            SetGrowthStatus(CropGrowthStatus.Growing, false);
        }

        GrowthChanged?.Invoke(this, NormalizedGrowth);
        if (previous == stage)
            return;

        StageChanged?.Invoke(this, stage);
        if (stage == CropStage.Mature)
            Matured?.Invoke(this);
    }

    #endregion

    #region 权威成长结算

    /// <summary>按世界时间分段补算成长与受害，每帧有界处理，离区期间不沿用返回时天气。</summary>
    private void AdvanceWorldState()
    {
        if (!GameNetwork.HasStateAuthority || Data == null || Data.isHarvested ||
            !PlantGrowthEnvironment.TryReadWorldClock(out TimeData clock, out double now))
            return;
        if (!Data.waterSimulationInitialized)
        {
            Data.waterSimulationInitialized = true;
            Data.lastWaterSimulationTime = now;
        }
        if (!Data.simulationInitialized || now < Data.lastSimulatedTime)
        {
            Data.lastSimulatedTime = now;
            Data.simulationInitialized = true;
            if (now < Data.lastWaterSimulationTime) Data.lastWaterSimulationTime = now;
        }
        float previousStress = ClimateStress;
        if (!PlantClimateTimeline.Advance(item, clock, now, ref Data.lastSimulatedTime,
                environmentConditions, AdvanceGrowth, out float stress))
        {
            Data.isHarvested = true;
            item.DestroySelf();
            return;
        }
        ClimateStress = stress;
        if (!Mathf.Approximately(previousStress, ClimateStress))
            GrowthChanged?.Invoke(this, NormalizedGrowth);
    }

    /// <summary>消费同一段世界时间的耕地资源，温度不适时暂停成长。</summary>
    private void AdvanceGrowth(float deltaTime, float environmentMultiplier, bool historical)
    {
        try { AdvanceGrowthCore(deltaTime, environmentMultiplier, historical); }
        finally
        {
            Vector2Int cell = Data.isPlanted ? Data.plantedTilePosition : Vector2Int.FloorToInt(item.transform.position);
            PlantGrowthEnvironment.AdvanceWater(cell, waterConsumptionMultiplier, !Data.isPlanted,
                ref Data.waterSimulationInitialized, ref Data.lastWaterSimulationTime, Data.lastSimulatedTime + deltaTime);
        }
    }

    private void AdvanceGrowthCore(float deltaTime, float environmentMultiplier, bool historical)
    {
        if (Data == null || !Data.isPlanted || Data.isHarvested || Data.stage == CropStage.Mature)
            return;

        if (!TryResolveFarmland(out TileData_Farmland farmlandData))
        {
            SetGrowthStatus(CropGrowthStatus.MissingFarmland);
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        farmlandData.NormalizeValues();
        FarmlandSystem.CommitSoil(farmlandData);

        if (environmentMultiplier <= 0f)
        {
            SetGrowthStatus(CropGrowthStatus.TemperatureStress);
            return;
        }

        if (!PlantGrowthEnvironment.HasSufficientLight(Data.plantedTilePosition, minimumLightIntensity))
        {
            SetGrowthStatus(CropGrowthStatus.NeedsLight);
            return;
        }

        if (farmlandData.waterValue <= 0f)
        {
            SetGrowthStatus(CropGrowthStatus.NeedsWater);
            return;
        }

        if (farmlandData.Fertility <= 0f)
        {
            SetGrowthStatus(CropGrowthStatus.NeedsFertility);
            return;
        }

        float farmlandMultiplier = PlantGrowthEnvironment.CalculateFarmlandGrowthMultiplier(farmlandData,
            minimumWaterGrowthMultiplier, minimumFertilityGrowthMultiplier);
        float weatherMultiplier = (historical ? 1f : PlantGrowthEnvironment.ResolveWeatherGrowthMultiplier(item, rainGrowthBonus)) * environmentMultiplier;
        float growthMultiplier = GameRuleConfiguration.Current.Production.CropGrowthMultiplier;
        float growthDelta = PlantGrowthEnvironment.CalculateGrowthDelta(safeDeltaTime / growthDurationSeconds,
            farmlandMultiplier, weatherMultiplier, growthMultiplier, 1f);
        if (growthDelta <= 0f)
            return;

        farmlandData.ConsumeFertility(fertilityConsumePerSecond * safeDeltaTime);
        FarmlandSystem.CommitSoil(farmlandData);
        SetGrowthStatus(CropGrowthStatus.Growing);

        Data.normalizedGrowth = Mathf.Clamp01(Data.normalizedGrowth + growthDelta);
        GrowthChanged?.Invoke(this, Data.normalizedGrowth);
        if (Data.normalizedGrowth >= 1f)
            SetStage(CropStage.Mature);
    }

    private bool TryResolveFarmland(out TileData_Farmland farmlandData) =>
        FarmlandSystem.TryReadSoil(Data.plantedTilePosition, out farmlandData);

    #endregion

    #region 成熟交互

    public bool CanInteract(Item playerItem)
    {
        return playerItem != null &&
               Data != null &&
               !HasPendingEnvironment() &&
               Data.stage == CropStage.Mature &&
               !Data.isHarvested &&
               !harvestInProgress &&
               harvestActions.Length > 0 &&
               (harvestPolicy?.CanHarvest ?? true);
    }

    // 作物收获保留鼠标点选，设备开面板仍走交互键。
    public bool CanPointerInteract(Item playerItem) => true;

    /// <summary>历史状态尚未结算时暂缓收获，避免重载首帧绕过气候死亡。</summary>
    private bool HasPendingEnvironment() => PlantGrowthEnvironment.TryReadWorldClock(out _, out double now) &&
        (!Data.simulationInitialized || now - Data.lastSimulatedTime > 1d);

    public void OnInteractStart(Item playerItem)
    {
        if (!CanInteract(playerItem))
            return;

        if (!ManualHarvestWork.TryPayStamina(playerItem)) return;
        ManualHarvestShake.Play(item);

        // 徒手采收累计有效点击，工具攻击仍沿原伤害和掉落规则完成采收。
        if (!ManualHarvestWork.Advance(ref manualHarvestProgress, manualHarvestWorkSteps)) return;

        bool preserveCrop = harvestPolicy?.PreserveCropAfterHarvest ?? false;
        harvestInProgress = true;
        if (!preserveCrop)
        {
            Data.isHarvested = true;
            SetGrowthStatus(CropGrowthStatus.Harvested);
        }
        CropHarvestContext context = new(item, playerItem, item.transform.position);

        try
        {
            foreach (ICropHarvestAction harvestAction in harvestActions)
                harvestAction.Execute(context);

            Harvested?.Invoke(context);
            if (preserveCrop)
            {
                Data.isHarvested = false;
                SetGrowthStatus(CropGrowthStatus.Mature, false);
            }
            else
            {
                item.DestroySelf();
            }
        }
        finally
        {
            harvestInProgress = false;
        }
    }

    public void OnInteractCancel(Item playerItem)
    {
    }

    // 成熟作物被工具击杀时复用一次正式收获动作，不把徒手次数乘进产量。
    private void HarvestAfterDamage(Mod_DamageReceiver health)
    {
        if (!GameNetwork.HasStateAuthority || Data == null || Data.stage != CropStage.Mature ||
            Data.isHarvested || harvestInProgress || !(harvestPolicy?.CanHarvest ?? true)) return;
        Data.isHarvested = true;
        harvestInProgress = true;
        try
        {
            health.Data.LootTable?.RemoveAll(entry => IsHarvestOutput(entry.LootPrefabName));
            var context = new CropHarvestContext(item, null, item.transform.position);
            foreach (ICropHarvestAction action in harvestActions) action.Execute(context);
            Harvested?.Invoke(context);
        }
        finally { harvestInProgress = false; }
    }

    private bool IsHarvestOutput(string itemId)
    {
        foreach (ICropHarvestAction action in harvestActions)
        {
            if (action is Mod_Collectable collectable && string.Equals(collectable.CollectItemId, itemId, StringComparison.OrdinalIgnoreCase)) return true;
            if (action is Mod_CropYield cropYield && cropYield.outputs.Exists(output => string.Equals(output.itemId, itemId, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    [Min(1)] public int manualHarvestWorkSteps = ManualHarvestWork.DefaultSteps;
    private int manualHarvestProgress;

    #endregion

    #region 校验与反馈

    private void NormalizeRuntimeState()
    {
        Data.normalizedGrowth = Mathf.Clamp01(Data.normalizedGrowth);
        if (Data.stage == CropStage.Mature || Data.normalizedGrowth >= 1f)
        {
            Data.stage = CropStage.Mature;
            Data.normalizedGrowth = 1f;
            Data.growthStatus = Data.isHarvested
                ? CropGrowthStatus.Harvested
                : CropGrowthStatus.Mature;
        }
        else
        {
            Data.stage = CropStage.Seedling;
            Data.isHarvested = false;
        }
    }

    private void SetGrowthStatus(CropGrowthStatus status, bool logChange = true)
    {
        if (Data.growthStatus == status)
            return;

        Data.growthStatus = status;
        if (!logChange)
            return;

        string message = status switch
        {
            CropGrowthStatus.MissingFarmland => "耕地已不存在，成长暂停",
            CropGrowthStatus.NeedsWater => "耕地缺水，成长暂停",
            CropGrowthStatus.NeedsFertility => "耕地缺肥，成长暂停",
            CropGrowthStatus.Mature => "作物已经成熟，可以交互收获",
            CropGrowthStatus.Harvested => "作物已经完成收获",
            CropGrowthStatus.TemperatureStress => "温度不适，成长暂停，持续受害会枯萎",
            CropGrowthStatus.NeedsLight => "光照不足，成长暂停",
            _ => "成长条件恢复，作物继续成长"
        };
        Debug.Log($"[Mod_Crop] {message}，作物={item?.itemData?.IDName}，地块={Data.plantedTilePosition}", item);
    }

    private void ValidateConfiguration()
    {
        if (growthDurationSeconds <= 0f)
            throw new InvalidOperationException("[Mod_Crop] growthDurationSeconds 必须大于 0。");

        minimumWaterGrowthMultiplier = Mathf.Clamp01(minimumWaterGrowthMultiplier);
        minimumFertilityGrowthMultiplier = Mathf.Clamp01(minimumFertilityGrowthMultiplier);
        waterConsumptionMultiplier = Mathf.Max(0f, waterConsumptionMultiplier);
        minimumLightIntensity = Mathf.Clamp01(minimumLightIntensity);
        fertilityConsumePerSecond = Mathf.Max(0f, fertilityConsumePerSecond);
        rainGrowthBonus = Mathf.Clamp01(rainGrowthBonus);
    }

    private void OnValidate()
    {
        growthDurationSeconds = Mathf.Max(0.01f, growthDurationSeconds);
        minimumWaterGrowthMultiplier = Mathf.Clamp01(minimumWaterGrowthMultiplier);
        minimumFertilityGrowthMultiplier = Mathf.Clamp01(minimumFertilityGrowthMultiplier);
        waterConsumptionMultiplier = Mathf.Max(0f, waterConsumptionMultiplier);
        minimumLightIntensity = Mathf.Clamp01(minimumLightIntensity);
        fertilityConsumePerSecond = Mathf.Max(0f, fertilityConsumePerSecond);
        rainGrowthBonus = Mathf.Clamp01(rainGrowthBonus);
    }

    #endregion
}
