using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 为可放置建筑和工具本体生成对应召唤器 Prefab。
/// 生成器通过稳定的本地对象 ID 修改 YAML，兼容目录移动及嵌套 Module_Building Prefab，
/// 同时保留已有召唤器的 meta/GUID，避免配方、存档和资源引用失效。
/// </summary>
public static class BuildingSummonerPrefabGenerator
{
    public const string BuildingRoot = "Assets/2_Prefabs/World/Buildings";
    public const string SummonerRoot = BuildingRoot + "/Summoners";
    public const string ToolRoot = "Assets/2_Prefabs/Gameplay/Items/Tools";
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
        // Play Mode 的运行时资源加载可能触发 Prefab 导入；此时禁止生成器改写资源。
        if (_running || EditorApplication.isPlayingOrWillChangePlaymode || importedPaths == null)
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
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            PendingBuildingPaths.Clear();
            return;
        }

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
        buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(buildingPath);
        summonerPrefab = null;
        if (buildingPrefab == null)
            throw new InvalidOperationException($"无法加载建筑 Prefab：{buildingPath}");

        Item buildingItem = buildingPrefab.GetComponent<Item>();
        Mod_Building buildingModule = buildingPrefab.GetComponentInChildren<Mod_Building>(true);
        if (buildingItem?.itemData == null || buildingModule == null)
            throw new InvalidOperationException($"建筑 Prefab 缺少 Item、ItemData 或 Mod_Building：{buildingPath}");

        string buildingId = string.IsNullOrWhiteSpace(buildingItem.itemData.IDName)
            ? Path.GetFileNameWithoutExtension(buildingPath)
            : Mod_Building.GetBuildingPrefabId(buildingItem.itemData.IDName);
        string summonerId = Mod_Building.GetSummonerPrefabId(buildingId);

        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(buildingPrefab, out _, out long rootFileId) ||
            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(buildingItem, out _, out long itemFileId))
        {
            throw new InvalidOperationException($"无法读取建筑 Prefab 根节点或 Item 的本地对象 ID：{buildingPath}");
        }

        ResolveModuleSerializationTarget(
            buildingPath,
            buildingModule,
            out string moduleSourceGuid,
            out long moduleFileId);

        bool changed = PatchPrefabYaml(
            buildingPath,
            rootFileId,
            itemFileId,
            moduleFileId,
            moduleSourceGuid,
            buildingId,
            summonerId,
            BuildingRole.PlacedBuilding);
        if (changed)
        {
            AssetDatabase.ImportAsset(
                buildingPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(buildingPath);
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

        changed |= PatchPrefabYaml(
            summonerPath,
            rootFileId,
            itemFileId,
            moduleFileId,
            moduleSourceGuid,
            buildingId,
            summonerId,
            BuildingRole.Summoner);
        AssetDatabase.ImportAsset(
            summonerPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        summonerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(summonerPath);
        return changed;
    }

    #region Prefab YAML 序列化

    /// <summary>解析 Mod_Building 在当前 Prefab YAML 中对应的直接对象或嵌套源对象。</summary>
    private static void ResolveModuleSerializationTarget(
        string prefabPath,
        Mod_Building buildingModule,
        out string moduleSourceGuid,
        out long moduleFileId)
    {
        UnityEngine.Object moduleSource = PrefabUtility.GetCorrespondingObjectFromSource(buildingModule);
        if (moduleSource != null &&
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                moduleSource,
                out moduleSourceGuid,
                out moduleFileId))
        {
            return;
        }

        moduleSourceGuid = null;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(buildingModule, out _, out moduleFileId))
            throw new InvalidOperationException($"无法读取 Mod_Building 的本地对象 ID：{prefabPath}");
    }

    /// <summary>按角色写入建筑本体或召唤器数据，并保留资源 GUID。</summary>
    private static bool PatchPrefabYaml(
        string prefabPath,
        long rootFileId,
        long itemFileId,
        long moduleFileId,
        string moduleSourceGuid,
        string buildingId,
        string summonerId,
        BuildingRole role)
    {
        string fullPath = Path.GetFullPath(prefabPath);
        string yaml = File.ReadAllText(fullPath, Encoding.UTF8);
        string originalYaml = yaml;
        string itemId = role == BuildingRole.Summoner ? summonerId : buildingId;
        string roleValue = role == BuildingRole.Summoner ? "0" : "1";

        yaml = ReplaceObjectField(yaml, rootFileId, "  m_Name:", ToYamlString(itemId));
        yaml = ReplaceObjectField(yaml, itemFileId, "    IDName:", ToYamlString(itemId));
        yaml = ReplaceObjectField(
            yaml,
            itemFileId,
            "      CanBePickedUp:",
            role == BuildingRole.Summoner ? "1" : "0");
        yaml = ReplaceObjectField(yaml, itemFileId, "    inHand:", "0");

        Mod_Building.Building_Data state = new()
        {
            Version = Mod_Building.CurrentBuildingDataVersion,
            State = BuildingState.NotInstalled,
            Role = role,
            SnapshotBase64 = null,
            BuildingPrefabId = buildingId,
            SummonerPrefabId = summonerId,
            TileBlockId = Mod_Building.GetDefaultTileBlockId(buildingId)
        };
        Ex_ModData container = new();
        container.WriteData(state);
        string yamlJson = $"'{container.BitData.Replace("'", "''")}'";

        if (!string.IsNullOrWhiteSpace(moduleSourceGuid))
        {
            UpsertPrefabOverrideValue(
                ref yaml,
                moduleSourceGuid,
                moduleFileId,
                "Data.Role",
                roleValue);
            UpsertPrefabOverrideValue(
                ref yaml,
                moduleSourceGuid,
                moduleFileId,
                "Data.Version",
                Mod_Building.CurrentBuildingDataVersion.ToString());
            UpsertPrefabOverrideValue(
                ref yaml,
                moduleSourceGuid,
                moduleFileId,
                "Data.BuildingPrefabId",
                ToYamlString(buildingId));
            UpsertPrefabOverrideValue(
                ref yaml,
                moduleSourceGuid,
                moduleFileId,
                "Data.SummonerPrefabId",
                ToYamlString(summonerId));
            UpsertPrefabOverrideValue(
                ref yaml,
                moduleSourceGuid,
                moduleFileId,
                "BuildingData.BitData",
                yamlJson);
        }
        else
        {
            yaml = ReplaceObjectField(
                yaml,
                moduleFileId,
                "    Version:",
                Mod_Building.CurrentBuildingDataVersion.ToString());
            yaml = ReplaceObjectField(yaml, moduleFileId, "    Role:", roleValue);
            yaml = ReplaceObjectField(
                yaml,
                moduleFileId,
                "    BuildingPrefabId:",
                ToYamlString(buildingId));
            yaml = ReplaceObjectField(
                yaml,
                moduleFileId,
                "    SummonerPrefabId:",
                ToYamlString(summonerId));
            yaml = ReplaceObjectField(yaml, moduleFileId, "    BitData:", yamlJson);
        }

        if (string.Equals(originalYaml, yaml, StringComparison.Ordinal))
            return false;

        File.WriteAllText(fullPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    /// <summary>更新已有覆盖项；不存在时写入对应嵌套 PrefabInstance。</summary>
    private static void UpsertPrefabOverrideValue(
        ref string yaml,
        string targetGuid,
        long targetFileId,
        string propertyPath,
        string value)
    {
        string pattern =
            $@"(?m)^    - target: \{{fileID: {targetFileId}, guid: {Regex.Escape(targetGuid)}, type: 3\}}\r?\n" +
            $@"      propertyPath: {Regex.Escape(propertyPath)}\r?\n" +
            @"(?<valueLine>      value:.*)\r?$";
        MatchCollection matches = Regex.Matches(yaml, pattern, RegexOptions.CultureInvariant);
        if (matches.Count > 1)
            throw new InvalidOperationException($"Prefab 中存在重复覆盖字段：{propertyPath}");

        if (matches.Count == 1)
        {
            Group valueLine = matches[0].Groups["valueLine"];
            yaml = yaml.Remove(valueLine.Index, valueLine.Length)
                .Insert(valueLine.Index, $"      value: {value}");
            return;
        }

        InsertPrefabOverrideValue(
            ref yaml,
            targetGuid,
            targetFileId,
            propertyPath,
            value);
    }

    /// <summary>将新覆盖项插入唯一匹配的嵌套 PrefabInstance。</summary>
    private static void InsertPrefabOverrideValue(
        ref string yaml,
        string targetGuid,
        long targetFileId,
        string propertyPath,
        string value)
    {
        string sourceMarker = $"  m_SourcePrefab: {{fileID: 100100000, guid: {targetGuid}, type: 3}}";
        int sourceIndex = yaml.IndexOf(sourceMarker, StringComparison.Ordinal);
        if (sourceIndex < 0 ||
            yaml.IndexOf(sourceMarker, sourceIndex + sourceMarker.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Prefab 中无法唯一定位嵌套资源：{targetGuid}");
        }

        int blockStart = yaml.LastIndexOf("--- !u!1001 &", sourceIndex, StringComparison.Ordinal);
        int blockEnd = yaml.IndexOf("\n--- !u!", sourceIndex, StringComparison.Ordinal);
        if (blockStart < 0)
            throw new InvalidOperationException($"Prefab 嵌套资源缺少 PrefabInstance：{targetGuid}");
        if (blockEnd < 0)
            blockEnd = yaml.Length;

        const string modificationsMarker = "    m_Modifications:";
        int modificationsIndex = yaml.IndexOf(
            modificationsMarker,
            blockStart,
            blockEnd - blockStart,
            StringComparison.Ordinal);
        if (modificationsIndex < 0)
            throw new InvalidOperationException($"Prefab 嵌套资源缺少修改列表：{targetGuid}");

        int markerLineEnd = yaml.IndexOf('\n', modificationsIndex);
        if (markerLineEnd < 0 || markerLineEnd >= blockEnd)
            throw new InvalidOperationException($"Prefab 嵌套资源修改列表格式异常：{targetGuid}");

        string newline = yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string entry =
            $"    - target: {{fileID: {targetFileId}, guid: {targetGuid}, type: 3}}{newline}" +
            $"      propertyPath: {propertyPath}{newline}" +
            $"      value: {value}{newline}" +
            $"      objectReference: {{fileID: 0}}{newline}";
        yaml = yaml.Insert(markerLineEnd + 1, entry);
    }

    #endregion

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
        int updated = RecipeJsonEditorService.RelinkOutputItemId(buildingItemId, summonerItemId);
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
