using FlatWorld.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 视觉特效分页的表现控制器：正式 Prefab 提供两种水体风格按钮和恢复默认入口。
/// 控件只通过 Provider 提交偏好；当前选中状态跟随设置事件刷新，不持有渲染业务。
/// </summary>
[DisallowMultipleComponent]
public sealed class VisualEffectsSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    #region Prefab 引用与状态

    [SerializeField] private Button stylizedButton;
    [SerializeField] private Button realisticButton;
    [SerializeField] private Button resetButton;
    private ISettingsProvider provider;
    private ISettingsSwitch styleSetting;

    #endregion

    #region 页面绑定

    /// <summary>页面首次显示时解析设置契约，保留正式 Prefab 的序列化控件引用。</summary>
    private void Awake()
    {
        if (stylizedButton == null || realisticButton == null || resetButton == null)
            throw new MissingReferenceException("视觉特效设置页缺少按钮引用。");

        provider = WaterVisualSettings.SettingsProvider;
        styleSetting = provider.GetSwitch(WaterVisualSettings.StyleSettingKey);
        stylizedButton.onClick.AddListener(SelectStylized);
        realisticButton.onClick.AddListener(SelectRealistic);
        resetButton.onClick.AddListener(ResetToDefaults);
    }

    /// <summary>页面激活后订阅外部设置变化并刷新当前选中项。</summary>
    private void OnEnable()
    {
        WaterVisualSettings.Changed += RefreshView;
        RefreshView();
    }

    /// <summary>页面隐藏后解除偏好订阅。</summary>
    private void OnDisable() => WaterVisualSettings.Changed -= RefreshView;

    /// <summary>通过设置契约选用风格化水面。</summary>
    private void SelectStylized() => SelectStyle(0);

    /// <summary>通过设置契约选用写实水面。</summary>
    private void SelectRealistic() => SelectStyle(1);

    /// <summary>提交风格并回填权威选中状态。</summary>
    private void SelectStyle(int index)
    {
        if (!styleSetting.TrySetSelectedIndex(index, out string error))
            Debug.LogError($"[VisualEffectsSettings] {error}", this);
        RefreshView();
    }

    /// <summary>仅恢复本页水体风格。</summary>
    private void ResetToDefaults() => provider.ResetToDefaults();

    /// <summary>用选中底色标识当前风格，两种按钮始终可导航和操作。</summary>
    private void RefreshView()
    {
        stylizedButton.targetGraphic.color = styleSetting.SelectedIndex == 0
            ? FlatWorldUITheme.Accent : FlatWorldUITheme.Surface;
        realisticButton.targetGraphic.color = styleSetting.SelectedIndex == 1
            ? FlatWorldUITheme.Accent : FlatWorldUITheme.Surface;
    }

    /// <summary>分页正式显示时与其它设置页保持统一生命周期。</summary>
    public void OnSettingsPageShown() => RefreshView();

    /// <summary>所有选择即时保存，隐藏时没有待提交草稿。</summary>
    public void OnSettingsPageHidden() { }

    /// <summary>销毁时解除本控制器的按钮监听。</summary>
    private void OnDestroy()
    {
        stylizedButton.onClick.RemoveListener(SelectStylized);
        realisticButton.onClick.RemoveListener(SelectRealistic);
        resetButton.onClick.RemoveListener(ResetToDefaults);
    }

    #endregion
}
