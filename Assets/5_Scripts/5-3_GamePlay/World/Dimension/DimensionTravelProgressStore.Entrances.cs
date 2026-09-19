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
            EntranceStateRevision++;
        }
    }

    /// <summary>状态与实体加载无关：地表入口所在区块卸载后，矿洞仍能判断出口是否被封堵。</summary>
    public static bool IsSurfaceEntranceBlocked(GameSaveData save, WorldAddress surface, Vector2 position) =>
        HasSurfaceEntranceFlag(save, surface, position, "blocked");

    public static bool WasGeneratedSurfaceEntrance(GameSaveData save, WorldAddress surface, Vector2 position) =>
        HasSurfaceEntranceFlag(save, surface, position, "generated");

    private static bool HasSurfaceEntranceFlag(GameSaveData save, WorldAddress surface, Vector2 position, string flag)
    {
        if (save?.PlayerData_Dict == null) return false;
        string key = EntranceCellKey(position);
        foreach (Data_Player player in save.PlayerData_Dict.Values)
        {
            JObject data = ItemSpecialDataJsonStore.ReadNamespace(player, NamespaceKey);
            if (data[EntranceStatesKey]?[surface.WorldKey]?[key]?.Value<bool?>(flag) == true) return true;
        }
        return false;
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
        if (save?.PlayerData_Dict == null) return false;
        foreach (Data_Player player in save.PlayerData_Dict.Values)
        {
            JObject data = ItemSpecialDataJsonStore.ReadNamespace(player, NamespaceKey);
            if (data[PortalAnchorsKey] is not JObject anchors) continue;
            foreach (JProperty property in anchors.Properties())
            {
                DimensionPortalAnchor anchor = ParseAnchor(property.Name, property.Value as JObject);
                if (anchor != null && anchor.CaveWorldKey == cave.WorldKey && anchor.CaveExitGuid == exit.itemData.Guid)
                    return IsSurfaceEntranceBlocked(save, WorldAddress.FromWorldKey(anchor.SurfaceWorldKey), anchor.SurfaceEntrancePosition);
            }
        }
        return false;
    }

    /// <summary>采光由已保存出口和确定性天然出口共同决定；手工出口不用保持远处区块加载。</summary>
    public static void CollectOpenCaveExitPositions(GameSaveData save, WorldAddress cave, List<Vector2> result)
    {
        if (save?.PlayerData_Dict == null || result == null ||
            cave.DimensionId != WorldAddress.CaveDimensionId) return;
        var seenCells = new HashSet<Vector2Int>();
        foreach (Data_Player player in save.PlayerData_Dict.Values)
        {
            JObject data = ItemSpecialDataJsonStore.ReadNamespace(player, NamespaceKey);
            if (data[PortalAnchorsKey] is JObject anchors)
            {
                foreach (JProperty property in anchors.Properties())
                {
                    DimensionPortalAnchor anchor = ParseAnchor(property.Name, property.Value as JObject);
                    if (anchor == null || anchor.CaveWorldKey != cave.WorldKey ||
                        IsSurfaceEntranceBlocked(save, WorldAddress.FromWorldKey(anchor.SurfaceWorldKey),
                            anchor.SurfaceEntrancePosition))
                    {
                        continue;
                    }

                    Vector2 position = anchor.CaveExitPosition;
                    if (seenCells.Add(Vector2Int.FloorToInt(position))) result.Add(position);
                }
            }

            // 天然入口不创建旧锚点，但与洞穴出口严格同格；入口状态已经保存地表格坐标。
            WorldAddress surface = cave.WithDimension(WorldAddress.SurfaceDimensionId);
            if (data[EntranceStatesKey]?[surface.WorldKey] is not JObject naturalCells) continue;
            foreach (JProperty property in naturalCells.Properties())
            {
                if (property.Value is not JObject state || state.Value<bool?>("generated") != true ||
                    state.Value<bool?>("blocked") == true ||
                    !TryParseEntranceCellKey(property.Name, out Vector2Int cell) || !seenCells.Add(cell))
                {
                    continue;
                }

                result.Add(new Vector2(cell.x + 0.5f, cell.y + 0.5f));
            }
        }
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
