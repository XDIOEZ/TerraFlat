#if UNITY_EDITOR

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FlatWorld.Editor.Diagnostics
{
    /// <summary>
    /// 同步外部修改的 Addressables YAML 与运行中的 Fast Mode Locator。
    /// AssetDatabase 导入不会自动触发 Settings.OnModification，因此必须显式失效 Locator 的键索引。
    /// </summary>
    internal sealed class AddressablesCatalogRefreshPostprocessor : AssetPostprocessor
    {
        #region Addressables 目录同步

        private const string AddressablesRoot = "Assets/AddressableAssetsData/";

        /// <summary>Addressables 目录发生导入、删除或移动时通知当前 Settings 的监听者重建索引。</summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!ContainsAddressablesPath(importedAssets) &&
                !ContainsAddressablesPath(deletedAssets) &&
                !ContainsAddressablesPath(movedAssets) &&
                !ContainsAddressablesPath(movedFromAssetPaths))
            {
                return;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[AddressablesHotReload] Addressables 目录已变化，但默认 Settings 不存在。");
                return;
            }

            // 不再次写盘；只补发事件，使 Fast Mode 的 AddressableAssetSettingsLocator 丢弃旧键缓存。
            settings.SetDirty(
                AddressableAssetSettings.ModificationEvent.BatchModification,
                null,
                postEvent: true,
                settingsModified: false);
            Debug.Log("[AddressablesHotReload] 已刷新 Fast Mode 目录索引。");
        }

        /// <summary>判断本轮 AssetDatabase 变化是否涉及 Addressables 配置目录。</summary>
        private static bool ContainsAddressablesPath(string[] paths)
        {
            return paths != null && paths.Any(path =>
                !string.IsNullOrWhiteSpace(path) &&
                path.StartsWith(AddressablesRoot, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}

#endif
