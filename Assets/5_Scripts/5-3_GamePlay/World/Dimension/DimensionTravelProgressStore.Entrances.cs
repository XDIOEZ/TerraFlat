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

    /// <summary>封堵和天然入口状态写入世界共有记录，不依赖任何玩家是否曾经进入。</summary>
    public static void SetSurfaceEntranceState(GameSaveData save, WorldAddress surface, Vector2 position,
        bool blocked, bool generated)
    {
        if (save == null || !surface.IsSurface || !IsFinite(position.x) || !IsFinite(position.y)) return;
        string key = EntranceCellKey(position);
        JObject data = ItemSpecialDataJsonStore.ReadRoot(save.DimensionStateJson);
        JObject worlds = data[EntranceStatesKey] as JObject ?? new JObject();
        JObject cells = worlds[surface.WorldKey] as JObject ?? new JObject();
        JObject previous = cells[key] as JObject;
        bool wasGenerated = previous?.Value<bool?>("generated") == true;
        if (previous != null && previous.Value<bool?>("blocked") == blocked && (!generated || wasGenerated)) return;
        cells[key] = new JObject { ["blocked"] = blocked, ["generated"] = generated || wasGenerated };
        worlds[surface.WorldKey] = cells;
        data[EntranceStatesKey] = worlds;
        save.DimensionStateJson = data.ToString(Newtonsoft.Json.Formatting.None);
        EntranceStateRevision++;
    }

    /// <summary>状态与实体加载无关：地表入口所在区块卸载后，矿洞仍能判断出口是否被封堵。</summary>
    public static bool IsSurfaceEntranceBlocked(GameSaveData save, WorldAddress surface, Vector2 position) =>
        HasSurfaceEntranceFlag(save, surface, position, EntranceReadFlags.Blocked);

    public static bool WasGeneratedSurfaceEntrance(GameSaveData save, WorldAddress surface, Vector2 position) =>
        HasSurfaceEntranceFlag(save, surface, position, EntranceReadFlags.Generated);

    private static bool HasSurfaceEntranceFlag(GameSaveData save, WorldAddress surface, Vector2 position,
        EntranceReadFlags flag)
    {
        if (save == null) return false;
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
            return IsSurfaceEntranceBlocked(save, cave.WithDimension(WorldAddress.SurfaceDimensionId), WorldLocalPresentation.ToLogical(exit.transform.position));
        if (save == null || exit.itemData == null) return false;
        SaveReadCache cache = GetSaveReadCache(save);
        return cache.IsCaveExitBlocked(cave.WorldKey, exit.itemData.Guid);
    }

    /// <summary>采光由已保存出口和确定性天然出口共同决定；手工出口不用保持远处区块加载。</summary>
    public static void CollectOpenCaveExitPositions(GameSaveData save, WorldAddress cave, List<Vector2> result)
    {
        if (save == null || result == null ||
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
