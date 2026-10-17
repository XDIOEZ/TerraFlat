using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class DimensionTravelProgressStore
{
    #region 角色维度数据的只读缓存

    [Flags]
    private enum EntranceReadFlags : byte
    {
        None = 0,
        Blocked = 1,
        Generated = 2,
        OpenGenerated = 4
    }

    // 弱键让缓存随存档和角色一起释放，字符串未更换时不再解析 JSON。
    private static readonly ConditionalWeakTable<Data_Player, PlayerReadCache> playerReadCaches = new();
    private static readonly ConditionalWeakTable<GameSaveData, SaveReadCache> saveReadCaches = new();

    private sealed class PlayerReadCache
    {
        private string source;
        private bool sourceCurrent;
        public long LightingRevision { get; private set; }
        public Dictionary<string, Vector3> LastPositions { get; private set; } = new(StringComparer.Ordinal);
        public Dictionary<(string World, Vector2Int Cell), EntranceReadFlags> Entrances { get; private set; } = new();
        public List<DimensionPortalAnchor> Anchors { get; private set; } = new();

        public void Invalidate() => sourceCurrent = false;

        public void Refresh(Data_Player player)
        {
            string current = player.ItemSpecialData;
            if (sourceCurrent && ReferenceEquals(source, current)) return;

            JObject data = ItemSpecialDataJsonStore.ReadRoot(current)[NamespaceKey] as JObject;
            var positions = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var entrances = new Dictionary<(string World, Vector2Int Cell), EntranceReadFlags>();
            var anchors = new List<DimensionPortalAnchor>();
            if (data?[LastPositionsKey] is JObject storedPositions)
            {
                foreach (JProperty property in storedPositions.Properties())
                    if (TryParsePosition(property.Value as JObject, out Vector3 position))
                        positions[property.Name] = position;
            }
            if (data?[PortalAnchorsKey] is JObject storedAnchors)
            {
                foreach (JProperty property in storedAnchors.Properties())
                {
                    DimensionPortalAnchor anchor = ParseAnchor(property.Name, property.Value as JObject);
                    if (anchor != null) anchors.Add(anchor);
                }
            }
            if (data?[EntranceStatesKey] is JObject worlds)
            {
                foreach (JProperty world in worlds.Properties())
                {
                    if (world.Value is not JObject cells) continue;
                    foreach (JProperty cell in cells.Properties())
                    {
                        if (cell.Value is not JObject state ||
                            !TryParseEntranceCellKey(cell.Name, out Vector2Int position)) continue;
                        bool blocked = state.Value<bool?>("blocked") == true;
                        bool generated = state.Value<bool?>("generated") == true;
                        EntranceReadFlags flags = blocked ? EntranceReadFlags.Blocked : EntranceReadFlags.None;
                        if (generated) flags |= EntranceReadFlags.Generated;
                        if (generated && !blocked) flags |= EntranceReadFlags.OpenGenerated;
                        if (flags != EntranceReadFlags.None) entrances[(world.Name, position)] = flags;
                    }
                }
            }

            // 最后位置和其它命名空间改写只更新读取快照，不使出口采光集合失效。
            if (!SameEntrances(Entrances, entrances) || !SameAnchors(Anchors, anchors))
                LightingRevision++;
            LastPositions = positions;
            Entrances = entrances;
            Anchors = anchors;
            source = current;
            sourceCurrent = true;
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
        if (player != null && playerReadCaches.TryGetValue(player, out PlayerReadCache cache))
            cache.Invalidate();
    }

    private static bool SameEntrances(Dictionary<(string World, Vector2Int Cell), EntranceReadFlags> left,
        Dictionary<(string World, Vector2Int Cell), EntranceReadFlags> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
            if (!right.TryGetValue(pair.Key, out EntranceReadFlags flags) || flags != pair.Value) return false;
        return true;
    }

    private static bool SameAnchors(List<DimensionPortalAnchor> left, List<DimensionPortalAnchor> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            DimensionPortalAnchor a = left[index], b = right[index];
            if (a.AnchorId != b.AnchorId || a.SurfaceWorldKey != b.SurfaceWorldKey ||
                a.SurfaceEntranceGuid != b.SurfaceEntranceGuid ||
                !a.SurfaceEntrancePosition.Equals(b.SurfaceEntrancePosition) ||
                a.CaveWorldKey != b.CaveWorldKey || a.CaveExitGuid != b.CaveExitGuid ||
                !a.CaveExitPosition.Equals(b.CaveExitPosition)) return false;
        }
        return true;
    }

    #endregion

    #region 当前存档的入口索引与出口采光

    private readonly struct PlayerReadSource
    {
        public readonly Data_Player Player;
        public readonly PlayerReadCache Cache;
        public readonly long Revision;

        public PlayerReadSource(Data_Player player, PlayerReadCache cache)
        {
            Player = player;
            Cache = cache;
            Revision = cache?.LightingRevision ?? 0;
        }
    }

    private sealed class SaveReadCache
    {
        private readonly List<PlayerReadSource> sources = new();
        private readonly Dictionary<(string World, Vector2Int Cell), EntranceReadFlags> entrances = new();
        private readonly Dictionary<(string World, int Guid), DimensionPortalAnchor> anchors = new();
        private readonly Dictionary<string, List<Vector2>> openExits = new(StringComparer.Ordinal);
        private bool initialized;
        public long LightingRevision { get; private set; }

        public void Refresh(GameSaveData save)
        {
            bool changed = !initialized;
            int index = 0;
            if (save.PlayerData_Dict != null)
            {
                foreach (Data_Player player in save.PlayerData_Dict.Values)
                {
                    PlayerReadCache cache = player != null ? GetPlayerReadCache(player) : null;
                    var current = new PlayerReadSource(player, cache);
                    if (index == sources.Count)
                    {
                        sources.Add(current);
                        changed = true;
                    }
                    else
                    {
                        PlayerReadSource previous = sources[index];
                        if (!ReferenceEquals(previous.Player, player) || previous.Revision != current.Revision)
                            changed = true;
                        sources[index] = current;
                    }
                    index++;
                }
            }
            if (index < sources.Count)
            {
                sources.RemoveRange(index, sources.Count - index);
                changed = true;
            }
            if (!changed) return;
            Rebuild();
            initialized = true;
            LightingRevision++;
        }

        public bool HasEntranceFlag(string world, Vector2Int cell, EntranceReadFlags flag) =>
            entrances.TryGetValue((world, cell), out EntranceReadFlags value) && (value & flag) != 0;

        public bool IsCaveExitBlocked(string world, int guid) =>
            anchors.TryGetValue((world, guid), out DimensionPortalAnchor anchor) &&
            HasEntranceFlag(anchor.SurfaceWorldKey, Vector2Int.FloorToInt(anchor.SurfaceEntrancePosition),
                EntranceReadFlags.Blocked);

        public void CollectOpenCaveExitPositions(string world, List<Vector2> result)
        {
            if (openExits.TryGetValue(world, out List<Vector2> positions)) result.AddRange(positions);
        }

        private void Rebuild()
        {
            entrances.Clear();
            anchors.Clear();
            openExits.Clear();
            foreach (PlayerReadSource source in sources)
            {
                if (source.Cache == null) continue;
                foreach (var pair in source.Cache.Entrances)
                {
                    entrances.TryGetValue(pair.Key, out EntranceReadFlags previous);
                    entrances[pair.Key] = previous | pair.Value;
                }
            }

            var seen = new Dictionary<string, HashSet<Vector2Int>>(StringComparer.Ordinal);
            foreach (PlayerReadSource source in sources)
            {
                if (source.Cache == null) continue;
                foreach (DimensionPortalAnchor anchor in source.Cache.Anchors)
                {
                    var key = (anchor.CaveWorldKey, anchor.CaveExitGuid);
                    if (!anchors.ContainsKey(key)) anchors.Add(key, anchor);
                    if (!HasEntranceFlag(anchor.SurfaceWorldKey,
                            Vector2Int.FloorToInt(anchor.SurfaceEntrancePosition), EntranceReadFlags.Blocked))
                        AddOpenExit(anchor.CaveWorldKey, anchor.CaveExitPosition, seen);
                }
                foreach (var pair in source.Cache.Entrances)
                {
                    if ((pair.Value & EntranceReadFlags.OpenGenerated) == 0) continue;
                    WorldAddress surface = WorldAddress.FromWorldKey(pair.Key.World);
                    if (!surface.IsSurface || surface.WorldKey != pair.Key.World) continue;
                    Vector2Int cell = pair.Key.Cell;
                    AddOpenExit(surface.WithDimension(WorldAddress.CaveDimensionId).WorldKey,
                        new Vector2(cell.x + 0.5f, cell.y + 0.5f), seen);
                }
            }
        }

        private void AddOpenExit(string world, Vector2 position,
            Dictionary<string, HashSet<Vector2Int>> seen)
        {
            if (!seen.TryGetValue(world, out HashSet<Vector2Int> cells))
            {
                cells = new HashSet<Vector2Int>();
                seen.Add(world, cells);
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

    /// <summary>只有玩家集合、入口状态或锚点实际改变时，出口采光版本才递增。</summary>
    public static long GetCaveExitLightingRevision(GameSaveData save) =>
        save != null ? GetSaveReadCache(save).LightingRevision : 0;

    #endregion
}
