using System.Collections.Generic;
using UnityEngine;

public static partial class MachineWorld
{
    #region 资源重绑定
    private static GameRes resourceOwner;

    private static void BindMachineResources()
    {
        GameRes current = GameRes.ExistingInstance;
        if (resourceOwner == current) return;
        if (resourceOwner != null) resourceOwner.ResourcesReloaded -= RebindMachineResources;
        resourceOwner = current;
        if (resourceOwner != null) resourceOwner.ResourcesReloaded += RebindMachineResources;
    }

    /// <summary>F5 先保存领域状态，再用新配置创建领域对象；这是显式资源切换，不是普通同步或区块重载。</summary>
    private static void RebindMachineResources()
    {
        if (graph == null) return;
        var changed = new List<Vector2Int>(nodes.Count);
        foreach (MachineEntity entity in nodes.Values) CaptureNode(entity);
        foreach (MachineEntity entity in nodes.Values)
        {
            MachineDefinition definition = MachineCatalog.Get(entity.Definition.Id);
            if (definition == null) continue;
            bool awake = entity.State != null;
            DisposeProcessor(entity);
            entity.Definition = definition;
            if (awake) { entity.State = null; WakeNode(entity); }
            changed.Add(entity.Cell);
        }
        dirty = true;
        foreach (Vector2Int cell in changed)
        {
            BuildingOccupancyRegistry.NotifyMechanicalChanged(cell);
            CellChanged?.Invoke(cell);
        }
    }

    private static void UnbindMachineResources()
    {
        if (resourceOwner != null) resourceOwner.ResourcesReloaded -= RebindMachineResources;
        resourceOwner = null;
    }
    #endregion
}
