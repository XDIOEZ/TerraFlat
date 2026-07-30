using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[MemoryPackable]
internal partial class LegacyGrowData
{
    public Mod_Grow.GrowState growState = Mod_Grow.GrowState.幼苗;
    public List<float> growState_Value = new List<float>() { 0f, 20f, 50f, 100f };
    public List<float> growState_Scale = new List<float>() { 0.1f, 0.2f, 0.6f, 1f };
    public float GrowProgress;
    public float MaxGrowProgress = 100f;
    public float GrowSpeed = 5f;
}

public partial class GrowData
{
#region 权威农作物存档

    [Header("是否为玩家播种的耕地作物")]
    public bool isCultivatedCrop;

    [Header("种植地块坐标")]
    public Vector2Int plantedTilePos;

    [Header("是否已经成熟")]
    public bool isMature;

    [Header("是否已经完成收获")]
    public bool isHarvested;

    [Header("当前成长反馈状态")]
    public Mod_Grow.GrowthStatus growthStatus = Mod_Grow.GrowthStatus.Growing;

    [Header("自然环境是否已经初始化")]
    public bool environmentInitialized;

    [Header("自然生成植物的环境成长倍率")]
    public float environmentGrowthMultiplier = 1f;

#endregion
}

public partial class Mod_Grow
{
#region 权威农作物配置

    public enum GrowthStatus
    {
        Growing,
        MissingFarmland,
        NeedsWater,
        NeedsFertility,
        Mature,
        Harvested
    }

    [Header("耕地成长配置")]
    [SerializeField, Range(0f, 1f)] private float minimumWaterGrowthMultiplier = 0.5f;
    [SerializeField, Range(0f, 1f)] private float minimumFertilityGrowthMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float waterConsumePerSecond = 0.02f;
    [SerializeField, Min(0f)] private float fertilityConsumePerSecond = 0.00035f;
    [SerializeField, Min(0f)] private float rainWaterPerSecond = 0.08f;
    [SerializeField, Range(0f, 1f)] private float rainGrowthBonus = 0.15f;

    [Header("一次性收获配置")]
    [SerializeField] private bool allowCultivatedHarvest;
    [SerializeField] private string harvestFoodItemId = string.Empty;
    [SerializeField] private string harvestSeedItemId = string.Empty;
    [SerializeField, Min(1)] private int harvestFoodMin = 2;
    [SerializeField, Min(1)] private int harvestFoodMax = 4;

    private bool _harvestInProgress;

#endregion

#region 生命周期接入

    private void ReadGrowthDataWithMigration()
    {
        if (ModData?.BitData == null || ModData.BitData.Length == 0)
            return;

        try
        {
            ModData.ReadData(ref Data);
        }
        catch (Exception currentFormatException)
        {
            try
            {
                LegacyGrowData legacy = ModData.GetData<LegacyGrowData>();
                Data = new GrowData
                {
                    growState = legacy.growState,
                    growState_Value = legacy.growState_Value ?? new List<float>() { 0f, 20f, 50f, 100f },
                    growState_Scale = legacy.growState_Scale ?? new List<float>() { 0.1f, 0.2f, 0.6f, 1f },
                    GrowProgress = legacy.GrowProgress,
                    MaxGrowProgress = legacy.MaxGrowProgress,
                    GrowSpeed = legacy.GrowSpeed,
                    environmentGrowthMultiplier = 1f
                };
                Debug.Log($"[Mod_Grow] 已迁移旧版成长存档，进度={Data.GrowProgress:F1}/{Data.MaxGrowProgress:F1}", item);
            }
            catch (Exception legacyFormatException)
            {
                throw new InvalidOperationException(
                    $"无法读取当前或旧版成长存档。当前格式错误={currentFormatException.Message}",
                    legacyFormatException);
            }
        }
    }

    private void LoadAuthoritativeCropState()
    {
        Data ??= new GrowData();
        Data.environmentGrowthMultiplier = Mathf.Max(0f, Data.environmentGrowthMultiplier);
        Data.MaxGrowProgress = Mathf.Max(0.01f, Data.MaxGrowProgress);
        Data.GrowProgress = Mathf.Clamp(Data.GrowProgress, 0f, Data.MaxGrowProgress);
        Data.isMature = Data.isMature || Data.GrowProgress >= Data.MaxGrowProgress || Data.growState == GrowState.成熟;
        Data.isHarvested = Data.isHarvested && Data.isMature;

        item.OnInit_Env -= AdjustByEnvironment;
        item.OnInit_Env += AdjustByEnvironment;

        if (Data.isHarvested)
            SetGrowthStatus(GrowthStatus.Harvested, false);
        else if (Data.isMature)
            SetGrowthStatus(GrowthStatus.Mature, false);
    }

    private void OnDestroy()
    {
        if (item != null)
            item.OnInit_Env -= AdjustByEnvironment;
    }

    private void ValidateAuthoritativeCropConfig()
    {
        minimumWaterGrowthMultiplier = Mathf.Clamp01(minimumWaterGrowthMultiplier);
        minimumFertilityGrowthMultiplier = Mathf.Clamp01(minimumFertilityGrowthMultiplier);
        waterConsumePerSecond = Mathf.Max(0f, waterConsumePerSecond);
        fertilityConsumePerSecond = Mathf.Max(0f, fertilityConsumePerSecond);
        rainWaterPerSecond = Mathf.Max(0f, rainWaterPerSecond);
        rainGrowthBonus = Mathf.Clamp01(rainGrowthBonus);
        harvestFoodMin = Mathf.Max(1, harvestFoodMin);
        harvestFoodMax = Mathf.Max(harvestFoodMin, harvestFoodMax);
    }

#endregion

#region 播种初始化与环境迁移

    public void InitializeCultivatedCrop(Vector2Int tilePos, float normalizedProgress = 0f)
    {
        Data.isCultivatedCrop = true;
        Data.plantedTilePos = tilePos;
        Data.GrowProgress = Mathf.Clamp01(normalizedProgress) * Data.MaxGrowProgress;
        Data.isMature = Data.GrowProgress >= Data.MaxGrowProgress;
        Data.isHarvested = false;
        Data.environmentInitialized = true;
        Data.environmentGrowthMultiplier = 1f;
        Data.growState = GrowState.幼苗;
        SetGrowthStatus(Data.isMature ? GrowthStatus.Mature : GrowthStatus.Growing, false);
        UpdateVisualAndBehavior();
    }

    private void InitializeNaturalEnvironment(EnvironmentLayers layers, Vector2Int localPos)
    {
        if (Data.isCultivatedCrop || Data.environmentInitialized || layers == null || !layers.Contains(localPos.x, localPos.y))
            return;

        Data.environmentInitialized = true;
        int guid = item?.itemData != null ? item.itemData.Guid : 0;
        float deterministicProgress = Mathf.Abs(guid % 10000) / 10000f;
        Data.GrowProgress = deterministicProgress * Data.MaxGrowProgress;
        Data.environmentGrowthMultiplier = Mathf.Lerp(
            0.8f,
            1.2f,
            Mathf.Clamp01(layers.Precipitation[localPos.x, localPos.y]));
        UpdateVisualAndBehavior();
    }

#endregion

#region 权威成长结算

    private void UpdateAuthoritativeGrowth(float deltaTime)
    {
        ApplyStageHealth();

        if (Data.isHarvested)
        {
            SetGrowthStatus(GrowthStatus.Harvested);
            return;
        }

        if (Data.isMature || Data.growState == GrowState.成熟 || Data.GrowProgress >= Data.MaxGrowProgress)
        {
            MarkMature();
            return;
        }

        float growthDelta;
        if (Data.isCultivatedCrop || TryAdoptFarmlandAsLegacyCrop())
        {
            if (!TryResolveFarmland(out TileData_Farmland farmlandData))
            {
                SetGrowthStatus(GrowthStatus.MissingFarmland);
                return;
            }

            ApplyRainWater(farmlandData, deltaTime);
            farmlandData.NormalizeValues();

            if (farmlandData.waterValue <= 0f)
            {
                SetGrowthStatus(GrowthStatus.NeedsWater);
                return;
            }

            if (farmlandData.Fertility <= 0f)
            {
                SetGrowthStatus(GrowthStatus.NeedsFertility);
                return;
            }

            float farmlandMultiplier = CalculateFarmlandGrowthMultiplier(
                farmlandData,
                minimumWaterGrowthMultiplier,
                minimumFertilityGrowthMultiplier);
            float weatherMultiplier = ResolveWeatherGrowthMultiplier();
            float difficultyMultiplier = GameDifficultyService.Current.Production.CropGrowthMultiplier;
            growthDelta = CalculateGrowthDelta(
                Data.GrowSpeed,
                farmlandMultiplier,
                weatherMultiplier,
                difficultyMultiplier,
                deltaTime);

            if (growthDelta > 0f)
            {
                farmlandData.ConsumeWater(waterConsumePerSecond * deltaTime);
                farmlandData.ConsumeFertility(fertilityConsumePerSecond * deltaTime);
            }
        }
        else
        {
            float weatherMultiplier = ResolveWeatherGrowthMultiplier();
            float difficultyMultiplier = GameDifficultyService.Current.Production.CropGrowthMultiplier;
            growthDelta = CalculateGrowthDelta(
                Data.GrowSpeed,
                Mathf.Max(0f, Data.environmentGrowthMultiplier),
                weatherMultiplier,
                difficultyMultiplier,
                deltaTime);
        }

        if (growthDelta <= 0f)
            return;

        Data.GrowProgress = Mathf.Min(Data.MaxGrowProgress, Data.GrowProgress + growthDelta);
        SetGrowthStatus(GrowthStatus.Growing);
        UpdateVisualAndBehavior();

        if (Data.GrowProgress >= Data.MaxGrowProgress || Data.growState == GrowState.成熟)
            MarkMature();
    }

    public static float CalculateGrowthDelta(
        float baseSpeed,
        float farmlandMultiplier,
        float weatherMultiplier,
        float difficultyMultiplier,
        float deltaTime)
    {
        return Mathf.Max(0f, baseSpeed) *
               Mathf.Max(0f, farmlandMultiplier) *
               Mathf.Max(0f, weatherMultiplier) *
               Mathf.Max(0f, difficultyMultiplier) *
               Mathf.Max(0f, deltaTime);
    }

    public static float CalculateFarmlandGrowthMultiplier(
        TileData_Farmland farmlandData,
        float minimumWaterMultiplier = 0.5f,
        float minimumFertilityMultiplier = 0.5f)
    {
        if (farmlandData == null)
            throw new ArgumentNullException(nameof(farmlandData));

        farmlandData.NormalizeValues();
        if (farmlandData.waterValue <= 0f || farmlandData.Fertility <= 0f)
            return 0f;

        float waterRatio = Mathf.Clamp01(farmlandData.waterValue / farmlandData.maxWater);
        float fertilityRatio = Mathf.Clamp01(farmlandData.Fertility / farmlandData.maxFertility);
        float waterMultiplier = Mathf.Lerp(Mathf.Clamp01(minimumWaterMultiplier), 1f, waterRatio);
        float fertilityMultiplier = Mathf.Lerp(Mathf.Clamp01(minimumFertilityMultiplier), 1f, fertilityRatio);
        return waterMultiplier * fertilityMultiplier;
    }

    private bool TryAdoptFarmlandAsLegacyCrop()
    {
        if (Data.isCultivatedCrop || item == null)
            return Data.isCultivatedCrop;

        Vector2Int tilePos = new Vector2Int(
            Mathf.FloorToInt(item.transform.position.x),
            Mathf.FloorToInt(item.transform.position.y));
        Data.plantedTilePos = tilePos;

        if (!TryResolveFarmland(out _))
            return false;

        Data.isCultivatedCrop = true;
        Data.environmentInitialized = true;
        Data.environmentGrowthMultiplier = 1f;
        Debug.Log($"[Mod_Grow] 已将旧存档植物迁移为耕地作物，地块={tilePos}", item);
        return true;
    }

    private bool TryResolveFarmland(out TileData_Farmland farmlandData)
    {
        farmlandData = null;
        if (ChunkMgr.Instance == null || item == null)
            return false;

        Vector3 worldCenter = new Vector3(Data.plantedTilePos.x + 0.5f, Data.plantedTilePos.y + 0.5f, 0f);
        ChunkMgr.Instance.GetChunkBy_ItemPosition(worldCenter, out Chunk chunk);
        if (chunk == null || chunk.Map == null)
            return false;

        TileData tileData = chunk.Map.GetTileAt(Data.plantedTilePos, 0);
        if (tileData is not TileData_Farmland resolved)
            return false;

        farmlandData = resolved;
        return true;
    }

    private void ApplyRainWater(TileData_Farmland farmlandData, float deltaTime)
    {
        float rainIntensity = ResolveRainIntensity();
        if (rainIntensity <= 0f || rainWaterPerSecond <= 0f)
            return;

        farmlandData.AddWater(rainWaterPerSecond * rainIntensity * Mathf.Max(0f, deltaTime));
    }

    private float ResolveWeatherGrowthMultiplier()
    {
        if (WeatherMgr.Instance == null)
            return 1f;

        float intensity = WeatherMgr.Instance.CurrentWeatherIntensity;
        return WeatherMgr.Instance.CurrentWeather switch
        {
            WeatherType.Cloudy => Mathf.Lerp(1f, 0.95f, intensity),
            WeatherType.Rain => Mathf.Lerp(1f, 1f + rainGrowthBonus, intensity),
            WeatherType.Storm => Mathf.Lerp(1f, 0.85f, intensity),
            _ => 1f
        };
    }

    private static float ResolveRainIntensity()
    {
        if (WeatherMgr.Instance == null)
            return 0f;

        WeatherType weather = WeatherMgr.Instance.CurrentWeather;
        return weather == WeatherType.Rain || weather == WeatherType.Storm
            ? WeatherMgr.Instance.CurrentWeatherIntensity
            : 0f;
    }

    private void MarkMature()
    {
        Data.GrowProgress = Data.MaxGrowProgress;
        UpdateVisualAndBehavior();
        Data.growState = GrowState.成熟;
        Data.isMature = true;
        ApplyStageHealth(true);
        SetGrowthStatus(GrowthStatus.Mature);
    }

#endregion

#region 成长反馈

    private void SetGrowthStatus(GrowthStatus status, bool logChange = true)
    {
        if (Data.growthStatus == status)
            return;

        Data.growthStatus = status;
        if (!logChange)
            return;

        string message = status switch
        {
            GrowthStatus.MissingFarmland => "耕地已不存在，成长暂停",
            GrowthStatus.NeedsWater => "耕地缺水，成长暂停；雨天或使用肥料可补充水分",
            GrowthStatus.NeedsFertility => "耕地缺肥，成长暂停；请使用肥料",
            GrowthStatus.Mature => "作物已经成熟，可以交互收获",
            GrowthStatus.Harvested => "作物已经完成收获",
            _ => "水分与肥力恢复，作物继续成长"
        };
        Debug.Log($"[Mod_Grow] {message}，作物={item?.itemData?.IDName}，地块={Data.plantedTilePos}", item);
    }

#endregion

#region 一次性收获

    public void OnInteractStart(Item playerItem)
    {
        if (!allowCultivatedHarvest || !Data.isCultivatedCrop)
            return;

        if (Data.isHarvested || _harvestInProgress)
        {
            Debug.Log("[Mod_Grow] 该作物已经收获，不能重复生成产物", item);
            return;
        }

        if (!Data.isMature)
        {
            Debug.Log($"[Mod_Grow] 作物尚未成熟，当前进度={Data.GrowProgress:F1}/{Data.MaxGrowProgress:F1}，状态={Data.growthStatus}", item);
            return;
        }

        HarvestOnce();
    }

    public void OnInteractCancel(Item playerItem)
    {
    }

    private void HarvestOnce()
    {
        ValidateHarvestConfiguration();
        _harvestInProgress = true;
        Data.isHarvested = true;
        SetGrowthStatus(GrowthStatus.Harvested);

        try
        {
            SpawnHarvestItem(harvestSeedItemId, 1);

            int baseFoodAmount = UnityEngine.Random.Range(harvestFoodMin, harvestFoodMax + 1);
            int foodAmount = GameDifficultyService.ScaleRandomizedAmount(
                baseFoodAmount,
                GameDifficultyService.Current.World.LootAmountMultiplier);
            if (foodAmount > 0)
                SpawnHarvestItem(harvestFoodItemId, foodAmount);

            Debug.Log($"[Mod_Grow] 收获完成：{harvestSeedItemId} ×1，{harvestFoodItemId} ×{foodAmount}", item);
            item.DestroySelf();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Mod_Grow] 收获产物生成失败。为避免重复产物，已保留已收获状态。\n{exception}", item);
        }
        finally
        {
            _harvestInProgress = false;
        }
    }

    private void ValidateHarvestConfiguration()
    {
        if (ItemMgr.Instance == null || GameRes.Instance == null)
            throw new InvalidOperationException("农业收获所需的 ItemMgr 或 GameRes 尚未初始化");

        if (string.IsNullOrWhiteSpace(harvestSeedItemId) || GameRes.Instance.GetPrefab(harvestSeedItemId) == null)
            throw new InvalidOperationException($"找不到收获种子 Prefab：{harvestSeedItemId}");

        if (string.IsNullOrWhiteSpace(harvestFoodItemId) || GameRes.Instance.GetPrefab(harvestFoodItemId) == null)
            throw new InvalidOperationException($"找不到收获食物 Prefab：{harvestFoodItemId}");
    }

    private void SpawnHarvestItem(string itemId, int amount)
    {
        Chunk parentChunk = null;
        if (ChunkMgr.Instance != null)
            ChunkMgr.Instance.GetChunkBy_ItemPosition(item.transform.position, out parentChunk);

        Item product = ItemMgr.Instance.InstantiateItem(
            itemId,
            item.transform.position,
            Quaternion.identity,
            Vector3.one,
            parentChunk != null ? parentChunk.gameObject : null);
        product.Load();
        product.SetInHand(false);
        product.itemData.Stack.Amount = amount;
        product.DropInRange();
    }

#endregion
}
