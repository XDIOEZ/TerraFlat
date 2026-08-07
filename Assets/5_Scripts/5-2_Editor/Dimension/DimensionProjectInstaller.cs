#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class DimensionProjectInstaller
{
    private const string CatalogDirectory = "Assets/Resources/Config";
    private const string CatalogPath = CatalogDirectory + "/DimensionCatalog_Default.asset";
    private const string WallStonePath = "Assets/2_Prefabs/Building/Wall_Stone.prefab";
    private const string MineEntrancePath = "Assets/2_Prefabs/Building/MineEntrance.prefab";
    private const string CaveExitPath = "Assets/2_Prefabs/Dimension/CaveExit.prefab";
    private const long PortalComponentId = 900000000000000001;
    private const long InteractionColliderId = 900000000000000002;

    [MenuItem("FlatWorld/Dimension/Install Default Catalog")]
    public static void InstallDefaultCatalog()
    {
        Directory.CreateDirectory(CatalogDirectory);
        DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<DimensionCatalogSO>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.ResetToDefaults();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[DimensionProjectInstaller] 默认维度目录已写入：{CatalogPath}");
    }

    [MenuItem("FlatWorld/Dimension/Install Mine Entrances")]
    public static void InstallMineEntrances()
    {
        CreateMineEntrance();
        if (!EditorApplication.ExecuteMenuItem("FlatWorld/建筑/生成或刷新全部建筑召唤器"))
            throw new InvalidOperationException("无法执行建筑召唤器生成器。");
        CreateCaveExit();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[DimensionProjectInstaller] 正式矿坑入口资源已写入：{MineEntrancePath}、{CaveExitPath}");
    }

    #region 地表矿坑建筑

    private static void CreateMineEntrance()
    {
        if (!File.Exists(WallStonePath))
            throw new FileNotFoundException("找不到矿坑建筑模板。", WallStonePath);

        Directory.CreateDirectory(Path.GetDirectoryName(MineEntrancePath));
        File.Copy(WallStonePath, MineEntrancePath, true);

        string yaml = File.ReadAllText(MineEntrancePath, Encoding.UTF8);
        long rootId = FindObjectId(yaml, 1, "  m_Name: Wall_Stone");
        long itemId = FindObjectId(yaml, 114, "    IDName: Wall_Stone");
        long bodyColliderId = FindRootColliderId(yaml, rootId);

        yaml = ReplaceObjectField(yaml, rootId, "  m_Name:", "MineEntrance");
        yaml = ReplaceObjectField(yaml, itemId, "    IDName:", "MineEntrance");
        yaml = ReplaceObjectField(yaml, itemId, "    GameName:", "\"矿坑入口\"");
        yaml = ReplaceObjectField(yaml, itemId, "    Description:", "\"安装后可通往地下矿洞，并在矿洞中生成对应出口。\"");
        yaml = ReplaceObjectField(yaml, itemId, "    Guid:", "194873621");
        yaml = ReplaceObjectField(yaml, bodyColliderId, "  m_Size:", "{x: 2, y: 2}");
        yaml = ReplaceFirstField(yaml, "  m_Color:", "{r: 0.28, g: 0.22, b: 0.18, a: 1}");

        yaml = yaml.Replace("\"BuildingPrefabId\":\"Wall_Stone\"", "\"BuildingPrefabId\":\"MineEntrance\"")
            .Replace("\"SummonerPrefabId\":\"Wall_Stone_Summoner\"", "\"SummonerPrefabId\":\"MineEntrance_Summoner\"")
            .Replace("      value: Wall_Stone\n", "      value: MineEntrance\n")
            .Replace("      value: Wall_Stone_Summoner\n", "      value: MineEntrance_Summoner\n");
        yaml = EnsureLootTableEmpty(yaml);
        yaml = AddComponentReference(yaml, rootId, PortalComponentId);
        yaml = AddComponentReference(yaml, rootId, InteractionColliderId);
        yaml += BuildPortalComponentYaml(rootId, PortalComponentId, WorldAddress.CaveDimensionId, true);
        yaml += DuplicateColliderAsTrigger(yaml, bodyColliderId, InteractionColliderId, new Vector2(2.6f, 2.6f));

        File.WriteAllText(MineEntrancePath, yaml, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(
            MineEntrancePath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    }

    private static string EnsureLootTableEmpty(string yaml)
    {
        const string propertyPath = "Data.LootTable.Array.data[0].LootPrefabName";
        Match match = Regex.Match(
            yaml,
            $@"(?m)^    - target: (?<target>\{{[^\r\n]+\}})\r?\n      propertyPath: {Regex.Escape(propertyPath)}$");
        if (!match.Success || yaml.Contains("      propertyPath: Data.LootTable.Array.size"))
            return yaml;

        string modification =
            $"    - target: {match.Groups["target"].Value}\n" +
            "      propertyPath: Data.LootTable.Array.size\n" +
            "      value: 0\n" +
            "      objectReference: {fileID: 0}\n";
        return yaml.Insert(match.Index, modification);
    }

    #endregion

    #region 矿洞出口

    private static void CreateCaveExit()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CaveExitPath));
        GameObject root = new GameObject("CaveExit");
        try
        {
            int colliderLayer = LayerMask.NameToLayer("Collider");
            if (colliderLayer >= 0)
                root.layer = colliderLayer;

            GameItem item = root.AddComponent<GameItem>();
            item.BindData(new Data_GeneralItem
            {
                IDName = "CaveExit",
                GameName = "矿洞出口",
                Description = "返回与此出口绑定的地表矿坑入口。",
                Durability = 1f,
                MaxDurability = 1f,
                Tags = new List<string>(),
                Stack = new ItemStack
                {
                    Amount = 1f,
                    Volume = 1f,
                    CanBePickedUp = false
                },
                transform = new ItemTransform
                {
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    scale = Vector3.one
                },
                Guid = 194873622
            });

            BoxCollider2D interaction = root.AddComponent<BoxCollider2D>();
            interaction.isTrigger = true;
            interaction.size = new Vector2(1.8f, 1.8f);

            DimensionPortal portal = root.AddComponent<DimensionPortal>();
            portal.Configure(WorldAddress.SurfaceDimensionId, false);

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(1.45f, 1.45f, 1f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            GameObject mineStone = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Mine/Mine_Stone.prefab");
            renderer.sprite = mineStone != null
                ? mineStone.GetComponentInChildren<SpriteRenderer>(true)?.sprite
                : null;
            renderer.color = new Color(0.35f, 0.78f, 1f, 1f);
            renderer.sortingOrder = 50;
            item.Sprite = renderer;

            PrefabUtility.SaveAsPrefabAsset(root, CaveExitPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion

    #region YAML 工具

    private static long FindObjectId(string yaml, int classId, string requiredField)
    {
        MatchCollection matches = Regex.Matches(
            yaml,
            $@"(?ms)^--- !u!{classId} &(?<id>\d+).*?(?=^--- !u!|\z)");
        foreach (Match match in matches)
        {
            if (match.Value.Contains(requiredField))
                return long.Parse(match.Groups["id"].Value);
        }

        throw new InvalidOperationException($"Prefab 中找不到对象字段：{requiredField}");
    }

    private static long FindRootColliderId(string yaml, long rootId)
    {
        MatchCollection matches = Regex.Matches(yaml, @"(?ms)^--- !u!61 &(?<id>\d+).*?(?=^--- !u!|\z)");
        foreach (Match match in matches)
        {
            if (match.Value.Contains($"  m_GameObject: {{fileID: {rootId}}}") &&
                match.Value.Contains("  m_IsTrigger: 0"))
            {
                return long.Parse(match.Groups["id"].Value);
            }
        }

        throw new InvalidOperationException("矿坑模板缺少根阻挡碰撞体。");
    }

    private static string ReplaceObjectField(string yaml, long objectId, string fieldPrefix, string value)
    {
        Match block = Regex.Match(
            yaml,
            $@"(?ms)^--- !u!\d+ &{objectId}.*?(?=^--- !u!|\z)");
        if (!block.Success)
            throw new InvalidOperationException($"Prefab 中找不到对象：{objectId}");

        Match field = Regex.Match(block.Value, $@"(?m)^{Regex.Escape(fieldPrefix)}.*$");
        if (!field.Success)
            throw new InvalidOperationException($"对象 {objectId} 缺少字段：{fieldPrefix}");

        string replacement = $"{fieldPrefix} {value}";
        return yaml.Remove(block.Index + field.Index, field.Length)
            .Insert(block.Index + field.Index, replacement);
    }

    private static string ReplaceFirstField(string yaml, string fieldPrefix, string value)
    {
        Match field = Regex.Match(yaml, $@"(?m)^{Regex.Escape(fieldPrefix)}.*$");
        if (!field.Success)
            throw new InvalidOperationException($"Prefab 缺少字段：{fieldPrefix}");
        return yaml.Remove(field.Index, field.Length).Insert(field.Index, $"{fieldPrefix} {value}");
    }

    private static string AddComponentReference(string yaml, long rootId, long componentId)
    {
        Match root = Regex.Match(yaml, $@"(?ms)^--- !u!1 &{rootId}.*?(?=^--- !u!|\z)");
        if (!root.Success)
            throw new InvalidOperationException("Prefab 缺少根 GameObject。");

        int layerIndex = root.Value.IndexOf("  m_Layer:", StringComparison.Ordinal);
        if (layerIndex < 0)
            throw new InvalidOperationException("根 GameObject 缺少 m_Layer 字段。");

        return yaml.Insert(root.Index + layerIndex, $"  - component: {{fileID: {componentId}}}\n");
    }

    private static string BuildPortalComponentYaml(long rootId, long componentId, string targetDimensionId, bool requiresBuilding)
    {
        string scriptGuid = AssetDatabase.AssetPathToGUID(
            "Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionPortal.cs");
        return
            $"\n--- !u!114 &{componentId}\n" +
            "MonoBehaviour:\n" +
            "  m_ObjectHideFlags: 0\n" +
            "  m_CorrespondingSourceObject: {fileID: 0}\n" +
            "  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n" +
            $"  m_GameObject: {{fileID: {rootId}}}\n" +
            "  m_Enabled: 1\n" +
            "  m_EditorHideFlags: 0\n" +
            $"  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}\n" +
            "  m_Name: \n" +
            "  m_EditorClassIdentifier: \n" +
            $"  targetDimensionId: {targetDimensionId}\n" +
            $"  requiresInstalledBuilding: {(requiresBuilding ? 1 : 0)}\n";
    }

    private static string DuplicateColliderAsTrigger(string yaml, long sourceColliderId, long targetColliderId, Vector2 size)
    {
        Match block = Regex.Match(
            yaml,
            $@"(?ms)^--- !u!61 &{sourceColliderId}.*?(?=^--- !u!|\z)");
        if (!block.Success)
            throw new InvalidOperationException("Prefab 缺少可复制的根碰撞体。");

        string trigger = block.Value
            .Replace($"--- !u!61 &{sourceColliderId}", $"--- !u!61 &{targetColliderId}")
            .Replace("  m_IsTrigger: 0", "  m_IsTrigger: 1");
        trigger = Regex.Replace(
            trigger,
            @"(?m)^  m_Size:.*$",
            $"  m_Size: {{x: {size.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}, y: {size.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
        return "\n" + trigger.TrimEnd() + "\n";
    }

    #endregion
}
#endif
