using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>0920 资源的显式同步与静态引用检查；不进入播放、不打开存档、不自动运行。</summary>
public static class Todo0920AssetValidation
{
    #region 显式资源入口

    [MenuItem("FlatWorld/诊断/待办/0920资源同步与静态检查")]
    public static void ApplyAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请在非播放模式执行资源同步。");
        GameUIPrefabRebuilder.RebuildCraftingSelectionUI();
        ItemStepScrollRectMigration.Migrate();
        ApplyGroundCoverSorting();
        ValidateCraftingPrefabs();
        AssetDatabase.SaveAssets();
        Debug.Log("[Todo0920] PASS：制作模板、60%手工面板、逐项滚动和花层预制体静态检查通过。");
    }

    /// <summary>只更新花层排序字段，不重建花朵贴图或覆盖当前草层和 BRG 的其它改动。</summary>
    private static void ApplyGroundCoverSorting()
    {
        const string path = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ChunkGroundCoverRenderer[] covers = root.GetComponentsInChildren<ChunkGroundCoverRenderer>(true);
            Require(covers.Length > 0, "ChunkView 缺少正式花层。");
            foreach (ChunkGroundCoverRenderer cover in covers)
            {
                var serialized = new SerializedObject(cover);
                var tilemap = serialized.FindProperty("tilemap").objectReferenceValue as Tilemap;
                Require(tilemap != null, "花层缺少正式 Tilemap 引用。");
                TilemapRenderer renderer = tilemap.GetComponent<TilemapRenderer>();
                Require(renderer != null, "花层缺少 TilemapRenderer。");
                renderer.sortingLayerName = "Default";
                renderer.sortingOrder = 0;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ValidateCraftingPrefabs()
    {
        foreach (string id in new[] { "UI_HandCraftTable", "UI_MakerTable" })
        {
            string path = $"Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/{id}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(prefab != null, "缺少制作 Prefab：" + path);
            Require(prefab.GetComponentInChildren<ItemStepScrollRect>(true) != null, id + " 未接入逐项滚动。");
            RectTransform row = Find(prefab.transform, CraftingStationController.CandidateTemplateName);
            Require(row != null && !row.gameObject.activeSelf, id + " 候选模板必须隐藏。");
            RectTransform materials = Find(row, CraftingStationController.CandidateMaterialsName);
            RectTransform materialTemplate = Find(row, CraftingStationController.CandidateMaterialTemplateName);
            Require(materials != null && materialTemplate != null && !materialTemplate.gameObject.activeSelf,
                id + " 缺少隐藏材料模板。");
            Require(Find(materialTemplate, CraftingStationController.CandidateMaterialIconName).GetComponent<Image>() != null,
                id + " 材料模板缺少图标。");
            Require(Find(materialTemplate, CraftingStationController.CandidateMaterialAmountName).GetComponent<TMPro.TMP_Text>() != null,
                id + " 材料模板缺少数量。");
            var scale = new SerializedObject(prefab.GetComponent<SafeAreaScaleGroup>());
            Require(Mathf.Abs(scale.FindProperty("presentationScale").floatValue - (id == "UI_HandCraftTable" ? 0.6f : 1f)) < 0.001f,
                id + " 面板缩放值不正确。");
        }
        foreach (string path in Directory.EnumerateFiles("Assets/2_Prefabs", "*.prefab", SearchOption.AllDirectories))
            Require(!File.ReadAllText(path).Contains("guid: 1aa08ab6e0800fa44ae55d278d1423e3"),
                "仍存在未迁移的普通 ScrollRect：" + path);
    }

    private static RectTransform Find(Transform root, string name)
    {
        if (root == null) return null;
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            if (rect.name == name) return rect;
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    #endregion
}
