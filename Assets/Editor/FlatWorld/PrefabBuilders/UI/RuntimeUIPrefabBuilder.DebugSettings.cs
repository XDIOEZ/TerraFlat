using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    /// <summary>只重建调试设置页，并同步游戏内与主菜单设置分页。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Debug Settings UI")]
    public static void RebuildDebugSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        SaveDebugSettingsPrefab();
        UpdateExistingPrefab(
            MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsActionListPages);
        SaveMainMenuSettingsPrefab();
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "调试",
            "日志悬浮窗",
            "启用日志悬浮窗",
            "日志悬浮窗只影响本机调试界面；关闭后日志记录仍会继续。修改会立即保存。");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化调试设置页，并同步游戏内与主菜单设置分页。");
    }

    /// <summary>保存日志悬浮窗设置页并登记为正式 Prefab。</summary>
    private static void SaveDebugSettingsPrefab()
    {
        Directory.CreateDirectory(SettingsPanelsRoot);
        string prefabPath = SettingsPanelsRoot + RuntimeUIPrefabKeys.DebugSettings + ".prefab";
        SaveNewPrefab(prefabPath, BuildDebugSettings);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>构建客户端本地调试设置页；日志记录本身不受悬浮窗显隐影响。</summary>
    private static GameObject BuildDebugSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.DebugSettings,
            out Transform content);
        CreateSettingsHeader(content, "调试");
        CreateSettingsHint(
            content,
            "日志悬浮窗只影响本机调试界面；关闭后日志记录仍会继续。修改会立即保存。",
            54f);

        GameObject overlayRow = CreateRow("日志悬浮窗行", content, 60f);
        TextMeshProUGUI overlayLabel = CreateText(
            "日志悬浮窗标签",
            overlayRow.transform,
            "启用日志悬浮窗",
            17f,
            Cream);
        overlayLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle overlayToggle = CreateToggle("日志悬浮窗开关", overlayRow.transform);
        overlayToggle.isOn = RuntimeDebugOverlayPreferences.DefaultOverlayEnabled;
        return root;
    }
}
