// AI-Context: 设置菜单的区块流送性能入口；模式写入 PlayerPrefs，并立即同步 ChunkMgr 调度器。
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>打开区块流送性能设置，并在自动、流畅和高吞吐模式之间切换。</summary>
[DisallowMultipleComponent]
public sealed class WorldStreamingSettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "流送性能";

    private static readonly List<string> ModeLabels = new()
    {
        "自动（推荐）",
        "流畅优先（单后台线程）",
        "高吞吐（安全多线程）"
    };

    private Button entryButton;
    private BasePanel settingsPanel;
    private TMP_Dropdown modeDropdown;
    private TextMeshProUGUI statusText;

    public static WorldStreamingSettingsPanelLauncher Ensure(Transform settingsRoot)
    {
        if (settingsRoot == null)
            return null;
        WorldStreamingSettingsPanelLauncher launcher =
            settingsRoot.GetComponent<WorldStreamingSettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsRoot.gameObject.AddComponent<WorldStreamingSettingsPanelLauncher>();
        launcher.EnsureEntryButton();
        return launcher;
    }

    #region 窗口生命周期

    private void EnsureEntryButton()
    {
        if (entryButton != null)
            return;
        entryButton = FindButton(transform, EntryButtonName);
        if (entryButton == null)
        {
            Debug.LogError($"[WorldStreamingSettings] Prefab 缺少入口按钮“{EntryButtonName}”。", this);
            return;
        }
        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

    private void Open()
    {
        EnsureWindow();
        if (settingsPanel == null)
            return;
        modeDropdown?.SetValueWithoutNotify((int)WorldStreamingPreferences.Mode);
        RefreshStatus();
        settingsPanel.Open();
        settingsPanel.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(settingsPanel.rectTransform);
    }

    private void EnsureWindow()
    {
        if (settingsPanel != null)
            return;
        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.WorldStreamingSettings);
        if (prefab == null)
        {
            Debug.LogError($"[WorldStreamingSettings] 缺少 Prefab：{RuntimeUIPrefabKeys.WorldStreamingSettings}。", this);
            return;
        }

        settingsPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab, RuntimeUIPrefabKeys.WorldStreamingSettings);
        modeDropdown = settingsPanel.GetComponentInChildren<TMP_Dropdown>(true);
        statusText = settingsPanel.GetText("状态文本");
        settingsPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("取消按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("应用按钮")?.onClick.AddListener(Apply);
        if (modeDropdown != null)
        {
            modeDropdown.ClearOptions();
            modeDropdown.AddOptions(new List<string>
            {
                FlatWorldLocalizationService.GetUiText(ModeLabels[0]),
                FlatWorldLocalizationService.GetUiText(ModeLabels[1]),
                FlatWorldLocalizationService.GetUiText(ModeLabels[2])
            });
        }
        else
        {
            Debug.LogError("[WorldStreamingSettings] Prefab 缺少性能模式下拉列表。", settingsPanel);
        }
        settingsPanel.PrepareForGamepadNavigation();
        settingsPanel.Close();
    }

    private void Apply()
    {
        if (modeDropdown == null)
            return;
        WorldStreamingPreferences.SetMode(
            (WorldStreamingPerformanceMode)modeDropdown.value);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (statusText == null)
            return;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        int workers = manager != null
            ? manager.EffectiveBackgroundGenerationConcurrency
            : WorldStreamingPreferences.ResolveBaseGenerationConcurrency(2);
        statusText.text = WorldStreamingPreferences.Mode switch
        {
            WorldStreamingPerformanceMode.Smooth =>
                FlatWorldLocalizationService.GetUiFormat(
                    "当前：单后台线程生成 + 主线程逐帧绘制（{0} 个生成任务并发）。",
                    workers),
            WorldStreamingPerformanceMode.Throughput =>
                FlatWorldLocalizationService.GetUiFormat(
                    "当前：安全多线程高吞吐（{0} 个生成任务并发）。",
                    workers),
            _ => FlatWorldLocalizationService.GetUiFormat(
                "当前：自动平衡（{0} 个生成任务并发）。",
                workers)
        };
    }

    private void Close() => settingsPanel?.Close();

    private void OnDestroy()
    {
        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (settingsPanel != null)
            Destroy(settingsPanel.gameObject);
    }

    #endregion

    #region 查找

    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == buttonName)
                return buttons[i];
        }
        return null;
    }

    #endregion
}
