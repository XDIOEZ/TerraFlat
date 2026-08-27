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
    Harvested
}

/// <summary>种植入口初始化世界农作物的统一契约。</summary>
public interface IPlantableCrop
{
    void InitializePlantedCrop(Vector2Int tilePosition);
}

/// <summary>成熟农作物交互后执行的可注册动作。</summary>
public interface ICropHarvestAction
{
    void Execute(CropHarvestContext context);
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
