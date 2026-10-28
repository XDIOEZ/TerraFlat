using System;
using UnityEngine;

// 建筑配置与载体状态访问。
public partial class Mod_Building
{
    #region 建筑配置与载体状态访问
    public void ConfigurePrefabRole(BuildingRole role, string buildingPrefabId, string summonerPrefabId)
    {
        if (string.IsNullOrWhiteSpace(buildingPrefabId))
            throw new ArgumentException("建筑本体 ID 不能为空", nameof(buildingPrefabId));

        summonerPrefabId = string.IsNullOrWhiteSpace(summonerPrefabId)
            ? GetSummonerPrefabId(buildingPrefabId)
            : summonerPrefabId;

        string configuredTileBlockId = Data?.TileBlockId;
        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.Role = role;
        Data.State = BuildingState.NotInstalled;
        Data.SnapshotBase64 = null;
        Data.BuildingPrefabId = buildingPrefabId;
        Data.SummonerPrefabId = summonerPrefabId;
        Data.TileBlockId = string.IsNullOrWhiteSpace(configuredTileBlockId)
            ? GetDefaultTileBlockId(buildingPrefabId)
            : configuredTileBlockId;
        _currentState = Data.State;

        BuildingData ??= new Ex_ModData();
        BuildingData.ID = ModText.Building;
        BuildingData.WriteData(Data);
    }

    public static string GetSummonerPrefabId(string buildingPrefabId)
    {
        if (string.IsNullOrWhiteSpace(buildingPrefabId))
            return string.Empty;

        return buildingPrefabId.EndsWith(SummonerPrefabSuffix, StringComparison.Ordinal)
            ? buildingPrefabId
            : buildingPrefabId + SummonerPrefabSuffix;
    }

    public static string GetBuildingPrefabId(string itemPrefabId)
    {
        if (string.IsNullOrWhiteSpace(itemPrefabId))
            return string.Empty;

        return itemPrefabId.EndsWith(SummonerPrefabSuffix, StringComparison.Ordinal)
            ? itemPrefabId.Substring(0, itemPrefabId.Length - SummonerPrefabSuffix.Length)
            : itemPrefabId;
    }

    public static string GetDefaultTileBlockId(string buildingPrefabId)
    {
        return string.Equals(buildingPrefabId, StoneWallBuildingId, StringComparison.Ordinal)
            ? StoneWallTileBlockId
            : null;
    }

    public static bool TryReadBuildingData(
        ItemData itemData,
        out Ex_ModData moduleData,
        out Building_Data buildingState)
    {
        moduleData = null;
        buildingState = null;
        if (itemData?.ModuleDataDic == null)
            return false;

        foreach (ModuleData candidate in itemData.ModuleDataDic.Values)
        {
            if (candidate is not Ex_ModData exData ||
                !string.Equals(candidate.ID, ModText.Building, StringComparison.Ordinal))
            {
                continue;
            }

            Building_Data state = new();
            exData.ReadData(ref state);
            state ??= new Building_Data();
            moduleData = exData;
            buildingState = state;
            return true;
        }

        return false;
    }

    /// <summary>服务端只允许配置明确声明过的建筑接收左右镜像请求。</summary>
    public static bool SupportsHorizontalMirrorPlacement(ItemData itemData)
    {
        if (!TryReadBuildingData(itemData, out _, out Building_Data state))
            return false;
        MigrateLegacyData(state, itemData?.IDName);
        return state.AllowHorizontalMirrorPlacement;
    }

    /// <summary>把本次临时左右朝向写入已生成的世界建筑候选数据。</summary>
    public static bool TrySetHorizontalMirrorPlacement(ItemData itemData, bool mirrorX)
        => WriteBuildingData(itemData, state =>
            state.HorizontalMirrorX = state.AllowHorizontalMirrorPlacement && mirrorX);

    internal static bool WriteBuildingData(ItemData itemData, Action<Building_Data> mutate)
    {
        if (!TryReadBuildingData(itemData, out Ex_ModData moduleData, out Building_Data state))
            return false;

        MigrateLegacyData(state, itemData?.IDName);
        mutate?.Invoke(state);
        moduleData.WriteData(state);
        return true;
    }

    /// <summary>以归一化建筑快照生成稳定堆叠身份；相同状态的召唤器可合堆，状态不同的快照仍分开。</summary>
    private static string CreateStatefulSummonerIdentity(string snapshotBase64, ItemData carrier = null)
        => BuildingSnapshotRepacking.CreateStatefulSummonerIdentity(snapshotBase64, carrier);

    /// <summary>验证堆叠数量对应的建筑快照身份，防止合并后丢失不同建筑的保存状态。</summary>
    private static bool HasCanonicalSummonerStackIdentity(ItemData itemData, Building_Data state)
    {
        return itemData != null && state != null &&
               !string.IsNullOrWhiteSpace(state.SnapshotBase64) &&
               string.Equals(itemData.ItemSpecialData,
                   CreateStatefulSummonerIdentity(state.SnapshotBase64),
                   StringComparison.Ordinal);
    }

    private static void MigrateLegacyData(Building_Data state, string carrierItemId = null)
    {
        if (state == null)
            return;

        if (state.Version < 2)
        {
            state.Role = state.State is BuildingState.Installed or BuildingState.Damaged
                ? BuildingRole.PlacedBuilding
                : BuildingRole.Summoner;
            state.SnapshotBase64 = null;
        }

        if (string.IsNullOrWhiteSpace(state.BuildingPrefabId))
            state.BuildingPrefabId = GetBuildingPrefabId(carrierItemId);

        if (string.IsNullOrWhiteSpace(state.SummonerPrefabId))
            state.SummonerPrefabId = GetSummonerPrefabId(state.BuildingPrefabId);

        // 旧石墙召唤器/存档没有 TileBlockId，迁移后新放置直接进入格子建筑系统。
        if (string.IsNullOrWhiteSpace(state.TileBlockId) &&
            !string.IsNullOrWhiteSpace(state.BuildingPrefabId))
        {
            state.TileBlockId = GetDefaultTileBlockId(state.BuildingPrefabId);
        }

        state.Version = CurrentDataVersion;
    }

    private static string ResolveBuildingPrefabId(string carrierItemId, Building_Data state)
        => !string.IsNullOrWhiteSpace(state?.BuildingPrefabId)
            ? state.BuildingPrefabId
            : GetBuildingPrefabId(carrierItemId);

    private static string ResolveSummonerPrefabId(string buildingPrefabId, Building_Data state)
        => !string.IsNullOrWhiteSpace(state?.SummonerPrefabId)
            ? state.SummonerPrefabId
            : GetSummonerPrefabId(buildingPrefabId);

    private static Vector3 NormalizePlacement(Vector3 position)
        => WorldTopologyRuntime.NormalizePosition(
            new Vector3(Mathf.Floor(position.x) + 0.5f, Mathf.Floor(position.y) + 0.5f, 0f));

    private int GetLocalPlacementQuarterTurns()
        => item?.GetComponentInChildren<Mod_MechanicalNode>(true)?.PlacementQuarterTurns ?? _placementQuarterTurns;
    #endregion
}
