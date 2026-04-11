using UnityEngine;

[System.Serializable]
public class Tile_Farmland : TileBlockBehaviour
{
#region 配置

    [Header("耕地生长影响配置")]
    [Tooltip("水分对生长倍率的影响曲线下限(缺水到湿润过程)"), Range(0f, 2f)]
    public float waterMultiplierMin = 0.2f; // 缺水时的最低倍率

    [Tooltip("水分对生长倍率的影响曲线上限"), Range(0f, 3f)]
    public float waterMultiplierMax = 1.2f; // 水分充足时的最高倍率

    [Tooltip("耕地每秒自然蒸发的水分"), Min(0f)]
    public float waterEvaporationPerSecond = 0.03f; // 地块水分自然流失

    [Tooltip("作物每秒消耗的耕地水分"), Min(0f)]
    public float cropWaterConsumePerSecond = 0.1f; // 作物生长时消耗水分

#endregion

#region 公共接口

    public bool CanGrow(TileData tileData) // 是否满足生长必要条件（水和肥料都必须有）
    {
        TileData_Farmland farmlandData = RequireFarmlandData(tileData);
        return farmlandData.waterValue > 0f && farmlandData.fertilityValue.Value > 0f;
    }

    public float GetGrowSpeedMultiplier(TileData tileData) // 计算作物生长速度倍率
    {
        TileData_Farmland farmlandData = RequireFarmlandData(tileData);
        if (farmlandData.maxWater <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(farmlandData.maxWater), "耕地 maxWater 必须大于 0");

        if (!CanGrow(tileData))
            return 0f;

        float fertility = Mathf.Max(0.01f, farmlandData.fertilityValue.Value);
        float waterRatio = Mathf.Clamp01(farmlandData.waterValue / farmlandData.maxWater);
        float waterMultiplier = Mathf.Lerp(waterMultiplierMin, waterMultiplierMax, waterRatio);
        return fertility * waterMultiplier;
    }

    public void Irrigate(TileData tileData, float waterAmount) // 灌溉地块
    {
        if (waterAmount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(waterAmount), "灌溉水量不能小于 0");

        TileData_Farmland farmlandData = RequireFarmlandData(tileData);
        farmlandData.waterValue = Mathf.Min(farmlandData.maxWater, farmlandData.waterValue + waterAmount);
    }

    public void ConsumeWater(TileData tileData, float waterAmount) // 消耗地块水分
    {
        if (waterAmount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(waterAmount), "消耗水量不能小于 0");

        TileData_Farmland farmlandData = RequireFarmlandData(tileData);
        farmlandData.waterValue = Mathf.Max(0f, farmlandData.waterValue - waterAmount);
    }

#endregion

#region 地块回调

    public override void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        TileData_Farmland farmlandData = RequireFarmlandData(tileData);

        // 蒸发：无论有没有作物，耕地都会慢速失水。
        if (waterEvaporationPerSecond > 0f)
        {
            ConsumeWater(farmlandData, waterEvaporationPerSecond * deltaTime);
        }

        if (item == null || item.itemMods == null)
            return;

        if (!item.itemMods.ContainsKey_ID(ModText.Grow))
            return;

        Mod_Grow growModule = item.itemMods.GetMod_ByID(ModText.Grow) as Mod_Grow;
        if (growModule == null)
            return;

        if (growModule.Data == null)
            throw new System.NullReferenceException("Mod_Grow.Data 为空，无法应用耕地生长逻辑");

        if (!CanGrow(farmlandData))
        {
            // 缺水时抵消一次基础增长，使“有水才生长”。
            float cancelGrowth = growModule.Data.GrowSpeed * deltaTime;
            growModule.Data.GrowProgress = Mathf.Max(0f, growModule.Data.GrowProgress - cancelGrowth);
            return;
        }

        float multiplier = GetGrowSpeedMultiplier(farmlandData);
        float bonusGrowth = growModule.Data.GrowSpeed * Mathf.Max(0f, multiplier - 1f) * deltaTime;

        if (bonusGrowth > 0f)
        {
            growModule.Data.GrowProgress = Mathf.Min(growModule.Data.MaxGrowProgress, growModule.Data.GrowProgress + bonusGrowth);
        }

        if (cropWaterConsumePerSecond > 0f)
        {
            ConsumeWater(farmlandData, cropWaterConsumePerSecond * deltaTime);
        }
    }

#endregion

#region 私有方法

    private static TileData_Farmland RequireFarmlandData(TileData tileData) // 要求耕地数据类型
    {
        if (tileData == null)
            throw new System.ArgumentNullException(nameof(tileData));

        if (tileData is not TileData_Farmland farmlandData)
            throw new System.InvalidCastException($"Tile_Farmland 需要 TileData_Farmland，当前类型={tileData.GetType().Name}");

        return farmlandData;
    }

    private static bool CanGrow(TileData_Farmland farmlandData)
    {
        return farmlandData.waterValue > 0f && farmlandData.fertilityValue.Value > 0f;
    }

    private float GetGrowSpeedMultiplier(TileData_Farmland farmlandData)
    {
        if (farmlandData.maxWater <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(farmlandData.maxWater), "耕地 maxWater 必须大于 0");

        if (!CanGrow(farmlandData))
            return 0f;

        float fertility = Mathf.Max(0.01f, farmlandData.fertilityValue.Value);
        float waterRatio = Mathf.Clamp01(farmlandData.waterValue / farmlandData.maxWater);
        float waterMultiplier = Mathf.Lerp(waterMultiplierMin, waterMultiplierMax, waterRatio);
        return fertility * waterMultiplier;
    }

    private static void ConsumeWater(TileData_Farmland farmlandData, float waterAmount)
    {
        if (waterAmount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(waterAmount), "消耗水量不能小于 0");

        farmlandData.waterValue = Mathf.Max(0f, farmlandData.waterValue - waterAmount);
    }

#endregion
}
