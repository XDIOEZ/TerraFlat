/// <summary>模块仅在正式材料事务已经完成后响应安装，失败候选和编辑器预览不触发世界副作用。</summary>
public interface IBuildingPlacementCommitted
{
    void OnBuildingPlacementCommitted();
}

/// <summary>允许特殊建筑在致死攻击时继续走 DamageReceiver 的死亡/战利品流程，而不是拆回召唤器。</summary>
public interface IBuildingFatalDamagePolicy
{
    bool UseDamageReceiverFatalResolution(DamageReceiver receiver);
}

/// <summary>扩展建筑的分层占格、候选状态和预览；手持临时配置只在提交候选时写入世界数据。</summary>
public interface IBuildingPlacementExtension
{
    int OccupancyLayer { get; }
    bool ValidatePlacement(UnityEngine.Vector2Int cell, out string reason);
    void PreparePlacedData(ItemData data);
    void PrepareRepackedSnapshot(ItemData snapshot);
    void ApplyPreview(BuildingShadow shadow);
}

public static class BuildingPlacementLifecycle
{
    #region 安装事务通知

    public static void NotifyCommitted(Item building)
    {
        if (building?.itemMods == null) return;
        foreach (Module module in building.itemMods.Mods.Values)
            if (module is IBuildingPlacementCommitted listener) listener.OnBuildingPlacementCommitted();
    }

    /// <summary>同一建筑只能有一套占地扩展，模块组合完成后解析。</summary>
    public static IBuildingPlacementExtension GetExtension(Item item)
    {
        if (item?.itemMods == null) return null;
        foreach (Module module in item.itemMods.Mods.Values)
            if (module is IBuildingPlacementExtension extension) return extension;
        return null;
    }

    #endregion
}
