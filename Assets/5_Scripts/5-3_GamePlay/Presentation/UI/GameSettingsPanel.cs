using System.Globalization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定游戏设置分页，主菜单和游戏内共用新世界默认参数。</summary>
[DisallowMultipleComponent]
public sealed class GameSettingsPanel : MonoBehaviour, ISettingsPageLifecycle
{
    #region 控件契约与状态

    public const string WidthSliderName = "默认区块宽度";
    public const string HeightSliderName = "默认区块高度";
    public const string Description = "用于新建世界的默认区块尺寸；不会改变已有存档。宽、高均可设置为 1–256 格。";
    private Slider widthSlider;
    private Slider heightSlider;
    private TextMeshProUGUI widthValue;
    private TextMeshProUGUI heightValue;
    private Button resetButton;
    private ISettingsProvider provider;
    private ISettingsSlider widthSetting;
    private ISettingsSlider heightSetting;
    private bool initialized;

    #endregion

    #region 初始化与生命周期

    private void Awake() => Initialize();

    /// <summary>只绑定正式 Prefab 中的控件，分页隐藏时也可由生命周期入口初始化。</summary>
    private void Initialize()
    {
        if (initialized)
            return;

        provider = NewWorldUserSettings.SettingsProvider;
        widthSetting = provider.GetSlider(NewWorldUserSettings.ChunkWidthSettingKey);
        heightSetting = provider.GetSlider(NewWorldUserSettings.ChunkHeightSettingKey);
        widthSlider = FindComponent<Slider>(WidthSliderName);
        heightSlider = FindComponent<Slider>(HeightSliderName);
        widthValue = FindComponent<TextMeshProUGUI>(WidthSliderName + "数值");
        heightValue = FindComponent<TextMeshProUGUI>(HeightSliderName + "数值");
        resetButton = FindComponent<Button>("恢复默认按钮");
        initialized = true;

        if (widthSlider == null || heightSlider == null || widthValue == null || heightValue == null || resetButton == null)
        {
            Debug.LogError("[GameSettingsPanel] 游戏设置分页缺少默认区块尺寸控件。", this);
            return;
        }

        ConfigureSlider(widthSlider, widthSetting);
        ConfigureSlider(heightSlider, heightSetting);
        widthSlider.onValueChanged.AddListener(OnWidthChanged);
        heightSlider.onValueChanged.AddListener(OnHeightChanged);
        resetButton.onClick.AddListener(ResetToDefaults);
        RefreshValues();
    }

    public void OnSettingsPageShown()
    {
        Initialize();
        RefreshValues();
    }

    public void OnSettingsPageHidden() { }

    private void OnDestroy()
    {
        widthSlider?.onValueChanged.RemoveListener(OnWidthChanged);
        heightSlider?.onValueChanged.RemoveListener(OnHeightChanged);
        resetButton?.onClick.RemoveListener(ResetToDefaults);
    }

    #endregion

    #region 设置读写

    private static void ConfigureSlider(Slider slider, ISettingsSlider setting)
    {
        slider.minValue = setting.MinValue;
        slider.maxValue = setting.MaxValue;
        slider.wholeNumbers = true;
    }

    private void OnWidthChanged(float value)
    {
        widthSetting.SetValue(value);
        RefreshValues();
    }

    private void OnHeightChanged(float value)
    {
        heightSetting.SetValue(value);
        RefreshValues();
    }

    private void ResetToDefaults()
    {
        provider.ResetToDefaults();
        RefreshValues();
    }

    /// <summary>每次切回页面时读取权威值，避免取消设置后仍显示旧值。</summary>
    private void RefreshValues()
    {
        if (widthSlider == null || heightSlider == null || widthValue == null || heightValue == null)
            return;

        widthSlider.SetValueWithoutNotify(widthSetting.Value);
        heightSlider.SetValueWithoutNotify(heightSetting.Value);
        widthValue.text = Mathf.RoundToInt(widthSetting.Value).ToString(CultureInfo.InvariantCulture);
        heightValue.text = Mathf.RoundToInt(heightSetting.Value).ToString(CultureInfo.InvariantCulture);
    }

    private T FindComponent<T>(string objectName) where T : Component
    {
        foreach (T component in GetComponentsInChildren<T>(true))
            if (component.name == objectName)
                return component;
        return null;
    }

    #endregion
}
