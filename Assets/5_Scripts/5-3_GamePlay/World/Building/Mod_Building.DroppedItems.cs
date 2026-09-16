using System;
using UnityEngine;

public partial class Mod_Building
{
    /// <summary>单机拆除直接生成带完整快照的 ECS 库存载体，不为地面召唤器装配建筑模块或碰撞体。</summary>
    private bool TryCreateDismantledEcsDrop(out string reason)
    {
        reason = null;
        if (item?.itemData == null || Data?.Role != BuildingRole.PlacedBuilding)
        {
            reason = "当前对象不是可拆除的世界建筑";
            return false;
        }
        if (!TryCapturePlacedSnapshot(out string snapshotBase64, out reason)) return false;
        try
        {
            Vector3 position = NormalizePlacement(item.transform.position);
            string buildingId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
            string summonerId = ResolveSummonerPrefabId(buildingId, Data);
            ItemData carrier = GameRes.ExistingInstance.CreateItemData(summonerId);
            carrier.Guid = GenerateUniqueRuntimeGuid();
            carrier.inHand = false;
            carrier.Stack.Amount = 1f;
            carrier.Stack.CanBePickedUp = true;
            carrier.ItemSpecialData = StatefulSummonerPrefix + carrier.Guid;
            BuildingModuleStateTransfer.Copy(item.itemData, carrier, Data.SharedModuleIds);
            if (!WriteBuildingData(carrier, state =>
                {
                    state.Version = CurrentDataVersion;
                    state.Role = BuildingRole.Summoner;
                    state.State = BuildingState.Uninstalled;
                    state.SnapshotBase64 = snapshotBase64;
                    state.BuildingPrefabId = buildingId;
                    state.SummonerPrefabId = summonerId;
                }))
                throw new InvalidOperationException($"建筑召唤器缺少状态载荷：{summonerId}");
            Vector2 end = (Vector2)position + UnityEngine.Random.insideUnitCircle.normalized * 1.2f;
            DroppedItemService.Spawn(carrier, position, end, 0.5f,
                rotation: NormalizeBuildingRotation(item.transform.rotation).eulerAngles.z);
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
    }
}
