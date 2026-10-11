using System;

public partial class Mod_Building
{
    #region 保留实际库存的建筑移交
    public static ItemData CreateRecoveredCarrier(ItemData placed, string carrierId)
    {
        if (placed == null) throw new ArgumentNullException(nameof(placed));
        if (string.IsNullOrWhiteSpace(carrierId)) return placed;
        ItemData carrier = GameRes.ExistingInstance.CreateItemData(carrierId);
        carrier.Guid = GenerateUniqueRuntimeGuid(); carrier.inHand = false; carrier.Stack.Amount = 1f;
        // 回收载体与普通拆回共用正式快照封包，供放置与载荷读取按同一契约恢复。
        if (!ItemNetworkStateSerialization.TrySerializeItemData(placed, out byte[] payload) ||
            payload.Length > MaxEmbeddedSnapshotBytes)
            throw new InvalidOperationException("回收建筑快照无法序列化：" + placed.IDName);
        string encoded = Convert.ToBase64String(payload);
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
