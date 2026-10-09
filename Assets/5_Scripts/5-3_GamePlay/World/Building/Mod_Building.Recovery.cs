using System;
using MemoryPack;
using UnityEngine;

public partial class Mod_Building
{
    #region 保留实际库存的建筑移交
    public static ItemData CreateRecoveredCarrier(ItemData placed, string carrierId)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (string.IsNullOrWhiteSpace(carrierId)) return placed;
        ItemData carrier = GameRes.ExistingInstance.CreateItemData(carrierId);
        carrier.Guid = GenerateUniqueRuntimeGuid(); carrier.inHand = false; carrier.Stack.Amount = 1f;
        string encoded = Convert.ToBase64String(ItemSnapshotSerialization.SerializePayload(placed.InstanceSnapshot));
        carrier.ItemSpecialData = CreateStatefulSummonerIdentity(encoded);
        if (!WriteBuildingData(carrier, state =>
        {
            state.Role = BuildingRole.Summoner; state.State = BuildingState.Uninstalled;
            state.BuildingPrefabId = placed.IDName; state.SummonerPrefabId = carrierId; state.SnapshotBase64 = encoded;
        })) throw new InvalidOperationException("返还物缺少建筑载荷：" + carrierId);
        return carrier;
    }
    #endregion
}
