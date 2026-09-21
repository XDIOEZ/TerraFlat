using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlatWorld.Localization;
using FlatWorld.WorldModel;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 为自主游玩 Agent 提供已加载运行时实体的紧凑查询。
    /// 该工具只读取 ItemMgr 的权威注册表，不生成、传送、拾取或修改任何实体。
    /// </summary>
    [McpForUnityTool(
        "gameplay_query",
        Description = "Query already-loaded FlatWorld runtime data. Sources: runtime Item objects, ecology placements, terrain environment layers, tile surface cells, and ECS drops. source=tile resolves an exact tile id/name and returns nearest loaded coordinates. source=drops observes ECS dropped items with live world positions. Read-only; it never spawns, teleports, picks up, or mutates gameplay.",
        Group = "core")]
    public static class GameplayQueryTool
    {
        private const int DefaultRuntimePageSize = 3;
        private const int MaximumRuntimePageSize = 32;
        private const float MaximumRuntimeRadius = 64f;

        public sealed class Parameters
        {
            [ToolParameter("Query source: runtime for instantiated Items, ecology for deterministic natural placements, terrain for environment layers, tile for surface tile identity, drops for ECS dropped items.", Required = false, DefaultValue = "runtime")]
            public string source { get; set; }

            [ToolParameter("Search text. runtime/drops accept an exact stable ItemDefinition id or exact localized item name. tile accepts an exact numeric tile id, Tile_Block id, tileItemName, or displayName.", Required = false)]
            public string query { get; set; }

            [ToolParameter("Backward-compatible exact stable ItemDefinition id filter. When set, it takes precedence over query. Empty means any id.", Required = false)]
            public string itemId { get; set; }

            [ToolParameter("Required item tag. Empty means any tag.", Required = false)]
            public string tag { get; set; }

            [ToolParameter("Optional pickup filter. Omit to include both pickup and non-pickup entities.", Required = false)]
            public bool? pickup { get; set; }

            [ToolParameter("Optional player-centered runtime query radius in world units. 0 or omitted searches all loaded runtime Items; positive values are capped at 64.", Required = false, DefaultValue = "0")]
            public float radius { get; set; }

            [ToolParameter("Maximum number of nearest matches returned. Defaults to 3; runtime queries are capped at 32 to protect model context.", Required = false, DefaultValue = "3")]
            public int limit { get; set; }

            [ToolParameter("Zero-based runtime result offset for paging through large result sets without returning everything at once.", Required = false, DefaultValue = "0")]
            public int offset { get; set; }

            [ToolParameter("Terrain environment layer id when source=terrain, for example riverFloodplain or height.", Required = false, DefaultValue = "riverFloodplain")]
            public string layerId { get; set; }

            [ToolParameter("Minimum terrain environment value when source=terrain.", Required = false, DefaultValue = "0")]
            public float minValue { get; set; }

            [ToolParameter("Optional maximum terrain environment value when source=terrain. Omit to leave the upper bound open.", Required = false)]
            public float? maxValue { get; set; }

            [ToolParameter("Only return walkable terrain cells when source=terrain.", Required = false, DefaultValue = "true")]
            public bool walkableOnly { get; set; }
        }

        /// <summary>按稳定条件查询当前已加载实体，并按玩家距离排序。</summary>
        public static object HandleCommand(JObject parameters)
        {
            if (!GameplayMcpRuntime.TryGetPlayerContext(
                    out Player player,
                    out _,
                    out _,
                    out string error))
            {
                return new ErrorResponse(error);
            }

            string itemId = parameters?["itemId"]?.ToString()?.Trim() ?? string.Empty;
            string query = parameters?["query"]?.ToString()?.Trim() ?? string.Empty;
            string tag = parameters?["tag"]?.ToString()?.Trim() ?? string.Empty;
            string source = parameters?["source"]?.ToString()?.Trim() ?? "runtime";
            float radius = float.TryParse(
                parameters?["radius"]?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedRadius) && parsedRadius > 0f
                ? Mathf.Clamp(parsedRadius, 0.1f, MaximumRuntimeRadius)
                : 0f;
            int limit = int.TryParse(parameters?["limit"]?.ToString(), out int parsedLimit)
                ? Mathf.Clamp(parsedLimit, 1, 128)
                : DefaultRuntimePageSize;
            int offset = int.TryParse(parameters?["offset"]?.ToString(), out int parsedOffset)
                ? Mathf.Max(0, parsedOffset)
                : 0;
            bool? pickup = TryReadNullableBool(parameters?["pickup"]);

            if (string.Equals(source, "ecology", StringComparison.OrdinalIgnoreCase))
                return QueryEcology(player, itemId, limit);

            if (string.Equals(source, "tile", StringComparison.OrdinalIgnoreCase))
            {
                bool tileWalkableOnly = bool.TryParse(
                    parameters?["walkableOnly"]?.ToString(),
                    out bool parsedTileWalkable) && parsedTileWalkable;
                int tileLimit = parameters?["limit"] == null
                    ? 1
                    : Mathf.Clamp(limit, 1, 9);
                return QueryTile(player, query, tileWalkableOnly, tileLimit);
            }

            if (string.Equals(source, "drops", StringComparison.OrdinalIgnoreCase))
            {
                float dropRadius = radius > 0f ? radius : 16f;
                return QueryDrops(player, itemId, query, tag, pickup, dropRadius, limit, offset);
            }

            if (string.Equals(source, "terrain", StringComparison.OrdinalIgnoreCase))
            {
                string layerId = parameters?["layerId"]?.ToString()?.Trim() ?? "riverFloodplain";
                float minValue = float.TryParse(
                    parameters?["minValue"]?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float parsedMinValue)
                    ? parsedMinValue
                    : 0f;
                float? maxValue = float.TryParse(
                    parameters?["maxValue"]?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float parsedMaxValue)
                    ? parsedMaxValue
                    : null;
                bool walkableOnly = !bool.TryParse(parameters?["walkableOnly"]?.ToString(), out bool parsedWalkable) ||
                                    parsedWalkable;
                return QueryTerrain(player, layerId, minValue, maxValue, walkableOnly, limit);
            }

            if (!string.Equals(source, "runtime", StringComparison.OrdinalIgnoreCase))
                return new ErrorResponse("unknown_query_source: source 只支持 runtime、ecology、terrain、tile 或 drops。");

            ItemMgr itemMgr = ItemMgr.Instance;
            if (itemMgr == null)
                return new ErrorResponse("item_runtime_not_ready: ItemMgr 尚未就绪。");

            // 运行时实体查询会直接进入模型上下文，因此强制分页并限制单页上限。
            limit = Mathf.Clamp(limit, 1, MaximumRuntimePageSize);

            Item[] loadedItems = GetRuntimeCandidates(itemMgr, player, radius);
            HashSet<string> resolvedItemIds = ResolveRuntimeItemIds(loadedItems, itemId, query);
            bool hasIdentityFilter = !string.IsNullOrWhiteSpace(itemId) || !string.IsNullOrWhiteSpace(query);

            var orderedMatches = loadedItems
                .Where(item => IsMatch(item, resolvedItemIds, hasIdentityFilter, tag, pickup))
                .Select(item => new
                {
                    Item = item,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, item.transform.position)
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Item.itemData.Guid)
                .ToArray();

            int totalCount = orderedMatches.Length;
            var matches = orderedMatches
                .Skip(offset)
                .Take(limit)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < matches.Length; i++)
            {
                Item item = matches[i].Item;
                ItemData data = item.itemData;
                DamageReceiver health = item.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
                bool interactable = GameplayMcpRuntime.CanPlayerInteract(item, player);

                var entry = new JObject
                {
                    ["guid"] = data.Guid,
                    ["id"] = data.IDName ?? string.Empty,
                    ["name"] = ResolveCurrentDisplayName(data),
                    ["position"] = new JObject
                    {
                        ["x"] = Round(item.transform.position.x),
                        ["y"] = Round(item.transform.position.y)
                    },
                    ["distance"] = Round(matches[i].Distance),
                    ["amount"] = Round(data.Stack?.Amount ?? 1f),
                    ["pickup"] = data.Stack?.CanBePickedUp ?? false,
                    ["interactable"] = interactable,
                    ["hp"] = health == null
                        ? JValue.CreateNull()
                        : new JArray(Round(health.Hp), Round(health.MaxHp))
                };

                JToken mechanical = BuildMechanicalSnapshot(item);
                if (mechanical != null)
                    entry["mechanical"] = mechanical;

                result.Add(entry);
            }

            bool truncated = offset + matches.Length < totalCount;
            JArray resolvedIdsJson = new JArray(
                (resolvedItemIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Take(16));

            return new SuccessResponse("FlatWorld loaded entity query.", new
            {
                source = "runtime",
                query,
                item_id = itemId,
                resolved_item_ids = resolvedIdsJson,
                tag,
                pickup,
                radius = radius > 0f ? Round(radius) : (float?)null,
                total_count = totalCount,
                offset,
                returned_count = matches.Length,
                limit,
                truncated,
                next_offset = truncated ? offset + matches.Length : (int?)null,
                matches = result
            });
        }

        /// <summary>机械实体额外暴露只读运行状态，供自主测试核对真实网络与加工进度。</summary>
        private static JToken BuildMechanicalSnapshot(Item item)
        {
            Mod_MechanicalNode view = item?.itemMods?.GetMod_ByID<Mod_MechanicalNode>(Mod_MechanicalNode.ModuleId);
            if (view == null)
                return null;

            MechanicalNode node = view.Node;
            MechanicalNetwork network = node?.Network;
            MechanicalNodeState state = node?.State ?? view.LocalState;
            MechanicalProcessor processor = node?.Processor;
            ItemData input = processor?.Input?.Data?.GetItemSlot(0)?.itemData;
            ItemData output = processor?.Output?.Data?.GetItemSlot(0)?.itemData;

            return new JObject
            {
                ["definition"] = view.Definition?.Id ?? string.Empty,
                ["attached"] = node != null,
                ["rpm"] = Round(node?.Rpm ?? 0f),
                ["networkStatus"] = network?.Status ?? string.Empty,
                ["supply"] = Round(network?.Supply ?? 0f),
                ["demand"] = Round(network?.Demand ?? 0f),
                ["manualSeconds"] = Round(state?.ManualSeconds ?? 0f),
                ["processor"] = processor == null
                    ? JValue.CreateNull()
                    : new JObject
                    {
                        ["station"] = processor.Station ?? string.Empty,
                        ["progress01"] = Round(processor.Progress01),
                        ["inputId"] = input?.IDName ?? string.Empty,
                        ["inputAmount"] = Round(input?.Stack?.Amount ?? 0f),
                        ["outputId"] = output?.IDName ?? string.Empty,
                        ["outputAmount"] = Round(output?.Stack?.Amount ?? 0f)
                    }
            };
        }

        /// <summary>按可选玩家半径取得运行时物品；半径查询复用 ItemMgr 的空间索引，避免扫描全场景。</summary>
        private static Item[] GetRuntimeCandidates(ItemMgr itemMgr, Player player, float radius)
        {
            if (radius <= 0f)
            {
                return itemMgr.WorldRunTimeItems.Values
                    .Where(item => IsQueryableRuntimeItem(item, player))
                    .ToArray();
            }

            var candidates = new List<Item>(64);
            var dedupe = new HashSet<Item>();
            itemMgr.QueryItemsInCircleNonAlloc(
                player.transform.position,
                radius,
                ~0,
                player,
                candidates,
                dedupe);

            return candidates
                .Where(item => IsQueryableRuntimeItem(item, player))
                .Where(item => WorldTopologyRuntime.Distance(
                    player.transform.position,
                    item.transform.position) <= radius)
                .ToArray();
        }

        /// <summary>
        /// 查询已加载 ChunkRuntime 的确定性自然物放置结果。
        /// 这里只暴露生成事实，真正交互仍必须等待正常 ChunkView 绑定并通过真实玩法 API 完成。
        /// </summary>
        private static object QueryEcology(Player player, string itemId, int limit)
        {
            ChunkMgr chunkMgr = ChunkMgr.Instance;
            if (chunkMgr == null)
                return new ErrorResponse("chunk_runtime_not_ready: ChunkMgr 尚未就绪。");

            var matches = chunkMgr.Chunks.Values
                .Where(chunk => chunk != null &&
                                chunk.DataStatus == ChunkDataStatus.Ready &&
                                chunk.Ecology != null)
                .SelectMany(chunk => chunk.Ecology.Placements.Select(placement => new
                {
                    Chunk = chunk,
                    Placement = placement,
                    Position = new Vector2(
                        chunk.Address.ChunkOrigin.X + placement.LocalX + 0.5f + placement.OffsetX,
                        chunk.Address.ChunkOrigin.Y + placement.LocalY + 0.5f + placement.OffsetY)
                }))
                .Where(entry => string.IsNullOrEmpty(itemId) ||
                                string.Equals(
                                    entry.Placement.ItemId,
                                    itemId,
                                    StringComparison.OrdinalIgnoreCase))
                .Select(entry => new
                {
                    entry.Placement,
                    entry.Position,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, entry.Position)
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Placement.Guid)
                .Take(limit)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < matches.Length; i++)
            {
                result.Add(new JObject
                {
                    ["guid"] = matches[i].Placement.Guid,
                    ["id"] = matches[i].Placement.ItemId,
                    ["rule"] = matches[i].Placement.RuleId,
                    ["position"] = new JArray(
                        Round(matches[i].Position.x),
                        Round(matches[i].Position.y)),
                    ["distance"] = Round(matches[i].Distance)
                });
            }

            return new SuccessResponse("FlatWorld loaded ecology placement query.", new
            {
                source = "ecology",
                item_id = itemId,
                count = matches.Length,
                matches = result
            });
        }

        /// <summary>查询已加载 ChunkRuntime 中符合环境层阈值的最近地形格。</summary>
        private static object QueryTerrain(
            Player player,
            string layerId,
            float minValue,
            float? maxValue,
            bool walkableOnly,
            int limit)
        {
            if (string.IsNullOrWhiteSpace(layerId))
                return new ErrorResponse("terrain_layer_required: source=terrain 时 layerId 不能为空。");

            ChunkMgr chunkMgr = ChunkMgr.Instance;
            if (chunkMgr == null)
                return new ErrorResponse("chunk_runtime_not_ready: ChunkMgr 尚未就绪。");

            var matches = chunkMgr.Chunks.Values
                .Where(chunk => chunk?.Terrain != null && chunk.DataStatus == ChunkDataStatus.Ready)
                .SelectMany(chunk => EnumerateTerrainMatches(chunk, layerId, minValue, maxValue, walkableOnly))
                .Select(entry => new
                {
                    entry.Position,
                    entry.Value,
                    entry.GroundTileId,
                    entry.BiomeId,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, entry.Position)
                })
                .OrderBy(entry => entry.Distance)
                .ThenByDescending(entry => entry.Value)
                .Take(limit)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < matches.Length; i++)
            {
                result.Add(new JObject
                {
                    ["position"] = new JArray(Round(matches[i].Position.x), Round(matches[i].Position.y)),
                    ["distance"] = Round(matches[i].Distance),
                    ["value"] = Round(matches[i].Value),
                    ["groundTileId"] = matches[i].GroundTileId,
                    ["biomeId"] = matches[i].BiomeId
                });
            }

            return new SuccessResponse("FlatWorld loaded terrain query.", new
            {
                source = "terrain",
                layer_id = layerId,
                min_value = minValue,
                max_value = maxValue,
                walkable_only = walkableOnly,
                count = matches.Length,
                matches = result
            });
        }

        /// <summary>按地块身份查询已加载世界中距离玩家最近的地表格。</summary>
        private static object QueryTile(
            Player player,
            string query,
            bool walkableOnly,
            int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new ErrorResponse("tile_query_required: source=tile 时 query 不能为空。");

            ChunkMgr chunkMgr = ChunkMgr.Instance;
            if (chunkMgr == null)
                return new ErrorResponse("chunk_runtime_not_ready: ChunkMgr 尚未就绪。");

            HashSet<int> resolvedIds = GameplayMcpRuntime.ResolveTerrainTileIds(query, out JArray resolved);
            if (resolvedIds.Count == 0)
            {
                return new SuccessResponse("FlatWorld tile query resolved no tile definition.", new
                {
                    source = "tile",
                    query,
                    resolved,
                    walkable_only = walkableOnly,
                    count = 0,
                    matches = new JArray()
                });
            }

            var matches = chunkMgr.Chunks.Values
                .Where(chunk => chunk?.Terrain != null &&
                                !chunk.Terrain.IsDisposed &&
                                chunk.DataStatus == ChunkDataStatus.Ready)
                .SelectMany(chunk => EnumerateTileMatches(chunk, resolvedIds, walkableOnly))
                .Select(entry => new
                {
                    entry.Position,
                    entry.TileId,
                    entry.BlockId,
                    entry.DisplayName,
                    entry.Walkable,
                    entry.BiomeId,
                    entry.Water,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, entry.Position)
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Position.x)
                .ThenBy(entry => entry.Position.y)
                .Take(limit)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < matches.Length; i++)
            {
                result.Add(new JObject
                {
                    ["position"] = new JObject
                    {
                        ["x"] = Round(matches[i].Position.x),
                        ["y"] = Round(matches[i].Position.y)
                    },
                    ["distance"] = Round(matches[i].Distance),
                    ["tileId"] = matches[i].TileId,
                    ["id"] = matches[i].BlockId,
                    ["name"] = matches[i].DisplayName,
                    ["walkable"] = matches[i].Walkable,
                    ["water"] = matches[i].Water,
                    ["biomeId"] = matches[i].BiomeId
                });
            }

            return new SuccessResponse("FlatWorld nearest loaded tile query.", new
            {
                source = "tile",
                query,
                resolved,
                walkable_only = walkableOnly,
                count = matches.Length,
                matches = result
            });
        }

        /// <summary>枚举单个已加载区块内指定 Tile ID 的有效地表格。</summary>
        private static IEnumerable<TileQueryEntry> EnumerateTileMatches(
            ChunkRuntime chunk,
            HashSet<int> tileIds,
            bool walkableOnly)
        {
            ChunkTerrainData terrain = chunk.Terrain;
            ChunkMgr chunkMgr = ChunkMgr.Instance;
            for (int y = 0; y < terrain.Height; y++)
            {
                for (int x = 0; x < terrain.Width; x++)
                {
                    bool walkable = terrain.IsWalkable(x, y);
                    if (walkableOnly && !walkable)
                        continue;

                    int tileId = GameplayMcpRuntime.ResolveEffectiveTerrainTileId(terrain, x, y);
                    if (!tileIds.Contains(tileId))
                        continue;

                    GameplayMcpRuntime.TryResolveTerrainTileMetadata(
                        chunkMgr,
                        tileId,
                        out string blockId,
                        out string displayName);
                    TerrainCell cell = TerrainSupportLayer.GetSurfaceCell(terrain, x, y);
                    yield return new TileQueryEntry(
                        new Vector2(
                            chunk.Address.ChunkOrigin.X + x + 0.5f,
                            chunk.Address.ChunkOrigin.Y + y + 0.5f),
                        tileId,
                        blockId,
                        displayName,
                        walkable,
                        cell.BiomeId,
                        (cell.Flags & TerrainCellFlags.Water) != 0);
                }
            }
        }

        /// <summary>查询玩家附近 ECS 掉落物，并保留飞行中掉落的实时世界位置。</summary>
        private static object QueryDrops(
            Player player,
            string itemId,
            string query,
            string tag,
            bool? pickup,
            float radius,
            int limit,
            int offset)
        {
            limit = Mathf.Clamp(limit, 1, MaximumRuntimePageSize);
            var candidates = new List<DroppedItemObservation>(64);
            DroppedItemService.QueryNearbyEntityDrops(player.transform.position, radius, candidates);
            HashSet<string> resolvedItemIds = ResolveCatalogItemIds(itemId, query);
            bool hasIdentityFilter = !string.IsNullOrWhiteSpace(itemId) || !string.IsNullOrWhiteSpace(query);

            var orderedMatches = candidates
                .Where(drop => !hasIdentityFilter || resolvedItemIds.Contains(drop.ItemId ?? string.Empty))
                .Where(drop => string.IsNullOrWhiteSpace(tag) ||
                               drop.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase)))
                .Where(drop => !pickup.HasValue || drop.Pickable == pickup.Value)
                .Select(drop => new
                {
                    Drop = drop,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, drop.Position)
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Drop.Id)
                .ToArray();

            int totalCount = orderedMatches.Length;
            var matches = orderedMatches.Skip(offset).Take(limit).ToArray();
            var result = new JArray();
            for (int i = 0; i < matches.Length; i++)
            {
                DroppedItemObservation drop = matches[i].Drop;
                var tags = new JArray();
                for (int tagIndex = 0; tagIndex < drop.Tags.Count; tagIndex++)
                    tags.Add(drop.Tags[tagIndex]);

                result.Add(new JObject
                {
                    ["guid"] = drop.Id,
                    ["id"] = drop.ItemId,
                    ["name"] = GameplayMcpRuntime.ResolveItemDisplayName(drop.ItemId, drop.GameName),
                    ["position"] = new JObject
                    {
                        ["x"] = Round(drop.Position.x),
                        ["y"] = Round(drop.Position.y)
                    },
                    ["distance"] = Round(matches[i].Distance),
                    ["amount"] = Round(drop.Amount),
                    ["pickable"] = drop.Pickable,
                    ["tags"] = tags
                });
            }

            bool truncated = offset + matches.Length < totalCount;
            return new SuccessResponse("FlatWorld ECS dropped item query.", new
            {
                source = "drops",
                query,
                item_id = itemId,
                resolved_item_ids = new JArray(
                    resolvedItemIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                tag,
                pickup,
                radius = Round(radius),
                total_count = totalCount,
                offset,
                returned_count = matches.Length,
                limit,
                truncated,
                next_offset = truncated ? offset + matches.Length : (int?)null,
                matches = result
            });
        }

        /// <summary>掉落物没有 GameObject，直接从运行时物品目录解析稳定 ID 或本地化精确名称。</summary>
        private static HashSet<string> ResolveCatalogItemIds(string explicitItemId, string query)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(explicitItemId))
            {
                result.Add(explicitItemId.Trim());
                return result;
            }

            if (string.IsNullOrWhiteSpace(query))
                return result;

            string normalizedQuery = query.Trim();
            GameRes gameRes = GameRes.ExistingInstance;
            if (gameRes?.ItemDefinitions == null)
                return result;

            string exactId = gameRes.ItemDefinitions.Keys.FirstOrDefault(id =>
                string.Equals(id, normalizedQuery, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactId))
            {
                result.Add(exactId);
                return result;
            }

            foreach (KeyValuePair<string, RuntimeItemDefinition> pair in gameRes.ItemDefinitions)
            {
                if (MatchesLocalizedItemName(normalizedQuery, null, pair.Value))
                    result.Add(pair.Key);
            }

            return result;
        }

        /// <summary>枚举单个已加载区块内符合环境阈值的地形格。</summary>
        private static System.Collections.Generic.IEnumerable<TerrainQueryEntry> EnumerateTerrainMatches(
            ChunkRuntime chunk,
            string layerId,
            float minValue,
            float? maxValue,
            bool walkableOnly)
        {
            ChunkTerrainData terrain = chunk.Terrain;
            for (int y = 0; y < terrain.Height; y++)
            {
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (walkableOnly && !terrain.IsWalkable(x, y))
                        continue;
                    if (!terrain.TryGetEnvironmentValue(layerId, x, y, out float value) ||
                        value < minValue ||
                        (maxValue.HasValue && value > maxValue.Value))
                        continue;

                    TerrainCell cell = terrain.GetCell(x, y);
                    yield return new TerrainQueryEntry(
                        new Vector2(
                            chunk.Address.ChunkOrigin.X + x + 0.5f,
                            chunk.Address.ChunkOrigin.Y + y + 0.5f),
                        value,
                        cell.GroundTileId,
                        cell.BiomeId);
                }
            }
        }

        /// <summary>地形查询的紧凑内部结果。</summary>
        private readonly struct TerrainQueryEntry
        {
            public TerrainQueryEntry(Vector2 position, float value, int groundTileId, int biomeId)
            {
                Position = position;
                Value = value;
                GroundTileId = groundTileId;
                BiomeId = biomeId;
            }

            public Vector2 Position { get; }
            public float Value { get; }
            public int GroundTileId { get; }
            public int BiomeId { get; }
        }

        /// <summary>按地块身份查询使用的紧凑内部结果。</summary>
        private readonly struct TileQueryEntry
        {
            public TileQueryEntry(
                Vector2 position,
                int tileId,
                string blockId,
                string displayName,
                bool walkable,
                int biomeId,
                bool water)
            {
                Position = position;
                TileId = tileId;
                BlockId = blockId ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                Walkable = walkable;
                BiomeId = biomeId;
                Water = water;
            }

            public Vector2 Position { get; }
            public int TileId { get; }
            public string BlockId { get; }
            public string DisplayName { get; }
            public bool Walkable { get; }
            public int BiomeId { get; }
            public bool Water { get; }
        }

        /// <summary>过滤不可用于场景查询的玩家、已销毁对象和未激活对象。</summary>
        private static bool IsQueryableRuntimeItem(Item item, Player player)
        {
            if (item == null || item == player || item.itemData == null || item.DestructionHandled ||
                !item.gameObject.activeInHierarchy)
            {
                return false;
            }

            return true;
        }

        /// <summary>判断运行时实体是否符合已解析的物品身份、标签与拾取条件。</summary>
        private static bool IsMatch(
            Item item,
            HashSet<string> resolvedItemIds,
            bool hasIdentityFilter,
            string tag,
            bool? pickup)
        {
            ItemData data = item.itemData;
            if (hasIdentityFilter &&
                (resolvedItemIds == null || !resolvedItemIds.Contains(data.IDName ?? string.Empty)))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(tag) &&
                (data.Tags == null || !data.Tags.Any(value =>
                    string.Equals(value, tag, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            return !pickup.HasValue || (data.Stack?.CanBePickedUp ?? false) == pickup.Value;
        }

        /// <summary>
        /// 把稳定 ID 或任意已配置 Locale 下的精确显示名解析成当前场景中已加载的 Item ID 集合。
        /// 同名物品允许解析出多个 ID；后续仍按实体距离分页返回。
        /// </summary>
        private static HashSet<string> ResolveRuntimeItemIds(
            IReadOnlyCollection<Item> loadedItems,
            string explicitItemId,
            string query)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(explicitItemId))
            {
                result.Add(explicitItemId.Trim());
                return result;
            }

            if (string.IsNullOrWhiteSpace(query))
                return result;

            string normalizedQuery = query.Trim();
            string[] loadedIds = loadedItems
                .Select(item => item.itemData?.IDName)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // ID 是最稳定且无歧义的入口；命中后不再把同名显示文本混进结果。
            string exactId = loadedIds.FirstOrDefault(id =>
                string.Equals(id, normalizedQuery, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exactId))
            {
                result.Add(exactId);
                return result;
            }

            GameRes gameRes = GameRes.ExistingInstance;
            foreach (string loadedId in loadedIds)
            {
                Item representative = loadedItems.FirstOrDefault(item =>
                    string.Equals(item.itemData?.IDName, loadedId, StringComparison.OrdinalIgnoreCase));
                RuntimeItemDefinition definition = null;
                gameRes?.ItemDefinitions?.TryGetValue(loadedId, out definition);

                if (MatchesLocalizedItemName(normalizedQuery, representative?.itemData, definition))
                    result.Add(loadedId);
            }

            return result;
        }

        /// <summary>匹配默认名、当前语言名以及 Localization Settings 中所有可用 Locale 的名称。</summary>
        private static bool MatchesLocalizedItemName(
            string query,
            ItemData itemData,
            RuntimeItemDefinition definition)
        {
            if (NameEquals(query, itemData?.GameName) ||
                NameEquals(query, definition?.SourceDisplayName) ||
                NameEquals(query, definition?.DisplayName))
            {
                return true;
            }

            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.LabelKey) ||
                !LocalizationSettings.HasSettings ||
                LocalizationSettings.AvailableLocales?.Locales == null)
            {
                return false;
            }

            foreach (Locale locale in LocalizationSettings.AvailableLocales.Locales)
            {
                if (locale == null)
                    continue;

                string localizedName;
                try
                {
                    localizedName = LocalizationSettings.StringDatabase.GetLocalizedString(
                        FlatWorldLocalizationService.DefaultTable,
                        definition.LabelKey,
                        locale,
                        FallbackBehavior.DontUseFallback);
                }
                catch (Exception)
                {
                    continue;
                }

                if (IsUsableLocalizedName(localizedName, definition.LabelKey) &&
                    NameEquals(query, localizedName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>返回当前语言下的正式物品名，定义不可用时回退到运行时数据。</summary>
        private static string ResolveCurrentDisplayName(ItemData data)
        {
            if (data == null)
                return string.Empty;

            GameRes gameRes = GameRes.ExistingInstance;
            if (gameRes?.ItemDefinitions != null &&
                gameRes.ItemDefinitions.TryGetValue(data.IDName ?? string.Empty, out RuntimeItemDefinition definition))
            {
                return definition.DisplayName;
            }

            return data.GameName ?? string.Empty;
        }

        /// <summary>名称查询使用完整精确匹配，防止“石头”误把石墙、石矛等一并返回。</summary>
        private static bool NameEquals(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>过滤 Localization 缺失条目产生的调试字符串和 key 回显。</summary>
        private static bool IsUsableLocalizedName(string value, string key)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.Equals(value, key, StringComparison.Ordinal) &&
                   !value.StartsWith("No translation found for", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>读取可省略的布尔查询参数。</summary>
        private static bool? TryReadNullableBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            return bool.TryParse(token.ToString(), out bool value) ? value : null;
        }

        /// <summary>压缩浮点输出，减少自主游玩循环中的 Token 噪声。</summary>
        private static float Round(float value)
        {
            return (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }
    }
}
