using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates one summoner prefab for every placeable building or tool body prefab.
/// Generated assets live beside their source category and are refreshed from the placed body
/// while preserving the summoner asset GUID used by recipes and saves.
/// </summary>
public static class BuildingSummonerPrefabGenerator
{
    public const string BuildingRoot = "Assets/2_Prefabs/Building";
    public const string SummonerRoot = BuildingRoot + "/Summoners";
    public const string ToolRoot = "Assets/2_Prefabs/Tools";
    public const string ToolSummonerRoot = ToolRoot + "/Summoners";

    private static readonly string[] SourceRoots = { BuildingRoot, ToolRoot };
    private static readonly string[] SummonerRoots = { SummonerRoot, ToolSummonerRoot };

    private static readonly HashSet<string> PendingBuildingPaths = new(StringComparer.OrdinalIgnoreCase);
    private static bool _scheduled;
    private static bool _running;

    [MenuItem("FlatWorld/建筑/生成或刷新全部建筑召唤器")]
    public static void GenerateAll()
    {
        GenerateFromRoots(SourceRoots, relinkAllRecipes: true);
    }

    [MenuItem("FlatWorld/Building/Generate Placeable Tool Summoners")]
    public static void GeneratePlaceableToolSummoners()
    {
        GenerateFromRoots(new[] { ToolRoot }, relinkAllRecipes: false);
    }

    private static void GenerateFromRoots(string[] roots, bool relinkAllRecipes)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", roots);
        List<string> buildingPaths = new(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (IsBuildingSourcePath(path))
                buildingPaths.Add(path);
        }

        Generate(buildingPaths, relinkAllRecipes);
    }

    internal static bool IsBuildingSourcePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) ||
            !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
            !IsUnderAnyRoot(assetPath, SourceRoots) ||
            IsUnderAnyRoot(assetPath, SummonerRoots))
        {
            return false;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        return prefab != null &&
               prefab.GetComponent<Item>() != null &&
               prefab.GetComponentInChildren<Mod_Building>(true) != null;
    }

    internal static void QueueImportedBuildings(IEnumerable<string> importedPaths)
    {
        if (_running || importedPaths == null)
            return;

        foreach (string path in importedPaths)
        {
            if (IsBuildingSourcePath(path))
                PendingBuildingPaths.Add(path);
        }

        if (_scheduled || PendingBuildingPaths.Count == 0)
            return;

        _scheduled = true;
        EditorApplication.delayCall += GeneratePending;
    }

    private static void GeneratePending()
    {
        _scheduled = false;
        if (PendingBuildingPaths.Count == 0)
            return;

        List<string> paths = new(PendingBuildingPaths);
        PendingBuildingPaths.Clear();
        Generate(paths, relinkAllRecipes: false);
    }

    private static void Generate(IReadOnlyList<string> buildingPaths, bool relinkAllRecipes)
    {
        if (_running || buildingPaths == null || buildingPaths.Count == 0)
            return;

        _running = true;
        int generatedCount = 0;
        int updatedRecipeCount = 0;
        try
        {
            EnsureSummonerFolder();
            for (int i = 0; i < buildingPaths.Count; i++)
            {
                string path = buildingPaths[i];
                if (!IsBuildingSourcePath(path))
                    continue;

                if (GenerateForBuilding(path, out GameObject buildingPrefab, out GameObject summonerPrefab))
                    generatedCount++;

                if (buildingPrefab != null && summonerPrefab != null)
                    updatedRecipeCount += RelinkRecipes(buildingPrefab, summonerPrefab);
            }

            if (relinkAllRecipes)
                updatedRecipeCount += RelinkEveryBuildingRecipe();

            AssetDatabase.SaveAssets();
            Debug.Log($"[建筑召唤器] 处理建筑 {buildingPaths.Count} 个，更新资源 {generatedCount} 个，更新配方 {updatedRecipeCount} 个。");
        }
        finally
        {
            _running = false;
        }
    }

    private static bool GenerateForBuilding(
        string buildingPath,
        out GameObject buildingPrefab,
        out GameObject summonerPrefab)
    {
        buildingPrefab = null;
        summonerPrefab = null;
        bool changed = false;

        GameObject buildingRoot = PrefabUtility.LoadPrefabContents(buildingPath);
        string buildingId;
        string summonerId;
        try
        {
            Item item = buildingRoot.GetComponent<Item>();
            if (item?.itemData == null)
                throw new InvalidOperationException($"建筑 Prefab 缺少 ItemData：{buildingPath}");

            buildingId = string.IsNullOrWhiteSpace(item.itemData.IDName)
                ? Path.GetFileNameWithoutExtension(buildingPath)
                : Mod_Building.GetBuildingPrefabId(item.itemData.IDName);
            summonerId = Mod_Building.GetSummonerPrefabId(buildingId);

            if (ConfigureRoot(buildingRoot, BuildingRole.PlacedBuilding, buildingId, summonerId))
            {
                PrefabUtility.SaveAsPrefabAsset(buildingRoot, buildingPath);
                changed = true;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(buildingRoot);
        }

        buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(buildingPath);
        if (buildingPrefab == null)
            throw new InvalidOperationException($"无法重新加载建筑 Prefab：{buildingPath}");

        Item buildingItem = buildingPrefab.GetComponent<Item>();
        Mod_Building buildingModule = buildingPrefab.GetComponentInChildren<Mod_Building>(true);
        if (buildingItem == null || buildingModule == null ||
            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(buildingPrefab, out _, out long rootFileId) ||
            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(buildingItem, out _, out long itemFileId) ||
            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(buildingModule, out _, out long moduleFileId))
        {
            throw new InvalidOperationException($"无法读取建筑 Prefab 的本地对象 ID：{buildingPath}");
        }

        string destinationRoot = buildingPath.StartsWith(
            ToolRoot + "/",
            StringComparison.OrdinalIgnoreCase)
            ? ToolSummonerRoot
            : SummonerRoot;
        string summonerPath = $"{destinationRoot}/{MakeSafeFileName(summonerId)}.prefab";
        // Prefab variants cannot represent every custom ItemData value (for example
        // DamageType dictionary keys). Mirror the source YAML instead and preserve an
        // existing destination .meta file so references to the summoner remain stable.
        File.Copy(Path.GetFullPath(buildingPath), Path.GetFullPath(summonerPath), true);

        PatchSummonerYaml(
            summonerPath,
            rootFileId,
            itemFileId,
            moduleFileId,
            buildingId,
            summonerId);
        AssetDatabase.ImportAsset(
            summonerPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        changed = true;

        summonerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(summonerPath);
        return changed;
    }

    private static void PatchSummonerYaml(
        string summonerPath,
        long rootFileId,
        long itemFileId,
        long moduleFileId,
        string buildingId,
        string summonerId)
    {
        string yaml = File.ReadAllText(Path.GetFullPath(summonerPath), Encoding.UTF8);
        yaml = ReplaceObjectField(yaml, rootFileId, "  m_Name:", ToYamlString(summonerId));
        yaml = ReplaceObjectField(yaml, itemFileId, "    IDName:", ToYamlString(summonerId));
        yaml = ReplaceObjectField(yaml, itemFileId, "      CanBePickedUp:", "1");
        yaml = ReplaceObjectField(yaml, itemFileId, "    inHand:", "0");

        Mod_Building.Building_Data state = new()
        {
            Version = Mod_Building.CurrentBuildingDataVersion,
            State = BuildingState.NotInstalled,
            Role = BuildingRole.Summoner,
            SnapshotBase64 = null,
            BuildingPrefabId = buildingId,
            SummonerPrefabId = summonerId,
            TileBlockId = Mod_Building.GetDefaultTileBlockId(buildingId)
        };
        Ex_ModData container = new();
        container.WriteData(state);
        string yamlJson = $"'{container.BitData.Replace("'", "''")}'";

        bool roleOverride = TryReplacePrefabOverrideValue(ref yaml, "Data.Role", "0");
        bool bitDataOverride = TryReplacePrefabOverrideValue(
            ref yaml,
            "BuildingData.BitData",
            yamlJson);

        if (roleOverride != bitDataOverride)
            throw new InvalidOperationException($"建筑模块覆盖数据不完整：{summonerPath}");

        if (!roleOverride)
        {
            yaml = ReplaceObjectField(yaml, moduleFileId, "    Role:", "0");
            yaml = ReplaceObjectField(yaml, moduleFileId, "    BitData:", yamlJson);
        }

        File.WriteAllText(
            Path.GetFullPath(summonerPath),
            yaml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool TryReplacePrefabOverrideValue(
        ref string yaml,
        string propertyPath,
        string value)
    {
        string marker = $"      propertyPath: {propertyPath}";
        int markerIndex = yaml.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return false;

        if (yaml.IndexOf(marker, markerIndex + marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Prefab 中存在重复覆盖字段：{propertyPath}");

        int valueLineStart = yaml.IndexOf("\n      value:", markerIndex, StringComparison.Ordinal);
        if (valueLineStart < 0)
            throw new InvalidOperationException($"Prefab 覆盖字段缺少值：{propertyPath}");

        valueLineStart++;
        int valueLineEnd = yaml.IndexOf('\n', valueLineStart);
        if (valueLineEnd < 0)
            valueLineEnd = yaml.Length;

        yaml = yaml.Remove(valueLineStart, valueLineEnd - valueLineStart)
            .Insert(valueLineStart, $"      value: {value}");
        return true;
    }

    private static string ReplaceObjectField(
        string yaml,
        long localFileId,
        string fieldPrefix,
        string value)
    {
        Match header = Regex.Match(
            yaml,
            $@"(?m)^--- !u!\d+ &{localFileId}(?: stripped)?\r?$",
            RegexOptions.CultureInvariant);
        if (!header.Success)
            throw new InvalidOperationException($"Prefab 中找不到本地对象：{localFileId}");

        int blockStart = header.Index;
        int blockEnd = yaml.IndexOf("\n--- !u!", header.Index + header.Length, StringComparison.Ordinal);
        if (blockEnd < 0)
            blockEnd = yaml.Length;

        string block = yaml.Substring(blockStart, blockEnd - blockStart);
        MatchCollection fields = Regex.Matches(
            block,
            $@"(?m)^{Regex.Escape(fieldPrefix)}.*\r?$",
            RegexOptions.CultureInvariant);
        if (fields.Count != 1)
        {
            throw new InvalidOperationException(
                $"Prefab 对象 {localFileId} 的字段 {fieldPrefix.Trim()} 数量异常：{fields.Count}");
        }

        Match field = fields[0];
        string replacement = $"{fieldPrefix} {value}";
        return yaml.Remove(blockStart + field.Index, field.Length)
            .Insert(blockStart + field.Index, replacement);
    }

    private static string ToYamlString(string value)
    {
        if (value == null)
            return "''";

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static bool ConfigureRoot(
        GameObject root,
        BuildingRole role,
        string buildingId,
        string summonerId)
    {
        Item item = root.GetComponent<Item>();
        Mod_Building building = root.GetComponentInChildren<Mod_Building>(true);
        if (item?.itemData == null || building == null)
            throw new InvalidOperationException($"{root.name} 缺少 Item、ItemData 或 Mod_Building");

        string desiredItemId = role == BuildingRole.Summoner ? summonerId : buildingId;
        bool desiredPickable = role == BuildingRole.Summoner;
        string desiredTileBlockId = Mod_Building.GetDefaultTileBlockId(buildingId);
        Mod_Building.Building_Data persisted = new();
        building.BuildingData?.ReadData(ref persisted);
        persisted ??= new Mod_Building.Building_Data();

        bool changed =
            !string.Equals(item.itemData.IDName, desiredItemId, StringComparison.Ordinal) ||
            item.itemData.Stack == null ||
            item.itemData.Stack.CanBePickedUp != desiredPickable ||
            item.itemData.inHand ||
            persisted.Version != Mod_Building.CurrentBuildingDataVersion ||
            persisted.Role != role ||
            persisted.State != BuildingState.NotInstalled ||
            !string.IsNullOrEmpty(persisted.SnapshotBase64) ||
            !string.Equals(persisted.BuildingPrefabId, buildingId, StringComparison.Ordinal) ||
            !string.Equals(persisted.SummonerPrefabId, summonerId, StringComparison.Ordinal) ||
            !string.Equals(persisted.TileBlockId, desiredTileBlockId, StringComparison.Ordinal);

        if (!changed)
            return false;

        root.name = desiredItemId;
        item.itemData.IDName = desiredItemId;
        item.itemData.inHand = false;
        item.itemData.Stack ??= new ItemStack();
        item.itemData.Stack.Amount = Mathf.Max(1f, item.itemData.Stack.Amount);
        item.itemData.Stack.CanBePickedUp = desiredPickable;
        if (role == BuildingRole.Summoner)
            item.itemData.ItemSpecialData = null;

        building.ConfigurePrefabRole(role, buildingId, summonerId);
        EditorUtility.SetDirty(item);
        EditorUtility.SetDirty(building);
        return true;
    }

    private static int RelinkRecipes(GameObject buildingPrefab, GameObject summonerPrefab)
    {
        Item buildingItem = buildingPrefab != null ? buildingPrefab.GetComponent<Item>() : null;
        Item summonerItem = summonerPrefab != null ? summonerPrefab.GetComponent<Item>() : null;
        string buildingItemId = buildingItem?.itemData != null && !string.IsNullOrWhiteSpace(buildingItem.itemData.IDName)
            ? buildingItem.itemData.IDName
            : buildingPrefab.name;
        string summonerItemId = summonerItem?.itemData != null && !string.IsNullOrWhiteSpace(summonerItem.itemData.IDName)
            ? summonerItem.itemData.IDName
            : summonerPrefab.name;
        int updated = RecipeExcelSyncService.RelinkOutputItemId(buildingItemId, summonerItemId);
        string[] recipeGuids = AssetDatabase.FindAssets("t:Recipe", new[] { "Assets" });
        for (int i = 0; i < recipeGuids.Length; i++)
        {
            string recipePath = AssetDatabase.GUIDToAssetPath(recipeGuids[i]);
            Recipe recipe = AssetDatabase.LoadAssetAtPath<Recipe>(recipePath);
            if (recipe?.outputs?.results == null)
                continue;

            bool recipeChanged = false;
            for (int resultIndex = 0; resultIndex < recipe.outputs.results.Count; resultIndex++)
            {
                Result_List result = recipe.outputs.results[resultIndex];
                if (result?.ItemPrefab != buildingPrefab)
                    continue;

                result.ItemPrefab = summonerPrefab;
                result.ItemName = summonerPrefab.name;
                recipeChanged = true;
            }

            if (!recipeChanged)
                continue;

            EditorUtility.SetDirty(recipe);
            updated++;
        }

        return updated;
    }

    private static int RelinkEveryBuildingRecipe()
    {
        int updated = 0;
        string[] summonerGuids = AssetDatabase.FindAssets("t:Prefab", SummonerRoots);
        for (int i = 0; i < summonerGuids.Length; i++)
        {
            GameObject summoner = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(summonerGuids[i]));
            Item summonerItem = summoner != null ? summoner.GetComponent<Item>() : null;
            Mod_Building module = summoner != null
                ? summoner.GetComponentInChildren<Mod_Building>(true)
                : null;
            if (summonerItem?.itemData == null || module?.BuildingData == null)
                continue;

            Mod_Building.Building_Data state = new();
            module.BuildingData.ReadData(ref state);
            if (state == null || string.IsNullOrWhiteSpace(state.BuildingPrefabId))
                continue;

            GameObject building = LoadPlaceablePrefab(state.BuildingPrefabId);
            if (building != null)
                updated += RelinkRecipes(building, summoner);
        }

        return updated;
    }

    private static void EnsureSummonerFolder()
    {
        if (!AssetDatabase.IsValidFolder(SummonerRoot))
            AssetDatabase.CreateFolder(BuildingRoot, "Summoners");
        if (!AssetDatabase.IsValidFolder(ToolSummonerRoot))
            AssetDatabase.CreateFolder(ToolRoot, "Summoners");
    }

    private static GameObject LoadPlaceablePrefab(string prefabId)
    {
        for (int i = 0; i < SourceRoots.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{SourceRoots[i]}/{prefabId}.prefab");
            if (prefab != null)
                return prefab;
        }

        return null;
    }

    private static bool IsUnderAnyRoot(string assetPath, IReadOnlyList<string> roots)
    {
        for (int i = 0; i < roots.Count; i++)
        {
            if (assetPath.StartsWith(roots[i] + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string MakeSafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            value = value.Replace(invalid[i], '_');
        return value;
    }
}

public sealed class BuildingSummonerPrefabPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        BuildingSummonerPrefabGenerator.QueueImportedBuildings(importedAssets);
        BuildingSummonerPrefabGenerator.QueueImportedBuildings(movedAssets);
    }
}
