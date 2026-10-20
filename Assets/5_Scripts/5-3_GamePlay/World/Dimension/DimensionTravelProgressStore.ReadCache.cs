using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class DimensionTravelProgressStore
{
    #region 玩家最后位置的只读缓存
    [Flags]
    private enum EntranceReadFlags : byte { None = 0, Blocked = 1, Generated = 2, OpenGenerated = 4 }
    private static readonly ConditionalWeakTable<Data_Player, PlayerReadCache> playerReadCaches = new();
    private static readonly ConditionalWeakTable<GameSaveData, SaveReadCache> saveReadCaches = new();

    private sealed class PlayerReadCache
    {
        private string source;
        private bool current;
        public readonly Dictionary<string, Vector3> LastPositions = new(StringComparer.Ordinal);
        public void Invalidate() => current = false;
        public void Refresh(Data_Player player)
        {
            if (current && ReferenceEquals(source, player.ItemSpecialData)) return;
            LastPositions.Clear();
            JObject data = ItemSpecialDataJsonStore.ReadNamespace(player, NamespaceKey);
            if (data[LastPositionsKey] is JObject positions)
                foreach (JProperty entry in positions.Properties())
                    if (TryParsePosition(entry.Value as JObject, out Vector3 point)) LastPositions[entry.Name] = point;
            source = player.ItemSpecialData;
            current = true;
        }
    }

    private static PlayerReadCache GetPlayerReadCache(Data_Player player)
    {
        PlayerReadCache cache = playerReadCaches.GetValue(player, _ => new PlayerReadCache());
        cache.Refresh(player);
        return cache;
    }

    private static void InvalidatePlayerReadCache(Data_Player player)
    {
        if (player != null && playerReadCaches.TryGetValue(player, out PlayerReadCache cache)) cache.Invalidate();
    }
    #endregion

    #region 世界入口索引与出口采光
    // 世界共有记录按字符串代际更新，玩家加入、离开或换档案不会重建出口集合。
    private sealed class SaveReadCache
    {
        private string source;
        private bool current;
        private readonly Dictionary<(string World, Vector2Int Cell), EntranceReadFlags> entrances = new();
        private readonly Dictionary<(string World, int Guid), DimensionPortalAnchor> anchors = new();
        private readonly Dictionary<string, List<Vector2>> openExits = new(StringComparer.Ordinal);
        public long LightingRevision { get; private set; }

        public void Refresh(GameSaveData save)
        {
            string json = save.DimensionStateJson;
            if (current && string.Equals(source, json, StringComparison.Ordinal)) return;
            entrances.Clear();
            anchors.Clear();
            openExits.Clear();
            JObject data = ItemSpecialDataJsonStore.ReadRoot(json);
            if (data[EntranceStatesKey] is JObject worlds)
                foreach (JProperty world in worlds.Properties())
                {
                    if (world.Value is not JObject cells) continue;
                    foreach (JProperty cell in cells.Properties())
                    {
                        if (cell.Value is not JObject state || !TryParseEntranceCellKey(cell.Name, out Vector2Int point)) continue;
                        bool blocked = state.Value<bool?>("blocked") == true;
                        bool generated = state.Value<bool?>("generated") == true;
                        EntranceReadFlags flags = blocked ? EntranceReadFlags.Blocked : EntranceReadFlags.None;
                        if (generated) flags |= EntranceReadFlags.Generated;
                        if (generated && !blocked) flags |= EntranceReadFlags.OpenGenerated;
                        entrances[(world.Name, point)] = flags;
                    }
                }
            var seen = new Dictionary<string, HashSet<Vector2Int>>(StringComparer.Ordinal);
            if (data[PortalAnchorsKey] is JObject storedAnchors)
                foreach (JProperty entry in storedAnchors.Properties())
                {
                    DimensionPortalAnchor anchor = ParseAnchor(entry.Name, entry.Value as JObject);
                    if (anchor == null) continue;
                    anchors[(anchor.CaveWorldKey, anchor.CaveExitGuid)] = anchor;
                    if (!HasEntranceFlag(anchor.SurfaceWorldKey, Vector2Int.FloorToInt(anchor.SurfaceEntrancePosition), EntranceReadFlags.Blocked))
                        AddOpenExit(anchor.CaveWorldKey, anchor.CaveExitPosition, seen);
                }
            foreach (var pair in entrances)
            {
                if ((pair.Value & EntranceReadFlags.OpenGenerated) == 0) continue;
                WorldAddress surface = WorldAddress.FromWorldKey(pair.Key.World);
                if (!surface.IsSurface || surface.WorldKey != pair.Key.World) continue;
                Vector2Int cell = pair.Key.Cell;
                AddOpenExit(surface.WithDimension(WorldAddress.CaveDimensionId).WorldKey,
                    new Vector2(cell.x + .5f, cell.y + .5f), seen);
            }
            source = json;
            current = true;
            LightingRevision++;
        }

        public bool HasEntranceFlag(string world, Vector2Int cell, EntranceReadFlags flag) =>
            entrances.TryGetValue((world, cell), out EntranceReadFlags value) && (value & flag) != 0;

        public bool IsCaveExitBlocked(string world, int guid) =>
            anchors.TryGetValue((world, guid), out DimensionPortalAnchor anchor) &&
            HasEntranceFlag(anchor.SurfaceWorldKey, Vector2Int.FloorToInt(anchor.SurfaceEntrancePosition), EntranceReadFlags.Blocked);

        public void CollectOpenCaveExitPositions(string world, List<Vector2> result)
        {
            if (openExits.TryGetValue(world, out List<Vector2> positions)) result.AddRange(positions);
        }

        private void AddOpenExit(string world, Vector2 position, Dictionary<string, HashSet<Vector2Int>> seen)
        {
            if (!seen.TryGetValue(world, out HashSet<Vector2Int> cells))
            {
                seen.Add(world, cells = new HashSet<Vector2Int>());
                openExits.Add(world, new List<Vector2>());
            }
            if (cells.Add(Vector2Int.FloorToInt(position))) openExits[world].Add(position);
        }
    }

    private static SaveReadCache GetSaveReadCache(GameSaveData save)
    {
        SaveReadCache cache = saveReadCaches.GetValue(save, _ => new SaveReadCache());
        cache.Refresh(save);
        return cache;
    }

    public static long GetCaveExitLightingRevision(GameSaveData save) =>
        save != null ? GetSaveReadCache(save).LightingRevision : 0;
    #endregion
}
