using System;
using FlatWorld.Gameplay.Building;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

// 建筑放置请求与候选数据。
public partial class Mod_Building
{
    #region 建筑放置请求与候选数据
    public virtual void Install()
    {
        if (item == null || item.DestructionHandled || _placementPending || !IsItemInInventory || !IsPlacementModeActive)
            return;

        // 提交以当前准线为准；虚影只是表现，不参与安装资格判定。
        Vector3 placement = NormalizeRequestedPlacement(GetPointerWorldPosition());
        if (!ValidatePlacement(placement, GetAuthorityPosition(), out string reason,
                out BuildingPlacementFailureReason failureReason))
        {
            BuildingPlacementFeedbackEvents.PublishPlacementRejected(
                ResolvePlacementActor(), failureReason, reason);
            Debug.LogWarning($"[建筑安装] {reason}", item);
            return;
        }

        CurrentState = BuildingState.Installing;
        StartInstall?.Invoke();
        _placementPending = true;
        _placementActor = ResolvePlacementActor();

        if (ItemNetworkStateSerialization.BeginNetworkBuilding(this, placement))
        {
            CleanupGhost();
            return;
        }

        _placementPending = false;
        Player actor = _placementActor;
        _placementActor = null;
        CurrentState = BuildingState.NotInstalled;
        Save();
        item.Save();
        if (!TryGetSourceHotBarSlot(out Mod_HotBar hotBar, out ItemSlot sourceSlot))
        {
            BuildingPlacementFeedbackEvents.PublishPlacementRejected(actor, detail: "建造材料已不在当前快捷栏槽位，请重新选择。");
            Debug.LogWarning("[建筑安装] 无法解析召唤器所属真实快捷栏槽位", item);
            return;
        }

        // 输入层只提交放置意图，创建、扣料和失败回滚由单机与联机共用的事务处理。
        var request = new BuildingPlacementRequest
        {
            SourceData = sourceSlot.itemData,
            SourceItem = item,
            SourceInventory = hotBar.Data,
            SourceSlot = sourceSlot,
            Actor = actor,
            Position = placement,
            AuthorityPosition = GetAuthorityPosition(),
            MaximumDistance = GetMaxPlacementDistance(actor),
            HorizontalMirrorX = _placementMirrorX,
            RotationQuarterTurns = PlacementQuarterTurns,
            Extension = BuildingPlacementLifecycle.GetExtension(item)
        };
        if (!BuildingPlacementService.TryPlace(request, out BuildingPlacementResult result, out reason))
        {
            RefreshSourceHotBarSlot(hotBar, sourceSlot);
            BuildingPlacementFeedbackEvents.PublishPlacementRejected(actor, detail: reason);
            Debug.LogWarning($"[建筑安装] {reason}", item);
            return;
        }

        RefreshSourceHotBarSlot(hotBar, sourceSlot);
        if (result.RemainingAmount <= 0f)
        {
            CleanupGhost();
            hotBar.RuntimeInventory.SyncHeldItemImmediately();
        }
        else hotBar.NotifyOwnerNetworkStateChanged();
    }

    /// <summary>纯数据建筑沿用现有角色和状态语义，不创建 Mod_Building 实例。</summary>
    public static void SetInstalledDataState(ItemData placedData)
    {
        if (!WriteBuildingData(placedData, state => state.State = BuildingState.Installed))
            throw new InvalidOperationException("机械建筑数据缺少建筑模块。");
    }

    /// <summary>服务端无需临时 Item 即可校验机械格、地形和建造距离。</summary>
    public static bool ValidateMechanicalDataPlacement(ItemData placedData, Vector3 authorityPosition,
        float maximumDistance, out string reason)
    {
        reason = null;
        if (placedData?.transform == null ||
            !IsWithinPlacementDistance(authorityPosition, placedData.transform.position, maximumDistance))
        {
            reason = "机械建筑超出建造距离";
            return false;
        }
        MachineDefinition definition = MachineCatalog.Get(placedData.IDName);
        if (definition == null)
        {
            reason = "机械定义不存在：" + placedData.IDName;
            return false;
        }
        Vector2Int cell = MachineWorld.CellOf(placedData.transform.position);
        int requiredGroundSupport = BuildingGroundPlacementRules.GetRequiredGroundSupport(placedData.IDName);
        return MachineWorld.ValidatePlacement(definition, cell, false, out reason) &&
            BuildingGroundPlacementRules.CheckTilePenalties(cell, requiredGroundSupport, out reason);
    }

    /// <summary>服务端在候选建筑生成后调用，不依赖客户端预览。</summary>
    public bool ValidateAuthoritativePlacement(Vector3 authorityPosition, out string reason,
        Player placementActor = null)
    {
        Vector3 position = item != null ? item.transform.position : transform.position;
        return ValidatePlacement(position, authorityPosition, out reason, out _, placementActor);
    }

    public void CompleteNetworkPlacement(float authoritativeRemainingAmount)
    {
        if (!_placementPending)
            return;

        Player actor = _placementActor;
        string buildingId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
        _placementPending = false;
        _placementActor = null;
        CurrentState = BuildingState.NotInstalled;
        Save();
        // Host 与服务端共用已扣料的库存，成功回执只同步表现，不能再次查找已清空的材料槽。
        if (GameNetwork.HasStateAuthority)
        {
            CleanupGhost();
            actor?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.RuntimeInventory.SyncHeldItemImmediately();
            return;
        }

        ApplySourceAmount(authoritativeRemainingAmount);
        GameplayProgressEvents.PublishBuildingPlaced(actor, buildingId);
    }

    public void RejectNetworkPlacement(string reason)
    {
        if (!_placementPending)
            return;

        Player actor = _placementActor ?? ResolvePlacementActor();
        _placementPending = false;
        _placementActor = null;
        CurrentState = BuildingState.NotInstalled;
        Save();
        BuildingPlacementFeedbackEvents.PublishPlacementRejected(actor, detail: reason);
        if (!string.IsNullOrWhiteSpace(reason))
            Debug.LogWarning($"[联机建造] 放置被拒绝：{reason}", item);
    }

    /// <summary>只构造候选数据。联机服务端可先实例化、校验位置，再提交安装。</summary>
    public static bool TryCreatePlacementCandidateData(
        ItemData summonerData,
        Vector3 position,
        out ItemData placedData,
        out bool restoredSnapshot,
        out string reason)
    {
        placedData = null;
        restoredSnapshot = false;
        reason = null;

        if (!TryReadBuildingData(summonerData, out _, out Building_Data carrierState))
        {
            reason = "物品不包含有效建筑模块";
            return false;
        }

        MigrateLegacyData(carrierState, summonerData.IDName);
        if (carrierState.Role != BuildingRole.Summoner)
        {
            reason = "只有建筑召唤器可以安装建筑";
            return false;
        }

        string buildingPrefabId = ResolveBuildingPrefabId(summonerData.IDName, carrierState);
        bool mechanical = !string.IsNullOrWhiteSpace(buildingPrefabId) &&
            MachineCatalog.Get(buildingPrefabId) != null;
        bool definitionExists = GameRes.Instance != null &&
            GameRes.Instance.TryGetItemDefinition(buildingPrefabId, out _);
        if (string.IsNullOrWhiteSpace(buildingPrefabId) || !definitionExists ||
            !mechanical && GameRes.Instance.GetPrefab(buildingPrefabId) == null)
        {
            reason = $"找不到召唤器对应的建筑定义：{buildingPrefabId}";
            return false;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(carrierState.SnapshotBase64))
            {
                byte[] payload = Convert.FromBase64String(carrierState.SnapshotBase64);
                if (!ItemNetworkStateSerialization.TryDeserializeItemData(payload, out placedData))
                {
                    reason = "召唤器内的建筑快照无效";
                    return false;
                }

                restoredSnapshot = true;
            }
            else
            {
                placedData = GameRes.Instance.CreateItemData(buildingPrefabId);
            }

            // 即使带有拆除快照，也以当前手持容器的数据覆盖共享模块，不能恢复旧水量/旧库存。
            BuildingSnapshotRepacking.CopySharedItemState(summonerData, placedData, carrierState.SharedModuleIds);
        }
        catch (Exception exception)
        {
            reason = $"构建建筑数据失败：{exception.Message}";
            return false;
        }

        if (placedData == null || !string.Equals(placedData.IDName, buildingPrefabId, StringComparison.Ordinal) ||
            placedData.Stack == null)
        {
            reason = "召唤器快照与建筑预制体不匹配";
            placedData = null;
            return false;
        }

        if (!WriteBuildingData(placedData, state =>
            {
                state.Version = CurrentDataVersion;
                state.Role = BuildingRole.PlacedBuilding;
                state.State = BuildingState.NotInstalled;
                state.SnapshotBase64 = null;
                state.BuildingPrefabId = buildingPrefabId;
                state.SummonerPrefabId = ResolveSummonerPrefabId(buildingPrefabId, carrierState);
            }))
        {
            reason = "建筑快照缺少建筑模块";
            placedData = null;
            return false;
        }

        placedData.Guid = GenerateUniqueRuntimeGuid();
        placedData.inHand = false;
        placedData.Stack.Amount = 1f;
        placedData.Stack.CanBePickedUp = false;
        placedData.transform ??= new ItemTransform();
        placedData.transform.position = NormalizePlacement(position);
        // 手持物会因交互表现产生旋转；落地建筑必须使用固定世界朝向，不能继承手持姿态或快照根旋转。
        placedData.transform.rotation = Quaternion.identity;
        placedData.transform.scale = Vector3.one;
        return true;
    }

    public static bool IsValidSummonerData(ItemData itemData, out string reason)
    {
        reason = null;
        if (!TryReadBuildingData(itemData, out _, out Building_Data data))
        {
            reason = "物品不包含建筑模块";
            return false;
        }

        MigrateLegacyData(data, itemData.IDName);
        if (data.Role != BuildingRole.Summoner)
        {
            reason = "物品不是建筑召唤器";
            return false;
        }

        bool carriesBuildingState = !string.IsNullOrWhiteSpace(data.SnapshotBase64) || data.SharedModuleIds?.Length > 0;
        if (carriesBuildingState && itemData.Stack?.Amount != 1f &&
            (itemData.Stack?.Stackable != true || !HasCanonicalSummonerStackIdentity(itemData, data)))
        {
            reason = "带状态的建筑召唤器只能与状态相同的同类物品堆叠";
            return false;
        }

        return true;
    }

    /// <summary>旧调用兼容：把候选数据显式标记为世界建筑。</summary>
    public static void PreparePlacementCandidateData(ItemData itemData)
    {
        if (!TryReadBuildingData(itemData, out _, out Building_Data currentState))
            return;

        MigrateLegacyData(currentState, itemData.IDName);
        string buildingPrefabId = ResolveBuildingPrefabId(itemData.IDName, currentState);
        WriteBuildingData(itemData, state =>
        {
            state.Version = CurrentDataVersion;
            state.Role = BuildingRole.PlacedBuilding;
            state.State = BuildingState.NotInstalled;
            state.SnapshotBase64 = null;
            state.BuildingPrefabId = buildingPrefabId;
            state.SummonerPrefabId = ResolveSummonerPrefabId(buildingPrefabId, state);
        });
        itemData.IDName = buildingPrefabId;
    }

    [Button]
    private bool ValidatePlacement(
        Vector3 position,
        Vector3 authorityPosition,
        out string reason)
        => ValidatePlacement(position, authorityPosition, out reason, out _);

    private bool ValidatePlacement(Vector3 position, Vector3 authorityPosition,
        out string reason, out BuildingPlacementFailureReason failureReason, Player placementActor = null)
    {
        reason = null;
        failureReason = BuildingPlacementFailureReason.InvalidPosition;
        position = NormalizeRequestedPlacement(position, placementActor);

        if (!IsFinite(position) || !IsFinite(authorityPosition))
        {
            reason = "坐标无效";
            return false;
        }

        if (item?.itemData?.Stack == null || item.itemData.Stack.Amount < 1f)
        {
            reason = "召唤器数量不足";
            return false;
        }

        if (!BuildingPlacementService.ValidateActorEligibility(Data?.BuildingPrefabId,
                placementActor != null ? placementActor : item?.Owner, out reason))
            return false;

        if (!BuildingPlacementLifecycle.ValidatePlacementRules(item,
                placementActor != null ? placementActor : item?.Owner, position, out reason)) return false;

        float maximumPlacementDistance = GetMaxPlacementDistance(placementActor);
        if (!IsWithinPlacementDistance(authorityPosition, position, maximumPlacementDistance))
        {
            reason = "目标超出建造距离";
            failureReason = BuildingPlacementFailureReason.OutOfRange;
            return false;
        }

        Vector2Int placementCell = GetPlacementCell(position);
        if (FlatWorld.Spaceflight.SpaceSession.Current?.TryValidatePlacement(item.itemData, position, PlacementQuarterTurns,
                out bool shipValid, out reason, placementActor != null ? placementActor : ResolvePlacementActor()) == true)
            return shipValid;
        IBuildingPlacementExtension placementExtension = BuildingPlacementLifecycle.GetExtension(item);
        if (placementExtension != null && !placementExtension.ValidatePlacement(placementCell, out reason))
            return false;
        if (!string.IsNullOrWhiteSpace(Data?.TileBlockId))
        {
            // 格子建筑的合法性以建筑阻挡层数据为准，不能再用 TilemapCollider2D 的边界判断相邻格。
            if (!TileBuildingSystem.CanPlace(position, Data.TileBlockId, out reason))
                return false;
            // 地表铺设独立校验来源格与占用，不能再用水格的原通行代价否决平台。
            if (TileBuildingSystem.IsGroundPlacement(Data.TileBlockId))
                return BuildingGroundPlacementRules.CheckGroundLoadCapacity(placementCell,
                    BuildingGroundPlacementRules.GetRequiredGroundSupport(item?.itemData?.IDName), out reason);
        }

        // 预览与真实提交逐格使用相同矩形占地，不能仅检查锚点格。
        int requiredGroundSupport = BuildingGroundPlacementRules.GetRequiredGroundSupport(item?.itemData?.IDName);
        foreach (Vector2Int cell in GetFootprintCells(placementCell))
        {
            if (!BuildingOccupancyRegistry.CanPlace(cell, this, out reason) ||
                !BuildingGroundPlacementRules.CheckTilePenalties(cell, requiredGroundSupport, out reason))
                return false;
        }
        return true;
    }

    /// <summary>格子后端和预览共用承重及占格规则，不需要生成临时 Item。</summary>
    public static bool ValidateTileDataPlacement(ItemData source, Vector3 position, out string reason)
    {
        if (!TryReadBuildingData(source, out _, out Building_Data state) ||
            string.IsNullOrWhiteSpace(state.TileBlockId))
        {
            reason = "材料缺少格子建筑定义";
            return false;
        }
        if (!TileBuildingSystem.CanPlace(position, state.TileBlockId, out reason)) return false;
        Vector2Int anchor = GetPlacementCell(position);
        int support = BuildingGroundPlacementRules.GetRequiredGroundSupport(source.IDName);
        if (TileBuildingSystem.IsGroundPlacement(state.TileBlockId))
            return BuildingGroundPlacementRules.CheckGroundLoadCapacity(anchor, support, out reason);
        for (int y = 0; y < Mathf.Clamp(state.FootprintHeight, 1, 8); y++)
            for (int x = 0; x < Mathf.Clamp(state.FootprintWidth, 1, 8); x++)
            {
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(anchor + new Vector2Int(x, y));
                if (!BuildingOccupancyRegistry.CanPlace(cell, null, out reason) ||
                    !BuildingGroundPlacementRules.CheckTilePenalties(cell, support, out reason)) return false;
            }
        reason = null;
        return true;
    }
    #endregion
}
