using System;

public static partial class MachineWorld
{
    #region 固定格机器的通用物理接入
    // 固定占格与管电网暂不支持自由运动，模式资格由世界物理服务统一判断。
    internal static WorldPhysicsProxyMode2D ResolveRequiredWorldFixedMachineProxyMode(string itemId, GameRes resources = null)
    {
        resources ??= GameRes.ExistingInstance;
        ItemPhysicsDefinitionDto definition = resources != null && resources.TryGetItemDefinition(itemId, out var item)
            ? item.Physics : null;
        return WorldPhysicsService2D.ResolveRequiredWorldFixedProxyMode(definition, "数据机器 " + itemId);
    }

    internal static WorldPhysicsProxyMode2D ResolveRequiredWorldFixedMachineProxyMode(MachineEntity entity)
    {
        if (entity?.Definition == null) throw new ArgumentException("物理代理申请缺少机器身份。", nameof(entity));
        return ResolveRequiredWorldFixedMachineProxyMode(entity.Definition.Id);
    }

    internal static bool CanBatchWorldFixedMachineBlockingRegions(MachineEntity entity)
    {
        if (entity?.Definition == null) return false;
        GameRes resources = GameRes.ExistingInstance;
        ItemPhysicsDefinitionDto definition = resources != null &&
            resources.TryGetItemDefinition(entity.Definition.Id, out var item) ? item.Physics : null;
        return WorldPhysicsService2D.CanBatchWorldFixedBlockingRegions(definition, WorldPhysicsPurpose2D.Blocking);
    }
    #endregion
}
