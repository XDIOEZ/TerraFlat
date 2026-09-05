#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace FlatWorld.Editor.Diagnostics
{
    /// <summary>
    /// Addressables 编辑器目录诊断：检查标签条目数与实际可解析的位置数。
    /// 只创建独立的 Fast Mode 目录视图，不加载资源，不修改运行时实例；
    /// Play 会话的静态状态统一由 Unity Domain Reload 重建。
    /// </summary>
    internal static class AddressablesBootstrapProbe
    {
        #region 常量

        private const string LogPrefix = "[AddressablesBootstrap]";

        #endregion

        #region 静态目录诊断

        /// <summary>只检查编辑器目录及标签解析，不加载 Prefab、不进入 Play Mode。</summary>
        [MenuItem("FlatWorld/诊断/检查 Addressables 目录")]
        private static void InspectEditorCatalog()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Addressables 默认 Settings 资源不存在。");

            Debug.Log($"{LogPrefix} 播放配置：optionsEnabled={EditorSettings.enterPlayModeOptionsEnabled}, " +
                      $"options={EditorSettings.enterPlayModeOptions}");

            Type locatorType = typeof(AddressableAssetSettings).Assembly.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsLocator", true);
            IResourceLocator locator = (IResourceLocator)Activator.CreateInstance(locatorType, new object[] { settings });
            try
            {
                foreach (string label in new[] { "Prefab", "ItemPrefab", "TileBlock", "ItemSprite" })
                {
                    Type assetType = label == "Prefab" || label == "ItemPrefab" ? typeof(GameObject) : null;
                    bool found = locator.Locate(label, assetType, out IList<IResourceLocation> locations);
                    int entryCount = settings.groups.Where(group => group != null)
                        .Sum(group => group.entries.Count(entry => entry.labels.Contains(label)));
                    Debug.Log($"{LogPrefix} 静态目录：label={label}, entries={entryCount}, " +
                              $"locations={(found ? locations.Count : 0)}, builder={settings.ActivePlayModeDataBuilder?.Name}");
                }
            }
            finally
            {
                MethodInfo callback = locatorType.GetMethod("Settings_OnModification", BindingFlags.Instance | BindingFlags.NonPublic);
                settings.OnModification -= (Action<AddressableAssetSettings, AddressableAssetSettings.ModificationEvent, object>)
                    Delegate.CreateDelegate(typeof(Action<AddressableAssetSettings, AddressableAssetSettings.ModificationEvent, object>), locator, callback);
            }
        }

        #endregion

    }
}

#endif
