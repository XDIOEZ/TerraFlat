using System.Collections.Generic;
using UnityEngine;

public static partial class DroppedItemService
{
    #region 工作者实物查询
    private static readonly List<DroppedItemObservation> workerDropObservations = new();

    /// <summary>工作者复用真实掉落与空间索引，完整物品和轻量物品使用同一种带纪元句柄。</summary>
    public static void QueryNearbyPickableDrops(Vector2 origin, float radius, List<DroppedItemHandle> results)
    {
        results.Clear();
        QueryNearbyLightweightDrops(origin, radius, workerDropObservations);
        foreach (DroppedItemObservation observation in workerDropObservations)
            if (observation.Pickable && TryGetObservationHandle(observation.Id, out DroppedItemHandle handle))
                results.Add(handle);
        workerDropObservations.Clear();
        ItemMgr manager = ItemMgr.GetExistingInstance();
        if (manager == null) return;
        manager.QueryItemCentersInCircleNonAlloc(origin, radius, ~0, null, forageLegacyCandidates, forageLegacyDedupe);
        foreach (Item candidate in forageLegacyCandidates)
            if (IsLoosePickable(candidate) && GameplayWorldRegistry.GetItemWorldKey(candidate) == MachineWorld.WorldKey)
                results.Add(new DroppedItemHandle(candidate.itemData.Guid, 0, candidate));
        forageLegacyCandidates.Clear(); forageLegacyDedupe.Clear();
    }

    /// <summary>只暴露正式可回滚端口，不允许搬运系统直接改世界物品数量。</summary>
    public static bool TryGetItemTransferOutput(DroppedItemHandle handle, out IItemTransferPort output)
    {
        output = null;
        if (!TryGetPickablePosition(handle, out _)) return false;
        output = handle.Legacy != null ? new DroppedItemTransferPort(handle.Legacy) : runtime.GetItemTransferOutput(handle.Id);
        return output.IsValid;
    }
    #endregion
}
