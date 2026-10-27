using UnityEngine;

/// <summary>农作物只保留幼苗与成熟两个权威阶段。</summary>
public enum CropStage
{
    Seedling,
    Mature
}

/// <summary>描述农作物当前不能成长或已经完成生命周期的原因。</summary>
public enum CropGrowthStatus
{
    Growing,
    MissingFarmland,
    NeedsWater,
    NeedsFertility,
    Mature,
    Harvested,
    TemperatureStress,
    NeedsLight
}

/// <summary>种植入口初始化世界农作物的统一契约。</summary>
public interface IPlantableCrop
{
    void InitializePlantedCrop(Vector2Int tilePosition);
    void RelocatePlantedCrop(Vector2Int tilePosition);
    void RelocatePlantedCropSnapshot(ModuleData data, Vector2Int tilePosition);
}

public static class CropWorldRelocation
{
    #region 作物土壤归属
    public static void RelocateLiveCrop(Item item, Vector2Int cell)
    {
        if (item?.Mods == null) return;
        foreach (Module module in item.Mods.Values)
            if (module is IPlantableCrop crop) crop.RelocatePlantedCrop(cell);
    }

    public static void RelocateSavedCrop(ItemData item, Vector2Int cell)
    {
        if (item?.ModuleDataDic == null) return;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition))
            throw new System.InvalidOperationException("作物移交缺少物品定义：" + item.IDName);
        bool relocated = false;
        foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
        {
            if (!module.Enabled || !item.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state)) continue;
            GameObject prefab = resources.GetPrefab(module.PrefabId, false);
            if (prefab == null) continue;
            foreach (IPlantableCrop crop in prefab.GetComponentsInChildren<IPlantableCrop>(true))
            { crop.RelocatePlantedCropSnapshot(state, cell); relocated = true; }
        }
        if (!relocated) throw new System.InvalidOperationException("作物定义缺少可移交的种植模块：" + item.IDName);
    }
    #endregion
}

/// <summary>已恢复的种植作物按当前世界时间补算离区期间的成长。</summary>
public interface IWorldTimePlant
{
    void CatchUpToWorldTime();
}

/// <summary>成熟农作物交互后执行的可注册动作。</summary>
public interface ICropHarvestAction
{
    void Execute(CropHarvestContext context);
}

/// <summary>
/// 可选的收获生命周期策略：持续采果等模块可在成熟植株上声明当前是否可采、以及采后是否保留植株。
/// 未提供该策略的普通作物继续使用一次性收获并销毁植株。
/// </summary>
public interface ICropHarvestPolicy
{
    bool CanHarvest { get; }
    bool PreserveCropAfterHarvest { get; }
}

/// <summary>一次收获行为的稳定上下文，供产物、任务或特效动作复用。</summary>
public readonly struct CropHarvestContext
{
    public Item CropItem { get; }
    public Item Interactor { get; }
    public Vector3 WorldPosition { get; }

    public CropHarvestContext(Item cropItem, Item interactor, Vector3 worldPosition)
    {
        CropItem = cropItem;
        Interactor = interactor;
        WorldPosition = worldPosition;
    }
}
