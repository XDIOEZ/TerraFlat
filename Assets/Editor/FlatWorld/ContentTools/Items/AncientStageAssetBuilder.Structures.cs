using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static partial class AncientStageAssetBuilder
{
    #region 材质墙与平台
    /// <summary>创建五类平台、三种金属墙及支撑面表现节点。</summary>
    private static void BuildStructures()
    {
        WaterPlatformAssetBuilder.Build();
        Color stone = new(0.65f, 0.68f, 0.72f);
        Color copper = new(0.95f, 0.53f, 0.30f);
        Color iron = new(0.72f, 0.79f, 0.86f);
        Color bronze = new(0.85f, 0.71f, 0.32f);
        CreateStructureTile(15, "Stone", "石", stone, true, 0f);
        CreateStructureTile(16, "Copper", "铜", copper, true, 0f);
        CreateStructureTile(17, "Iron", "铁", iron, true, 0f);
        CreateStructureTile(18, "Bronze", "青铜", bronze, true, 0f);
        CreateStructureTile(19, "Copper", "铜", copper, false, 280f);
        CreateStructureTile(20, "Iron", "铁", iron, false, 360f);
        CreateStructureTile(21, "Bronze", "青铜", bronze, false, 320f);
        BindSupportRenderer();
    }

    /// <summary>材质只改变数据与颜色，共用墙和平台规则。</summary>
    private static void CreateStructureTile(int runtimeId, string materialId, string label, Color tint, bool platform, float health)
    {
        string id = platform ? $"Tile_Platform_{materialId}" : $"Tile_Wall_{materialId}";
        string blockPath = $"Assets/4_ScriptObjects/World/Tiles/{id}.asset";
        string tilePath = $"Assets/7_Tiles/Base/{id}.asset";
        Tile_Block source = AssetDatabase.LoadAssetAtPath<Tile_Block>(platform
            ? "Assets/4_ScriptObjects/World/Tiles/Tile_WaterPlatform.asset"
            : "Assets/4_ScriptObjects/World/Tiles/TileBase_BuiltStoneWall.asset");
        TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(tilePath);
        if (tile == null)
        {
            tile = UnityEngine.Object.Instantiate(source.TileBase);
            AssetDatabase.CreateAsset(tile, tilePath);
            SerializedObject tileSettings = new(tile);
            SerializedProperty color = tileSettings.FindProperty("m_Color");
            if (color != null) { color.colorValue = tint; tileSettings.ApplyModifiedPropertiesWithoutUndo(); }
        }
        Tile_Block block = AssetDatabase.LoadAssetAtPath<Tile_Block>(blockPath);
        if (block == null)
        {
            block = UnityEngine.Object.Instantiate(source);
            AssetDatabase.CreateAsset(block, blockPath);
        }
        block.tileItemName = id;
        block.displayName = label + (platform ? "平台" : "墙");
        block.tileDataTemplate.ID = id;
        block.tileDataTemplate.Name = id;
        block.TileBase = tile;
        if (platform)
            block.groundPlacement = new GroundTilePlacementRule { RefundItemId = $"Platform_{materialId}_Summoner" };
        else
        {
            block.damageProfile.MaxHealth = health;
            block.damageProfile.DropItemId = $"Wall_{materialId}_Summoner";
        }
        EditorUtility.SetDirty(block);
        Register(blockPath, "TileBlock");
        RegisterTileMapping(runtimeId, id, tile);
    }

    /// <summary>同时登记地块表现和地表、矿洞的玩法地址。</summary>
    private static void RegisterTileMapping(int runtimeId, string id, TileBase tile)
    {
        var palette = new SerializedObject(AssetDatabase.LoadAssetAtPath<ChunkTilePaletteSO>(
            "Assets/Resources/Config/WorldModel/ChunkTilePalette_Default.asset"));
        SerializedProperty entries = palette.FindProperty("entries");
        SerializedProperty match = null;
        for (int i = 0; i < entries.arraySize; i++)
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("TileId").intValue == runtimeId)
                match = entries.GetArrayElementAtIndex(i);
        if (match != null && match.FindPropertyRelative("Tile").objectReferenceValue != tile)
            throw new InvalidOperationException($"地块编号冲突：{runtimeId}");
        match ??= entries.GetArrayElementAtIndex(entries.arraySize++);
        match.FindPropertyRelative("TileId").intValue = runtimeId;
        match.FindPropertyRelative("Tile").objectReferenceValue = tile;
        palette.ApplyModifiedPropertiesWithoutUndo();
        foreach (string dimension in new[] { "Surface", "Cave" })
        {
            var profile = new SerializedObject(AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                $"Assets/Resources/Config/WorldModel/ChunkGenerationProfile_{dimension}.asset"));
            SerializedProperty values = profile.FindProperty("textParameters");
            string key = $"tile.block.{runtimeId}";
            SerializedProperty found = null;
            for (int i = 0; i < values.arraySize; i++)
                if (values.GetArrayElementAtIndex(i).FindPropertyRelative("Id").stringValue == key)
                    found = values.GetArrayElementAtIndex(i);
            if (found != null && found.FindPropertyRelative("Value").stringValue != id)
                throw new InvalidOperationException($"{dimension} 地块地址冲突：{key}");
            found ??= values.GetArrayElementAtIndex(values.arraySize++);
            found.FindPropertyRelative("Id").stringValue = key;
            found.FindPropertyRelative("Value").stringValue = id;
            profile.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>把独立平台图层正式保存到区块资源，排序位于水面上、建筑下。</summary>
    private static void BindSupportRenderer()
    {
        const string path = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform node = root.transform.Find("Support");
            if (node == null)
            {
                GameObject created = new("Support", typeof(Tilemap), typeof(TilemapRenderer));
                created.transform.SetParent(root.transform, false);
                node = created.transform;
            }
            TilemapRenderer ground = root.transform.Find("Ground").GetComponent<TilemapRenderer>();
            TilemapRenderer renderer = node.GetComponent<TilemapRenderer>();
            renderer.sharedMaterial = ground.sharedMaterial;
            renderer.sortingLayerID = ground.sortingLayerID;
            renderer.sortingOrder = 2;
            var component = root.GetComponent<ChunkSupportSurfaceRenderer>() ?? root.AddComponent<ChunkSupportSurfaceRenderer>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("palette").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ChunkTilePaletteSO>(
                "Assets/Resources/Config/WorldModel/ChunkTilePalette_Default.asset");
            serialized.FindProperty("tilemap").objectReferenceValue = node.GetComponent<Tilemap>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    #endregion
}
