// [UNITY-SKILL:SPRITEATLAS]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>只读扫描 Addressables Sprite 用途并输出图集分桶预览。</summary>
public static class FlatWorldSpriteAtlasBuildPlanTool
{
    #region 计划常量与数据

    private const string UiPrefabRoot = "Assets/2_Prefabs/2-1_UI/";
    internal static readonly string[] MenuPrefabPaths =
    {
        UiPrefabRoot + "MainMenu/Core/UI_MainMenu.prefab",
        UiPrefabRoot + "MainMenu/Save/UI_SaveSelectionPanel.prefab",
        UiPrefabRoot + "MainMenu/WorldSetup/UI_NewGame.prefab",
        UiPrefabRoot + "Settings/Panels/UI_MainMenuSettings.prefab",
        UiPrefabRoot + "MainMenu/Core/UI_MainMenuExitConfirmation.prefab",
        UiPrefabRoot + "MainMenu/WorldSetup/UI_NetworkMode.prefab",
        UiPrefabRoot + "MainMenu/Core/UI_ModManager.prefab",
        UiPrefabRoot + "Common/Controls/UI_SliderControl.prefab",
        UiPrefabRoot + "Common/Controls/UI_TabButton.prefab",
        UiPrefabRoot + "Settings/Components/UI_InputBindingRow.prefab",
        UiPrefabRoot + "Gameplay/Loading/UI_WorldLoading.prefab",
        UiPrefabRoot + "Gameplay/Mobile/UI_MobileControls.prefab",
        UiPrefabRoot + "Gameplay/Mobile/UI_MobileControlLayoutEditor.prefab"
    };
    private const string PlayerAppearanceGroup = "PlayerAppearance";
    private const string ReportPath = "Library/FlatWorldSpriteAtlasPlan/atlas-plan.json";
    private const int MaxAtlasTextureSize = 2048;
    private const int SpritePadding = 4;
    internal const string PreserveSourceSpriteSheetUvLabel = "PreserveSourceSpriteSheetUV";

    [Serializable]
    public sealed class PlanReport
    {
        public int schemaVersion = 1;
        public string unityVersion;
        public string generatedUtc;
        public int addressableGroupCount;
        public int itemSpriteEntryCount;
        public int actorVisualEntryCount;
        public int uiPrefabCount;
        public int menuPrefabCount;
        public int gameplayUiPrefabCount;
        public int creaturePrefabCount;
        public int itemPrefabCount;
        public int worldPrefabCount;
        public int tileEntryCount;
        public int configuredSpriteAddressReferenceCount;
        public int uniqueConfiguredSpriteAddressCount;
        public int repeatedConfiguredSpriteAddressCount;
        public int liquidSpriteAddressReferenceCount;
        public int uniqueLiquidSpriteAddressCount;
        public int uniqueSpriteCount;
        public int excludedPlayerAppearanceSpriteCount;
        public int oversizedSpriteCount;
        public int duplicateAddressCount;
        public SpritePlanEntry[] sprites;
        public AtlasPageEstimate[] estimatedPages;
        public string[] duplicateAddresses;
        public AddressFrequency[] configuredAddresses;
        public AddressFrequency[] repeatedConfiguredAddresses;
        public AddressFrequency[] liquidSpriteAddresses;
    }

    [Serializable]
    public sealed class AddressFrequency
    {
        public string address;
        public int referenceCount;
    }

    [Serializable]
    public sealed class SpritePlanEntry
    {
        public string usage;
        public string sourcePath;
        public string sourceGuid;
        public long sourceLocalId;
        public string spriteName;
        public string[] sourceAddresses;
        public int width;
        public int height;
        public bool eligible;
        public string exclusionReason;
        public string estimatedAtlas;
    }

    [Serializable]
    public sealed class AtlasPageEstimate
    {
        public string name;
        public string usage;
        public int spriteCount;
        public long estimatedSourceArea;
        public int maxTextureSize;
    }

    private sealed class SpriteCandidate
    {
        public Sprite Sprite;
        public string Path;
        public string Guid;
        public long LocalId;
        public readonly HashSet<string> Usages = new(StringComparer.Ordinal);
        public readonly HashSet<string> Addresses = new(StringComparer.Ordinal);
    }

    private sealed class PageBuilder
    {
        public readonly AtlasPageEstimate Estimate;
        private readonly List<(int Height, int UsedWidth)> rows = new();
        private int usedHeight;

        public bool TryPlace(int width, int height)
        {
            width += SpritePadding; height += SpritePadding;
            for (int index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (height > row.Height || row.UsedWidth + width > MaxAtlasTextureSize) continue;
                rows[index] = (row.Height, row.UsedWidth + width);
                return true;
            }
            if (width > MaxAtlasTextureSize || usedHeight + height > MaxAtlasTextureSize) return false;
            rows.Add((height, width));
            usedHeight += height;
            return true;
        }

        public PageBuilder(string name, string usage)
        {
            Estimate = new AtlasPageEstimate
            {
                name = name,
                usage = usage,
                maxTextureSize = MaxAtlasTextureSize
            };
        }
    }

    #endregion

    #region 预览入口

    [MenuItem("FlatWorld/其他工具/SpriteAtlas/生成只读分桶预览")]
    public static void WriteSpriteAtlasReadOnlyBuildPlan()
    {
        PlanReport report = CreateSpriteAtlasBuildPlan();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string absolutePath = Path.Combine(projectRoot, ReportPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        File.WriteAllText(absolutePath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        Debug.Log($"[SpriteAtlasPlan] 只读预览已写入 {ReportPath}：{report.uniqueSpriteCount} 个 Sprite、" +
                  $"{report.estimatedPages.Length} 张估算图集、{report.configuredSpriteAddressReferenceCount}/" +
                  $"{report.uniqueConfiguredSpriteAddressCount} 个静态配置地址引用/唯一地址；" +
                  "静态配置数不等于运行时请求数。未修改 Addressables 或生成图集。");
    }

    internal static PlanReport CreateSpriteAtlasBuildPlan()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("Addressables 尚未初始化。");

        List<ItemDefinitionDto> itemDefinitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        List<ItemDefinitionDto> actorDefinitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitionsForBake();
        HashSet<string> redundantPrefabs = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths(itemDefinitions);
        var candidates = new Dictionary<string, SpriteCandidate>(StringComparer.Ordinal);
        var excludedPlayerSprites = new HashSet<string>(StringComparer.Ordinal);
        int itemSpriteEntries = 0;
        int actorVisualEntries = 0;
        int uiPrefabCount = 0;
        int menuPrefabCount = 0;
        int gameplayUiPrefabCount = 0;
        var menuPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int creaturePrefabCount = 0;
        int itemPrefabCount = 0;
        int worldPrefabCount = 0;
        int tileEntryCount = 0;

        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null) continue;
            foreach (AddressableAssetEntry entry in group.entries)
            {
                string path = AssetDatabase.GUIDToAssetPath(entry.guid)?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(path)) continue;

                // 玩家外观组是独立资源所有者，所有 Sprite 都排除出用途图集，不依赖标签是否齐全。
                if (string.Equals(group.Name, PlayerAppearanceGroup, StringComparison.OrdinalIgnoreCase))
                {
                    AddExcludedSpriteIdentities(path, excludedPlayerSprites);
                    continue;
                }

                bool isItemSprite = entry.labels.Contains("ItemSprite");
                bool isActorVisual = entry.labels.Contains("ActorVisual");
                bool isUiPrefab = entry.labels.Contains("Prefab") &&
                                  path.StartsWith(UiPrefabRoot, StringComparison.OrdinalIgnoreCase);
                string prefabUsage = redundantPrefabs.Contains(path) ? null :
                    entry.labels.Contains(ActorDefinitionCatalogLoader.ShellAddressableLabel) ? "Creatures" :
                    entry.labels.Contains("Prefab") || entry.labels.Contains("ItemPrefab") ? ClassifyPrefabUsage(path) : null;
                bool isWorldTile = entry.labels.Contains("TileBase");

                if (isItemSprite) itemSpriteEntries++;
                if (isActorVisual) actorVisualEntries++;
                if (isUiPrefab) uiPrefabCount++;
                if (isUiPrefab && prefabUsage == "Menu" && menuPaths.Add(path)) menuPrefabCount++;
                if (isUiPrefab && prefabUsage == "GameplayUI") gameplayUiPrefabCount++;
                if (prefabUsage == "Creatures") creaturePrefabCount++;
                if (prefabUsage == "CommonItems") itemPrefabCount++;
                if (prefabUsage == "WorldEnvironment") worldPrefabCount++;
                if (isWorldTile) tileEntryCount++;

                if ((isItemSprite || isActorVisual) && IsPlayerAppearancePath(path))
                    AddExcludedSpriteIdentities(path, excludedPlayerSprites);

                if (prefabUsage != null)
                    AddDependencySprites(path, prefabUsage, candidates, excludedPlayerSprites);
                if (isWorldTile)
                    AddDependencySprites(path, "WorldEnvironment", candidates, excludedPlayerSprites);
            }
        }

        // WorldManager 直接引用的菜单 Prefab 不一定带 Addressables 标签，仍必须纳入启动用途。
        foreach (string menuPath in MenuPrefabPaths)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(menuPath) == null)
                throw new InvalidDataException("主菜单启动 Prefab 缺失：" + menuPath);
            if (menuPaths.Add(menuPath)) menuPrefabCount++;
            AddDependencySprites(menuPath, "Menu", candidates, excludedPlayerSprites);
        }

        Dictionary<string, int> liquidSpriteAddressFrequencies = CollectBuiltInLiquidSpriteAddressFrequencies();
        var configuredAddressUsages = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Dictionary<string, int> configuredAddressFrequencies = CollectConfiguredSpriteAddressFrequencies(
            itemDefinitions, actorDefinitions, configuredAddressUsages);
        foreach (KeyValuePair<string, int> pair in liquidSpriteAddressFrequencies)
        {
            configuredAddressFrequencies[pair.Key] = configuredAddressFrequencies.TryGetValue(pair.Key, out int count)
                ? count + pair.Value : pair.Value;
            AddConfiguredAddressUsage(configuredAddressUsages, pair.Key, "WorldEnvironment");
        }
        var configuredAssetPaths = settings.groups.Where(group => group != null).SelectMany(group => group.entries)
            .GroupBy(entry => entry.address, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().AssetPath, StringComparer.Ordinal);
        foreach (string address in configuredAddressFrequencies.Keys)
        {
            int subAsset = address.LastIndexOf('[');
            string mainAddress = subAsset >= 0 && address.EndsWith("]", StringComparison.Ordinal)
                ? address.Substring(0, subAsset) : address;
            string path = configuredAssetPaths.TryGetValue(mainAddress, out string configuredPath) ? configuredPath : mainAddress;
            if (IsPlayerAppearancePath(path)) AddExcludedSpriteIdentities(path, excludedPlayerSprites);
            foreach (string usage in configuredAddressUsages[address])
                AddSpritesAtPath(path, address, usage, candidates);
        }
        foreach (ItemDefinitionDto definition in itemDefinitions)
        {
            string address = definition?.Visual?.AnimatorControllerAddress?.Trim();
            if (definition == null || definition.Abstract || string.IsNullOrEmpty(address)) continue;
            string path = configuredAssetPaths.TryGetValue(address, out string animationPath) ? animationPath : address;
            AddDependencySprites(path, "CommonItems", candidates, excludedPlayerSprites);
        }
        foreach (ItemDefinitionDto definition in actorDefinitions)
        {
            string address = definition?.Visual?.AnimatorControllerAddress?.Trim();
            if (definition == null || definition.Abstract || string.IsNullOrEmpty(address)) continue;
            string path = configuredAssetPaths.TryGetValue(address, out string animationPath) ? animationPath : address;
            AddDependencySprites(path, "Creatures", candidates, excludedPlayerSprites);
        }
        List<SpritePlanEntry> spriteEntries = BuildSpriteEntries(candidates, excludedPlayerSprites);
        foreach (var texture in spriteEntries.GroupBy(sprite => sprite.sourceGuid, StringComparer.Ordinal))
        {
            string usage = ResolveSpriteAtlasUsageFromSharedDependencies(texture.Select(sprite => sprite.usage));
            foreach (SpritePlanEntry sprite in texture) sprite.usage = usage;
        }

        List<AtlasPageEstimate> pages = EstimateAtlasPages(spriteEntries);
        string[] duplicateAddresses = FindDuplicateAddresses(spriteEntries);
        AddressFrequency[] configuredAddresses = configuredAddressFrequencies
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new AddressFrequency { address = pair.Key, referenceCount = pair.Value })
            .ToArray();
        AddressFrequency[] repeatedConfiguredAddresses = configuredAddressFrequencies
            .Where(pair => pair.Value > 1)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new AddressFrequency { address = pair.Key, referenceCount = pair.Value })
            .ToArray();

        var report = new PlanReport
        {
            unityVersion = Application.unityVersion,
            generatedUtc = DateTime.UtcNow.ToString("O"),
            addressableGroupCount = settings.groups.Count(group => group != null),
            itemSpriteEntryCount = itemSpriteEntries,
            actorVisualEntryCount = actorVisualEntries,
            uiPrefabCount = uiPrefabCount,
            menuPrefabCount = menuPrefabCount,
            gameplayUiPrefabCount = gameplayUiPrefabCount,
            creaturePrefabCount = creaturePrefabCount,
            itemPrefabCount = itemPrefabCount,
            worldPrefabCount = worldPrefabCount,
            tileEntryCount = tileEntryCount,
            configuredSpriteAddressReferenceCount = configuredAddressFrequencies.Values.Sum(),
            uniqueConfiguredSpriteAddressCount = configuredAddressFrequencies.Count,
            repeatedConfiguredSpriteAddressCount = repeatedConfiguredAddresses.Length,
            liquidSpriteAddressReferenceCount = liquidSpriteAddressFrequencies.Values.Sum(),
            uniqueLiquidSpriteAddressCount = liquidSpriteAddressFrequencies.Count,
            uniqueSpriteCount = spriteEntries.Count,
            excludedPlayerAppearanceSpriteCount = excludedPlayerSprites.Count,
            oversizedSpriteCount = spriteEntries.Count(entry => !entry.eligible && entry.exclusionReason == "Sprite尺寸超过2048"),
            duplicateAddressCount = duplicateAddresses.Length,
            sprites = spriteEntries.ToArray(),
            estimatedPages = pages.ToArray(),
            duplicateAddresses = duplicateAddresses,
            configuredAddresses = configuredAddresses,
            liquidSpriteAddresses = liquidSpriteAddressFrequencies.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new AddressFrequency { address = pair.Key, referenceCount = pair.Value }).ToArray(),
            repeatedConfiguredAddresses = repeatedConfiguredAddresses
        };

        return report;
    }

    #endregion

    #region Sprite候选收集

    private static void AddDependencySprites(string dependencyOwnerPath, string usage,
        IDictionary<string, SpriteCandidate> candidates, ISet<string> excludedPlayerSprites)
    {
        foreach (string dependencyPath in AssetDatabase.GetDependencies(dependencyOwnerPath, true))
        {
            string normalizedPath = dependencyPath.Replace('\\', '/');
            if (IsPlayerAppearancePath(normalizedPath))
            {
                AddExcludedSpriteIdentities(normalizedPath, excludedPlayerSprites);
                continue;
            }

            AddSpritesAtPath(normalizedPath, normalizedPath, usage, candidates);
        }
    }

    private static Dictionary<string, int> CollectConfiguredSpriteAddressFrequencies(
        IEnumerable<ItemDefinitionDto> itemDefinitions, IEnumerable<ItemDefinitionDto> actorDefinitions,
        IDictionary<string, HashSet<string>> usages)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (ItemDefinitionDto definition in itemDefinitions)
            if (definition != null && !definition.Abstract)
                AddVisualSpriteAddresses(frequencies, definition.Visual, usages, "CommonItems");
        foreach (ItemDefinitionDto definition in actorDefinitions)
            if (definition != null && !definition.Abstract)
                AddVisualSpriteAddresses(frequencies, definition.Visual, usages, "Creatures");

        foreach (BuffDefinition definition in BuffCatalogLoader.LoadBuiltInDefinitions())
        {
            AddAddressFrequency(frequencies, definition.IconAddress?.Trim());
            AddConfiguredAddressUsage(usages, definition.IconAddress?.Trim(), "GameplayUI");
        }
        return frequencies;
    }

    /// <summary>使用正式液体 DTO 解析与构建规则，收齐 GameRes.WorldLiquids 真正请求的 Sprite 地址。</summary>
    private static Dictionary<string, int> CollectBuiltInLiquidSpriteAddressFrequencies()
    {
        LiquidCatalogDto input = LiquidDefinitionFactory.DeserializeCatalog(
            StreamingAssetsTextLoader.ReadAllText(LiquidCatalogLoader.BuiltInPath));
        List<LiquidDefinition> definitions = LiquidDefinitionFactory.BuildCatalog(input);
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LiquidDefinition definition in definitions)
            if (definition.WorldWater != null)
            {
                definition.WorldWater.Validate(definition.Id);
                AddAddressFrequency(frequencies, definition.WorldWater.SpriteAddress?.Trim());
            }
        return frequencies;
    }

    private static void AddVisualSpriteAddresses(IDictionary<string, int> frequencies,
        ItemVisualDefinitionDto visual, IDictionary<string, HashSet<string>> usages, string usage)
    {
        if (!string.IsNullOrWhiteSpace(visual?.SpriteAddress))
        {
            AddAddressFrequency(frequencies, visual.SpriteAddress.Trim());
            AddConfiguredAddressUsage(usages, visual.SpriteAddress.Trim(), usage);
        }
        if (visual?.SpriteStates == null) return;
        foreach (string address in visual.SpriteStates.Values)
        {
            AddAddressFrequency(frequencies, address?.Trim());
            AddConfiguredAddressUsage(usages, address?.Trim(), usage);
        }
    }

    private static void AddConfiguredAddressUsage(IDictionary<string, HashSet<string>> usages, string address, string usage)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        if (!usages.TryGetValue(address, out HashSet<string> groups))
            usages.Add(address, groups = new HashSet<string>(StringComparer.Ordinal));
        groups.Add(usage);
    }

    private static void AddAddressFrequency(IDictionary<string, int> frequencies, string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        frequencies[address] = frequencies.TryGetValue(address, out int count) ? count + 1 : 1;
    }

    private static void AddSpritesAtPath(string path, string address, string usage,
        IDictionary<string, SpriteCandidate> candidates)
    {
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        foreach (Sprite sprite in sprites)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId))
                continue;

            string identity = $"{guid}:{localId}";
            if (!candidates.TryGetValue(identity, out SpriteCandidate candidate))
            {
                candidate = new SpriteCandidate
                {
                    Sprite = sprite,
                    Path = path,
                    Guid = guid,
                    LocalId = localId
                };
                candidates.Add(identity, candidate);
            }

            candidate.Usages.Add(usage);
            string sourceAddress = CreateSourceAddress(address, path, sprite, sprites.Length);
            if (!string.IsNullOrWhiteSpace(sourceAddress)) candidate.Addresses.Add(sourceAddress);
        }
    }

    private static void AddExcludedSpriteIdentities(string path, ISet<string> excluded)
    {
        foreach (Sprite sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId))
                excluded.Add($"{guid}:{localId}");
        }
    }

    private static string CreateSourceAddress(string address, string path, Sprite sprite, int spriteCount)
    {
        string rootAddress = string.IsNullOrWhiteSpace(address) ? path : address.Trim();
        int subAssetStart = rootAddress.LastIndexOf('[');
        bool isQualifiedSubAsset = subAssetStart >= 0 && rootAddress.EndsWith("]", StringComparison.Ordinal);
        if (isQualifiedSubAsset)
        {
            string qualifiedSpriteName = rootAddress.Substring(subAssetStart + 1, rootAddress.Length - subAssetStart - 2);
            return string.Equals(qualifiedSpriteName, sprite.name, StringComparison.Ordinal) ? rootAddress : null;
        }

        return spriteCount > 1 ? $"{rootAddress}[{sprite.name}]" : rootAddress;
    }

    private static bool IsPlayerAppearancePath(string path) =>
        path.IndexOf("/PlayerTemplate/", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string ClassifyPrefabUsage(string path)
    {
        if (path.StartsWith(UiPrefabRoot, StringComparison.OrdinalIgnoreCase))
            return MenuPrefabPaths.Contains(path, StringComparer.OrdinalIgnoreCase) ||
                   path.StartsWith(UiPrefabRoot + "MainMenu/", StringComparison.OrdinalIgnoreCase)
                ? "Menu" : "GameplayUI";
        if (path.IndexOf("/Gameplay/AI/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/Entity_AI/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Creatures";
        if (path.IndexOf("/Gameplay/Items/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/2_Prefabs/Item/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "CommonItems";
        if (path.IndexOf("/Gameplay/World/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/Gameplay/Building/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/Gameplay/Environment/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/2_Prefabs/Building/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/2_Prefabs/Plant/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/2_Prefabs/Mineral/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("/2_Prefabs/TileBlock/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "WorldEnvironment";
        return null;
    }

    #endregion

    #region 估算与报告

    private static List<SpritePlanEntry> BuildSpriteEntries(
        IReadOnlyDictionary<string, SpriteCandidate> candidates, ISet<string> excludedPlayerSprites)
    {
        var result = new List<SpritePlanEntry>(candidates.Count);
        var importedPixelScales = new Dictionary<string, (float X, float Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteCandidate candidate in candidates.Values)
        {
            int width = Mathf.CeilToInt(candidate.Sprite.rect.width);
            int height = Mathf.CeilToInt(candidate.Sprite.rect.height);
            // 图集规划使用实际导入的像素占用，不能把降采样前的 Sprite.rect 当作打包尺寸。
            if (!importedPixelScales.TryGetValue(candidate.Path, out var scale))
            {
                scale = (1f, 1f);
                if (AssetImporter.GetAtPath(candidate.Path) is TextureImporter importer &&
                    importer.spriteImportMode == SpriteImportMode.Single)
                {
                    importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                    Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(candidate.Path);
                    if (importedTexture != null && sourceWidth > 0 && sourceHeight > 0)
                        scale = (Mathf.Min(1f, (float)importedTexture.width / sourceWidth),
                                 Mathf.Min(1f, (float)importedTexture.height / sourceHeight));
                }
                importedPixelScales.Add(candidate.Path, scale);
            }
            width = Mathf.Max(1, Mathf.CeilToInt(width * scale.X));
            height = Mathf.Max(1, Mathf.CeilToInt(height * scale.Y));
            bool isPlayerAppearance = excludedPlayerSprites.Contains($"{candidate.Guid}:{candidate.LocalId}");
            // 按源表移动 UV 的 GPU 动画不能把独立帧重排进 SpriteAtlas。
            bool preservesSourceSheetUv = AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(candidate.Path))
                .Contains(PreserveSourceSpriteSheetUvLabel, StringComparer.Ordinal);
            bool eligible = !isPlayerAppearance && !preservesSourceSheetUv &&
                width + SpritePadding <= MaxAtlasTextureSize && height + SpritePadding <= MaxAtlasTextureSize;
            string usage = ResolveSpriteAtlasUsageFromSharedDependencies(candidate.Usages);
            result.Add(new SpritePlanEntry
            {
                usage = usage,
                sourcePath = candidate.Path,
                sourceGuid = candidate.Guid,
                sourceLocalId = candidate.LocalId,
                spriteName = candidate.Sprite.name,
                sourceAddresses = candidate.Addresses.OrderBy(address => address, StringComparer.Ordinal).ToArray(),
                width = width,
                height = height,
                eligible = eligible,
                exclusionReason = eligible ? string.Empty : isPlayerAppearance ? "PlayerAppearance保持独立图集" :
                    preservesSourceSheetUv ? "GPU动画保持源表UV" : "Sprite尺寸超过2048"
            });
        }

        result.Sort((left, right) =>
        {
            int usage = string.CompareOrdinal(left.usage, right.usage);
            if (usage != 0) return usage;
            int path = string.CompareOrdinal(left.sourcePath, right.sourcePath);
            return path != 0 ? path : left.sourceLocalId.CompareTo(right.sourceLocalId);
        });
        return result;
    }

    internal static string ResolveSpriteAtlasUsageFromSharedDependencies(IEnumerable<string> usages)
    {
        // 菜单共享纹理留在菜单桶，避免与世界专用 Shared 图集合包。
        var dependencies = new HashSet<string>(usages, StringComparer.Ordinal);
        return dependencies.Contains("Menu") ? "Menu" :
            dependencies.Count > 1 ? "Shared" : dependencies.FirstOrDefault() ?? "Unclassified";
    }

    internal static List<AtlasPageEstimate> EstimateAtlasPages(IList<SpritePlanEntry> sprites)
    {
        var result = new List<AtlasPageEstimate>();
        foreach (var usage in sprites.Where(sprite => sprite.eligible).GroupBy(sprite => sprite.usage)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var pages = new List<PageBuilder>();
            foreach (SpritePlanEntry sprite in usage.OrderByDescending(value => Math.Max(value.width, value.height))
                         .ThenByDescending(value => (long)value.width * value.height)
                         .ThenBy(value => value.sourceGuid, StringComparer.Ordinal).ThenBy(value => value.sourceLocalId))
            {
                PageBuilder page = pages.FirstOrDefault(value => value.TryPlace(sprite.width, sprite.height));
                if (page == null)
                {
                    page = new PageBuilder($"FlatWorld_{usage.Key}_{pages.Count + 1:00}", usage.Key);
                    if (!page.TryPlace(sprite.width, sprite.height)) continue;
                    pages.Add(page);
                    result.Add(page.Estimate);
                }
                page.Estimate.spriteCount++;
                page.Estimate.estimatedSourceArea += (long)sprite.width * sprite.height;
                sprite.estimatedAtlas = page.Estimate.name;
            }
        }
        return result;
    }

    private static string[] FindDuplicateAddresses(IEnumerable<SpritePlanEntry> sprites) => sprites
        .SelectMany(sprite => sprite.sourceAddresses.Select(address => new { address, sprite.sourceGuid, sprite.sourceLocalId }))
        .GroupBy(value => value.address, StringComparer.Ordinal)
        .Where(group => group.Select(value => $"{value.sourceGuid}:{value.sourceLocalId}").Distinct(StringComparer.Ordinal).Count() > 1)
        .Select(group => group.Key)
        .OrderBy(address => address, StringComparer.Ordinal)
        .ToArray();

    #endregion
}
