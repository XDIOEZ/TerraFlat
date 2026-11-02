// [UNITY-SKILL:SPRITEATLAS]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;

/// <summary>构建前按用途生成SpriteAtlas、Addressables分组和直接Sprite引用索引。</summary>
public sealed class FlatWorldSpriteAtlasPrebuildGenerator : BuildPlayerProcessor
{
    #region 构建常量与报告

    private const string GeneratedRoot = "Assets/6_Art/Generated/FlatWorldSpriteAtlases";
    private const string AtlasRoot = GeneratedRoot + "/Atlases";
    private const string CatalogPath = GeneratedRoot + "/StartupSpriteAtlasCatalog.asset";
    private const string PurposeCatalogRoot = GeneratedRoot + "/Catalogs";
    private const string CatalogGroupName = "FlatWorldSpriteAtlas_StartupCatalog";
    private const string AddressableGroupPrefix = "FlatWorldSpriteAtlas_";
    private const string BaselineRoot = "Library/FlatWorldContentBakeTaskBaseline";
    private const string BuildReportPath = "Library/FlatWorldSpriteAtlasPlan/build-report.json";
    private const string AtlasFingerprintPrefix = "FlatWorldSpriteAtlas:v1:";
    private const int MaximumAtlasTextureSize = 2048;
    private const int AtlasPadding = 4;
    private static readonly string[] UsageGroups = { "Menu", "GameplayUI", "CommonItems", "WorldEnvironment", "Creatures", "Shared" };
    private static readonly HashSet<string> NewGeneratedAssetPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SpriteCompatibleShaderNames = new(StringComparer.Ordinal)
    {
        "Sprites/Default", "Sprites/Diffuse", "UI/Default", "UI/DefaultETC1",
        "Universal Render Pipeline/2D/Sprite-Lit-Default", "Universal Render Pipeline/2D/Sprite-Unlit-Default"
    };

    public override int callbackOrder => -20000;

    [Serializable]
    private sealed class BuildReportData
    {
        public int schemaVersion = 2;
        public string generatedUtc;
        public int plannedSpriteCount;
        public int atlasPageCount;
        public int catalogEntryCount;
        public int purposeCatalogCount;
        public int manifestSpriteReferenceCount;
        public int liquidSpriteAddressReferenceCount;
        public int uniqueLiquidSpriteAddressCount;
        public int managedSourceAssetCount;
        public int migratedSourceAssetCount;
        public int restoredSourceAssetCount;
        public int actualPackedTexturePageCount;
        public int actualBoundSpriteCount;
        public int missingPackedSpriteCount;
        public int readableTextureExclusionCount;
        public int specialShaderExclusionCount;
        public int oversizedSpriteExclusionCount;
        public int ambiguousAddressCount;
        public int configuredAddressCount;
        public int configuredAddressCatalogHitCount;
        public int configuredAddressFallbackCount;
        public int playerAppearanceExclusionCount;
        public int menuPrefabCount;
        public int gameplayUiPrefabCount;
        public bool atlasIncludeInBuild = true;
        public string spritePackerMode;
        public string catalogAddress = StartupSpriteAtlasCatalog.Address;
        public string catalogGroup = CatalogGroupName;
        public string atlasGroupPacking = "PackTogether per usage";
        public int[] pageSpriteCounts;
        public int[] pageSourceTextureCounts;
        public string[] pageNames;
        public FlatWorldSpriteAtlasPackVerification.PageResult[] packedPageResults;
        public string[] ambiguousAddresses;
        public string[] unmappedConfiguredAddresses;
    }

    private sealed class CatalogSpriteMapping
    {
        public string Address;
        public string Identity;
        public string Usage;
        public Sprite Sprite;
    }

    private sealed class AtlasGenerationPage
    {
        public string Name;
        public string Usage;
        public string Path;
        public Sprite[] Sprites;
        public FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry[] Candidates;
    }

    private sealed class SpriteAtlasGenerationPlan
    {
        public readonly List<AtlasGenerationPage> Pages = new();
        public StartupSpriteAtlasEntry[] CatalogEntries;
        public readonly Dictionary<string, StartupSpriteAtlasEntry[]> PurposeCatalogs = new(StringComparer.Ordinal);
        public readonly HashSet<string> AmbiguousAddresses = new(StringComparer.Ordinal);
        public int ReadableTextureExclusionCount;
        public int SpecialShaderExclusionCount;
        public int OversizedSpriteExclusionCount;
    }

    #endregion

    #region 预构建入口

    public override void PrepareForBuild(BuildPlayerContext context) => GenerateSpriteAtlasesAndPurposeCatalogs();

    public static void GenerateSpriteAtlasesAndPurposeCatalogs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new BuildFailedException("SpriteAtlas 资源只能在非运行状态生成。");
        string spritePackerMode = EditorSettings.spritePackerMode.ToString();
        if (!spritePackerMode.Contains("SpriteAtlasV2"))
            throw new BuildFailedException("SpriteAtlas 打包已禁用，请将项目 Sprite Packer Mode 设为 Sprite Atlas V2。");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new BuildFailedException("SpriteAtlas 生成需要已初始化的 Addressables 设置。");
        FlatWorldSpriteAtlasBuildPlanTool.PlanReport plan =
            FlatWorldSpriteAtlasBuildPlanTool.CreateSpriteAtlasBuildPlan();
        if (plan.sprites == null || plan.estimatedPages == null)
            throw new BuildFailedException("SpriteAtlas 分桶计划为空。");

        // 所有可预测的地址、类型和源 Sprite 错误在任何资产/组写入前检查。
        SpriteAtlasGenerationPlan generation = CreateSpriteAtlasGenerationPlan(settings, plan);
        ValidateSpriteAtlasGenerationPlan(settings, generation);

        NewGeneratedAssetPaths.Clear();
        BackupExistingAsset(AssetDatabase.GetAssetPath(settings));
        var activePageNames = new List<string>();
        var activePageSpriteCounts = new List<int>();
        try
        {
            EnsureAddressableGroup(settings, CatalogGroupName, BundledAssetGroupSchema.BundlePackingMode.PackTogether);
            foreach (string usage in UsageGroups)
                EnsureAddressableGroup(settings, AddressableGroupPrefix + usage,
                    BundledAssetGroupSchema.BundlePackingMode.PackTogether);

            foreach (AtlasGenerationPage page in generation.Pages)
            {
                EnsureSpriteAtlasAsset(page.Path, page.Name, page.Sprites, page.Candidates);
                RegisterAddressableAsset(settings, page.Path, AddressableGroupPrefix + page.Usage, page.Name);
                activePageNames.Add(page.Name);
                activePageSpriteCounts.Add(page.Sprites.Length);
            }

            AssetDatabase.SaveAssets();
            FlatWorldSpriteAtlasPackVerification.VerificationResult packed =
                FlatWorldSpriteAtlasPackVerification.PackAndVerify(generation.Pages.Select(page =>
                    new FlatWorldSpriteAtlasPackVerification.PageInput
                    { Name = page.Name, Path = page.Path, Sprites = page.Sprites }).ToArray());

            RemoveStaleGeneratedSpriteAssets(settings, generation.Pages.Select(page => page.Path)
                .Concat(generation.PurposeCatalogs.Keys.Select(usage => PurposeCatalogRoot + "/" + usage + ".asset"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var purpose in generation.PurposeCatalogs)
            {
                string path = PurposeCatalogRoot + "/" + purpose.Key + ".asset";
                WriteStartupSpriteAtlasCatalog(path, purpose.Value, Array.Empty<StartupSpriteAtlasLocation>());
                RegisterAddressableAsset(settings, path, AddressableGroupPrefix + purpose.Key,
                    StartupSpriteAtlasCatalog.Address + "/" + purpose.Key);
            }
            var locations = generation.PurposeCatalogs.SelectMany(purpose => purpose.Value.Select(entry =>
                new StartupSpriteAtlasLocation { Address = entry.Address,
                    CatalogAddress = StartupSpriteAtlasCatalog.Address + "/" + purpose.Key }))
                .OrderBy(location => location.Address, StringComparer.Ordinal).ToArray();
            StartupSpriteAtlasCatalog catalog = WriteStartupSpriteAtlasCatalog(CatalogPath,
                Array.Empty<StartupSpriteAtlasEntry>(), locations);
            RegisterAddressableAsset(settings, CatalogPath, CatalogGroupName, StartupSpriteAtlasCatalog.Address);
            FlatWorldSpriteAtlasSourceEntryOwnership.Result sources =
                FlatWorldSpriteAtlasSourceEntryOwnership.Reconcile(settings,
                    CollectActiveSourceAddressableEntries(settings, plan, generation),
                    (path, group, address) => RegisterAddressableAsset(settings, path, group, address, allowSourceEntryMove: true));

            AssetDatabase.SaveAssets();
            WriteBuildReport(plan, catalog, activePageNames, activePageSpriteCounts, generation, packed, sources);
            Debug.Log($"[SpriteAtlas构建] {activePageNames.Count} 个用途图集页，位置清单 {catalog.Locations.Length} 个地址、{generation.PurposeCatalogs.Count} 个用途目录；" +
                      $"可读纹理排除 {generation.ReadableTextureExclusionCount}、特殊Shader排除 {generation.SpecialShaderExclusionCount}、" +
                      $"大图排除 {generation.OversizedSpriteExclusionCount}、地址冲突 {generation.AmbiguousAddresses.Count}；" +
                      $"实际纹理页 {packed.actualTexturePageCount}、漏绑 {packed.missingSpriteCount}、源组恢复 {sources.Restored}。" +
                      "图集 IncludeInBuild 且显式 Addressable，catalog 由 Addressables 依赖图接入。");
        }
        catch (Exception exception)
        {
            throw new BuildFailedException("SpriteAtlas 生成中途失败；可能已写入 " + GeneratedRoot +
                                           " 与 Addressables 组，源文件基线位于 " + BaselineRoot + "。\n" + exception);
        }
    }

    #endregion

    #region 源贴图实际归属

    /// <summary>图集源与菜单依赖分别归组，避免菜单 Prefab 通过 Default 大包拉入整个世界。</summary>
    private static IReadOnlyDictionary<string, (string Path, string Group)> CollectActiveSourceAddressableEntries(
        AddressableAssetSettings settings, FlatWorldSpriteAtlasBuildPlanTool.PlanReport plan, SpriteAtlasGenerationPlan generation)
    {
        var externalPlayerAppearance = plan.sprites
            .Where(sprite => sprite.exclusionReason == "PlayerAppearance保持独立图集")
            .Select(sprite => sprite.sourceGuid).ToHashSet(StringComparer.Ordinal);
        var required = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Register(string guid, string usage)
        {
            // PlayerAppearance 明确保留原分组，即使用途目录直接引用也不迁移源纹理。
            if (externalPlayerAppearance.Contains(guid)) return;
            if (!required.TryGetValue(guid, out HashSet<string> groups))
                required.Add(guid, groups = new HashSet<string>(StringComparer.Ordinal));
            groups.Add(usage);
        }

        foreach (AtlasGenerationPage page in generation.Pages)
            foreach (FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry sprite in page.Candidates)
                Register(sprite.sourceGuid, page.Usage);
        foreach (KeyValuePair<string, StartupSpriteAtlasEntry[]> purpose in generation.PurposeCatalogs)
            foreach (StartupSpriteAtlasEntry entry in purpose.Value)
            {
                if (entry?.Sprite == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(entry.Sprite, out string guid, out long _))
                    throw new BuildFailedException("用途目录引用的 Sprite 没有可追踪源 GUID：" + entry?.Address);
                Register(guid, purpose.Key);
            }

        var active = plan.sprites.GroupBy(sprite => sprite.sourceGuid, StringComparer.Ordinal)
            .Where(group => required.ContainsKey(group.Key))
            .ToDictionary(group => group.Key, group =>
                (Path: group.First().sourcePath, Group: AddressableGroupPrefix +
                    FlatWorldSpriteAtlasBuildPlanTool.ResolveSpriteAtlasUsageFromSharedDependencies(required[group.Key])),
                StringComparer.Ordinal);
        if (active.Count != required.Count)
            throw new BuildFailedException("用途目录存在未收集到源计划的 Sprite：" +
                string.Join(", ", required.Keys.Except(active.Keys)));
        foreach (string path in AssetDatabase.GetDependencies(FlatWorldSpriteAtlasBuildPlanTool.MenuPrefabPaths, true))
        {
            if (path.StartsWith(GeneratedRoot, StringComparison.OrdinalIgnoreCase)) continue;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (settings.FindAssetEntry(guid) != null)
                active[guid] = (path, AddressableGroupPrefix + "Menu");
        }
        return active;
    }

    #endregion

    #region 纯内存预检

    private static SpriteAtlasGenerationPlan CreateSpriteAtlasGenerationPlan(AddressableAssetSettings settings,
        FlatWorldSpriteAtlasBuildPlanTool.PlanReport plan)
    {
        HashSet<string> specialShaderSpriteIdentities = CollectSpecialShaderSpriteIdentities(settings);
        var catalogByAddress = new Dictionary<string, CatalogSpriteMapping>(StringComparer.Ordinal);
        var generation = new SpriteAtlasGenerationPlan
        {
            OversizedSpriteExclusionCount = plan.oversizedSpriteCount
        };
        var resolvedSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        var requiredAddresses = new HashSet<string>((plan.configuredAddresses ??
            Array.Empty<FlatWorldSpriteAtlasBuildPlanTool.AddressFrequency>()).Select(value => value.address), StringComparer.Ordinal);
        // 索引只保留正式启动链会请求的地址，间接依赖的图片不强行提前引用。
        foreach (FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry candidate in plan.sprites)
        {
            string identity = CreateSpriteIdentity(candidate.sourceGuid, candidate.sourceLocalId);
            Sprite sprite = ResolveSprite(candidate);
            if (sprite == null)
                throw new BuildFailedException($"Sprite Catalog 源资源无法解析：{candidate.sourcePath}/{candidate.spriteName}");
            resolvedSprites.Add(identity, sprite);
            foreach (string address in candidate.sourceAddresses ?? Array.Empty<string>())
                if (requiredAddresses.Contains(address))
                    AddCatalogAddress(catalogByAddress, generation.AmbiguousAddresses, address, identity, sprite, candidate.usage);
        }

        foreach (FlatWorldSpriteAtlasBuildPlanTool.AtlasPageEstimate page in plan.estimatedPages)
        {
            FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry[] candidates = plan.sprites
                .Where(sprite => sprite.eligible && string.Equals(sprite.estimatedAtlas, page.name, StringComparison.Ordinal))
                .OrderBy(sprite => sprite.sourceGuid, StringComparer.Ordinal)
                .ThenBy(sprite => sprite.sourceLocalId)
                .ToArray();
            var packableSprites = new List<Sprite>(candidates.Length);
            var packedCandidates = new List<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry>(candidates.Length);
            var pageIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry candidate in candidates)
            {
                string identity = CreateSpriteIdentity(candidate.sourceGuid, candidate.sourceLocalId);
                Sprite sprite = resolvedSprites[identity];
                if (!pageIdentities.Add(identity)) continue;

                if (IsReadableTexture(candidate.sourcePath))
                {
                    generation.ReadableTextureExclusionCount++;
                    continue;
                }
                if (specialShaderSpriteIdentities.Contains(identity))
                {
                    generation.SpecialShaderExclusionCount++;
                    continue;
                }

                packableSprites.Add(sprite);
                packedCandidates.Add(candidate);
            }

            // 一个源纹理本来已在同一张图里，不再给它增加独立 Atlas 和额外纹理内存。
            if (packedCandidates.Select(candidate => candidate.sourceGuid).Distinct(StringComparer.Ordinal).Take(2).Count() < 2) continue;
            generation.Pages.Add(new AtlasGenerationPage
            {
                Name = page.name,
                Usage = page.usage,
                Path = AtlasRoot + "/" + page.name + ".spriteatlasv2",
                Sprites = packableSprites.ToArray(),
                Candidates = packedCandidates.ToArray()
            });
        }

        if (generation.AmbiguousAddresses.Count > 0)
        {
            string[] ambiguous = generation.AmbiguousAddresses.OrderBy(address => address, StringComparer.Ordinal).ToArray();
            throw new BuildFailedException("Sprite Catalog 地址映射歧义，拒绝静默回退：" + string.Join(", ", ambiguous));
        }

        foreach (var purpose in catalogByAddress.Values.GroupBy(value => value.Usage, StringComparer.Ordinal))
            generation.PurposeCatalogs.Add(purpose.Key, purpose.OrderBy(value => value.Address, StringComparer.Ordinal)
                .Select(value => new StartupSpriteAtlasEntry { Address = value.Address, Sprite = value.Sprite }).ToArray());
        generation.CatalogEntries = catalogByAddress.Values
            .OrderBy(entry => entry.Address, StringComparer.Ordinal)
            .Select(entry => new StartupSpriteAtlasEntry { Address = entry.Address, Sprite = entry.Sprite })
            .ToArray();

        var catalogAddresses = new HashSet<string>(generation.CatalogEntries.Select(entry => entry.Address), StringComparer.Ordinal);
        string[] missingConfiguredAddresses = (plan.configuredAddresses ?? Array.Empty<FlatWorldSpriteAtlasBuildPlanTool.AddressFrequency>())
            .Select(entry => entry.address)
            .Where(address => !catalogAddresses.Contains(address))
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToArray();
        if (missingConfiguredAddresses.Length > 0)
            throw new BuildFailedException($"Sprite Catalog 未覆盖 {missingConfiguredAddresses.Length} 个配置地址（spriteAddress/spriteStates）：" +
                                           string.Join(", ", missingConfiguredAddresses));
        if (generation.CatalogEntries.Length == 0)
            throw new BuildFailedException("启动 Sprite Catalog 为空，拒绝生成无效索引。");
        return generation;
    }

    private static void ValidateSpriteAtlasGenerationPlan(AddressableAssetSettings settings,
        SpriteAtlasGenerationPlan generation)
    {
        var outputs = generation.Pages.Select(page => new
            {
                path = page.Path,
                address = page.Name,
                group = AddressableGroupPrefix + page.Usage,
                isCatalog = false
            })
            .Concat(generation.PurposeCatalogs.Keys.Select(usage => new
            { path = PurposeCatalogRoot + "/" + usage + ".asset", address = StartupSpriteAtlasCatalog.Address + "/" + usage,
                group = AddressableGroupPrefix + usage, isCatalog = true }))
            .Append(new { path = CatalogPath, address = StartupSpriteAtlasCatalog.Address, group = CatalogGroupName, isCatalog = true })
            .ToArray();
        var plannedAddresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            if (!plannedAddresses.Add(output.address))
                throw new BuildFailedException("SpriteAtlas 预检发现重复 Address：" + output.address);

            bool fileExists = File.Exists(output.path);
            if (output.isCatalog && fileExists && AssetDatabase.LoadAssetAtPath<StartupSpriteAtlasCatalog>(output.path) == null)
                throw new BuildFailedException("Catalog 路径已被其他资源占用：" + output.path);
            if (!output.isCatalog && fileExists && AssetImporter.GetAtPath(output.path) is not SpriteAtlasImporter)
                throw new BuildFailedException("图集路径已被其他资源占用：" + output.path);
            if (!output.isCatalog && !UsageGroups.Contains(output.group.Substring(AddressableGroupPrefix.Length), StringComparer.Ordinal))
                throw new BuildFailedException("SpriteAtlas 计划包含未配置用途分组：" + output.group);

            string guid = AssetDatabase.AssetPathToGUID(output.path);
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null) continue;
                foreach (AddressableAssetEntry entry in group.entries)
                {
                    if (!string.Equals(entry.address, output.address, StringComparison.Ordinal) || entry.guid == guid) continue;
                    throw new BuildFailedException($"SpriteAtlas Address 已被其他资源占用：{output.address}/{entry.AssetPath}");
                }
            }

            AddressableAssetEntry existing = string.IsNullOrEmpty(guid) ? null : settings.FindAssetEntry(guid);
            if (existing != null && existing.parentGroup != null &&
                !string.Equals(existing.parentGroup.Name, output.group, StringComparison.Ordinal) &&
                !existing.parentGroup.Name.StartsWith(AddressableGroupPrefix, StringComparison.Ordinal))
                throw new BuildFailedException($"生成资源被非SpriteAtlas组占用：{output.path}/{existing.parentGroup.Name}");
        }

    }

    #endregion

    #region SpriteAtlas与索引资源

    private static void EnsureSpriteAtlasAsset(string path, string atlasName, IReadOnlyList<Sprite> sprites,
        IReadOnlyList<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry> plannedSprites)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        bool existedBeforeGeneration = File.Exists(path);
        bool metaExistedBeforeGeneration = File.Exists(path + ".meta");
        string fingerprint = ComputeAtlasFingerprint(atlasName, plannedSprites);
        SpriteAtlasImporter importer = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
        bool rebuild = importer == null || !string.Equals(importer.userData,
            AtlasFingerprintPrefix + fingerprint, StringComparison.Ordinal);
        if (rebuild)
        {
            BackupExistingAsset(path);
            BackupExistingAsset(path + ".meta");
            if (!existedBeforeGeneration) MarkNewGeneratedAssetPath(path);
            if (!metaExistedBeforeGeneration) MarkNewGeneratedAssetPath(path + ".meta");
            var atlasAsset = new SpriteAtlasAsset();
            atlasAsset.name = atlasName;
            atlasAsset.Add(sprites.Cast<UnityEngine.Object>().ToArray());
            SpriteAtlasAsset.Save(atlasAsset, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
        }

        if (importer == null) throw new BuildFailedException("SpriteAtlas V2 Importer 无效：" + path);
        bool changed = ConfigureSpriteAtlasImporter(importer, fingerprint);
        if (!changed) return;
        BackupExistingAsset(path);
        BackupExistingAsset(path + ".meta");
        importer.SaveAndReimport();
    }

    private static bool ConfigureSpriteAtlasImporter(SpriteAtlasImporter importer, string fingerprint)
    {
        bool changed = !importer.includeInBuild;
        importer.includeInBuild = true;

        SpriteAtlasTextureSettings textureSettings = importer.textureSettings;
        changed |= textureSettings.filterMode != FilterMode.Point || textureSettings.generateMipMaps;
        textureSettings.filterMode = FilterMode.Point;
        textureSettings.generateMipMaps = false;
        importer.textureSettings = textureSettings;

        SpriteAtlasPackingSettings packingSettings = importer.packingSettings;
        changed |= packingSettings.padding != AtlasPadding || packingSettings.enableRotation || packingSettings.enableTightPacking;
        packingSettings.padding = AtlasPadding;
        packingSettings.enableRotation = false;
        packingSettings.enableTightPacking = false;
        importer.packingSettings = packingSettings;

        TextureImporterPlatformSettings standalone = importer.GetPlatformSettings("Standalone");
        changed |= !standalone.overridden || standalone.maxTextureSize != MaximumAtlasTextureSize;
        standalone.name = "Standalone";
        standalone.overridden = true;
        standalone.maxTextureSize = MaximumAtlasTextureSize;
        importer.SetPlatformSettings(standalone);

        // Android 显式使用 ETC2 RGBA，避免图集以未压缩 RGBA32 写入 APK。
        TextureImporterPlatformSettings android = importer.GetPlatformSettings("Android");
        changed |= !android.overridden || android.maxTextureSize != MaximumAtlasTextureSize ||
                   android.format != TextureImporterFormat.ETC2_RGBA8 ||
                   android.textureCompression != TextureImporterCompression.Compressed ||
                   android.crunchedCompression;
        android.name = "Android";
        android.overridden = true;
        android.maxTextureSize = MaximumAtlasTextureSize;
        android.format = TextureImporterFormat.ETC2_RGBA8;
        android.textureCompression = TextureImporterCompression.Compressed;
        android.crunchedCompression = false;
        importer.SetPlatformSettings(android);

        string fingerprintValue = AtlasFingerprintPrefix + fingerprint;
        changed |= !string.Equals(importer.userData, fingerprintValue, StringComparison.Ordinal);
        importer.userData = fingerprintValue;
        return changed;
    }

    private static StartupSpriteAtlasCatalog WriteStartupSpriteAtlasCatalog(string path,
        StartupSpriteAtlasEntry[] entries, StartupSpriteAtlasLocation[] locations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        StartupSpriteAtlasCatalog catalog = AssetDatabase.LoadAssetAtPath<StartupSpriteAtlasCatalog>(path);
        if (catalog == null)
        {
            if (File.Exists(path)) throw new BuildFailedException("Sprite 目录路径已被其他资源占用：" + path);
            MarkNewGeneratedAssetPath(path);
            catalog = ScriptableObject.CreateInstance<StartupSpriteAtlasCatalog>();
            catalog.Entries = entries;
            catalog.Locations = locations;
            AssetDatabase.CreateAsset(catalog, path);
        }
        else
        {
            BackupExistingAsset(path);
            catalog.Entries = entries;
            catalog.Locations = locations;
            EditorUtility.SetDirty(catalog);
        }
        return catalog;
    }

    private static void AddCatalogAddress(IDictionary<string, CatalogSpriteMapping> entries,
        ISet<string> ambiguous, string address, string identity, Sprite sprite, string usage)
    {
        if (string.IsNullOrWhiteSpace(address) || ambiguous.Contains(address)) return;
        address = address.Trim();
        if (!entries.TryGetValue(address, out CatalogSpriteMapping existing))
        {
            entries.Add(address, new CatalogSpriteMapping { Address = address, Identity = identity, Usage = usage, Sprite = sprite });
            return;
        }
        if (string.Equals(existing.Identity, identity, StringComparison.Ordinal)) return;
        entries.Remove(address);
        ambiguous.Add(address);
    }

    private static Sprite ResolveSprite(FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry candidate)
    {
        foreach (Sprite sprite in AssetDatabase.LoadAllAssetsAtPath(candidate.sourcePath).OfType<Sprite>())
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId) &&
                string.Equals(guid, candidate.sourceGuid, StringComparison.Ordinal) && localId == candidate.sourceLocalId)
                return sprite;
        return null;
    }

    private static string ComputeAtlasFingerprint(string atlasName,
        IEnumerable<FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry> sprites)
    {
        var source = new StringBuilder(atlasName).Append('|').Append(MaximumAtlasTextureSize).Append('|')
            .Append(AtlasPadding).Append("|Point|NoMipmaps|NoRotation|NoTightPacking\n");
        foreach (FlatWorldSpriteAtlasBuildPlanTool.SpritePlanEntry sprite in sprites)
            source.Append(sprite.sourceGuid).Append(':').Append(sprite.sourceLocalId).Append('|')
                .Append(sprite.spriteName).Append('|').Append(sprite.sourcePath).Append('|')
                .Append(AssetDatabase.GetAssetDependencyHash(sprite.sourcePath)).Append('\n');
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source.ToString()))).Replace("-", string.Empty);
    }

    private static bool IsReadableTexture(string sourcePath) =>
        AssetImporter.GetAtPath(sourcePath) is TextureImporter importer && importer.isReadable;

    #endregion

    #region 特殊Shader排除

    private static HashSet<string> CollectSpecialShaderSpriteIdentities(AddressableAssetSettings settings)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null) continue;
            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (!entry.labels.Contains("Prefab")) continue;
                string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (SpriteRenderer renderer in prefab.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.sprite != null && IsSpecialShader(renderer.sharedMaterial))
                        AddSpriteIdentity(renderer.sprite, identities);
                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                    if (image.sprite != null && IsSpecialShader(image.material))
                        AddSpriteIdentity(image.sprite, identities);
            }
        }
        return identities;
    }

    private static bool IsSpecialShader(Material material) => material != null && material.shader != null &&
        !SpriteCompatibleShaderNames.Contains(material.shader.name);

    private static void AddSpriteIdentity(Sprite sprite, ISet<string> identities)
    {
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId))
            identities.Add(CreateSpriteIdentity(guid, localId));
    }

    private static string CreateSpriteIdentity(string guid, long localId) => guid + ":" + localId;

    #endregion

    #region Addressables用途分组

    private static AddressableAssetGroup EnsureAddressableGroup(AddressableAssetSettings settings,
        string groupName, BundledAssetGroupSchema.BundlePackingMode packingMode)
    {
        AddressableAssetGroup group = settings.FindGroup(groupName);
        bool groupCreated = group == null;
        if (group == null)
        {
            BackupExistingAsset(AssetDatabase.GetAssetPath(settings));
            group = settings.CreateGroup(groupName, false, false, false, new List<AddressableAssetGroupSchema>(),
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            MarkNewGeneratedAssetPath(AssetDatabase.GetAssetPath(group));
            BundledAssetGroupSchema newBundledSchema = group.GetSchema<BundledAssetGroupSchema>();
            ContentUpdateGroupSchema newUpdateSchema = group.GetSchema<ContentUpdateGroupSchema>();
            if (newBundledSchema != null) MarkNewGeneratedAssetPath(AssetDatabase.GetAssetPath(newBundledSchema));
            if (newUpdateSchema != null) MarkNewGeneratedAssetPath(AssetDatabase.GetAssetPath(newUpdateSchema));
        }

        string groupPath = AssetDatabase.GetAssetPath(group);
        if (!groupCreated) BackupExistingAsset(groupPath);
        BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
        bool schemaCreated = schema == null;
        if (schemaCreated)
        {
            schema = group.AddSchema<BundledAssetGroupSchema>(false);
            MarkNewGeneratedAssetPath(AssetDatabase.GetAssetPath(schema));
        }
        else if (!groupCreated)
        {
            BackupExistingAsset(AssetDatabase.GetAssetPath(schema));
        }
        bool changed = schema.BundleMode != packingMode || !schema.IncludeInBuild;
        if (!string.Equals(schema.BuildPath.GetName(settings), AddressableAssetSettings.kLocalBuildPath, StringComparison.Ordinal))
        {
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            changed = true;
        }
        if (!string.Equals(schema.LoadPath.GetName(settings), AddressableAssetSettings.kLocalLoadPath, StringComparison.Ordinal))
        {
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            changed = true;
        }
        schema.BundleMode = packingMode;
        schema.IncludeInBuild = true;
        if (changed)
        {
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, schema, true, true);
        }
        return group;
    }

    private static void RegisterAddressableAsset(AddressableAssetSettings settings, string assetPath,
        string groupName, string address, bool allowSourceEntryMove = false)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) throw new BuildFailedException("生成资源缺少GUID：" + assetPath);
        AddressableAssetGroup group = settings.FindGroup(groupName);
        AddressableAssetEntry existing = settings.FindAssetEntry(guid);
        AddressableAssetEntry addressCollision = settings.groups.Where(value => value != null)
            .SelectMany(value => value.entries)
            .FirstOrDefault(value => value.guid != guid && string.Equals(value.address, address, StringComparison.Ordinal));
        if (addressCollision != null)
            throw new BuildFailedException("SpriteAtlas Address 已被其他资源占用：" + address + "/" + addressCollision.AssetPath);
        if (!allowSourceEntryMove && existing != null && existing.parentGroup != null && existing.parentGroup != group &&
            !existing.parentGroup.Name.StartsWith(AddressableGroupPrefix, StringComparison.Ordinal))
            throw new BuildFailedException($"生成SpriteAtlas被其他Addressables组占用：{assetPath}/{existing.parentGroup.Name}");

        BackupExistingAsset(AssetDatabase.GetAssetPath(settings));
        BackupExistingAsset(AssetDatabase.GetAssetPath(group));
        if (existing?.parentGroup != null && existing.parentGroup != group)
            BackupExistingAsset(AssetDatabase.GetAssetPath(existing.parentGroup));
        AddressableAssetEntry entry = existing != null && existing.parentGroup == group
            ? existing : settings.CreateOrMoveEntry(guid, group, false, false);
        if (!string.Equals(entry.address, address, StringComparison.Ordinal)) entry.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, true);
    }

    private static void RemoveStaleGeneratedSpriteAssets(AddressableAssetSettings settings, ISet<string> activePaths)
    {
        foreach (var directory in new[] { (Root: AtlasRoot, Pattern: "*.spriteatlasv2"),
                     (Root: PurposeCatalogRoot, Pattern: "*.asset") })
        {
            if (!Directory.Exists(directory.Root)) continue;
            foreach (string rawPath in Directory.EnumerateFiles(directory.Root, directory.Pattern, SearchOption.TopDirectoryOnly).ToArray())
            {
                string path = rawPath.Replace('\\', '/');
                if (activePaths.Contains(path)) continue;
                BackupExistingAsset(path);
                BackupExistingAsset(path + ".meta");
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) settings.RemoveAssetEntry(guid, false);
                if (!AssetDatabase.DeleteAsset(path)) throw new BuildFailedException("无法清理旧生成 Sprite 资源：" + path);
            }
        }
    }

    #endregion

    #region 可追溯备份与报告

    private static void BackupExistingAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return;
        string normalized = assetPath.Replace('\\', '/');
        if (NewGeneratedAssetPaths.Contains(normalized)) return;
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string sourcePath = Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath)) return;
        string backupPath = Path.Combine(projectRoot,
            (BaselineRoot + "/" + normalized).Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(backupPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
        File.Copy(sourcePath, backupPath);
    }

    private static void MarkNewGeneratedAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return;
        string normalized = assetPath.Replace('\\', '/');
        NewGeneratedAssetPaths.Add(normalized);
        NewGeneratedAssetPaths.Add(normalized + ".meta");
    }

    private static void WriteBuildReport(FlatWorldSpriteAtlasBuildPlanTool.PlanReport plan,
        StartupSpriteAtlasCatalog catalog, IReadOnlyList<string> pageNames, IReadOnlyList<int> pageSpriteCounts,
        SpriteAtlasGenerationPlan generation, FlatWorldSpriteAtlasPackVerification.VerificationResult packed,
        FlatWorldSpriteAtlasSourceEntryOwnership.Result sources)
    {
        var catalogAddresses = new HashSet<string>(catalog.Locations.Select(entry => entry.Address), StringComparer.Ordinal);
        string[] unmappedConfiguredAddresses = (plan.configuredAddresses ?? Array.Empty<FlatWorldSpriteAtlasBuildPlanTool.AddressFrequency>())
            .Select(entry => entry.address).Where(address => !catalogAddresses.Contains(address))
            .OrderBy(address => address, StringComparer.Ordinal).ToArray();
        var report = new BuildReportData
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            plannedSpriteCount = plan.uniqueSpriteCount,
            atlasPageCount = pageNames.Count,
            catalogEntryCount = catalog.Locations.Length,
            purposeCatalogCount = generation.PurposeCatalogs.Count,
            // 根目录只有地址位置表，真实 Sprite 引用分散在按用途加载的目录中。
            manifestSpriteReferenceCount = catalog.Entries.Length,
            liquidSpriteAddressReferenceCount = plan.liquidSpriteAddressReferenceCount,
            uniqueLiquidSpriteAddressCount = plan.uniqueLiquidSpriteAddressCount,
            managedSourceAssetCount = sources.Managed,
            migratedSourceAssetCount = sources.Migrated,
            restoredSourceAssetCount = sources.Restored,
            actualPackedTexturePageCount = packed.actualTexturePageCount,
            actualBoundSpriteCount = packed.boundSpriteCount,
            missingPackedSpriteCount = packed.missingSpriteCount,
            readableTextureExclusionCount = generation.ReadableTextureExclusionCount,
            specialShaderExclusionCount = generation.SpecialShaderExclusionCount,
            oversizedSpriteExclusionCount = generation.OversizedSpriteExclusionCount,
            ambiguousAddressCount = generation.AmbiguousAddresses.Count,
            configuredAddressCount = plan.uniqueConfiguredSpriteAddressCount,
            configuredAddressCatalogHitCount = plan.uniqueConfiguredSpriteAddressCount - unmappedConfiguredAddresses.Length,
            configuredAddressFallbackCount = unmappedConfiguredAddresses.Length,
            playerAppearanceExclusionCount = plan.excludedPlayerAppearanceSpriteCount,
            menuPrefabCount = plan.menuPrefabCount,
            gameplayUiPrefabCount = plan.gameplayUiPrefabCount,
            spritePackerMode = EditorSettings.spritePackerMode.ToString(),
            pageSpriteCounts = pageSpriteCounts.ToArray(),
            pageSourceTextureCounts = generation.Pages.Select(page => page.Candidates.Select(candidate => candidate.sourceGuid)
                .Distinct(StringComparer.Ordinal).Count()).ToArray(),
            pageNames = pageNames.ToArray(),
            packedPageResults = packed.pages,
            ambiguousAddresses = generation.AmbiguousAddresses.OrderBy(address => address, StringComparer.Ordinal).ToArray(),
            unmappedConfiguredAddresses = unmappedConfiguredAddresses
        };
        Directory.CreateDirectory(Path.GetDirectoryName(BuildReportPath));
        File.WriteAllText(BuildReportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
    }

    #endregion
}
