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

public static class BuildingPlacementLifecycle
{
    #region 安装事务通知

    public static void NotifyCommitted(Item building)
    {
        if (building?.itemMods == null) return;
        foreach (Module module in building.itemMods.Mods.Values)
            if (module is IBuildingPlacementCommitted listener) listener.OnBuildingPlacementCommitted();
    }

    #endregion
}
