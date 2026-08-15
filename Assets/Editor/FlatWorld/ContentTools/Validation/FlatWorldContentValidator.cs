using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

#region 校验结果模型

public enum FlatWorldContentValidationSeverity
{
    Warning,
    Error
}

public enum FlatWorldContentValidationMode
{
    Manual,
    Build
}

public sealed class FlatWorldContentValidationIssue
{
    public FlatWorldContentValidationIssue(
        FlatWorldContentValidationSeverity severity,
        string errorId,
        string assetPath,
        string fieldName,
        string message,
        Object context)
    {
        Severity = severity;
        ErrorId = errorId;
        AssetPath = string.IsNullOrWhiteSpace(assetPath) ? "<未知资源>" : assetPath;
        FieldName = string.IsNullOrWhiteSpace(fieldName) ? "<未知字段>" : fieldName;
        Message = message ?? string.Empty;
        Context = context;
    }

    public FlatWorldContentValidationSeverity Severity { get; }
    public string ErrorId { get; }
    public string AssetPath { get; }
    public string FieldName { get; }
    public string Message { get; }
    public Object Context { get; }

    public override string ToString()
    {
        return $"[{ErrorId}] {Severity} | 资源: {AssetPath} | 字段: {FieldName} | {Message}";
    }
}

public sealed class FlatWorldContentValidationReport
{
    private readonly List<FlatWorldContentValidationIssue> _issues = new();
    private readonly HashSet<string> _issueKeys = new(StringComparer.Ordinal);

    public IReadOnlyList<FlatWorldContentValidationIssue> Issues => _issues;
    public int ErrorCount => _issues.Count(issue => issue.Severity == FlatWorldContentValidationSeverity.Error);
    public int WarningCount => _issues.Count(issue => issue.Severity == FlatWorldContentValidationSeverity.Warning);
    public bool HasErrors => ErrorCount > 0;

    internal void Add(FlatWorldContentValidationIssue issue)
    {
        string key = $"{issue.Severity}|{issue.ErrorId}|{issue.AssetPath}|{issue.FieldName}|{issue.Message}";
        if (_issueKeys.Add(key))
            _issues.Add(issue);
    }
}

#endregion

/// <summary>
/// FlatWorld 本体内容的只读校验入口。只报告问题，不保存、不导入、不自动修复任何资产。
/// </summary>
public static class FlatWorldContentValidator
{
    #region 常量与内部模型

    public const string MenuPath = "FlatWorld/内容配置/校验全部本体内容";

    private const string PrefabRoot = "Assets/2_Prefabs";
    private const string ModulePrefabRoot = PrefabRoot + "/Module";
    private const string ScriptObjectRoot = "Assets/4_ScriptObjects";
    private const string ResourcesRoot = "Assets/Resources";
    private const string ItemRootAssetPath = "Assets/StreamingAssets/GameConfig/Items";
    private const string ItemManifestAssetPath = ItemRootAssetPath + "/item-manifest.json";
    private const string RecipeRootAssetPath = "Assets/StreamingAssets/GameConfig/Recipes";
    private const string RecipeManifestAssetPath = RecipeRootAssetPath + "/recipe-manifest.json";
    private const string WorldManagerPrefabPath = "Assets/2_Prefabs/Core/Managers/WorldManager.prefab";
    private const string BuildingShadowPrefabId = "BuildingShadow";

    private static readonly string[] SerializedValidationRoots =
    {
        PrefabRoot,
        ScriptObjectRoot,
        ResourcesRoot
    };

    private sealed class PrefabRecord
    {
        public string Path;
        public GameObject Prefab;
        public Item Item;
        public string ItemId;
        public Module[] Modules;
    }

    private sealed class ValidationContext
    {
        public readonly List<PrefabRecord> Prefabs = new();
        public readonly List<PrefabRecord> ItemPrefabs = new();
        public readonly Dictionary<string, PrefabRecord> PrefabAliases = new(StringComparer.Ordinal);
        public readonly Dictionary<string, PrefabRecord> ItemIds = new(StringComparer.Ordinal);
        public readonly HashSet<string> ItemDefinitionIds = new(StringComparer.Ordinal);
        public readonly HashSet<string> BiomeNames = new(StringComparer.Ordinal);
    }

    private sealed class ResourceRequirement
    {
        public ResourceRequirement(string resourcePath, string assetPath, Type expectedType)
        {
            ResourcePath = resourcePath;
            AssetPath = assetPath;
            ExpectedType = expectedType;
        }

        public string ResourcePath { get; }
        public string AssetPath { get; }
        public Type ExpectedType { get; }
    }

    #endregion

    #region 公共入口

    [MenuItem(MenuPath, priority = 2000)]
    public static void ValidateAllMenu()
    {
        ValidateAll(FlatWorldContentValidationMode.Manual, true);
    }

    public static FlatWorldContentValidationReport ValidateAll(
        FlatWorldContentValidationMode mode = FlatWorldContentValidationMode.Manual,
        bool logResults = true)
    {
        FlatWorldContentValidationReport report = new();
        ValidationContext context = new();

        RunRule(report, "FWC-SYS-001", PrefabRoot, "PrefabCatalog", () => BuildPrefabCatalog(context, report));
        RunRule(report, "FWC-SYS-011", ItemManifestAssetPath, "ItemDefinitionCatalog", () => ValidateItemDefinitions(context, report));
        RunRule(report, "FWC-SYS-002", PrefabRoot, "ItemAndModule", () => ValidateItemsAndModules(context, report));
        RunRule(report, "FWC-SYS-003", RecipeManifestAssetPath, "RecipeCatalog", () => ValidateRecipes(context, report));
        RunRule(report, "FWC-SYS-004", PrefabRoot + "/Building", "BuildingPair", () => ValidateBuildings(context, report));
        RunRule(report, "FWC-SYS-005", ScriptObjectRoot + "/4-8_Biome", "BiomeData", () => ValidateBiomes(context, report));
        RunRule(report, "FWC-SYS-006", ResourcesRoot + "/Config", "SpawnerConfig", () => ValidateSpawners(context, report));
        RunRule(report, "FWC-SYS-007", PrefabRoot, "LootEntry", () => ValidateLootTables(context, report));
        RunRule(report, "FWC-SYS-008", ResourcesRoot, "ResourcesPath", () => ValidateFixedResources(report));
        RunRule(report, "FWC-SYS-009", PrefabRoot, "RequiredPrefab", () => ValidateRequiredPrefabs(context, report));
        RunRule(report, "FWC-SYS-010", PrefabRoot, "SerializedReference", () => ValidateSerializedReferences(report));

        if (logResults)
            LogReport(report, mode);

        return report;
    }

    #endregion

    #region Prefab、物品与模块

    private static void BuildPrefabCatalog(ValidationContext context, FlatWorldContentValidationReport report)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
        Array.Sort(prefabGuids, StringComparer.Ordinal);

        Dictionary<string, PrefabRecord> itemIdsIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                AddError(report, "FWC-PREFAB-001", path, "Prefab", "无法加载 Prefab 主资源。", null);
                continue;
            }

            Item[] rootItems = prefab.GetComponents<Item>();
            if (rootItems.Length > 1)
            {
                AddError(report, "FWC-ITEM-001", path, "Item", "Prefab 根节点存在多个 Item 组件，运行时注册目标不明确。", prefab);
            }

            Item item = rootItems.FirstOrDefault();
            string itemId = item?.itemData?.IDName?.Trim();
            PrefabRecord record = new()
            {
                Path = path,
                Prefab = prefab,
                Item = item,
                ItemId = itemId,
                Modules = prefab.GetComponentsInChildren<Module>(true)
            };
            context.Prefabs.Add(record);
            RegisterPrefabAlias(context, report, prefab.name, record, "GameObject.name");

            if (item == null)
            {
                foreach (Module module in record.Modules)
                {
                    if (module == null)
                        continue;

                    RegisterPrefabAlias(
                        context,
                        report,
                        module.CanonicalModuleId,
                        record,
                        "Module.CanonicalModuleId");
                    RegisterPrefabAlias(
                        context,
                        report,
                        module._Data?.ID,
                        record,
                        "ModuleData.ID");
                }

                continue;
            }

            context.ItemPrefabs.Add(record);
            if (item.itemData == null)
            {
                AddError(report, "FWC-ITEM-002", path, "Item.itemData", "Item 缺少物品数据。", item);
                continue;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                AddError(report, "FWC-ITEM-003", path, "ItemData.IDName", "物品 ID 为空。", item);
                continue;
            }

            if (!string.Equals(item.itemData.IDName, itemId, StringComparison.Ordinal))
            {
                AddError(report, "FWC-ITEM-004", path, "ItemData.IDName", "物品 ID 首尾含空白字符。", item);
            }

            if (ContainsControlCharacter(itemId))
            {
                AddError(report, "FWC-ITEM-005", path, "ItemData.IDName", "物品 ID 含控制字符。", item);
            }

            if (context.ItemIds.TryGetValue(itemId, out PrefabRecord existingItem) && existingItem.Prefab != prefab)
            {
                AddError(
                    report,
                    "FWC-ITEM-006",
                    path,
                    "ItemData.IDName",
                    $"物品 ID '{itemId}' 与 {existingItem.Path} 重复。",
                    item);
            }
            else
            {
                context.ItemIds[itemId] = record;
            }

            if (itemIdsIgnoreCase.TryGetValue(itemId, out PrefabRecord caseConflict) &&
                caseConflict.Prefab != prefab &&
                !string.Equals(caseConflict.ItemId, itemId, StringComparison.Ordinal))
            {
                AddWarning(
                    report,
                    "FWC-ITEM-101",
                    path,
                    "ItemData.IDName",
                    $"物品 ID '{itemId}' 与 {caseConflict.Path} 仅大小写不同，跨平台引用容易混淆。",
                    item);
            }
            else
            {
                itemIdsIgnoreCase[itemId] = record;
            }

            RegisterPrefabAlias(context, report, itemId, record, "ItemData.IDName");
        }
    }

    private static void RegisterPrefabAlias(
        ValidationContext context,
        FlatWorldContentValidationReport report,
        string key,
        PrefabRecord record,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (context.PrefabAliases.TryGetValue(key, out PrefabRecord existing) && existing.Prefab != record.Prefab)
        {
            AddError(
                report,
                "FWC-PREFAB-002",
                record.Path,
                fieldName,
                $"运行时 Prefab 注册键 '{key}' 与 {existing.Path} 冲突，GameRes 会静默覆盖其中一个。",
                record.Prefab);
            return;
        }

        context.PrefabAliases[key] = record;
    }

    private static void ValidateItemsAndModules(ValidationContext context, FlatWorldContentValidationReport report)
    {
        foreach (PrefabRecord record in context.ItemPrefabs)
        {
            Item item = record.Item;
            if (item?.itemData == null)
                continue;

            // 已迁移物品的运行时文字来自 JSON；旧 Prefab 只承担外壳或迁移定位职责。
            if (!context.ItemDefinitionIds.Contains(record.ItemId))
                ValidateItemText(record, report);
            if (item.itemData.Stack == null)
            {
                AddError(report, "FWC-ITEM-007", record.Path, "ItemData.Stack", "物品缺少堆叠数据。", item);
            }

            Dictionary<string, Module> componentNames = new(StringComparer.Ordinal);
            foreach (Module module in record.Modules)
            {
                if (module == null)
                    continue;

                ModuleData data;
                try
                {
                    data = module._Data;
                }
                catch (Exception exception)
                {
                    AddError(
                        report,
                        "FWC-MODULE-001",
                        record.Path,
                        module.GetType().Name + "._Data",
                        $"读取模块数据失败：{exception.Message}",
                        module);
                    continue;
                }

                if (data == null)
                {
                    AddError(report, "FWC-MODULE-002", record.Path, module.GetType().Name + "._Data", "模块数据引用为空。", module);
                    continue;
                }

                string moduleId = data.ID?.Trim();
                if (string.IsNullOrWhiteSpace(moduleId))
                {
                    AddError(report, "FWC-MODULE-003", record.Path, module.GetType().Name + "._Data.ID", "模块 ID 为空。", module);
                }
                else
                {
                    ValidateModulePrefabReference(context, report, record.Path, module, moduleId);
                }

                string moduleName = data.Name?.Trim();
                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    if (componentNames.TryGetValue(moduleName, out Module existing) && existing != module)
                    {
                        AddError(
                            report,
                            "FWC-MODULE-004",
                            record.Path,
                            module.GetType().Name + "._Data.Name",
                            $"同一物品内模块实例名 '{moduleName}' 重复。",
                            module);
                    }
                    else
                    {
                        componentNames[moduleName] = module;
                    }
                }
            }

            ValidateModuleDataDictionary(context, report, record);
        }
    }

    private static void ValidateModulePrefabReference(
        ValidationContext context,
        FlatWorldContentValidationReport report,
        string ownerPath,
        Module ownerModule,
        string moduleId)
    {
        if (!context.PrefabAliases.TryGetValue(moduleId, out PrefabRecord modulePrefab))
        {
            AddError(
                report,
                "FWC-MODULE-005",
                ownerPath,
                ownerModule.GetType().Name + "._Data.ID",
                $"模块 ID '{moduleId}' 无法解析到 Addressables Prefab，运行时缺失模块修复会失败。",
                ownerModule);
            return;
        }

        bool matchingModule = false;
        foreach (Module candidate in modulePrefab.Modules)
        {
            if (candidate == null)
                continue;

            try
            {
                if (candidate._Data != null && string.Equals(candidate._Data.ID, moduleId, StringComparison.Ordinal))
                {
                    matchingModule = true;
                    break;
                }
            }
            catch
            {
                // 具体读取异常会在目标 Prefab 自身的模块校验中报告。
            }
        }

        if (!matchingModule)
        {
            AddError(
                report,
                "FWC-MODULE-006",
                ownerPath,
                ownerModule.GetType().Name + "._Data.ID",
                $"注册键 '{moduleId}' 指向 {modulePrefab.Path}，但目标 Prefab 不含相同 ID 的 Module。",
                ownerModule);
        }
    }

    private static void ValidateModuleDataDictionary(
        ValidationContext context,
        FlatWorldContentValidationReport report,
        PrefabRecord record)
    {
        Dictionary<string, ModuleData> dataDictionary = record.Item.itemData.ModuleDataDic;
        if (dataDictionary == null)
        {
            AddError(report, "FWC-MODULE-007", record.Path, "ItemData.ModuleDataDic", "模块数据字典为空引用。", record.Item);
            return;
        }

        foreach (KeyValuePair<string, ModuleData> pair in dataDictionary)
        {
            if (pair.Value == null)
            {
                AddError(report, "FWC-MODULE-008", record.Path, $"ItemData.ModuleDataDic[{pair.Key}]", "模块数据条目为空。", record.Item);
                continue;
            }

            if (!string.Equals(pair.Key, pair.Value.Name, StringComparison.Ordinal))
            {
                AddError(
                    report,
                    "FWC-MODULE-009",
                    record.Path,
                    $"ItemData.ModuleDataDic[{pair.Key}]",
                    $"字典键与 ModuleData.Name '{pair.Value.Name}' 不一致。",
                    record.Item);
            }

            string moduleId = pair.Value.ID?.Trim();
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                AddError(report, "FWC-MODULE-010", record.Path, $"ItemData.ModuleDataDic[{pair.Key}].ID", "模块数据 ID 为空。", record.Item);
                continue;
            }

            if (!context.PrefabAliases.ContainsKey(moduleId))
            {
                AddError(
                    report,
                    "FWC-MODULE-011",
                    record.Path,
                    $"ItemData.ModuleDataDic[{pair.Key}].ID",
                    $"模块数据 ID '{moduleId}' 无法解析到 Prefab。",
                    record.Item);
            }
        }
    }

    private static void ValidateItemText(PrefabRecord record, FlatWorldContentValidationReport report)
    {
        ItemData data = record.Item.itemData;
        string gameName = data.GameName;
        string description = data.Description;

        if (string.IsNullOrWhiteSpace(gameName))
        {
            AddError(report, "FWC-TEXT-001", record.Path, "ItemData.GameName", "物品显示名为空。", record.Item);
        }
        else
        {
            if (!string.Equals(gameName, gameName.Trim(), StringComparison.Ordinal))
                AddWarning(report, "FWC-TEXT-101", record.Path, "ItemData.GameName", "物品显示名首尾含空白字符。", record.Item);
            if (ContainsControlCharacter(gameName))
                AddError(report, "FWC-TEXT-002", record.Path, "ItemData.GameName", "物品显示名含换行、制表符或其他控制字符。", record.Item);
        }

        if (IsDebugDescription(description))
        {
            AddError(
                report,
                "FWC-TEXT-003",
                record.Path,
                "ItemData.Description",
                "描述被 ItemData.ToString() 调试文本污染，可能把 GUID、堆叠和标签等内部信息显示给玩家。",
                record.Item);
            return;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            AddWarning(report, "FWC-TEXT-102", record.Path, "ItemData.Description", "物品描述为空。", record.Item);
            return;
        }

        if (string.Equals(description.Trim(), "什么都没有描述", StringComparison.Ordinal))
            AddWarning(report, "FWC-TEXT-103", record.Path, "ItemData.Description", "物品描述仍是默认占位文本。", record.Item);
        if (!string.Equals(description, description.Trim(), StringComparison.Ordinal))
            AddWarning(report, "FWC-TEXT-104", record.Path, "ItemData.Description", "物品描述首尾含空白字符。", record.Item);
        if (description.Length > 1000)
            AddWarning(report, "FWC-TEXT-105", record.Path, "ItemData.Description", $"物品描述长度为 {description.Length}，可能异常撑高 UI。", record.Item);
        if (ContainsDisallowedRichText(description))
            AddWarning(report, "FWC-TEXT-106", record.Path, "ItemData.Description", "物品描述包含 TMP 富文本标签，请确认不是配置污染。", record.Item);
    }

    #endregion

    #region JSON 物品定义

    /// <summary>按运行时相同的 Manifest、分包和跨文件继承路径校验权威物品目录。</summary>
    private static void ValidateItemDefinitions(
        ValidationContext context,
        FlatWorldContentValidationReport report)
    {
        string manifestAbsolutePath = ToAbsolutePath(ItemManifestAssetPath);
        if (!File.Exists(manifestAbsolutePath))
        {
            AddError(report, "FWC-ITEMJSON-001", ItemManifestAssetPath, "Manifest", "物品清单不存在。", null);
            return;
        }

        ItemDefinitionManifestDto manifest;
        try
        {
            manifest = ItemDefinitionCatalogLoader.DeserializeManifest(File.ReadAllText(manifestAbsolutePath));
            ItemDefinitionCatalogLoader.ValidateManifest(manifest);
        }
        catch (Exception exception)
        {
            AddError(report, "FWC-ITEMJSON-002", ItemManifestAssetPath, "Manifest", exception.Message, null);
            return;
        }

        var packageJsons = new List<string>();
        var sourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string itemRootAbsolutePath = ToAbsolutePath(ItemRootAssetPath);
        foreach (ItemDefinitionPackageDto package in manifest.Packages ?? new List<ItemDefinitionPackageDto>())
        {
            if (package == null || !package.Enabled)
                continue;

            string packageAbsolutePath;
            try
            {
                packageAbsolutePath = ItemDefinitionCatalogLoader.ResolvePackagePath(
                    itemRootAbsolutePath,
                    package.Path);
            }
            catch (Exception exception)
            {
                AddError(
                    report,
                    "FWC-ITEMJSON-003",
                    ItemManifestAssetPath,
                    $"packages[{package?.Id}].path",
                    exception.Message,
                    null);
                continue;
            }

            string packageAssetPath = ToAssetPath(packageAbsolutePath);
            if (!File.Exists(packageAbsolutePath))
            {
                AddError(
                    report,
                    "FWC-ITEMJSON-004",
                    packageAssetPath,
                    "Package",
                    $"启用的物品分包 '{package.Id}' 不存在。",
                    null);
                continue;
            }

            string packageJson = File.ReadAllText(packageAbsolutePath);
            packageJsons.Add(packageJson);
            try
            {
                JObject root = JObject.Parse(packageJson);
                if (root["items"] is not JArray items)
                    throw new InvalidDataException("分包缺少 items 数组。");

                for (int index = 0; index < items.Count; index++)
                {
                    if (items[index] is not JObject item)
                        continue;
                    string id = item.Value<string>("id")?.Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                        sourcePaths.TryAdd(id, packageAssetPath);
                }
            }
            catch (Exception exception)
            {
                AddError(report, "FWC-ITEMJSON-005", packageAssetPath, "items", exception.Message, null);
            }
        }

        List<ItemDefinitionDto> definitions;
        try
        {
            definitions = ItemDefinitionCatalogLoader.ResolveDefinitions(packageJsons);
        }
        catch (Exception exception)
        {
            AddError(report, "FWC-ITEMJSON-006", ItemManifestAssetPath, "items", exception.Message, null);
            return;
        }

        var knownIdsIgnoreCase = new HashSet<string>(
            definitions.Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase);
        var knownDefinitionIds = new HashSet<string>(
            definitions.Select(definition => definition.Id),
            StringComparer.Ordinal);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                continue;

            string assetPath = sourcePaths.TryGetValue(definition.Id, out string sourcePath)
                ? sourcePath
                : ItemManifestAssetPath;
            if (!definition.Abstract)
                context.ItemDefinitionIds.Add(definition.Id);
            ValidateResolvedItemDefinition(
                context,
                report,
                definition,
                knownIdsIgnoreCase,
                knownDefinitionIds,
                assetPath);
        }
    }

    private static void ValidateResolvedItemDefinition(
        ValidationContext context,
        FlatWorldContentValidationReport report,
        ItemDefinitionDto definition,
        ISet<string> knownIds,
        ISet<string> knownDefinitionIds,
        string assetPath)
    {
        string field = $"items[{definition.Id}]";
        if (definition.Abstract)
            return;

        if (string.IsNullOrWhiteSpace(definition.ShellPrefab) ||
            !context.PrefabAliases.ContainsKey(definition.ShellPrefab.Trim()))
        {
            AddError(
                report,
                "FWC-ITEMJSON-007",
                assetPath,
                field + ".shellPrefab",
                $"运行时外壳 '{definition.ShellPrefab}' 无法解析到 Prefab。",
                null);
        }

        if (string.IsNullOrWhiteSpace(definition.SourcePrefab))
        {
            AddWarning(report, "FWC-ITEMJSON-101", assetPath, field + ".sourcePrefab", "缺少编辑器迁移源定位。", null);
        }
        else
        {
            string sourcePrefabPath = definition.SourcePrefab.Trim().Replace('\\', '/');
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            Item sourceItem = sourcePrefab != null ? sourcePrefab.GetComponent<Item>() : null;
            if (sourceItem?.itemData == null)
            {
                AddError(
                    report,
                    "FWC-ITEMJSON-008",
                    assetPath,
                    field + ".sourcePrefab",
                    $"迁移源 '{sourcePrefabPath}' 不存在或缺少 ItemData。",
                    null);
            }
            else if (!string.Equals(
                         sourceItem.itemData.IDName,
                         definition.Id,
                         StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    report,
                    "FWC-ITEMJSON-009",
                    assetPath,
                    field + ".sourcePrefab",
                    $"迁移源 ItemData.IDName '{sourceItem.itemData.IDName}' 与定义 ID '{definition.Id}' 不一致。",
                    sourcePrefab);
            }
        }

        if (string.IsNullOrWhiteSpace(definition.GameName))
        {
            AddError(report, "FWC-ITEMJSON-010", assetPath, field + ".gameName", "物品显示名回退值为空。", null);
        }
        else if (!string.Equals(definition.GameName, definition.Id, StringComparison.OrdinalIgnoreCase) &&
                 knownIds.Contains(definition.GameName.Trim()))
        {
            AddError(
                report,
                "FWC-ITEMJSON-011",
                assetPath,
                field + ".gameName",
                $"显示名错误引用了另一物品 ID '{definition.GameName}'。",
                null);
        }

        if (IsDebugDescription(definition.Description))
        {
            AddError(
                report,
                "FWC-ITEMJSON-012",
                assetPath,
                field + ".description",
                "描述被 ItemData.ToString() 调试文本污染。",
                null);
        }
        else if (string.IsNullOrWhiteSpace(definition.Description))
        {
            AddWarning(report, "FWC-ITEMJSON-102", assetPath, field + ".description", "物品描述为空。", null);
        }

        if (definition.Tags != null)
        {
            for (int index = 0; index < definition.Tags.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(definition.Tags[index]))
                {
                    AddError(
                        report,
                        "FWC-ITEMJSON-013",
                        assetPath,
                        $"{field}.tags[{index}]",
                        "标签不得包含空字符串。",
                        null);
                }
            }
        }

        foreach (KeyValuePair<string, ItemModuleDefinitionDto> pair in
                 definition.Modules ?? new Dictionary<string, ItemModuleDefinitionDto>())
        {
            string moduleField = $"{field}.modules[{pair.Key}]";
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
            {
                AddError(report, "FWC-ITEMJSON-014", assetPath, moduleField, "模块稳定名为空或定义为空。", null);
                continue;
            }

            string modulePrefabId = pair.Value.Prefab?.Trim();
            if (string.IsNullOrWhiteSpace(modulePrefabId) ||
                !context.PrefabAliases.ContainsKey(modulePrefabId) &&
                !knownDefinitionIds.Contains(modulePrefabId))
            {
                AddError(
                    report,
                    "FWC-ITEMJSON-015",
                    assetPath,
                    moduleField + ".prefab",
                    $"模块 Prefab ID '{pair.Value.Prefab}' 无法解析。",
                    null);
            }
        }
    }

    #endregion

    #region 配方

    private static void ValidateRecipes(ValidationContext context, FlatWorldContentValidationReport report)
    {
        string manifestAbsolutePath = ToAbsolutePath(RecipeManifestAssetPath);
        if (!File.Exists(manifestAbsolutePath))
        {
            AddError(report, "FWC-RECIPE-001", RecipeManifestAssetPath, "Manifest", "配方清单不存在。", null);
            return;
        }

        RecipeManifestDto manifest;
        try
        {
            manifest = RecipeCatalogLoader.DeserializeManifest(File.ReadAllText(manifestAbsolutePath));
            RecipeCatalogLoader.ValidateManifest(manifest);
        }
        catch (Exception exception)
        {
            AddError(report, "FWC-RECIPE-002", RecipeManifestAssetPath, "Manifest", exception.Message, null);
            return;
        }

        HashSet<string> recipeIds = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RecipeDto> recipesById = new(StringComparer.Ordinal);
        Dictionary<string, int> materialConsumers = new(StringComparer.Ordinal);
        string recipeRootAbsolutePath = ToAbsolutePath(RecipeRootAssetPath);
        foreach (RecipePackageDto package in manifest.Packages ?? new List<RecipePackageDto>())
        {
            if (package == null || !package.Enabled)
                continue;

            string packageAbsolutePath;
            try
            {
                packageAbsolutePath = RecipeCatalogLoader.ResolvePackagePath(recipeRootAbsolutePath, package.Path);
            }
            catch (Exception exception)
            {
                AddError(report, "FWC-RECIPE-003", RecipeManifestAssetPath, $"packages[{package?.Id}].path", exception.Message, null);
                continue;
            }

            string packageAssetPath = ToAssetPath(packageAbsolutePath);
            if (!File.Exists(packageAbsolutePath))
            {
                AddError(report, "FWC-RECIPE-004", packageAssetPath, "Package", $"启用的配方分包 '{package.Id}' 不存在。", null);
                continue;
            }

            RecipeCatalogDto catalog;
            try
            {
                catalog = RecipeRuntimeFactory.Deserialize(File.ReadAllText(packageAbsolutePath));
                RecipeRuntimeFactory.BuildCatalog(catalog, _ => true, out _);
            }
            catch (Exception exception)
            {
                AddError(report, "FWC-RECIPE-005", packageAssetPath, "recipes", exception.Message, AssetDatabase.LoadAssetAtPath<TextAsset>(packageAssetPath));
                continue;
            }

            foreach (RecipeDto recipe in catalog.Recipes ?? new List<RecipeDto>())
            {
                if (recipe == null)
                    continue;

                string recipeId = recipe.Id?.Trim() ?? "<空ID>";
                if (!recipeIds.Add(recipeId))
                {
                    AddError(report, "FWC-RECIPE-006", packageAssetPath, $"recipes[{recipeId}].id", $"跨分包重复配方 ID：{recipeId}", null);
                }
                else
                {
                    recipesById[recipeId] = recipe;
                }

                for (int inputIndex = 0; inputIndex < (recipe.Inputs?.Count ?? 0); inputIndex++)
                {
                    RecipeIngredientDto input = recipe.Inputs[inputIndex];
                    if (input == null || string.Equals(input.Match, "tag", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrWhiteSpace(input.ItemId))
                        continue;
                    string inputItemId = input.ItemId.Trim();
                    materialConsumers[inputItemId] = materialConsumers.TryGetValue(inputItemId, out int count)
                        ? count + 1
                        : 1;
                    if (!ItemExists(context, inputItemId))
                    {
                        AddError(
                            report,
                            "FWC-RECIPE-007",
                            packageAssetPath,
                            $"recipes[{recipeId}].inputs[{inputIndex}].itemId",
                            $"配方输入物品 ID '{input.ItemId}' 不存在。",
                            null);
                    }
                }

                for (int outputIndex = 0; outputIndex < (recipe.Outputs?.Count ?? 0); outputIndex++)
                {
                    RecipeOutputDto output = recipe.Outputs[outputIndex];
                    if (output == null || string.IsNullOrWhiteSpace(output.ItemId))
                        continue;
                    if (!ItemExists(context, output.ItemId.Trim()))
                    {
                        AddError(
                            report,
                            "FWC-RECIPE-008",
                            packageAssetPath,
                            $"recipes[{recipeId}].outputs[{outputIndex}].itemId",
                            $"配方输出物品 ID '{output.ItemId}' 不存在。",
                            null);
                    }
                }
            }
        }

        ValidateMetallurgyProgression(context, recipesById, materialConsumers, report);
    }

    /// <summary>锁定当前首条铁器纵向链，避免产物回退成石器或冶炼出没有用途的死材料。</summary>
    private static void ValidateMetallurgyProgression(
        ValidationContext context,
        IReadOnlyDictionary<string, RecipeDto> recipesById,
        IReadOnlyDictionary<string, int> materialConsumers,
        FlatWorldContentValidationReport report)
    {
        const string rawIronPickaxeRecipeId = "core:粗铁镐";
        if (!recipesById.TryGetValue(rawIronPickaxeRecipeId, out RecipeDto rawIronPickaxe) ||
            rawIronPickaxe.Outputs == null ||
            rawIronPickaxe.Outputs.Count != 1 ||
            !string.Equals(rawIronPickaxe.Outputs[0]?.ItemId, "Pickaxe_RawIron", StringComparison.Ordinal))
        {
            AddError(
                report,
                "FWC-PROGRESSION-001",
                RecipeManifestAssetPath,
                $"recipes[{rawIronPickaxeRecipeId}].outputs",
                "粗铁镐配方必须唯一产出 Pickaxe_RawIron。",
                null);
        }

        ValidateRecipeOutput(
            context,
            recipesById,
            "core:熟铁镐",
            "Pickaxe_Iron",
            report);
        ValidateRecipeOutput(
            context,
            recipesById,
            "core:钢制铁甲",
            "Chestplate_Iron",
            report);

        string[] requiredConsumerIds = { "Ingot_RawIron", "Ingot_WroughtIron", "Ingot_Steel" };
        for (int index = 0; index < requiredConsumerIds.Length; index++)
        {
            string itemId = requiredConsumerIds[index];
            if (!materialConsumers.ContainsKey(itemId))
            {
                AddError(
                    report,
                    "FWC-PROGRESSION-002",
                    RecipeManifestAssetPath,
                    $"material[{itemId}]",
                    $"铁器链材料 '{itemId}' 没有任何已启用配方消费，会成为死产物。",
                    null);
            }
        }
    }

    /// <summary>铁器消费端必须保留稳定配方 ID、唯一产物和可解析的 JSON 物品定义。</summary>
    private static void ValidateRecipeOutput(
        ValidationContext context,
        IReadOnlyDictionary<string, RecipeDto> recipesById,
        string recipeId,
        string outputItemId,
        FlatWorldContentValidationReport report)
    {
        if (recipesById.TryGetValue(recipeId, out RecipeDto recipe) &&
            recipe.Outputs != null &&
            recipe.Outputs.Count == 1 &&
            string.Equals(recipe.Outputs[0]?.ItemId, outputItemId, StringComparison.Ordinal) &&
            ItemExists(context, outputItemId))
        {
            return;
        }

        AddError(
            report,
            "FWC-PROGRESSION-003",
            RecipeManifestAssetPath,
            $"recipes[{recipeId}].outputs",
            $"配方 '{recipeId}' 必须唯一产出可解析物品 '{outputItemId}'。",
            null);
    }

    private static bool ItemExists(ValidationContext context, string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               (context.ItemDefinitionIds.Contains(itemId) || context.PrefabAliases.ContainsKey(itemId));
    }

    #endregion

    #region 建筑关系

    private static void ValidateBuildings(ValidationContext context, FlatWorldContentValidationReport report)
    {
        Dictionary<string, PrefabRecord> buildingRecords = new(StringComparer.Ordinal);
        foreach (PrefabRecord record in context.ItemPrefabs)
        {
            if (string.IsNullOrWhiteSpace(record.ItemId) ||
                !record.Modules.Any(module => module is Mod_Building) ||
                buildingRecords.ContainsKey(record.ItemId))
            {
                continue;
            }

            buildingRecords.Add(record.ItemId, record);
        }

        foreach (PrefabRecord record in buildingRecords.Values)
        {
            Mod_Building[] modules = record.Prefab.GetComponentsInChildren<Mod_Building>(true);
            if (modules.Length != 1)
            {
                AddError(
                    report,
                    "FWC-BUILDING-001",
                    record.Path,
                    "Mod_Building",
                    $"建筑 Prefab 应且只能包含一个 Mod_Building，当前数量：{modules.Length}。",
                    record.Prefab);
                continue;
            }

            Mod_Building module = modules[0];
            Mod_Building.Building_Data state = module.Data;
            if (module.BuildingData == null)
            {
                AddError(report, "FWC-BUILDING-002", record.Path, "Mod_Building.BuildingData", "建筑模块缺少 Ex_ModData。", module);
            }
            else
            {
                try
                {
                    Mod_Building.Building_Data persisted = module.BuildingData.GetData<Mod_Building.Building_Data>();
                    if (persisted == null)
                    {
                        AddError(report, "FWC-BUILDING-003", record.Path, "Mod_Building.BuildingData.BitData", "建筑关系数据为空。", module);
                    }
                    else if (!BuildingStatesMatch(state, persisted))
                    {
                        AddError(
                            report,
                            "FWC-BUILDING-004",
                            record.Path,
                            "Mod_Building.Data",
                            "Inspector 中的建筑关系与 BuildingData.BitData 不一致。",
                            module);
                    }
                }
                catch (Exception exception)
                {
                    AddError(report, "FWC-BUILDING-005", record.Path, "Mod_Building.BuildingData.BitData", $"建筑关系 JSON 无法读取：{exception.Message}", module);
                }
            }

            if (state == null)
            {
                AddError(report, "FWC-BUILDING-006", record.Path, "Mod_Building.Data", "建筑关系数据为空。", module);
                continue;
            }

            string expectedBuildingId = Mod_Building.GetBuildingPrefabId(record.ItemId);
            string expectedSummonerId = Mod_Building.GetSummonerPrefabId(expectedBuildingId);
            BuildingRole expectedRole = record.ItemId.EndsWith(Mod_Building.SummonerPrefabSuffix, StringComparison.Ordinal)
                ? BuildingRole.Summoner
                : BuildingRole.PlacedBuilding;

            if (state.Role != expectedRole)
            {
                AddError(report, "FWC-BUILDING-007", record.Path, "Mod_Building.Data.Role", $"ID '{record.ItemId}' 应为 {expectedRole}，实际为 {state.Role}。", module);
            }
            if (!string.Equals(state.BuildingPrefabId, expectedBuildingId, StringComparison.Ordinal))
            {
                AddError(report, "FWC-BUILDING-008", record.Path, "Mod_Building.Data.BuildingPrefabId", $"应为 '{expectedBuildingId}'，实际为 '{state.BuildingPrefabId}'。", module);
            }
            if (!string.Equals(state.SummonerPrefabId, expectedSummonerId, StringComparison.Ordinal))
            {
                AddError(report, "FWC-BUILDING-009", record.Path, "Mod_Building.Data.SummonerPrefabId", $"应为 '{expectedSummonerId}'，实际为 '{state.SummonerPrefabId}'。", module);
            }

            string pairId = expectedRole == BuildingRole.Summoner ? expectedBuildingId : expectedSummonerId;
            if (!buildingRecords.TryGetValue(pairId, out PrefabRecord pair))
            {
                AddError(report, "FWC-BUILDING-010", record.Path, "Mod_Building.Data", $"缺少配对 Prefab：{pairId}", module);
            }
            else
            {
                Mod_Building pairModule = pair.Prefab.GetComponentInChildren<Mod_Building>(true);
                BuildingRole pairRole = expectedRole == BuildingRole.Summoner
                    ? BuildingRole.PlacedBuilding
                    : BuildingRole.Summoner;
                if (pairModule?.Data == null || pairModule.Data.Role != pairRole)
                {
                    AddError(report, "FWC-BUILDING-011", pair.Path, "Mod_Building.Data.Role", $"与 {record.Path} 配对的 Prefab 角色应为 {pairRole}。", pairModule);
                }
            }

            bool canBePickedUp = record.Item.itemData.Stack?.CanBePickedUp ?? false;
            bool expectedPickup = expectedRole == BuildingRole.Summoner;
            if (canBePickedUp != expectedPickup)
            {
                AddError(
                    report,
                    "FWC-BUILDING-012",
                    record.Path,
                    "ItemData.Stack.CanBePickedUp",
                    expectedPickup ? "建筑召唤器必须可拾取。" : "世界建筑本体不得可拾取。",
                    record.Item);
            }

            if (record.Prefab.GetComponentInChildren<DamageReceiver>(true) == null)
                AddError(report, "FWC-BUILDING-013", record.Path, "DamageReceiver", "建筑缺少必填生命值模块。", module);
            if (record.Prefab.GetComponentInChildren<BoxCollider2D>(true) == null)
                AddError(report, "FWC-BUILDING-014", record.Path, "BoxCollider2D", "建筑缺少必填占地碰撞体。", module);
            if (record.Prefab.GetComponentInChildren<SpriteRenderer>(true) == null)
                AddError(report, "FWC-BUILDING-015", record.Path, "SpriteRenderer", "建筑缺少放置预览所需的 SpriteRenderer。", module);
        }
    }

    private static bool BuildingStatesMatch(Mod_Building.Building_Data left, Mod_Building.Building_Data right)
    {
        if (left == null || right == null)
            return left == right;

        return left.Version == right.Version &&
               left.Role == right.Role &&
               left.State == right.State &&
               string.Equals(left.SnapshotBase64, right.SnapshotBase64, StringComparison.Ordinal) &&
               string.Equals(left.BuildingPrefabId, right.BuildingPrefabId, StringComparison.Ordinal) &&
               string.Equals(left.SummonerPrefabId, right.SummonerPrefabId, StringComparison.Ordinal);
    }

    #endregion

    #region 生物生成器

    private static void ValidateSpawners(ValidationContext context, FlatWorldContentValidationReport report)
    {
        bool spawnerJsonLoaded = ValidateSpawnerJson(context, report);
        string[] guids = AssetDatabase.FindAssets("t:SpawnerConfig", new[] { ResourcesRoot + "/Config" });
        Array.Sort(guids, StringComparer.Ordinal);

        Dictionary<string, string> persistentIds = new(StringComparer.Ordinal);
        Dictionary<string, string> speciesOwners = new(StringComparer.Ordinal);
        Dictionary<SpawnerConfig, string> configPaths = new();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            SpawnerConfig config = AssetDatabase.LoadAssetAtPath<SpawnerConfig>(path);
            if (config == null)
                continue;

            configPaths[config] = path;
            string persistentId = config.PersistentId?.Trim();
            if (string.IsNullOrWhiteSpace(persistentId))
            {
                AddError(report, "FWC-SPAWNER-001", path, "SpawnerConfig.PersistentId", "生成配置持久化 ID 为空。", config);
            }
            else if (persistentIds.TryGetValue(persistentId, out string existingPath))
            {
                AddError(report, "FWC-SPAWNER-002", path, "SpawnerConfig.PersistentId", $"持久化 ID '{persistentId}' 与 {existingPath} 重复。", config);
            }
            else
            {
                persistentIds[persistentId] = path;
            }

            if (config.SpawnEntries == null || config.SpawnEntries.Count == 0)
            {
                AddError(report, "FWC-SPAWNER-003", path, "SpawnerConfig.SpawnEntries", "生成列表为空。", config);
                continue;
            }

            HashSet<string> localSpecies = new(StringComparer.Ordinal);
            float positiveWeight = 0f;
            for (int index = 0; index < config.SpawnEntries.Count; index++)
            {
                SpawnerConfig.SpawnEntry entry = config.SpawnEntries[index];
                string field = $"SpawnerConfig.SpawnEntries[{index}]";
                if (entry == null)
                {
                    AddError(report, "FWC-SPAWNER-004", path, field, "生成条目为空。", config);
                    continue;
                }

                string speciesId = entry.PrefabName?.Trim();
                if (string.IsNullOrWhiteSpace(speciesId))
                {
                    AddError(report, "FWC-SPAWNER-005", path, field + ".PrefabName", "生物 ID 为空。", config);
                }
                else
                {
                    if (!localSpecies.Add(speciesId))
                        AddError(report, "FWC-SPAWNER-006", path, field + ".PrefabName", $"同一配置内重复生物 ID：{speciesId}", config);
                    if (speciesOwners.TryGetValue(speciesId, out string ownerPath) && !string.Equals(ownerPath, path, StringComparison.Ordinal))
                        AddError(report, "FWC-SPAWNER-007", path, field + ".PrefabName", $"生物 ID '{speciesId}' 已在 {ownerPath} 配置。", config);
                    else
                        speciesOwners[speciesId] = path;
                    if (!context.PrefabAliases.ContainsKey(speciesId))
                        AddError(report, "FWC-SPAWNER-008", path, field + ".PrefabName", $"生物 ID '{speciesId}' 无法解析到 Prefab。", config);
                }

                if (float.IsNaN(entry.Probability) || float.IsInfinity(entry.Probability) || entry.Probability < 0f)
                    AddError(report, "FWC-SPAWNER-009", path, field + ".Probability", $"生成权重无效：{entry.Probability}", config);
                else if (entry.Probability == 0f)
                    AddWarning(report, "FWC-SPAWNER-101", path, field + ".Probability", "权重为 0，该条目不会被抽中。", config);
                else
                    positiveWeight += entry.Probability;

                if (entry.EcologyCost <= 0)
                    AddError(report, "FWC-SPAWNER-010", path, field + ".EcologyCost", "生态成本必须大于 0。", config);
                if (entry.SpeciesAliveLimit < 0)
                    AddError(report, "FWC-SPAWNER-011", path, field + ".SpeciesAliveLimit", "物种存活上限不能小于 0。", config);
            }

            if (positiveWeight <= 0f)
                AddError(report, "FWC-SPAWNER-012", path, "SpawnerConfig.SpawnEntries.Probability", "配置没有任何正权重条目。", config);

            for (int index = 0; index < (config.AllowedBiomeNames?.Count ?? 0); index++)
            {
                string biomeName = config.AllowedBiomeNames[index]?.Trim();
                if (!string.IsNullOrWhiteSpace(biomeName) && context.BiomeNames.Count > 0 && !context.BiomeNames.Contains(biomeName))
                {
                    AddError(report, "FWC-SPAWNER-013", path, $"SpawnerConfig.AllowedBiomeNames[{index}]", $"群系 '{biomeName}' 不存在。", config);
                }
            }
        }

        ValidateWorldManagerSpawnerReferences(configPaths, report, spawnerJsonLoaded);
    }

    private static bool ValidateSpawnerJson(
        ValidationContext context,
        FlatWorldContentValidationReport report)
    {
        const string path = "Assets/StreamingAssets/GameConfig/Spawners/spawner-manifest.json";
        SpawnerConfigCatalog catalog;
        try
        {
            catalog = SpawnerConfigCatalogLoader.LoadBuiltIn();
        }
        catch (Exception exception)
        {
            AddError(report, "FWC-SPAWNER-019", path, "spawner-manifest.json", exception.Message, null);
            return false;
        }

        if (catalog?.Configs == null)
            return false;

        for (int configIndex = 0; configIndex < catalog.Configs.Count; configIndex++)
        {
            SpawnerConfigDefinition config = catalog.Configs[configIndex];
            if (config?.SpawnEntries == null)
                continue;

            for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
            {
                SpawnerSpawnEntryDefinition entry = config.SpawnEntries[entryIndex];
                string speciesId = entry?.PrefabName?.Trim();
                if (!string.IsNullOrWhiteSpace(speciesId) &&
                    !context.PrefabAliases.ContainsKey(speciesId))
                {
                    AddError(
                        report,
                        "FWC-SPAWNER-020",
                        path,
                        $"configs[{configIndex}].spawnEntries[{entryIndex}].prefabName",
                        $"生物 ID '{speciesId}' 无法解析到 Prefab。",
                        null);
                }
            }
        }

        return true;
    }

    private static void ValidateWorldManagerSpawnerReferences(
        Dictionary<SpawnerConfig, string> configPaths,
        FlatWorldContentValidationReport report,
        bool spawnerJsonLoaded)
    {
        if (spawnerJsonLoaded)
            return;

        GameObject worldManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldManagerPrefabPath);
        if (worldManagerPrefab == null)
        {
            AddError(report, "FWC-SPAWNER-014", WorldManagerPrefabPath, "WorldManager", "无法加载世界管理器 Prefab。", null);
            return;
        }

        MonsterSpawnerManager manager = worldManagerPrefab.GetComponentInChildren<MonsterSpawnerManager>(true);
        if (manager == null)
        {
            AddError(report, "FWC-SPAWNER-015", WorldManagerPrefabPath, "MonsterSpawnerManager", "世界管理器缺少 MonsterSpawnerManager。", worldManagerPrefab);
            return;
        }

        SerializedProperty configs = new SerializedObject(manager).FindProperty("_spawnerConfigs");
        if (configs == null || !configs.isArray || configs.arraySize == 0)
        {
            AddError(report, "FWC-SPAWNER-016", WorldManagerPrefabPath, "MonsterSpawnerManager._spawnerConfigs", "活跃生成配置列表为空。", manager);
            return;
        }

        HashSet<SpawnerConfig> activeConfigs = new();
        for (int index = 0; index < configs.arraySize; index++)
        {
            SerializedProperty element = configs.GetArrayElementAtIndex(index);
            SpawnerConfig config = element.objectReferenceValue as SpawnerConfig;
            if (config == null)
            {
                AddError(report, "FWC-SPAWNER-017", WorldManagerPrefabPath, $"MonsterSpawnerManager._spawnerConfigs[{index}]", "生成配置引用为空或丢失。", manager);
                continue;
            }

            if (!activeConfigs.Add(config))
                AddError(report, "FWC-SPAWNER-018", WorldManagerPrefabPath, $"MonsterSpawnerManager._spawnerConfigs[{index}]", $"重复引用生成配置：{AssetDatabase.GetAssetPath(config)}", manager);
        }

        foreach (KeyValuePair<SpawnerConfig, string> pair in configPaths)
        {
            if (!activeConfigs.Contains(pair.Key))
                AddWarning(report, "FWC-SPAWNER-102", pair.Value, "SpawnerConfig", "Resources 中的生成配置未接入 WorldManager.prefab。", pair.Key);
        }
    }

    #endregion

    #region 战利品表

    private static void ValidateLootTables(ValidationContext context, FlatWorldContentValidationReport report)
    {
        foreach (string guid in FindAssetGuids("t:Prefab", PrefabRoot))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component != null)
                    ValidateLootProperties(new SerializedObject(component), path, component, context, report);
            }
        }

        foreach (string guid in FindAssetGuids("t:ScriptableObject", ScriptObjectRoot, ResourcesRoot))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset != null)
                ValidateLootProperties(new SerializedObject(asset), path, asset, context, report);
        }
    }

    private static void ValidateLootProperties(
        SerializedObject serializedObject,
        string assetPath,
        Object owner,
        ValidationContext context,
        FlatWorldContentValidationReport report)
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType != SerializedPropertyType.Generic)
                continue;

            if (string.Equals(iterator.type, nameof(LootEntry), StringComparison.Ordinal))
            {
                ValidateLootEntry(iterator, assetPath, owner, context, report);
                enterChildren = false;
            }
            else if (string.Equals(iterator.type, nameof(LootData), StringComparison.Ordinal))
            {
                ValidateLegacyLootEntry(iterator, assetPath, owner, context, report);
                enterChildren = false;
            }
        }
    }

    private static void ValidateLootEntry(
        SerializedProperty property,
        string assetPath,
        Object owner,
        ValidationContext context,
        FlatWorldContentValidationReport report)
    {
        SerializedProperty prefabProperty = property.FindPropertyRelative("LootPrefab");
        SerializedProperty idProperty = property.FindPropertyRelative("LootPrefabName");
        SerializedProperty chanceProperty = property.FindPropertyRelative("DropChance");
        SerializedProperty minProperty = property.FindPropertyRelative("MinAmount");
        SerializedProperty maxProperty = property.FindPropertyRelative("MaxAmount");
        string field = property.propertyPath;
        string itemId = idProperty?.stringValue?.Trim();

        if (string.IsNullOrWhiteSpace(itemId))
        {
            AddError(report, "FWC-LOOT-001", assetPath, field + ".LootPrefabName", "战利品物品 ID 为空。", owner);
        }
        else if (!context.PrefabAliases.ContainsKey(itemId))
        {
            AddError(report, "FWC-LOOT-002", assetPath, field + ".LootPrefabName", $"战利品物品 ID '{itemId}' 不存在。", owner);
        }

        if (prefabProperty != null && prefabProperty.objectReferenceInstanceIDValue != 0 && prefabProperty.objectReferenceValue == null)
        {
            AddError(report, "FWC-LOOT-003", assetPath, field + ".LootPrefab", "战利品 Prefab 引用已丢失。", owner);
        }
        else if (prefabProperty?.objectReferenceValue is GameObject lootPrefab)
        {
            Item lootItem = lootPrefab.GetComponent<Item>();
            string referencedId = lootItem?.itemData?.IDName;
            if (string.IsNullOrWhiteSpace(referencedId))
                referencedId = lootPrefab.name;
            if (!string.IsNullOrWhiteSpace(itemId) && !string.Equals(referencedId, itemId, StringComparison.Ordinal))
            {
                AddError(report, "FWC-LOOT-004", assetPath, field + ".LootPrefab", $"Prefab 的物品 ID '{referencedId}' 与 LootPrefabName '{itemId}' 不一致。", owner);
            }
        }

        if (chanceProperty != null &&
            (float.IsNaN(chanceProperty.floatValue) || chanceProperty.floatValue < 0f || chanceProperty.floatValue > 1f))
        {
            AddError(report, "FWC-LOOT-005", assetPath, field + ".DropChance", $"掉落概率必须在 0-1，实际为 {chanceProperty.floatValue}。", owner);
        }
        if (minProperty != null && minProperty.intValue < 0)
            AddError(report, "FWC-LOOT-006", assetPath, field + ".MinAmount", "最小掉落数量不能小于 0。", owner);
        if (minProperty != null && maxProperty != null && maxProperty.intValue < minProperty.intValue)
            AddError(report, "FWC-LOOT-007", assetPath, field + ".MaxAmount", "最大掉落数量小于最小掉落数量。", owner);
    }

    private static void ValidateLegacyLootEntry(
        SerializedProperty property,
        string assetPath,
        Object owner,
        ValidationContext context,
        FlatWorldContentValidationReport report)
    {
        SerializedProperty idProperty = property.FindPropertyRelative("lootName");
        SerializedProperty prefabProperty = property.FindPropertyRelative("lootPrefab");
        SerializedProperty amountProperty = property.FindPropertyRelative("lootAmountRange");
        string field = property.propertyPath;
        string itemId = idProperty?.stringValue?.Trim();

        if (string.IsNullOrWhiteSpace(itemId))
            AddError(report, "FWC-LOOT-008", assetPath, field + ".lootName", "旧战利品物品 ID 为空。", owner);
        else if (!context.PrefabAliases.ContainsKey(itemId))
            AddError(report, "FWC-LOOT-009", assetPath, field + ".lootName", $"旧战利品物品 ID '{itemId}' 不存在。", owner);

        if (prefabProperty != null && prefabProperty.objectReferenceInstanceIDValue != 0 && prefabProperty.objectReferenceValue == null)
            AddError(report, "FWC-LOOT-010", assetPath, field + ".lootPrefab", "旧战利品 Prefab 引用已丢失。", owner);
        if (amountProperty != null)
        {
            Vector2Int range = amountProperty.vector2IntValue;
            if (range.x < 0 || range.y < range.x)
                AddError(report, "FWC-LOOT-011", assetPath, field + ".lootAmountRange", $"旧战利品数量范围无效：{range}", owner);
        }
    }

    #endregion

    #region 生物群系生成物

    private static void ValidateBiomes(ValidationContext context, FlatWorldContentValidationReport report)
    {
        string biomeRoot = ScriptObjectRoot + "/4-8_Biome";
        string[] guids = AssetDatabase.FindAssets("t:BiomeData", new[] { biomeRoot });
        Array.Sort(guids, StringComparer.Ordinal);
        Dictionary<string, string> biomePaths = new(StringComparer.Ordinal);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BiomeData biome = AssetDatabase.LoadAssetAtPath<BiomeData>(path);
            if (biome == null)
                continue;

            string biomeName = biome.BiomeName?.Trim();
            if (string.IsNullOrWhiteSpace(biomeName))
            {
                AddError(report, "FWC-BIOME-001", path, "BiomeData.BiomeName", "群系 ID 为空。", biome);
            }
            else if (biomePaths.TryGetValue(biomeName, out string existingPath))
            {
                AddError(report, "FWC-BIOME-002", path, "BiomeData.BiomeName", $"群系 ID '{biomeName}' 与 {existingPath} 重复。", biome);
            }
            else
            {
                biomePaths[biomeName] = path;
                context.BiomeNames.Add(biomeName);
            }

            if (biome.TerrainConfig == null)
            {
                AddError(report, "FWC-BIOME-003", path, "BiomeData.TerrainConfig", "群系缺少地形生成配置。", biome);
                continue;
            }

            ValidateBiomeTileSpawns(biome, path, report);
            ValidateBiomeItemSpawns(context, biome, path, report);
        }
    }

    private static void ValidateBiomeTileSpawns(
        BiomeData biome,
        string path,
        FlatWorldContentValidationReport report)
    {
        for (int index = 0; index < (biome.TerrainConfig.TileSpawns_NoSO?.Count ?? 0); index++)
        {
            BiomeTileSpawn_NoSo entry = biome.TerrainConfig.TileSpawns_NoSO[index];
            string field = $"BiomeData.TerrainConfig.TileSpawns_NoSO[{index}]";
            if (entry == null)
            {
                AddError(report, "FWC-BIOME-004", path, field, "地块生成条目为空。", biome);
                continue;
            }
            if (entry.TileBlock == null)
                AddError(report, "FWC-BIOME-005", path, field + ".TileBlock", "地块生成物引用为空。", biome);
            if (entry.environmentConditionRange == null)
                AddError(report, "FWC-BIOME-006", path, field + ".environmentConditionRange", "地块生成条件为空。", biome);
        }
    }

    private static void ValidateBiomeItemSpawns(
        ValidationContext context,
        BiomeData biome,
        string path,
        FlatWorldContentValidationReport report)
    {
        for (int index = 0; index < (biome.TerrainConfig.ItemSpawn_NoSO?.Count ?? 0); index++)
        {
            Biome_ItemSpawn_NoSO entry = biome.TerrainConfig.ItemSpawn_NoSO[index];
            string field = $"BiomeData.TerrainConfig.ItemSpawn_NoSO[{index}]";
            if (entry == null)
            {
                AddError(report, "FWC-BIOME-007", path, field, "物品生成条目为空。", biome);
                continue;
            }

            string itemId = entry.itemName?.Trim();
            if (string.IsNullOrWhiteSpace(itemId))
                AddError(report, "FWC-BIOME-008", path, field + ".itemName", "群系生成物 ID 为空。", biome);
            else if (!context.PrefabAliases.ContainsKey(itemId))
                AddError(report, "FWC-BIOME-009", path, field + ".itemName", $"群系生成物 ID '{itemId}' 不存在。", biome);

            if (entry.itemPrefab == null)
            {
                AddError(report, "FWC-BIOME-010", path, field + ".itemPrefab", "群系生成物 Prefab 引用为空。", biome);
            }
            else
            {
                Item prefabItem = entry.itemPrefab.GetComponent<Item>();
                string referencedId = prefabItem?.itemData?.IDName;
                if (string.IsNullOrWhiteSpace(referencedId))
                {
                    AddError(report, "FWC-BIOME-011", path, field + ".itemPrefab", "引用的 Prefab 缺少 Item 或 ItemData.IDName。", biome);
                }
                else if (!string.IsNullOrWhiteSpace(itemId) && !string.Equals(referencedId, itemId, StringComparison.Ordinal))
                {
                    AddError(report, "FWC-BIOME-012", path, field + ".itemPrefab", $"Prefab 物品 ID '{referencedId}' 与 itemName '{itemId}' 不一致。", biome);
                }
            }

            if (entry.itemCount <= 0)
                AddError(report, "FWC-BIOME-013", path, field + ".itemCount", "生成数量必须大于 0。", biome);
            if (float.IsNaN(entry.SpawnChance) || entry.SpawnChance < 0f || entry.SpawnChance > 1f)
                AddError(report, "FWC-BIOME-014", path, field + ".SpawnChance", $"生成概率必须在 0-1，实际为 {entry.SpawnChance}。", biome);
            if (float.IsNaN(entry.SpawnChanceMultiplier) || entry.SpawnChanceMultiplier < 0f)
                AddError(report, "FWC-BIOME-015", path, field + ".SpawnChanceMultiplier", "生成倍率不能小于 0。", biome);
            if (entry.environmentConditionRange == null)
                AddError(report, "FWC-BIOME-016", path, field + ".environmentConditionRange", "物品生成条件为空，运行时会跳过该条目。", biome);
            if (entry.CompanionOnly && string.IsNullOrWhiteSpace(entry.CompanionHostTag))
                AddError(report, "FWC-BIOME-017", path, field + ".CompanionHostTag", "仅伴生生成条目必须填写宿主标签。", biome);
        }
    }

    #endregion

    #region 固定 Resources 与必填 Prefab

    private static void ValidateFixedResources(FlatWorldContentValidationReport report)
    {
        ResourceRequirement[] requirements =
        {
            new("Weather/RainEffect", "Assets/Resources/Weather/RainEffect.prefab", typeof(GameObject)),
            new("Config/StructureCatalog_Default", "Assets/Resources/Config/StructureCatalog_Default.asset", typeof(StructureCatalogSO)),
            new("Config/SpawnerConfig", "Assets/Resources/Config/SpawnerConfig.asset", typeof(SpawnerConfig)),
            new("Config/SpawnerConfig_Wolves", "Assets/Resources/Config/SpawnerConfig_Wolves.asset", typeof(SpawnerConfig)),
            new("Config/SpawnerConfig_Ghost", "Assets/Resources/Config/SpawnerConfig_Ghost.asset", typeof(SpawnerConfig)),
            new("Networking/FlatWorldNetworkPlayer", "Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab", typeof(GameObject))
        };

        foreach (ResourceRequirement requirement in requirements)
        {
            Object asset = AssetDatabase.LoadAssetAtPath(requirement.AssetPath, requirement.ExpectedType);
            if (asset == null)
            {
                AddError(
                    report,
                    "FWC-RES-001",
                    requirement.AssetPath,
                    requirement.ResourcePath,
                    $"固定 Resources 路径缺少 {requirement.ExpectedType.Name} 资源。",
                    null);
            }
        }

        ValidateResourceFolder(report, "Dialogue/Soliloquy", "Assets/Resources/Dialogue/Soliloquy", "t:TextAsset");
        ValidateResourceFolder(report, "Audio/Cues", "Assets/Resources/Audio/Cues", "t:ScriptableObject");
        ValidateUntypedResource(report, "Audio/AudioRuntimeConfig", "Assets/Resources/Audio/AudioRuntimeConfig.asset");
        ValidateUntypedResource(report, "Audio/AudioCatalog", "Assets/Resources/Audio/AudioCatalog.asset");
    }

    private static void ValidateResourceFolder(
        FlatWorldContentValidationReport report,
        string resourcePath,
        string assetPath,
        string filter)
    {
        if (!AssetDatabase.IsValidFolder(assetPath))
        {
            AddError(report, "FWC-RES-002", assetPath, resourcePath, "固定 Resources 文件夹不存在。", null);
            return;
        }

        if (AssetDatabase.FindAssets(filter, new[] { assetPath }).Length == 0)
            AddError(report, "FWC-RES-003", assetPath, resourcePath, "固定 Resources 文件夹内没有可加载资源。", null);
    }

    private static void ValidateUntypedResource(
        FlatWorldContentValidationReport report,
        string resourcePath,
        string assetPath)
    {
        Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null)
            AddError(report, "FWC-RES-004", assetPath, resourcePath, "固定 Resources 资源不存在。", null);
    }

    private static void ValidateRequiredPrefabs(ValidationContext context, FlatWorldContentValidationReport report)
    {
        if (!context.PrefabAliases.TryGetValue(BuildingShadowPrefabId, out PrefabRecord shadowRecord))
        {
            AddError(report, "FWC-PREFAB-003", PrefabRoot, "GameRes.AllPrefabs[BuildingShadow]", "缺少建筑预览必填 Prefab 注册键。", null);
        }
        else
        {
            BuildingShadow shadow = shadowRecord.Prefab.GetComponentInChildren<BuildingShadow>(true);
            if (shadow == null)
                AddError(report, "FWC-PREFAB-004", shadowRecord.Path, "BuildingShadow", "BuildingShadow Prefab 缺少 BuildingShadow 组件。", shadowRecord.Prefab);
            else
            {
                if (shadow.ShadowRenderer == null)
                    AddError(report, "FWC-PREFAB-005", shadowRecord.Path, "BuildingShadow.ShadowRenderer", "建筑预览缺少 SpriteRenderer 引用。", shadow);
                if (shadowRecord.Prefab.GetComponentInChildren<BoxCollider2D>(true) == null)
                    AddError(report, "FWC-PREFAB-006", shadowRecord.Path, "BuildingShadow.previewCollider", "建筑预览缺少 BoxCollider2D。", shadow);
            }
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            AddError(report, "FWC-PREFAB-007", "Assets/AddressableAssetsData", "AddressableAssetSettings", "Addressables 设置不存在。", null);
            return;
        }

        string prefabFolderGuid = AssetDatabase.AssetPathToGUID(PrefabRoot);
        AddressableAssetEntry entry = settings.FindAssetEntry(prefabFolderGuid);
        if (entry == null)
        {
            AddError(report, "FWC-PREFAB-008", PrefabRoot, "Addressables.Entry", "Prefab 根目录未加入 Addressables，GameRes 无法加载本体 Prefab。", settings);
        }
        else if (!entry.labels.Contains("Prefab"))
        {
            AddError(report, "FWC-PREFAB-009", PrefabRoot, "Addressables.Labels", "Prefab 根目录缺少 'Prefab' 标签。", settings);
        }
    }

    #endregion

    #region 序列化引用完整性

    private static void ValidateSerializedReferences(FlatWorldContentValidationReport report)
    {
        foreach (string guid in FindAssetGuids("t:Prefab", PrefabRoot))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                {
                    AddError(report, "FWC-REF-001", path, $"Components[{index}]", "Prefab 存在 Missing Script。", prefab);
                    continue;
                }

                ValidateBrokenObjectReferences(new SerializedObject(component), path, component, report);
            }
        }

        foreach (string guid in FindAssetGuids("t:ScriptableObject", ScriptObjectRoot, ResourcesRoot))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset != null)
                ValidateBrokenObjectReferences(new SerializedObject(asset), path, asset, report);
        }
    }

    private static void ValidateBrokenObjectReferences(
        SerializedObject serializedObject,
        string assetPath,
        Object owner,
        FlatWorldContentValidationReport report)
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = true;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            if (iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
            {
                AddError(report, "FWC-REF-002", assetPath, iterator.propertyPath, "序列化对象引用已丢失。", owner);
            }
        }
    }

    #endregion

    #region 工具方法

    private static IEnumerable<string> FindAssetGuids(string filter, params string[] roots)
    {
        string[] guids = AssetDatabase.FindAssets(filter, roots);
        Array.Sort(guids, StringComparer.Ordinal);
        return guids;
    }

    private static void RunRule(
        FlatWorldContentValidationReport report,
        string errorId,
        string assetPath,
        string fieldName,
        Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            AddError(report, errorId, assetPath, fieldName, $"校验规则异常：{exception.Message}", null);
            Debug.LogException(exception);
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (char character in value)
        {
            if (char.IsControl(character))
                return true;
        }
        return false;
    }

    private static bool IsDebugDescription(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        string[] markers =
        {
            "物品名称：",
            "物品描述：",
            "物品体积：",
            "物品堆叠信息：",
            "全局唯一标识："
        };
        int markerCount = markers.Count(value.Contains);
        return value.StartsWith("物品名称：", StringComparison.Ordinal) || markerCount >= 3;
    }

    private static bool ContainsDisallowedRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        string lower = value.ToLowerInvariant();
        string[] tags = { "<color", "<size", "<sprite", "<font", "<link", "<style", "<material" };
        return tags.Any(lower.Contains);
    }

    private static string ToAbsolutePath(string assetPath)
    {
        return Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ToAssetPath(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(absolutePath);
        if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath.Replace('\\', '/');
        return fullPath.Substring(projectRoot.Length + 1).Replace('\\', '/');
    }

    private static void LogReport(
        FlatWorldContentValidationReport report,
        FlatWorldContentValidationMode mode)
    {
        foreach (FlatWorldContentValidationIssue issue in report.Issues
                     .OrderBy(issue => issue.Severity)
                     .ThenBy(issue => issue.AssetPath, StringComparer.Ordinal)
                     .ThenBy(issue => issue.FieldName, StringComparer.Ordinal))
        {
            if (issue.Severity == FlatWorldContentValidationSeverity.Error)
                Debug.LogError(issue.ToString(), issue.Context);
            else
                Debug.LogWarning(issue.ToString(), issue.Context);
        }

        string summary =
            $"[FlatWorldContentValidation] 模式: {mode}，错误: {report.ErrorCount}，警告: {report.WarningCount}。校验过程未修改任何资产。";
        if (report.HasErrors)
            Debug.LogError(summary);
        else if (report.WarningCount > 0)
            Debug.LogWarning(summary);
        else
            Debug.Log(summary);
    }

    private static void AddError(
        FlatWorldContentValidationReport report,
        string errorId,
        string assetPath,
        string fieldName,
        string message,
        Object context)
    {
        report.Add(new FlatWorldContentValidationIssue(
            FlatWorldContentValidationSeverity.Error,
            errorId,
            assetPath,
            fieldName,
            message,
            context));
    }

    private static void AddWarning(
        FlatWorldContentValidationReport report,
        string errorId,
        string assetPath,
        string fieldName,
        string message,
        Object context)
    {
        report.Add(new FlatWorldContentValidationIssue(
            FlatWorldContentValidationSeverity.Warning,
            errorId,
            assetPath,
            fieldName,
            message,
            context));
    }

    #endregion
}

/// <summary>正式构建前执行同一套只读内容校验；存在错误时阻止构建。</summary>
public sealed class FlatWorldContentValidationBuildHook : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        FlatWorldContentValidationReport validationReport = FlatWorldContentValidator.ValidateAll(
            FlatWorldContentValidationMode.Build,
            true);
        if (validationReport.HasErrors)
        {
            throw new BuildFailedException(
                $"FlatWorld 本体内容校验失败：{validationReport.ErrorCount} 个错误，{validationReport.WarningCount} 个警告。" +
                $" 请通过菜单“{FlatWorldContentValidator.MenuPath}”查看详细资源路径、字段和错误 ID。");
        }
    }
}
