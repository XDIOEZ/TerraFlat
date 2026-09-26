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

/// <summary>建筑对角色通行的声明；物理碰撞、导航与玩家地块移速统一读取这里，避免三套规则漂移。</summary>
public interface IBuildingTraversalPolicy
{
    bool BlocksMovement { get; }
    float PlayerMoveSpeedMultiplier { get; }
}

/// <summary>允许可重建的建筑扩展省略默认拆回快照，避免无状态物品被快照身份阻断堆叠。</summary>
public interface IBuildingSnapshotRepackPolicy
{
    bool CanOmitRepackedSnapshot(ItemData normalizedSnapshot);
}

/// <summary>放置预览的离散旋转入口；输入层只判断能否旋转，具体朝向由建筑模块维护。</summary>
public interface IBuildingPreviewRotation
{
    bool CanRotatePlacement { get; }
    void RotatePlacement();
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

    /// <summary>读取可选通行策略；未声明时保持普通建筑的阻挡语义。</summary>
    public static IBuildingTraversalPolicy GetTraversalPolicy(Item item)
    {
        if (item?.itemMods == null) return null;
        foreach (Module module in item.itemMods.Mods.Values)
            if (module is IBuildingTraversalPolicy policy) return policy;
        return null;
    }

    #endregion
}
