using System;
using FlatWorld.Gameplay.Building;
using FlatWorld.Networking;
using UnityEngine;

// 建筑与机器拆回事务。
public partial class Mod_Building
{
    #region 建筑与机器拆回事务
    public virtual void UnInstall()
    {
        if (_dismantlePending || Data?.Role != BuildingRole.PlacedBuilding || !IsInstalled())
            return;

        BuildingState previousState = CurrentState;
        CurrentState = BuildingState.Uninstalling;
        StartUnInstall?.Invoke();
        _dismantlePending = true;

        if (ItemNetworkStateSerialization.BeginNetworkBuildingDismantle(this))
            return;

        _dismantlePending = false;
        string reason;
        bool created = DroppedItemService.UsesLightweightDrops
            ? TryCreateDismantledDrop(out reason)
            : TryCreateDismantledSummoner(out _, out reason);
        if (!created)
        {
            CurrentState = previousState;
            Save();
            Debug.LogWarning($"[建筑拆除] {reason}", item);
            return;
        }

        BuildingOccupancyRegistry.Unregister(this);
        ItemMgr.Instance.DespawnItem(item, false);
    }
    /// <summary>纯数据机械先生成返还召唤器，成功后才移除权威节点和占格。</summary>
    public static bool TryDismantleMechanical(MachineEntity node, out string reason)
        => TryDismantleMechanical(node, out _, out reason);

    /// <summary>地表返还物可供服务端发布，船上返还物由太空会话持有。</summary>
    public static bool TryDismantleMechanical(MachineEntity node, out Item summoner, out string reason)
        => TryCreateMechanicalDismantledSummoner(node, true, out summoner, out reason);

    /// <summary>联机事务先生成可发布的返还物，待广播成功后再提交机械节点删除。</summary>
    public static bool TryPrepareMechanicalDismantle(MachineEntity node, out Item summoner, out string reason)
        => TryCreateMechanicalDismantledSummoner(node, false, out summoner, out reason);

    private static bool TryCreateMechanicalDismantledSummoner(
        MachineEntity node, bool removeNode, out Item summoner, out string reason)
    {
        summoner = null;
        reason = null;
        if (!GameNetwork.HasStateAuthority || node == null || ItemMgr.Instance == null)
        {
            reason = "机械拆除需要当前世界写入权限";
            return false;
        }
        // 面板回调已离开交互调用栈，整笔拆除必须留在节点所属的世界。
        using var nodeScope = MachineWorld.UseNodeScope(node);
        if (!MachineWorld.Contains(node))
        {
            reason = "机械节点已失效";
            return false;
        }
        bool onShip = MachineWorld.IsShipScope;
        if (onShip && !removeNode)
        {
            reason = "当前联机拆除事务尚不支持船上设施";
            return false;
        }
        if (onShip && FlatWorld.Spaceflight.SpaceSession.Current?.Owns(node.Id) != true)
        {
            reason = "机械节点所属的船部件已失效";
            return false;
        }
        if (!MachineWorld.CanDismantleFluid(node, out reason)) return false;
        ItemData placed = MachineWorld.CaptureSnapshot(node);
        MachineWorld.PrepareDismantledFluidSnapshot(node, placed);
        if (placed == null || !TryReadBuildingData(placed, out _, out Building_Data building))
        {
            reason = "机械节点快照无效";
            return false;
        }
        try
        {
            BuildingSnapshotRepacking.NormalizePlacementIndependentSnapshot(placed);
            MachineState state = MachineWorld.ReadMachineState(placed);
            state.RotationQuarterTurns = 0;
            state.Generated = false;
            MachineWorld.WriteMachineState(placed, state);
            string snapshotBase64 = BuildingSnapshotRepacking.EncodeSnapshot(placed);
            string summonerId = ResolveSummonerPrefabId(node.Definition.Id, building);
            Vector3 position = onShip ? Vector3.zero : NormalizePlacement(node.Snapshot.transform.position);
            ItemData returned = BuildingSnapshotRepacking.CreateReturnedCarrierData(node.Snapshot,
                node.Definition.Id, summonerId, snapshotBase64, position, Quaternion.identity, building.SharedModuleIds);
            if (onShip)
                return FlatWorld.Spaceflight.SpaceSession.Current.TryDismantleMachinePiece(node, returned, out reason);
            summoner = ItemMgr.Instance.InstantiateItem(returned, position,
                returned.transform.rotation, returned.transform.scale);
            summoner.Load();
            summoner.DropInRange();
            summoner.Save();
            if (removeNode && MachineWorld.Remove(node.Id) == null)
                throw new InvalidOperationException("机械节点已失效");
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(summoner);
            return true;
        }
        catch (Exception exception)
        {
            if (summoner != null) ItemMgr.Instance.DespawnItem(summoner, false);
            summoner = null;
            reason = exception.Message;
            return false;
        }
    }

    /// <summary>非锤击致命伤害按原建筑材料回收规则处理纯数据机械。</summary>
    public static void DestroyMechanical(MachineEntity node)
    {
        if (node == null || !GameNetwork.HasStateAuthority) return;
        if (FlatWorld.Spaceflight.SpaceSession.Current?.Owns(node.Id) == true)
        { FlatWorld.Spaceflight.SpaceSession.Current.ApplyPieceDamage(node.Id, float.MaxValue, "结构摧毁"); return; }
        MachineWorld.ReleaseDestroyedFluidContents(node);
        if (TryReadBuildingData(node.Snapshot, out _, out Building_Data building))
        {
            string summonerId = ResolveSummonerPrefabId(node.Definition.Id, building);
            if (!BuildingMaterialSalvage.TrySpawnRandomMaterialSalvage(
                    summonerId, NormalizePlacement(node.Snapshot.transform.position), out string reason,
                    buildingData: node.Snapshot))
                Debug.LogWarning("[机械摧毁] " + reason);
        }
        MachineWorld.Remove(node.Id);
    }

    /// <summary>纯数据机械受损时同步建筑快照中的正式状态。</summary>
    public static void SetMechanicalDamageState(ItemData snapshot, bool damaged)
    {
        if (!WriteBuildingData(snapshot, state =>
                state.State = damaged ? BuildingState.Damaged : BuildingState.Installed))
            throw new InvalidOperationException("机械建筑数据缺少建筑模块");
    }

    /// <summary>生成带完整建筑快照的世界召唤器，不删除当前建筑。</summary>
    public bool TryCreateDismantledSummoner(out Item summoner, out string reason)
    {
        summoner = null;
        reason = null;
        if (ItemMgr.Instance == null || item?.itemData == null || Data?.Role != BuildingRole.PlacedBuilding)
        {
            reason = "当前对象不是可拆除的世界建筑";
            return false;
        }

        if (!TryCapturePlacedSnapshot(out string snapshotBase64, out reason))
            return false;

        try
        {
            Vector3 dropPosition = NormalizePlacement(item.transform.position);
            string buildingPrefabId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
            string summonerPrefabId = ResolveSummonerPrefabId(buildingPrefabId, Data);
            ItemData summonerData = BuildingSnapshotRepacking.CreateReturnedCarrierData(item.itemData,
                buildingPrefabId, summonerPrefabId, snapshotBase64, dropPosition,
                NormalizeBuildingRotation(item.transform.rotation), Data.SharedModuleIds);

            summoner = ItemMgr.Instance.InstantiateItem(
                summonerData,
                dropPosition,
                summonerData.transform.rotation,
                summonerData.transform.scale);
            summoner.Load();

            Mod_Building summonerModule = summoner.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
            if (summonerModule == null)
                throw new MissingComponentException($"{summoner.name} 缺少建筑模块");

            summonerModule.ConfigureAsSummoner(snapshotBase64);
            summoner.DropInRange();
            summoner.Save();
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(summoner);
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            if (summoner != null && ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(summoner, false);
            summoner = null;
            return false;
        }
    }

    public void CompleteNetworkDismantle()
    {
        _dismantlePending = false;
    }

    /// <summary>迁移便携实例的物态、制作品质和耐久，手钻保留动态耐久上限。</summary>
    public void RejectNetworkDismantle(string reason)
    {
        if (!_dismantlePending)
            return;

        _dismantlePending = false;
        CurrentState = damageReceiver != null && damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
            ? BuildingState.Damaged
            : BuildingState.Installed;
        Save();
        if (!string.IsNullOrWhiteSpace(reason))
            Debug.LogWarning($"[联机拆除] 请求被拒绝：{reason}", item);
    }

    private void ConfigureAsSummoner(string snapshotBase64)
    {
        EnsureRuntimeReferences();
        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.Role = BuildingRole.Summoner;
        Data.State = BuildingState.Uninstalled;
        Data.SnapshotBase64 = snapshotBase64;
        Data.BuildingPrefabId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
        Data.SummonerPrefabId = ResolveSummonerPrefabId(Data.BuildingPrefabId, Data);
        _currentState = BuildingState.Uninstalled;

        item.SetInHand(false);
        item.itemData.inHand = false;
        item.itemData.Stack.Amount = 1f;
        item.itemData.Stack.CanBePickedUp = true;
        item.itemData.ItemSpecialData = CreateStatefulSummonerIdentity(snapshotBase64, item.itemData);
        SetColliderMode(enabled: true, trigger: true, damageReceiverEnabled: false);
        BuildingOccupancyRegistry.Unregister(this);
        SyncLightOccluder();
        Save();
    }

    private bool TryCapturePlacedSnapshot(out string snapshotBase64, out string reason)
    {
        snapshotBase64 = null;
        reason = null;

        foreach (Module module in item.itemMods.Mods.Values)
            if (module is IBuildingDismantlePolicy policy && !policy.CanDismantle(out reason)) return false;

        try
        {
            item.Save();
            ItemData snapshot = FastCloner.FastCloner.DeepClone(item.itemData);
            if (!WriteBuildingData(snapshot, state =>
                {
                    state.Version = CurrentDataVersion;
                    state.Role = BuildingRole.PlacedBuilding;
                    state.State = damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
                        ? BuildingState.Damaged
                        : BuildingState.Installed;
                    state.BuildingPrefabId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
                    state.SummonerPrefabId = ResolveSummonerPrefabId(state.BuildingPrefabId, Data);
                    // 朝向是放置时的临时选择，拆回后不参与召唤器堆叠身份。
                    state.HorizontalMirrorX = false;
                    // 防止快照递归包含自己。
                    state.SnapshotBase64 = null;
                }))
            {
                reason = "建筑数据缺少建筑模块";
                return false;
            }

            BuildingSnapshotRepacking.NormalizePlacementIndependentSnapshot(snapshot);
            BuildingPlacementLifecycle.GetExtension(item)?.PrepareRepackedSnapshot(snapshot);

            if (TryReadBuildingData(snapshot, out _, out Building_Data repackedBuildingData) &&
                damageReceiver != null && Mathf.Approximately(damageReceiver.Hp, damageReceiver.MaxHp) &&
                (repackedBuildingData.SharedModuleIds == null || repackedBuildingData.SharedModuleIds.Length == 0) &&
                BuildingPlacementLifecycle.GetExtension(item) is IBuildingSnapshotRepackPolicy repackPolicy &&
                repackPolicy.CanOmitRepackedSnapshot(snapshot))
            {
                // 扩展确认全部状态可从物品定义重建时，不附加快照身份，默认召唤器可继续合堆。
                return true;
            }

            snapshotBase64 = BuildingSnapshotRepacking.EncodeSnapshot(snapshot);
            return true;
        }
        catch (Exception exception)
        {
            reason = $"保存建筑快照失败：{exception.Message}";
            return false;
        }
    }
    #endregion
}
