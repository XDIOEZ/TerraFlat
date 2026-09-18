using FlatWorld.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置页中的运行时调试控制器；只通过 Settings Provider 读写偏好，
/// 不直接持有日志悬浮窗实例或 Prefab。
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeDebugSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    #region 绑定状态

    private Toggle overlayToggle;
    private ISettingsToggle overlaySetting;
    private bool initialized;

    #endregion

    #region 页面绑定

    /// <summary>在指定调试分页上复用或挂载设置控制器。</summary>
    public static RuntimeDebugSettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null)
            return null;

        RuntimeDebugSettingsPanelLauncher launcher =
            pageRoot.GetComponent<RuntimeDebugSettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<RuntimeDebugSettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    /// <summary>解析正式 Prefab 控件并绑定日志悬浮窗设置。</summary>
    private void Initialize()
    {
        if (initialized)
            return;

        overlaySetting = RuntimeDebugOverlayPreferences.SettingsProvider.GetToggle(
            RuntimeDebugOverlayPreferences.OverlayEnabledSettingKey);
        overlayToggle = FindComponent<Toggle>(transform, "日志悬浮窗开关");
        if (overlaySetting == null || overlayToggle == null)
        {
            Debug.LogError("[RuntimeDebugSettings] 调试设置页控件或 Provider 契约不完整。", this);
            return;
        }

        overlayToggle.onValueChanged.AddListener(OnOverlayToggleChanged);
        initialized = true;
        RefreshValue();
    }

    private void OnEnable()
    {
        RuntimeDebugOverlayPreferences.Changed += RefreshValue;
        if (initialized)
            RefreshValue();
    }

    private void OnDisable() => RuntimeDebugOverlayPreferences.Changed -= RefreshValue;

    /// <summary>把玩家选择提交给通用设置契约。</summary>
    private void OnOverlayToggleChanged(bool enabled)
    {
        overlaySetting?.SetValue(enabled);
        RefreshValue();
    }

    /// <summary>从权威偏好回填 Toggle，避免外部恢复默认后显示过期。</summary>
    private void RefreshValue()
    {
        if (overlayToggle != null && overlaySetting != null)
            overlayToggle.SetIsOnWithoutNotify(overlaySetting.Value);
    }

    public void OnSettingsPageShown() => RefreshValue();
    public void OnSettingsPageHidden() { }

    private void OnDestroy()
    {
        RuntimeDebugOverlayPreferences.Changed -= RefreshValue;
        overlayToggle?.onValueChanged.RemoveListener(OnOverlayToggleChanged);
    }

    /// <summary>按稳定节点名在当前分页内部查找组件。</summary>
    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        if (root == null)
            return null;

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] != null && components[index].name == objectName)
                return components[index];
        }

        return null;
    }

    #endregion
}
