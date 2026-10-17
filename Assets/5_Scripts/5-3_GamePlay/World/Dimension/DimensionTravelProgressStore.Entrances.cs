using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class DimensionTravelProgressStore
{
    #region 入口状态的持久化

    private const string EntranceStatesKey = "surfaceEntranceStates";
    public static uint EntranceStateRevision { get; private set; }

    /// <summary>
    /// 与现有入口锚点共用维度 JSON 扩展，不改变 MemoryPack 布局。维度旅行当前只在离线模式开放，
    /// 同步写入已有角色的旅行记录，切换角色也读取同一世界的封堵状态。
    /// </summary>
    public static void SetSurfaceEntranceState(GameSaveData save, WorldAddress surface, Vector2 position,
        bool blocked, bool generated)
    {
        if (save?.PlayerData_Dict == null || !surface.IsSurface || !IsFinite(position.x) || !IsFinite(position.y)) return;
        string key = EntranceCellKey(position);
        foreach (Data_Player player in save.PlayerData_Dict.Values)
        {
            JObject data = ItemSpecialDataJsonStore.ReadNamespace(player, NamespaceKey);
            JObject worlds = data[EntranceStatesKey] as JObject ?? new JObject();
            JObject cells = worlds[surface.WorldKey] as JObject ?? new JObject();
            JObject previous = cells[key] as JObject;
            bool wasGenerated = previous?.Value<bool?>("generated") == true;
            if (previous != null && previous.Value<bool?>("blocked") == blocked && (!generated || wasGenerated)) continue;
            cells[key] = new JObject { ["blocked"] = blocked, ["generated"] = generated || wasGenerated };
            worlds[surface.WorldKey] = cells;
            data[EntranceStatesKey] = worlds;
            ItemSpecialDataJsonStore.WriteNamespace(player, NamespaceKey, data);
            InvalidatePlayerReadCache(player);
            EntranceStateRevision++;
        }
    }

    /// <summary>状态与实体加载无关：地表入口所在区块卸载后，矿洞仍能判断出口是否被封堵。</summary>
    public static bool IsSurfaceEntranceBlocked(GameSaveData save, WorldAddress surface, Vector2 position) =>
        HasSurfaceEntranceFlag(save, surface, position, EntranceReadFlags.Blocked);

    public static bool WasGeneratedSurfaceEntrance(GameSaveData save, WorldAddress surface, Vector2 position) =>
        HasSurfaceEntranceFlag(save, surface, position, EntranceReadFlags.Generated);

    private static bool HasSurfaceEntranceFlag(GameSaveData save, WorldAddress surface, Vector2 position,
        EntranceReadFlags flag)
    {
        if (save?.PlayerData_Dict == null) return false;
        return GetSaveReadCache(save).HasEntranceFlag(surface.WorldKey, Vector2Int.FloorToInt(position), flag);
    }

    private static string EntranceCellKey(Vector2 position)
    {
        Vector2Int cell = Vector2Int.FloorToInt(position);
        return FormattableString.Invariant($"{cell.x},{cell.y}");
    }

    #endregion

    #region 出口解析与采光查询

    /// <summary>手工出口优先复用已经保存的锚点；天然出口与地表入口同格配对。</summary>
    public static bool IsCaveExitBlocked(GameSaveData save, WorldAddress cave, Item exit, bool generated)
    {
        if (exit == null || cave.DimensionId != WorldAddress.CaveDimensionId) return false;
        if (generated)
            return IsSurfaceEntranceBlocked(save, cave.WithDimension(WorldAddress.SurfaceDimensionId), exit.transform.position);
        if (save?.PlayerData_Dict == null || exit.itemData == null) return false;
        SaveReadCache cache = GetSaveReadCache(save);
        return cache.IsCaveExitBlocked(cave.WorldKey, exit.itemData.Guid);
    }

    /// <summary>采光由已保存出口和确定性天然出口共同决定；手工出口不用保持远处区块加载。</summary>
    public static void CollectOpenCaveExitPositions(GameSaveData save, WorldAddress cave, List<Vector2> result)
    {
        if (save?.PlayerData_Dict == null || result == null ||
            cave.DimensionId != WorldAddress.CaveDimensionId) return;
        GetSaveReadCache(save).CollectOpenCaveExitPositions(cave.WorldKey, result);
    }

    private static bool TryParseEntranceCellKey(string key, out Vector2Int cell)
    {
        cell = default;
        if (string.IsNullOrWhiteSpace(key)) return false;
        int separator = key.IndexOf(',');
        if (separator <= 0 || separator >= key.Length - 1 ||
            !int.TryParse(key.Substring(0, separator), out int x) ||
            !int.TryParse(key.Substring(separator + 1), out int y))
        {
            return false;
        }

        cell = new Vector2Int(x, y);
        return true;
    }

    #endregion
}
