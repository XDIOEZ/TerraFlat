using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 装配通用温度产量模块 Prefab 并登记稳定 Addressables 地址。
/// 只提供模块外壳，产物 ID 与温度曲线由具体物品 JSON 配置，不保存松树专属参数。
/// </summary>
public static class ResourceYieldAssetBuilder
{
    #region 资源地址

    private const string PrefabPath = "Assets/2_Prefabs/Gameplay/Modules/World/Module_TemperatureYield.prefab";

    #endregion

    #region 模块装配

    /// <summary>通过 Unity 序列化生成通用模块；重复执行只检查已有模块并确保登记。</summary>
    [MenuItem("FlatWorld/内容配置/装配温度产量模块")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式再装配温度产量模块。");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("Addressables 尚未初始化。");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            GameObject root = new("Module_TemperatureYield");
            try
            {
                Mod_TemperatureYield module = root.AddComponent<Mod_TemperatureYield>();
                module.ModData.ID = Mod_TemperatureYield.ModuleId;
                module.ModData.Name = "Module_TemperatureYield";
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        if (prefab == null || prefab.GetComponent<Mod_TemperatureYield>() == null)
            throw new InvalidOperationException($"温度产量模块 Prefab 无效：{PrefabPath}");

        string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ??
            settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = PrefabPath;
        entry.SetLabel("Prefab", true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
        AssetDatabase.SaveAssetIfDirty(entry.parentGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log("[ResourceYield] 通用温度产量模块已装配并登记。");
    }

    #endregion
}
