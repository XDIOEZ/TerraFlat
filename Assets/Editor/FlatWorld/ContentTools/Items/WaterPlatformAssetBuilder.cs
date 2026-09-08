using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 水上平台的资源装配入口：将 16×16 木板精灵连接到 Tile、地块规则、世界映射与 Addressables。
/// TileId 13 是平台的稳定运行时编号；只登记资源，不进入游戏或修改玩家存档。
/// </summary>
public static class WaterPlatformAssetBuilder
{
    #region 配置

    private const int RuntimeTileId = 13;
    private const string TileBlockId = "Tile_WaterPlatform";
    private const string TexturePath = "Assets/6_Art/Generated/WaterPlatform/WaterPlatform_Tile.png";
    private const string TilePath = "Assets/7_Tiles/Base/Tile_WaterPlatform.asset";
    private const string BlockPath = "Assets/4_ScriptObjects/World/Tiles/Tile_WaterPlatform.asset";
    private const string PalettePath = "Assets/Resources/Config/WorldModel/ChunkTilePalette_Default.asset";

    #endregion

    #region 资源装配

    /// <summary>生成并保存水上平台全部静态资源；允许重复执行，遇到编号冲突直接报错。</summary>
    [MenuItem("FlatWorld/内容配置/装配水上平台资源")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式再装配平台资源。");

        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"平台精灵不存在：{TexturePath}");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16;
        importer.spritePivot = new Vector2(0.5f, 0.5f);
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
        if (sprite == null || sprite.rect.size != new Vector2(16, 16))
            throw new InvalidOperationException("平台运行时精灵必须为 16×16。");

        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, TilePath);
        }
        tile.sprite = sprite;
        tile.colliderType = Tile.ColliderType.None;
        EditorUtility.SetDirty(tile);

        Tile_Block block = AssetDatabase.LoadAssetAtPath<Tile_Block>(BlockPath);
        if (block == null)
        {
            block = ScriptableObject.CreateInstance<Tile_Block>();
            AssetDatabase.CreateAsset(block, BlockPath);
        }
        block.tileItemName = TileBlockId;
        block.displayName = "水上平台";
        block.TileBase = tile;
        block.tileDataTemplate = new TileData_Universal
        {
            ID = TileBlockId,
            Name = TileBlockId,
            IsWalkable = true,
            Penalty = 1000
        };
        block.groundPlacement = new GroundTilePlacementRule { RefundItemId = "WaterPlatform_Summoner" };
        block.behaviours.Clear();
        block.damageProfile = new TileBuildingDamageProfile();
        EditorUtility.SetDirty(block);

        RegisterPalette(tile);
        RegisterProfile("Surface");
        RegisterProfile("Cave");
        RegisterAddressable(TexturePath, "ItemSprite");
        RegisterAddressable(BlockPath, "TileBlock");
        AssetDatabase.SaveAssetIfDirty(tile);
        AssetDatabase.SaveAssetIfDirty(block);
        Debug.Log("[水上平台] 资源装配完成：16×16、PPU 16、Point、无压缩、TileId 13，地表/矿洞与 Addressables 已登记。");
    }

    /// <summary>登记平台的纯 TileId 到实际 Tile 的映射。</summary>
    private static void RegisterPalette(Tile tile)
    {
        ChunkTilePaletteSO palette = AssetDatabase.LoadAssetAtPath<ChunkTilePaletteSO>(PalettePath);
        SerializedObject serialized = new SerializedObject(palette);
        SerializedProperty entries = serialized.FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("TileId").intValue != RuntimeTileId)
                continue;
            if (entry.FindPropertyRelative("Tile").objectReferenceValue != tile)
                throw new InvalidOperationException($"TileId {RuntimeTileId} 已被其他地块占用。");
            return;
        }
        SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize++);
        added.FindPropertyRelative("TileId").intValue = RuntimeTileId;
        added.FindPropertyRelative("Tile").objectReferenceValue = tile;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(palette);
    }

    /// <summary>登记平台行为定义，冻结后的生成快照会携带同一映射。</summary>
    private static void RegisterProfile(string dimension)
    {
        string path = $"Assets/Resources/Config/WorldModel/ChunkGenerationProfile_{dimension}.asset";
        ChunkGenerationProfileSO profile = AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(path);
        SerializedObject serialized = new SerializedObject(profile);
        SerializedProperty entries = serialized.FindProperty("textParameters");
        string key = $"tile.block.{RuntimeTileId}";
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("Id").stringValue != key)
                continue;
            if (entry.FindPropertyRelative("Value").stringValue != TileBlockId)
                throw new InvalidOperationException($"{dimension} 已将 {key} 用于其他地块。");
            return;
        }
        SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize++);
        added.FindPropertyRelative("Id").stringValue = key;
        added.FindPropertyRelative("Value").stringValue = TileBlockId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(profile);
    }

    /// <summary>保存条目所属组，保证打包时包含本次新资源。</summary>
    private static void RegisterAddressable(string path, string label)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("Addressables 设置不存在。");
        string guid = AssetDatabase.AssetPathToGUID(path);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ??
            settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = path;
        entry.SetLabel(label, true, true);
        EditorUtility.SetDirty(entry.parentGroup);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(entry.parentGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    #endregion
}
