using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 地表植被采集的正式资源装配入口。通过 Unity 序列化创建通用采集模块并绑定 ChunkView BRG 花层，
/// 不生成美术、不重建其他世界图层、不改变场景或玩家存档。
/// </summary>
public static class GroundCoverAssetBuilder
{
    #region 资源入口

    private const string ModulePath = "Assets/2_Prefabs/Gameplay/Modules/World/Module_GroundCoverHarvest.prefab";
    private const string ChunkPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
    private const string ModuleAddress = "Module_GroundCoverHarvest";

    /// <summary>只装配采集模块与无 Tilemap/碰撞体的 BRG 花层，并保存实际 Addressables 分组。</summary>
    [MenuItem("FlatWorld/内容配置/装配地表植被采集资源")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请退出 Play Mode 后装配地表植被资源。");
        BuildModule();
        BindChunkLayer();
        RegisterModule();
        Debug.Log("[GroundCoverAssets] 采集模块、Flowers BRG 图层与 Addressables 已装配；花层没有 Tilemap、Item 或 Collider2D。");
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

    /// <summary>绑定无 Tilemap 的花表现器，复用草层材质与 Default/0 BRG 排序域。</summary>
    private static void BindChunkLayer()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ChunkPath);
        try
        {
            ChunkGrassRenderer grass = root.GetComponent<ChunkGrassRenderer>();
            Material material = grass != null ? grass.GrassMaterial : null;
            if (material == null)
                throw new InvalidOperationException("ChunkView 缺少现有草层或 BRG 材质。");

            Transform legacyFlowers = root.transform.Find("Flowers");
            if (legacyFlowers != null)
                UnityEngine.Object.DestroyImmediate(legacyFlowers.gameObject);
            ChunkGroundCoverRenderer cover = root.GetComponent<ChunkGroundCoverRenderer>() ?? root.AddComponent<ChunkGroundCoverRenderer>();
            SerializedObject coverData = new(cover);
            coverData.FindProperty("groundCoverMaterial").objectReferenceValue = material;
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
