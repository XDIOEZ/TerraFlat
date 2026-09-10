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
        Description = "Query already-loaded FlatWorld runtime entities. For source=runtime, query accepts either a stable ItemDefinition id or an exact localized item name from any configured locale (for example Ore_Stone, 石头, or Stone). Set radius to query only Items around the player. Results are distance-sorted and paged with a hard result cap to keep context small. Read-only; it never spawns, teleports, picks up, or mutates gameplay.",
        Group = "core")]
    public static class GameplayQueryTool
    {
        private const int DefaultRuntimePageSize = 3;
        private const int MaximumRuntimePageSize = 32;
        private const float MaximumRuntimeRadius = 64f;

        public sealed class Parameters
        {
            [ToolParameter("Query source: runtime for instantiated Items, ecology for deterministic natural placements, terrain for loaded terrain cells.", Required = false, DefaultValue = "runtime")]
            public string source { get; set; }

            [ToolParameter("Runtime search text. Accepts an exact stable ItemDefinition id or an exact localized item name from any configured locale. Examples: Ore_Stone, 石头, Stone.", Required = false)]
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

            ItemMgr itemMgr = ItemMgr.Instance;
            if (itemMgr == null)
                return new ErrorResponse("item_runtime_not_ready: ItemMgr 尚未就绪。");

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
                bool walkableOnly = !bool.TryParse(parameters?["walkableOnly"]?.ToString(), out bool parsedWalkable) ||
                                    parsedWalkable;
                return QueryTerrain(player, layerId, minValue, walkableOnly, limit);
            }

            if (!string.Equals(source, "runtime", StringComparison.OrdinalIgnoreCase))
                return new ErrorResponse("unknown_query_source: source 只支持 runtime、ecology 或 terrain。");

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
                bool interactable = item.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(component => component is IInteractable);

                result.Add(new JObject
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
                });
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
                .SelectMany(chunk => EnumerateTerrainMatches(chunk, layerId, minValue, walkableOnly))
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
                walkable_only = walkableOnly,
                count = matches.Length,
                matches = result
            });
        }

        /// <summary>枚举单个已加载区块内符合环境阈值的地形格。</summary>
        private static System.Collections.Generic.IEnumerable<TerrainQueryEntry> EnumerateTerrainMatches(
            ChunkRuntime chunk,
            string layerId,
            float minValue,
            bool walkableOnly)
        {
            ChunkTerrainData terrain = chunk.Terrain;
            for (int y = 0; y < terrain.Height; y++)
            {
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (walkableOnly && !terrain.IsWalkable(x, y))
                        continue;
                    if (!terrain.TryGetEnvironmentValue(layerId, x, y, out float value) || value < minValue)
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
