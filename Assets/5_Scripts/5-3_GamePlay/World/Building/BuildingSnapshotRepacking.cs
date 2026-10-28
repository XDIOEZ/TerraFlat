using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// 普通建筑与机器共用快照规范化、载体装配和堆叠身份，不参与各后端的删除事务。
internal static class BuildingSnapshotRepacking
{
    #region 建筑快照与返还载体
    public static void NormalizePlacementIndependentSnapshot(ItemData snapshot)
    {
        snapshot.Guid = 0;
        snapshot.inHand = false;
        snapshot.Stack ??= new ItemStack();
        snapshot.Stack.Amount = 1f;
        snapshot.Stack.CanBePickedUp = false;
        snapshot.transform ??= new ItemTransform();
        snapshot.transform.position = Vector3.zero;
        snapshot.transform.rotation = Quaternion.identity;
        snapshot.transform.scale = Vector3.one;
        Mod_Building.WriteBuildingData(snapshot, state =>
        {
            state.HorizontalMirrorX = false;
            state.SnapshotBase64 = null;
        });
    }

    public static string EncodeSnapshot(ItemData snapshot)
    {
        if (!ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out byte[] payload) ||
            payload.Length > Mod_Building.MaxEmbeddedSnapshotBytes)
            throw new InvalidOperationException("建筑快照过大或无法序列化：" + snapshot.IDName);
        return Convert.ToBase64String(payload);
    }

    public static ItemData CreateReturnedCarrierData(ItemData source, string buildingId, string carrierId,
        string encodedSnapshot, Vector3 position, Quaternion rotation, string[] sharedModuleIds)
    {
        ItemData carrier = GameRes.ExistingInstance.CreateItemData(carrierId) ??
            throw new InvalidOperationException("找不到建筑召唤器定义：" + carrierId);
        CopySharedItemState(source, carrier, sharedModuleIds);
        carrier.Guid = Mod_Building.GenerateUniqueRuntimeGuid();
        carrier.inHand = false;
        carrier.Stack ??= new ItemStack();
        carrier.Stack.Amount = 1f;
        carrier.Stack.CanBePickedUp = true;
        carrier.transform ??= new ItemTransform();
        carrier.transform.position = position;
        carrier.transform.rotation = rotation;
        carrier.transform.scale = DroppedItemService.ResolveDefaultWorldDropScale(carrier);
        if (!Mod_Building.WriteBuildingData(carrier, state =>
            {
                state.Role = BuildingRole.Summoner;
                state.State = BuildingState.Uninstalled;
                state.BuildingPrefabId = buildingId;
                state.SummonerPrefabId = carrierId;
                state.SnapshotBase64 = encodedSnapshot;
            }))
            throw new InvalidOperationException("建筑召唤器缺少状态载荷：" + carrierId);
        carrier.ItemSpecialData = CreateStatefulSummonerIdentity(encodedSnapshot, carrier);
        return carrier;
    }

    public static void CopySharedItemState(ItemData source, ItemData target,
        string[] sharedModuleIds)
    {
        BuildingModuleStateTransfer.Copy(source, target, sharedModuleIds);
        CraftedDurabilityQuality.CopyInstanceQuality(source, target);
        if (source == null || target == null) return;
        if (source.MatterState?.Initialized == true)
            target.MatterState = FastCloner.FastCloner.DeepClone(source.MatterState);
        if (sharedModuleIds?.Contains(Mod_HandDrill.ModuleId) == true)
            target.MaxDurability = Mathf.Max(0f, source.MaxDurability);
        BuildingModuleStateTransfer.CopyDurabilityRatio(source, target);
    }

    public static string CreateStatefulSummonerIdentity(string encodedSnapshot, ItemData carrier = null)
    {
        if (string.IsNullOrWhiteSpace(encodedSnapshot))
            return Mod_Building.TryReadBuildingData(carrier, out _, out Mod_Building.Building_Data state) &&
                   state.CraftingMaterials != null
                ? BuildingMaterialSalvage.CreateMaterialStackIdentity(state.CraftingMaterials) : string.Empty;
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(encodedSnapshot));
        return "building-snapshot:" + BitConverter.ToString(hash).Replace("-", string.Empty);
    }
    #endregion
}
