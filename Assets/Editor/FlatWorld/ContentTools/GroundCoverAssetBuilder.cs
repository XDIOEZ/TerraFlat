using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 地表植被采集的正式资源装配入口。通过 Unity 序列化创建通用采集模块、绑定 ChunkView 花层，
/// 不生成美术、不重建其他世界图层、不改变场景或玩家存档。
/// </summary>
public static class GroundCoverAssetBuilder
{
    #region 资源入口

    private const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/World/Module_GroundCoverHarvest.prefab";
    private const string ChunkPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
    private const string ModuleAddress = "Module_GroundCoverHarvest";

    /// <summary>只装配采集模块与无碰撞体的花层，并保存实际 Addressables 分组。</summary>
    [MenuItem("FlatWorld/内容配置/装配地表植被采集资源")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请退出 Play Mode 后装配地表植被资源。");
        BuildModule();
        BindChunkLayer();
        RegisterModule();
        Debug.Log("[GroundCoverAssets] 采集模块、Flowers Tilemap 与 Addressables 已装配；花层没有 Item 或 Collider2D。");
    }

    #endregion

    #region 模块与图层

    /// <summary>创建只有工具行为和稳定模块数据的通用 Prefab。</summary>
    private static void BuildModule()
    {
        GameObject root = new(ModuleAddress);
        try
        {
            Mod_GroundCoverHarvest module = root.AddComponent<Mod_GroundCoverHarvest>();
            module.ModData.ID = Mod_GroundCoverHarvest.ModuleId;
            module.ModData.Name = "地表植被采集";
            module.ModData.isRunning = true;
            PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// <summary>在现有 Grid 下添加花层，复用草层的排序、单元锚点和风摆材质。</summary>
    private static void BindChunkLayer()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ChunkPath);
        try
        {
            ChunkGrassRenderer grass = root.GetComponent<ChunkGrassRenderer>();
            if (grass == null || root.GetComponent<Grid>() == null)
                throw new InvalidOperationException("ChunkView 缺少现有草层或 Grid。");
            SerializedObject grassData = new(grass);
            Tilemap grassTilemap = grassData.FindProperty("tilemap").objectReferenceValue as Tilemap;
            Material material = grassData.FindProperty("grassMaterial").objectReferenceValue as Material;
            if (grassTilemap == null || material == null)
                throw new InvalidOperationException("现有草层 Tilemap 或材质未配置。");

            Transform flowers = root.transform.Find("Flowers");
            GameObject layer;
            Tilemap tilemap;
            TilemapRenderer renderer;
            if (flowers == null)
            {
                // Tilemap/Renderer 一次性随对象创建，避免 Prefab Stage 中逐个 AddComponent 后拿到失效引用。
                layer = new GameObject("Flowers", typeof(Tilemap), typeof(TilemapRenderer));
                layer.transform.SetParent(root.transform, false);
                tilemap = layer.GetComponent<Tilemap>();
                renderer = layer.GetComponent<TilemapRenderer>();
            }
            else
            {
                layer = flowers.gameObject;
                tilemap = layer.GetComponent<Tilemap>();
                if (tilemap == null) tilemap = layer.AddComponent<Tilemap>();
                renderer = layer.GetComponent<TilemapRenderer>();
                if (renderer == null) renderer = layer.AddComponent<TilemapRenderer>();
            }
            if (tilemap == null || renderer == null)
                throw new InvalidOperationException("Flowers 图层无法创建 Tilemap 或 TilemapRenderer。");
            TilemapRenderer sourceRenderer = grassTilemap.GetComponent<TilemapRenderer>();
            if (sourceRenderer == null)
                throw new InvalidOperationException("现有草层缺少 TilemapRenderer。");
            tilemap.tileAnchor = grassTilemap.tileAnchor;
            renderer.sharedMaterial = material;
            // 草层会在绑定时切换到 BRG 共用的 Default；不能复制 Prefab 内遗留的 Tilemap 层。
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 0;
            renderer.mode = TilemapRenderer.Mode.Chunk;

            ChunkGroundCoverRenderer cover = root.GetComponent<ChunkGroundCoverRenderer>() ?? root.AddComponent<ChunkGroundCoverRenderer>();
            SerializedObject coverData = new(cover);
            coverData.FindProperty("tilemap").objectReferenceValue = tilemap;
            coverData.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ChunkPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>为模块登记稳定加载地址，并显式保存分组，避免条目只停留在编辑器内存。</summary>
    private static void RegisterModule()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("Addressables 尚未配置。");
        string guid = AssetDatabase.AssetPathToGUID(ModulePath);
        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = ModuleAddress;
        entry.SetLabel("Prefab", true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
        EditorUtility.SetDirty(entry.parentGroup);
        AssetDatabase.SaveAssetIfDirty(entry.parentGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    #endregion
}
