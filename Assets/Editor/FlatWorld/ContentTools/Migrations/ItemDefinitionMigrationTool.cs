using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 把同构物品 Prefab 批量导出为 JSON 定义。目标根目录和共享组均为显式白名单，
/// 可重复执行；旧 Prefab 只作为编辑器迁移源，运行时不会读取 sourcePrefab。
/// </summary>
public static class ItemDefinitionMigrationTool
{
    #region 路径与分类配置

    private const string CatalogRootPath = "Assets/StreamingAssets/GameConfig/Items";
    private const string ManifestPath = CatalogRootPath + "/item-manifest.json";
    private const string PackageRootPath = CatalogRootPath + "/shells";
    private const string RequestPath = "Temp/FlatWorldItemDefinitionMigration.request";
    private const string ItemSpriteLabel = "ItemSprite";

    /// <summary>物品定义文件按玩法类别命名，避免文件名绑定某个具体物品或运行时外壳。</summary>
    private static readonly ItemPackageCategory[] PackageCategories =
    {
        new("basic_items", shellPrefab => string.Equals(shellPrefab, "Prop", StringComparison.OrdinalIgnoreCase)),
        new("tools", shellPrefab =>
            string.Equals(shellPrefab, "Stick", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shellPrefab, "Axe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shellPrefab, "Pickaxe", StringComparison.OrdinalIgnoreCase)),
        new("weapons", shellPrefab =>
            string.Equals(shellPrefab, "Dagger_Copper", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shellPrefab, "Spear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shellPrefab, "Torch", StringComparison.OrdinalIgnoreCase)),
        new("equipment", shellPrefab =>
            string.Equals(shellPrefab, "Chestplate_Iron", StringComparison.OrdinalIgnoreCase)),
        new("seeds", shellPrefab => string.Equals(shellPrefab, "Seed", StringComparison.OrdinalIgnoreCase)),
        new("building_summoners", shellPrefab =>
            string.Equals(shellPrefab, "BuildingSummonerShell", StringComparison.OrdinalIgnoreCase)),
        new("building_bodies", shellPrefab =>
            string.Equals(shellPrefab, "BuildingBodyShell", StringComparison.OrdinalIgnoreCase))
    };

    /// <summary>已经以 JSON 为权威、无需再从具体 Prefab 导出的定义。</summary>
    private static readonly HashSet<string> PreservedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Knife_Base", "Dagger_Stone", "Dagger_Copper", "Dagger_Bone", "Knife_Flint", "Torch",
        "WorldResource_Base", "MineResource_Base", "AppleTree", "Tree_Coconut", "Mine_Coal", "Mine_Copper",
        "Mine_Iron", "Mine_Stone", "Mine_Tin", "Iceberg", "Bush", "Weed"
    };

    /// <summary>预览统计时不计入运行时物品数量的抽象定义。</summary>
    private static readonly HashSet<string> PreservedAbstractIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Knife_Base", "WorldResource_Base", "MineResource_Base"
    };

    private static readonly HashSet<string> EquipmentInstanceTypeNames = new(StringComparer.Ordinal)
    {
        nameof(EquipmentInstance_Debug),
        nameof(EquipmentInstance_Bag),
        nameof(EquipmentInstance_Speed),
        nameof(EquipmentInstance_Defense)
    };

    private static readonly Dictionary<string, string> PreservedSourcePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dagger_Stone"] = "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Dagger.prefab",
        ["Dagger_Copper"] = "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Dagger_Copper.prefab",
        ["Dagger_Bone"] = "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Dagger_Bone.prefab",
        ["Knife_Flint"] = "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Knife_Flint.prefab",
        ["Torch"] = "Assets/2_Prefabs/Gameplay/Items/Tools/Torches/Torch.prefab"
    };

    private static readonly MigrationGroup[] Groups =
    {
        new(
            "BasicItem_Base",
            "Assets/2_Prefabs/Gameplay/Items/Common/Prop.prefab",
            new[]
            {
                "Assets/2_Prefabs/Gameplay/Items/Common/Prop.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/CharredMatter.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Earth.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Leaf.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Leather.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Plank.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/RawHide.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Rope.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Twine.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ingot/Ingot_Bronze.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ingot/Ingot_Copper.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ingot/Ingot_RawIron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ingot/Ingot_Steel.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ingot/Ingot_Tin.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ingot/Ingot_WroughtIron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ore/Ore_Coal.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ore/Ore_Copper.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ore/Ore_Flint.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ore/Ore_Iron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ore/Ore_MagicalStone.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Minerals/Ore/Ore_Tin.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Apple.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Berry.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_Addle.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_Green.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_Half.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_Nude.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_Shell.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_Water.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Coconut_WaterSalt.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/CoconutMeat.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Egg.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Egg_Cooked.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Fat.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Meat.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Meat_Cooked.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Meat_Dehydrate.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Meat_Rotten.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Food/Tea.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Materials/Humus.prefab"
            }),
        new(
            "Equipment_Base",
            "Assets/2_Prefabs/Gameplay/Items/Equipment/Chestplate_Iron.prefab",
            new[]
            {
                "Assets/2_Prefabs/Gameplay/Items/Equipment/Chestplate_Iron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Equipment/Chestplate_Twine.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Equipment/Chestplate_Wood.prefab"
            }),
        new(
            "WoodTool_Base",
            "Assets/2_Prefabs/Gameplay/Items/Common/Stick.prefab",
            new[]
            {
                "Assets/2_Prefabs/Gameplay/Items/Common/Stick.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Common/Log.prefab"
            }),
        new(
            "Axe_Base",
            "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe.prefab",
            new[]
            {
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe_Flint.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe_Copper.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe_Bronze.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe_RawIron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Axes/Axe_Iron.prefab"
            }),
        new(
            "Pickaxe_Base",
            "Assets/2_Prefabs/Gameplay/Items/Weapons/Pickaxes/Pickaxe.prefab",
            new[]
            {
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Pickaxes/Pickaxe.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Pickaxes/Pickaxe_Copper.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Pickaxes/Pickaxe_Bronze.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Pickaxes/Pickaxe_RawIron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Pickaxes/Pickaxe_Iron.prefab"
            }),
        new(
            "Spear_Base",
            "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Spear.prefab",
            new[]
            {
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Spear.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Spear_Copper.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Spear_Iron.prefab",
                "Assets/2_Prefabs/Gameplay/Items/Weapons/Melee/Spear_Stone_Animation.prefab"
            }),
        new(
            "Seed_Base",
            "Assets/2_Prefabs/Gameplay/Items/Seeds/Seed.prefab",
            new[] { "Assets/2_Prefabs/Gameplay/Items/Seeds/Seed.prefab" })
    };

    #endregion

    #region 编辑器入口

    private static bool requestHookInstalled;

    [InitializeOnLoadMethod]
    private static void InstallRequestHook()
    {
        if (requestHookInstalled)
            return;
        requestHookInstalled = true;
        EditorApplication.update += PollMigrationRequest;
    }

    [MenuItem("FlatWorld/物品JSON迁移/预览共享外壳迁移")]
    public static void Preview()
    {
        MigrationPreview preview = BuildPreview();
        Debug.Log($"[ItemDefinitionMigration] 预览：{preview.ItemCount} 个物品 -> " +
                  $"{preview.ShellCount} 个共享外壳；可移出运行时加载 {preview.RedundantPrefabCount} 个 Prefab。\n" +
                  string.Join("\n", preview.GroupLines));
    }

    [MenuItem("FlatWorld/物品JSON迁移/执行全部迁移 %#j")]
    public static void ExportAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 PlayMode 再执行物品 JSON 迁移。运行时状态不会被迁移器修改。");

        JObject existingRoot = File.Exists(ManifestPath)
            ? ItemDefinitionCatalogLoader.LoadBuiltInSourceCatalog()
            : new JObject();
        Dictionary<string, JObject> existingDefinitions = BuildExistingDefinitions(existingRoot);
        JArray output = new();
        PreserveManualDefinitions(existingRoot, output);

        var concreteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject preserved in output.OfType<JObject>())
        {
            if (preserved.Value<bool?>("abstract") != true)
                concreteIds.Add(preserved.Value<string>("id") ?? string.Empty);
        }

        foreach (MigrationGroup group in Groups)
            ExportGroup(group, output, concreteIds, existingDefinitions);

        JObject root = new()
        {
            ["schemaVersion"] = ItemDefinitionCatalogLoader.SupportedSchemaVersion,
            ["items"] = output
        };
        WriteCatalogPackages(root);
        RebuildRuntimePrefabAddressables();
        AssetDatabase.SaveAssets();

        ValidateCatalogInternal(ItemDefinitionCatalogLoader.LoadBuiltInDefinitions(), true);
        MigrationPreview preview = BuildPreview();
        Debug.Log($"[ItemDefinitionMigration] 已完成：{preview.ItemCount} 个物品共用 {preview.ShellCount} 个外壳，" +
                  $"{preview.RedundantPrefabCount} 个旧物品 Prefab 不再是运行时依赖。Manifest={ManifestPath}");
    }

    [MenuItem("FlatWorld/物品JSON迁移/校验当前目录")]
    public static void ValidateCatalog()
    {
        if (!File.Exists(ManifestPath))
            throw new FileNotFoundException("找不到物品 Manifest", ManifestPath);
        ValidateCatalogInternal(ItemDefinitionCatalogLoader.LoadBuiltInDefinitions(), true);
    }

    /// <summary>把当前物品目录引用的全部 Sprite 主资源同步到 Addressables 稳定地址。</summary>
    [MenuItem("FlatWorld/物品JSON迁移/同步物品 Sprite Addressables")]
    public static void SynchronizeSpriteAddressables()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 PlayMode 再同步物品 Sprite Addressables。");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");

        int createdCount = 0;
        int updatedCount = 0;
        var processedGuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
        {
            string spriteAddress = definition?.Visual?.SpriteAddress?.Trim();
            if (definition == null || definition.Abstract || string.IsNullOrWhiteSpace(spriteAddress))
                continue;

            if (!ItemDefinitionCatalogLoader.TryLoadEditorSprite(
                    spriteAddress,
                    out Sprite sprite,
                    out string error))
            {
                throw new InvalidDataException($"物品 {definition.Id} 的 Sprite 无法解析：{error}");
            }

            string assetPath = AssetDatabase.GetAssetPath(sprite).Replace('\\', '/');
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
                throw new InvalidDataException($"物品 {definition.Id} 的 Sprite 不是项目资源：{spriteAddress}");
            if (!processedGuids.Add(guid))
                continue;

            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            bool entryCreated = entry == null;
            if (entryCreated)
            {
                entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
                createdCount++;
            }

            bool entryChanged = !string.Equals(entry.address, assetPath, StringComparison.Ordinal) ||
                                !entry.labels.Contains(ItemSpriteLabel);
            entry.address = assetPath;
            entry.SetLabel(ItemSpriteLabel, true, true, false);
            if (entryChanged && !entryCreated)
                updatedCount++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true);
        AssetDatabase.SaveAssetIfDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log(
            $"[ItemDefinitionMigration] 已同步物品 Sprite Addressables：新增 {createdCount}，更新 {updatedCount}。");
    }

    #endregion

    #region 类别分包

    /// <summary>按物品玩法类别重建分包与唯一 Manifest 入口。</summary>
    private static void WriteCatalogPackages(JObject root)
    {
        if (root?["items"] is not JArray sourceItems)
            throw new InvalidDataException("待写入物品目录缺少 items 数组");

        List<ItemDefinitionDto> resolved = ItemDefinitionCatalogLoader.ResolveDefinitions(
            root.ToString(Formatting.None));
        Dictionary<string, ItemDefinitionDto> resolvedById = resolved.ToDictionary(
            definition => definition.Id,
            StringComparer.OrdinalIgnoreCase);
        var packageOrder = new List<string>();
        var packageItems = new Dictionary<string, JArray>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject source in sourceItems.OfType<JObject>())
        {
            string id = source.Value<string>("id")?.Trim();
            if (!resolvedById.TryGetValue(id ?? string.Empty, out ItemDefinitionDto definition) ||
                string.IsNullOrWhiteSpace(definition.ShellPrefab))
            {
                throw new InvalidDataException($"物品 {id} 无法按 shellPrefab 分类");
            }

            string packageName = ResolvePackageCategory(source, definition);
            if (!packageItems.TryGetValue(packageName, out JArray items))
            {
                items = new JArray();
                packageItems.Add(packageName, items);
                packageOrder.Add(packageName);
            }
            items.Add(source.DeepClone());
        }

        HashSet<string> previousGeneratedPaths = ReadPreviousGeneratedPackagePaths();
        var currentGeneratedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new ItemDefinitionManifestDto();
        Directory.CreateDirectory(PackageRootPath);
        foreach (string packageName in packageOrder)
        {
            string relativePath = "shells/" + GetSafePackageFileName(packageName) + ".json";
            string assetPath = CatalogRootPath + "/" + relativePath;
            if (!currentGeneratedPaths.Add(assetPath))
                throw new InvalidDataException($"物品类别文件名冲突：{packageName}");

            JObject packageRoot = new()
            {
                ["schemaVersion"] = ItemDefinitionCatalogLoader.SupportedSchemaVersion,
                ["items"] = packageItems[packageName]
            };
            File.WriteAllText(
                assetPath,
                packageRoot.ToString(Formatting.Indented),
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            manifest.Packages.Add(new ItemDefinitionPackageDto
            {
                Id = packageName,
                Path = relativePath,
                // 类别包可能包含多个外壳；每条定义自己的 shellPrefab 仍是运行时权威值。
                ShellPrefab = null,
                Enabled = true
            });
        }

        foreach (string stalePath in previousGeneratedPaths.Except(currentGeneratedPaths))
            AssetDatabase.DeleteAsset(stalePath);

        Directory.CreateDirectory(CatalogRootPath);
        File.WriteAllText(
            ManifestPath,
            JsonConvert.SerializeObject(manifest, Formatting.Indented),
            new UTF8Encoding(false));
        AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>只清理上一个 Manifest 明确登记在 shells/ 下的旧生成包。</summary>
    private static HashSet<string> ReadPreviousGeneratedPackagePaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(ManifestPath))
            return result;

        ItemDefinitionManifestDto manifest = ItemDefinitionCatalogLoader.DeserializeManifest(
            File.ReadAllText(ManifestPath, Encoding.UTF8));
        ItemDefinitionCatalogLoader.ValidateManifest(manifest);
        string fullPackageRoot = Path.GetFullPath(PackageRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (ItemDefinitionPackageDto package in manifest.Packages)
        {
            string relativePath = package.Path.Trim().Replace('\\', '/');
            string fullPath = ItemDefinitionCatalogLoader.ResolvePackagePath(
                Path.GetFullPath(CatalogRootPath),
                relativePath);
            if (!fullPath.StartsWith(fullPackageRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(CatalogRootPath + "/" + relativePath);
        }
        return result;
    }

    private static string GetSafePackageFileName(string packageName)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\' };
        var builder = new StringBuilder(packageName.Length);
        foreach (char character in packageName)
            builder.Append(invalid.Contains(character) ? '_' : character);
        string result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result)
            ? throw new InvalidDataException($"物品类别无法生成文件名：{packageName}")
            : result;
    }

    /// <summary>世界资源与普通道具共用 Prop 外壳，但必须保持独立分包。</summary>
    private static string ResolvePackageCategory(JObject source, ItemDefinitionDto definition)
    {
        string sourceId = source?.Value<string>("id")?.Trim();
        string parentId = source?.Value<string>("parent")?.Trim();
        if (string.Equals(sourceId, "WorldResource_Base", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceId, "MineResource_Base", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentId, "WorldResource_Base", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentId, "MineResource_Base", StringComparison.OrdinalIgnoreCase))
        {
            return "resource_nodes";
        }

        string shellPrefab = definition?.ShellPrefab?.Trim();
        foreach (ItemPackageCategory category in PackageCategories)
        {
            if (category.Matches(shellPrefab))
                return category.Name;
        }

        throw new InvalidDataException($"shellPrefab 没有登记物品类别：{shellPrefab}");
    }

    #endregion

    private static void PollMigrationRequest()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        File.Delete(RequestPath);
        try
        {
            ExportAll();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void PreserveManualDefinitions(JObject existingRoot, JArray output)
    {
        if (existingRoot["items"] is not JArray items)
            return;

        foreach (JObject source in items.OfType<JObject>())
        {
            string id = source.Value<string>("id");
            bool buildingShellDefinition = IsBuildingShellDefinition(source);
            if (!PreservedIds.Contains(id ?? string.Empty) && !buildingShellDefinition)
                continue;

            JObject copy = (JObject)source.DeepClone();
            if (PreservedSourcePaths.TryGetValue(id, out string sourcePath))
                copy["sourcePrefab"] = sourcePath;
            else if (!buildingShellDefinition)
                copy.Remove("sourcePrefab");
            output.Add(copy);
        }
    }

    /// <summary>识别由专用建筑迁移器维护的共享召唤器与本体定义。</summary>
    private static bool IsBuildingShellDefinition(JObject source)
    {
        string id = source?.Value<string>("id")?.Trim();
        string parent = source?.Value<string>("parent")?.Trim();
        string shell = source?.Value<string>("shellPrefab")?.Trim();
        return string.Equals(id, "BuildingSummoner_Base", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id, "BuildingBody_Base", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(parent, "BuildingSummoner_Base", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(parent, "BuildingBody_Base", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(shell, "BuildingSummonerShell", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(shell, "BuildingBodyShell", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JObject> BuildExistingDefinitions(JObject existingRoot)
    {
        var result = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        if (existingRoot?["items"] is not JArray items)
            return result;

        foreach (JObject item in items.OfType<JObject>())
        {
            string id = item.Value<string>("id")?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
                result[id] = item;
        }
        return result;
    }

    private static void ExportGroup(
        MigrationGroup group,
        JArray output,
        HashSet<string> concreteIds,
        IReadOnlyDictionary<string, JObject> existingDefinitions)
    {
        GameObject shell = LoadPrefab(group.ShellPath);
        Item shellItem = RequireItem(shell, group.ShellPath);
        SpriteRenderer shellRenderer = RequireRenderer(shell, group.ShellPath);
        Collider2D shellCollider = FindItemCollider(shellItem);

        output.Add(new JObject
        {
            ["id"] = group.BaseId,
            ["abstract"] = true,
            ["shellPrefab"] = shell.name,
            ["visual"] = new JObject
            {
                ["rendererPath"] = GetRelativePath(shell.transform, shellRenderer.transform)
            }
        });

        foreach (string sourcePath in group.SourcePaths)
        {
            GameObject source = LoadPrefab(sourcePath);
            Item sourceItem = RequireItem(source, sourcePath);
            if (sourceItem.itemData.GetType() != shellItem.itemData.GetType())
            {
                throw new InvalidDataException(
                    $"{sourcePath} 的 ItemData={sourceItem.itemData.GetType().Name}，与外壳 {group.ShellPath} 不一致");
            }

            string id = sourceItem.itemData.IDName?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !concreteIds.Add(id))
                throw new InvalidDataException($"迁移物品 ID 为空或重复：{id}（{sourcePath}）");

            existingDefinitions.TryGetValue(id, out JObject existingDefinition);
            JObject definition = BuildDefinition(
                group,
                sourcePath,
                sourceItem,
                shell,
                shellRenderer,
                shellCollider,
                existingDefinition);
            output.Add(definition);
        }
    }

    private static JObject BuildDefinition(
        MigrationGroup group,
        string sourcePath,
        Item sourceItem,
        GameObject shell,
        SpriteRenderer shellRenderer,
        Collider2D shellCollider,
        JObject existingDefinition)
    {
        ItemData data = sourceItem.itemData;
        SpriteRenderer sourceRenderer = RequireRenderer(sourceItem.gameObject, sourcePath);
        string spriteAddress = EnsureSpriteAddressable(sourceRenderer.sprite, sourcePath);
        JObject visual = BuildVisual(sourceItem, sourceRenderer, shell, shellRenderer, shellCollider, spriteAddress);
        JObject itemData = SerializeFields(data);
        RemoveProperties(
            itemData,
            "IDName", "GameName", "Description", "Durability", "MaxDurability", "Tags", "Stack", "Guid", "ModuleDataDic");

        JObject modules = new();
        var moduleNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Module module in sourceItem.GetComponentsInChildren<Module>(true))
        {
            if (module?._Data == null)
                continue;

            string moduleId = ResolveModuleId(module);
            string modulePrefab = ResolveModulePrefab(module, moduleId);
            ValidateModuleAvailable(shell, module, moduleId, modulePrefab, sourcePath);
            string stableName = ResolveStableModuleName(module, moduleId, moduleNameCounts);
            JObject moduleData = SerializeFields(module._Data);
            RemoveProperties(
                moduleData,
                "Name", "ID", "isRunning", "RuntimeOwnerItemData", "RuntimeOwnerInventoryData",
                "RuntimeOwnerSlot", "RuntimeOwnerSlotIndex");

            JObject moduleDefinition = new()
            {
                ["prefab"] = modulePrefab,
                ["id"] = moduleId,
                ["enabled"] = module._Data.isRunning
            };
            if (moduleData.HasValues)
                moduleDefinition["data"] = moduleData;

            JObject parameters = group.PreserveShellModuleFields
                ? new JObject()
                : SerializeModuleParameters(module);
            if (parameters.HasValues)
                moduleDefinition["parameters"] = parameters;
            modules[stableName] = moduleDefinition;
        }

        JObject definition = new()
        {
            ["id"] = data.IDName,
            ["parent"] = group.BaseId,
            ["sourcePrefab"] = sourcePath,
            ["gameName"] = data.GameName,
            ["description"] = ResolveSafeDescription(data, existingDefinition),
            ["durability"] = data.Durability,
            ["maxDurability"] = data.MaxDurability,
            ["amount"] = data.Stack?.Amount ?? 1f,
            ["volume"] = data.Stack?.Volume ?? 0f,
            ["canBePickedUp"] = data.Stack?.CanBePickedUp ?? true,
            ["tags"] = new JArray((data.Tags ?? new List<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())),
            ["visual"] = visual,
            ["modules"] = modules
        };
        if (itemData.HasValues)
            definition["itemData"] = itemData;
        return definition;
    }

    /// <summary>迁移源仍含旧调试串时保留已人工清洗的 JSON 文案，避免重复迁移把污染写回来。</summary>
    private static string ResolveSafeDescription(ItemData data, JObject existingDefinition)
    {
        string sourceDescription = data?.Description;
        if (!IsDebugDescription(sourceDescription))
            return sourceDescription?.Trim() ?? string.Empty;

        string existingDescription = existingDefinition?.Value<string>("description");
        if (!IsDebugDescription(existingDescription) && !string.IsNullOrWhiteSpace(existingDescription))
            return existingDescription.Trim();

        string displayName = string.IsNullOrWhiteSpace(data?.GameName) ? data?.IDName : data.GameName;
        return string.IsNullOrWhiteSpace(displayName)
            ? "可用于探索、生存或制作的物品。"
            : $"{displayName.Trim()}，可用于探索、生存或制作。";
    }

    private static bool IsDebugDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("物品名称：", StringComparison.Ordinal) ||
               value.Contains("物品堆叠信息：", StringComparison.Ordinal) ||
               value.Contains("全局唯一标识：", StringComparison.Ordinal) ||
               value.Contains("TagDictionary:", StringComparison.Ordinal);
    }

    private static JObject BuildVisual(
        Item sourceItem,
        SpriteRenderer sourceRenderer,
        GameObject shell,
        SpriteRenderer shellRenderer,
        Collider2D shellCollider,
        string spriteAddress)
    {
        JObject visual = new()
        {
            ["spriteAddress"] = spriteAddress,
            ["rendererLocalPosition"] = Vector3Token(sourceRenderer.transform.localPosition),
            ["rendererLocalEulerAngles"] = Vector3Token(sourceRenderer.transform.localEulerAngles),
            ["rendererLocalScale"] = Vector3Token(sourceRenderer.transform.localScale),
            ["color"] = ColorToken(sourceRenderer.color),
            ["flipX"] = sourceRenderer.flipX,
            ["flipY"] = sourceRenderer.flipY,
            ["sortingLayerName"] = sourceRenderer.sortingLayerName,
            ["sortingOrder"] = sourceRenderer.sortingOrder
        };

        Collider2D sourceCollider = FindItemCollider(sourceItem);
        if (sourceCollider != null || shellCollider != null)
        {
            if (sourceCollider == null || shellCollider == null || sourceCollider.GetType() != shellCollider.GetType())
            {
                throw new InvalidDataException(
                    $"物品 {sourceItem.itemData.IDName} 与外壳 {shell.name} 的主碰撞体类型不一致");
            }
            visual["collider"] = SerializeCollider(
                sourceCollider,
                GetRelativePath(shell.transform, shellCollider.transform));
        }

        return visual;
    }

    internal static JObject SerializeModuleParameters(Module module)
    {
        JObject parameters = new();
        for (Type type = module.GetType(); type != null && type != typeof(Module); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsUnitySerializedField(field) || IsIgnoredField(field) ||
                    field.GetCustomAttribute<SerializeReference>() != null ||
                    typeof(ModuleData).IsAssignableFrom(field.FieldType) ||
                    typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) ||
                    IsUnityObjectReferenceCollection(field.FieldType) ||
                    typeof(Delegate).IsAssignableFrom(field.FieldType) ||
                    typeof(UnityEventBase).IsAssignableFrom(field.FieldType) ||
                    field.FieldType.FullName?.Contains("UltEvent", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                if (TrySerializeValue(field.GetValue(module), field.FieldType, new HashSet<object>(ReferenceComparer.Instance), 0,
                        out JToken token))
                {
                    parameters[field.Name] = token;
                }
            }
        }

        parameters["$transform"] = new JObject
        {
            ["localPosition"] = Vector3Token(module.transform.localPosition),
            ["localEulerAngles"] = Vector3Token(module.transform.localEulerAngles),
            ["localScale"] = Vector3Token(module.transform.localScale)
        };
        Collider2D collider = module.GetComponent<Item>() == null
            ? module.GetComponent<Collider2D>()
            : null;
        if (collider != null)
            parameters["$collider2D"] = SerializeCollider(collider, null);
        return parameters;
    }

    internal static JObject SerializeFields(object value)
    {
        if (value == null)
            return new JObject();

        JObject result = new();
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        for (Type type = value.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsUnitySerializedField(field) || IsIgnoredField(field))
                    continue;
                if (TrySerializeValue(field.GetValue(value), field.FieldType, visited, 0, out JToken token))
                    result[field.Name] = token;
            }
        }
        return result;
    }

    private static bool TrySerializeValue(
        object value,
        Type declaredType,
        HashSet<object> visited,
        int depth,
        out JToken token)
    {
        token = null;
        if (depth > 10 || typeof(UnityEngine.Object).IsAssignableFrom(declaredType) ||
            typeof(Delegate).IsAssignableFrom(declaredType) || typeof(UnityEventBase).IsAssignableFrom(declaredType) ||
            declaredType.FullName?.Contains("UltEvent", StringComparison.Ordinal) == true)
        {
            return false;
        }

        if (value == null)
        {
            token = JValue.CreateNull();
            return true;
        }

        Type type = value.GetType();
        if (type.IsEnum)
        {
            token = new JValue(Convert.ToInt32(value));
            return true;
        }
        if (type == typeof(string) || type == typeof(char) || type == typeof(bool) || type.IsPrimitive ||
            type == typeof(decimal))
        {
            token = JToken.FromObject(value);
            return true;
        }
        if (value is Vector2 vector2) { token = Vector2Token(vector2); return true; }
        if (value is Vector3 vector3) { token = Vector3Token(vector3); return true; }
        if (value is Vector2Int vector2Int)
        {
            token = new JObject { ["x"] = vector2Int.x, ["y"] = vector2Int.y };
            return true;
        }
        if (value is Vector3Int vector3Int)
        {
            token = new JObject { ["x"] = vector3Int.x, ["y"] = vector3Int.y, ["z"] = vector3Int.z };
            return true;
        }
        if (value is Vector4 vector4)
        {
            token = new JObject { ["x"] = vector4.x, ["y"] = vector4.y, ["z"] = vector4.z, ["w"] = vector4.w };
            return true;
        }
        if (value is Quaternion quaternion)
        {
            token = new JObject { ["x"] = quaternion.x, ["y"] = quaternion.y, ["z"] = quaternion.z, ["w"] = quaternion.w };
            return true;
        }
        if (value is Color color) { token = ColorToken(color); return true; }
        if (value is LayerMask layerMask) { token = new JValue(layerMask.value); return true; }

        bool trackReference = !type.IsValueType;
        if (trackReference && !visited.Add(value))
            return false;
        try
        {
            if (value is IDictionary dictionary)
            {
                JObject obj = new();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key ||
                        !TrySerializeValue(entry.Value, entry.Value?.GetType() ?? typeof(object), visited, depth + 1,
                            out JToken child))
                        continue;
                    obj[key] = child;
                }
                token = obj;
                return true;
            }
            if (value is IEnumerable enumerable)
            {
                JArray array = new();
                Type elementType = GetEnumerableElementType(declaredType);
                foreach (object element in enumerable)
                {
                    if (TrySerializeValue(element, elementType ?? element?.GetType() ?? typeof(object), visited, depth + 1,
                        out JToken child))
                        array.Add(child);
                }
                token = array;
                return true;
            }

            JObject result = new();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!IsUnitySerializedField(field) || IsIgnoredField(field))
                        continue;
                    if (TrySerializeValue(field.GetValue(value), field.FieldType, visited, depth + 1, out JToken child))
                        result[field.Name] = child;
                }
            }

            // SerializeReference 的装备实例需要显式类型标签，运行时只接受白名单类型。
            if (declaredType == typeof(EquipmentInstance) &&
                typeof(EquipmentInstance).IsAssignableFrom(type))
            {
                if (!EquipmentInstanceTypeNames.Contains(type.Name))
                    throw new InvalidDataException($"装备实例类型未登记，不能迁移：{type.FullName}");
                result["$concreteType"] = type.Name;
            }
            token = result;
            return true;
        }
        finally
        {
            if (trackReference)
                visited.Remove(value);
        }
    }

    private static Type GetEnumerableElementType(Type declaredType)
    {
        if (declaredType == null || declaredType == typeof(string))
            return null;
        if (declaredType.IsArray)
            return declaredType.GetElementType();
        if (declaredType.IsGenericType)
        {
            Type[] arguments = declaredType.GetGenericArguments();
            if (arguments.Length == 1)
                return arguments[0];
        }
        return null;
    }

    /// <summary>Unity 资源引用集合由 Prefab 保留，JSON 迁移不应把它们导出为空数组。</summary>
    private static bool IsUnityObjectReferenceCollection(Type declaredType)
    {
        Type elementType = GetEnumerableElementType(declaredType);
        return elementType != null && typeof(UnityEngine.Object).IsAssignableFrom(elementType);
    }

    private static bool IsUnitySerializedField(FieldInfo field)
    {
        return !field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized &&
               (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null ||
                field.GetCustomAttribute<SerializeReference>() != null);
    }

    private static bool IsIgnoredField(FieldInfo field)
    {
        if (field.Name.StartsWith("Runtime", StringComparison.Ordinal) ||
            field.GetCustomAttribute<JsonIgnoreAttribute>() != null)
            return true;

        return field.GetCustomAttributes(true).Any(attribute =>
            attribute.GetType().Name is "MemoryPackIgnoreAttribute" or "FastClonerIgnoreAttribute");
    }

    internal static JObject SerializeCollider(Collider2D collider, string path)
    {
        JObject data = new()
        {
            ["type"] = collider.GetType().Name,
            ["enabled"] = collider.enabled,
            ["isTrigger"] = collider.isTrigger,
            ["offset"] = Vector2Token(collider.offset)
        };
        if (path != null)
            data["path"] = path;

        switch (collider)
        {
            case BoxCollider2D box:
                data["size"] = Vector2Token(box.size);
                data["edgeRadius"] = box.edgeRadius;
                break;
            case CircleCollider2D circle:
                data["radius"] = circle.radius;
                break;
            case CapsuleCollider2D capsule:
                data["size"] = Vector2Token(capsule.size);
                data["direction"] = (int)capsule.direction;
                break;
            case PolygonCollider2D polygon:
                data["points"] = JArray.FromObject(polygon.GetPath(0));
                break;
        }
        return data;
    }

    internal static string ResolveModuleId(Module module)
    {
        string canonicalId = module?.CanonicalModuleId;
        if (!string.IsNullOrWhiteSpace(canonicalId))
            return canonicalId.Trim();

        return module.GetType().Name;
    }

    internal static string ResolveModulePrefab(Module module, string fallbackId)
    {
        UnityEngine.Object original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(module.gameObject);
        string assetPath = original != null ? AssetDatabase.GetAssetPath(original) : null;
        if (!string.IsNullOrWhiteSpace(assetPath) &&
            assetPath.StartsWith("Assets/2_Prefabs/Gameplay/Modules/", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(assetPath);

        return module.GetType().Name;
    }

    private static string ResolveStableModuleName(
        Module module,
        string moduleId,
        Dictionary<string, int> nameCounts)
    {
        string baseName = module switch
        {
            Mod_Weapon_AnimationAction => "animation",
            Mod_Damage => "damage",
            Mod_Food => "food",
            _ => moduleId
        };
        nameCounts.TryGetValue(baseName, out int count);
        nameCounts[baseName] = ++count;
        return count == 1 ? baseName : $"{baseName}_{count}";
    }

    private static void ValidateModuleAvailable(
        GameObject shell,
        Module source,
        string moduleId,
        string modulePrefab,
        string sourcePath)
    {
        bool inShell = shell.GetComponentsInChildren<Module>(true).Any(candidate =>
            candidate.GetType() == source.GetType() ||
            string.Equals(candidate._Data?.ID, moduleId, StringComparison.OrdinalIgnoreCase));
        if (inShell)
            return;

        bool prefabExists = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2_Prefabs/Gameplay/Modules" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Any(path => string.Equals(
                Path.GetFileNameWithoutExtension(path), modulePrefab, StringComparison.OrdinalIgnoreCase));
        if (!prefabExists)
            throw new InvalidDataException(
                $"{sourcePath} 的模块 {moduleId} 既不在共享外壳中，也没有独立 Module Prefab：{modulePrefab}");
    }

    private static string EnsureSpriteAddressable(Sprite sprite, string sourcePath)
    {
        if (sprite == null)
            throw new InvalidDataException($"{sourcePath} 缺少 Sprite");
        string assetPath = AssetDatabase.GetAssetPath(sprite);
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(guid))
            throw new InvalidDataException($"{sourcePath} 的 Sprite 不是项目资源：{sprite.name}");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        // 方括号会被 Addressables 同时用于解析真实内部路径和子资源名，资源目录必须先改成安全名称。
        string address = GetSpriteAssetAddress(assetPath);
        entry.address = address;
        entry.SetLabel(ItemSpriteLabel, true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        return ReferenceEquals(mainAsset, sprite) ? address : $"{address}[{sprite.name}]";
    }

    private static string GetSpriteAssetAddress(string assetPath)
    {
        if (assetPath.IndexOf('[') >= 0 || assetPath.IndexOf(']') >= 0)
            throw new InvalidDataException(
                $"Sprite 路径包含 Addressables 保留方括号：{assetPath}；请先重命名资源目录或文件后再迁移");
        return assetPath;
    }

    private static void RebuildRuntimePrefabAddressables()
    {
        const string prefabRoot = "Assets/2_Prefabs";
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");

        HashSet<string> redundant = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths();
        string folderGuid = AssetDatabase.AssetPathToGUID(prefabRoot);
        if (!string.IsNullOrWhiteSpace(folderGuid))
            settings.RemoveAssetEntry(folderGuid, false);

        int included = 0;
        int excluded = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { prefabRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (redundant.Contains(path))
            {
                settings.RemoveAssetEntry(guid, false);
                excluded++;
                continue;
            }

            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            entry ??= settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
            entry.address = path;
            entry.SetLabel("Prefab", true, true, false);
            included++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true);
        Debug.Log($"[ItemDefinitionMigration] Addressables Prefab 白名单：保留 {included}，排除迁移源 {excluded}");
    }

    private static void ValidateCatalogInternal(
        IReadOnlyCollection<ItemDefinitionDto> resolved,
        bool logSuccess)
    {
        var ids = new HashSet<string>(resolved.Where(definition => !definition.Abstract).Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase);
        foreach (MigrationGroup group in Groups)
        {
            foreach (string sourcePath in group.SourcePaths)
            {
                Item item = RequireItem(LoadPrefab(sourcePath), sourcePath);
                if (!ids.Contains(item.itemData.IDName))
                    throw new InvalidDataException($"JSON 缺少迁移物品：{item.itemData.IDName}");
            }
        }

        if (logSuccess)
            Debug.Log($"[ItemDefinitionMigration] 校验通过：{ids.Count} 个运行时物品定义。 ");
    }

    private static MigrationPreview BuildPreview()
    {
        var lines = new List<string>();
        int itemCount = PreservedIds.Count - PreservedAbstractIds.Count;
        int redundant = Math.Max(0, PreservedSourcePaths.Count - 1);
        var shells = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dagger_Stone", "Prop", "MineResource"
        };
        foreach (MigrationGroup group in Groups)
        {
            itemCount += group.SourcePaths.Length;
            redundant += Math.Max(0, group.SourcePaths.Length - 1);
            shells.Add(Path.GetFileNameWithoutExtension(group.ShellPath));
            lines.Add($"{group.BaseId}: {group.SourcePaths.Length} -> {Path.GetFileNameWithoutExtension(group.ShellPath)}");
        }
        return new MigrationPreview(itemCount, shells.Count, redundant, lines);
    }

    private static GameObject LoadPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return prefab != null ? prefab : throw new FileNotFoundException("找不到迁移 Prefab", path);
    }

    private static Item RequireItem(GameObject prefab, string path)
    {
        Item item = prefab.GetComponent<Item>();
        if (item?.itemData == null)
            throw new InvalidDataException($"迁移 Prefab 缺少 Item/itemData：{path}");
        return item;
    }

    private static SpriteRenderer RequireRenderer(GameObject prefab, string path)
    {
        SpriteRenderer renderer = prefab.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate.sprite != null) ?? prefab.GetComponentInChildren<SpriteRenderer>(true);
        return renderer != null ? renderer : throw new InvalidDataException($"迁移 Prefab 缺少 SpriteRenderer：{path}");
    }

    private static Collider2D FindItemCollider(Item item)
    {
        return item.GetComponentsInChildren<Collider2D>(true)
            .FirstOrDefault(collider => collider.transform == item.transform ||
                                        collider.GetComponentInParent<Module>(true) == null);
    }

    internal static string GetRelativePath(Transform root, Transform child)
    {
        if (root == child)
            return string.Empty;
        var parts = new Stack<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }
        if (current != root)
            throw new InvalidOperationException($"{child.name} 不属于 {root.name}");
        return string.Join("/", parts);
    }

    internal static void RemoveProperties(JObject target, params string[] names)
    {
        foreach (string name in names)
            target.Property(name, StringComparison.OrdinalIgnoreCase)?.Remove();
    }

    internal static JObject Vector2Token(Vector2 value) => new() { ["x"] = value.x, ["y"] = value.y };
    internal static JObject Vector3Token(Vector3 value) => new() { ["x"] = value.x, ["y"] = value.y, ["z"] = value.z };
    internal static JObject ColorToken(Color value) =>
        new() { ["r"] = value.r, ["g"] = value.g, ["b"] = value.b, ["a"] = value.a };

    private sealed class MigrationGroup
    {
        public MigrationGroup(
            string baseId,
            string shellPath,
            string[] sourcePaths,
            bool preserveShellModuleFields = false)
        {
            BaseId = baseId;
            ShellPath = shellPath;
            SourcePaths = sourcePaths;
            PreserveShellModuleFields = preserveShellModuleFields;
        }

        public string BaseId { get; }
        public string ShellPath { get; }
        public string[] SourcePaths { get; }
        public bool PreserveShellModuleFields { get; }
    }

    /// <summary>声明一个稳定文件类别及其运行时外壳匹配规则。</summary>
    private sealed class ItemPackageCategory
    {
        public ItemPackageCategory(string name, Func<string, bool> matches)
        {
            Name = name;
            Matches = matches;
        }

        public string Name { get; }
        public Func<string, bool> Matches { get; }
    }

    private sealed class MigrationPreview
    {
        public MigrationPreview(int itemCount, int shellCount, int redundantPrefabCount, List<string> groupLines)
        {
            ItemCount = itemCount;
            ShellCount = shellCount;
            RedundantPrefabCount = redundantPrefabCount;
            GroupLines = groupLines;
        }

        public int ItemCount { get; }
        public int ShellCount { get; }
        public int RedundantPrefabCount { get; }
        public List<string> GroupLines { get; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
