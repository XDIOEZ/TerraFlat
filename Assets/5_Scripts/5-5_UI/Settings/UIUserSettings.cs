// AI-Context: 全局 UI 用户偏好与 CanvasScaler 应用器；缩放通过参考分辨率实现，保持锚点语义并使用 Expand 防止宽高比导致界面出框。

using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 全局 UI 用户偏好缓存。PlayerPrefs 只在首次访问和显式写入时读取/保存，
/// 运行时消费者通过 Changed 事件更新，不在逐帧路径访问磁盘偏好。
/// </summary>
public static class UIUserSettings
{
    #region 键与默认值

    private const string ScaleKey = "FlatWorld.UI.Scale";
    private const string RespectSafeAreaKey = "FlatWorld.UI.RespectSafeArea";
    private const string FloatingMoveJoystickKey = "FlatWorld.Mobile.FloatingMoveJoystick";
    private const string EnablePinchZoomKey = "FlatWorld.Mobile.EnablePinchZoom";

    public const float DefaultScale = 1f;
    public const float MinimumScale = 0.75f;
    public const float MaximumScale = 1.2f;
    public const float ScaleStep = 0.05f;

    public const string SettingsProviderId = "ui";
    public const string ScaleSettingKey = "ui.scale";
    public const string RespectSafeAreaSettingKey = "ui.respectSafeArea";
    public const string FloatingMoveJoystickSettingKey = "ui.floatingMoveJoystick";
    public const string EnablePinchZoomSettingKey = "ui.enablePinchZoom";

    #endregion

    #region 缓存与事件

    private static bool initialized;
    private static float cachedScale = DefaultScale;
    private static bool cachedRespectSafeArea = true;
    private static bool cachedFloatingMoveJoystick = true;
    private static bool cachedEnablePinchZoom;

    /// <summary>任一 UI 偏好实际改变后广播一次。</summary>
    public static event Action Changed;

    /// <summary>移动摇杆固定/浮动偏好改变时广播，避免无关 UI 设置触发摇杆重配。</summary>
    public static event Action MobileControlsChanged;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供设置 UI 查找的 UI 偏好提供者；首次访问时自动注册。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static float Scale
    {
        get
        {
            EnsureInitialized();
            return cachedScale;
        }
    }

    public static bool RespectSafeArea
    {
        get
        {
            EnsureInitialized();
            return cachedRespectSafeArea;
        }
    }

    public static bool FloatingMoveJoystick
    {
        get
        {
            EnsureInitialized();
            return cachedFloatingMoveJoystick;
        }
    }

    public static bool EnablePinchZoom
    {
        get
        {
            EnsureInitialized();
            return cachedEnablePinchZoom;
        }
    }

    #endregion

    #region 写入入口

    public static float SetScale(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizeScale(value);
        if (Mathf.Approximately(cachedScale, sanitized))
            return cachedScale;

        cachedScale = sanitized;
        PlayerPrefs.SetFloat(ScaleKey, sanitized);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return sanitized;
    }

    public static void SetRespectSafeArea(bool value)
    {
        EnsureInitialized();
        if (cachedRespectSafeArea == value)
            return;

        cachedRespectSafeArea = value;
        PlayerPrefs.SetInt(RespectSafeAreaKey, value ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>保存左手移动摇杆模式；开启为左半屏浮动，关闭为左下角固定。</summary>
    public static void SetFloatingMoveJoystick(bool value)
    {
        EnsureInitialized();
        if (cachedFloatingMoveJoystick == value)
            return;

        cachedFloatingMoveJoystick = value;
        PlayerPrefs.SetInt(FloatingMoveJoystickKey, value ? 1 : 0);
        PlayerPrefs.Save();
        MobileControlsChanged?.Invoke();
    }

    /// <summary>保存双指缩放开关；默认关闭，避免普通双指触控误改变镜头。</summary>
    public static void SetEnablePinchZoom(bool value)
    {
        EnsureInitialized();
        if (cachedEnablePinchZoom == value)
            return;

        cachedEnablePinchZoom = value;
        PlayerPrefs.SetInt(EnablePinchZoomKey, value ? 1 : 0);
        PlayerPrefs.Save();
        MobileControlsChanged?.Invoke();
    }

    /// <summary>只恢复界面页可见设置，不改动已经拆到镜头控制页的双指缩放。</summary>
    public static void ResetInterfaceToDefaults()
    {
        EnsureInitialized();
        bool visualChanged = !Mathf.Approximately(cachedScale, DefaultScale) ||
                             !cachedRespectSafeArea;
        bool mobileChanged = !cachedFloatingMoveJoystick;
        if (!visualChanged && !mobileChanged)
            return;

        cachedScale = DefaultScale;
        cachedRespectSafeArea = true;
        cachedFloatingMoveJoystick = true;
        PlayerPrefs.SetFloat(ScaleKey, DefaultScale);
        PlayerPrefs.SetInt(RespectSafeAreaKey, 1);
        PlayerPrefs.SetInt(FloatingMoveJoystickKey, 1);
        PlayerPrefs.Save();
        if (visualChanged)
            Changed?.Invoke();
        if (mobileChanged)
            MobileControlsChanged?.Invoke();
    }

    /// <summary>只恢复镜头控制页的双指缩放开关。</summary>
    public static void ResetPinchZoomToDefault()
    {
        SetEnablePinchZoom(false);
    }

    public static void ResetToDefaults()
    {
        EnsureInitialized();
        bool changed = !Mathf.Approximately(cachedScale, DefaultScale) ||
                       !cachedRespectSafeArea ||
                       !cachedFloatingMoveJoystick ||
                       cachedEnablePinchZoom;
        if (!changed)
            return;

        cachedScale = DefaultScale;
        cachedRespectSafeArea = true;
        bool mobileControlsChanged = !cachedFloatingMoveJoystick || cachedEnablePinchZoom;
        cachedFloatingMoveJoystick = true;
        cachedEnablePinchZoom = false;
        PlayerPrefs.SetFloat(ScaleKey, DefaultScale);
        PlayerPrefs.SetInt(RespectSafeAreaKey, 1);
        PlayerPrefs.SetInt(FloatingMoveJoystickKey, 1);
        PlayerPrefs.SetInt(EnablePinchZoomKey, 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
        if (mobileControlsChanged)
            MobileControlsChanged?.Invoke();
    }

    #endregion

    #region 初始化与校验

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        cachedScale = DefaultScale;
        cachedRespectSafeArea = true;
        cachedFloatingMoveJoystick = true;
        cachedEnablePinchZoom = false;
        Changed = null;
        MobileControlsChanged = null;
    }

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private static ISettingsProvider CreateSettingsProvider()
    {
        return new UISettingsProvider();
    }

    private sealed class UISettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsToggle> toggles;
        private readonly IReadOnlyList<ISettingsSlider> sliders;

        public UISettingsProvider()
        {
            sliders = new ISettingsSlider[]
            {
                new SettingsSlider(
                    new SettingDescriptor(
                        ScaleSettingKey,
                        "界面缩放",
                        SettingControlType.Slider,
                        "ui",
                        order: 0),
                    MinimumScale,
                    MaximumScale,
                    ScaleStep,
                    () => Scale,
                    value => SetScale(value))
            };
            toggles = new ISettingsToggle[]
            {
                new SettingsToggle(
                    new SettingDescriptor(
                        RespectSafeAreaSettingKey,
                        "安全区域适配",
                        SettingControlType.Toggle,
                        "ui",
                        order: 0),
                    () => RespectSafeArea,
                    value => SetRespectSafeArea(value)),
                new SettingsToggle(
                    new SettingDescriptor(
                        FloatingMoveJoystickSettingKey,
                        "浮动移动摇杆",
                        SettingControlType.Toggle,
                        "ui",
                        order: 1),
                    () => FloatingMoveJoystick,
                    value => SetFloatingMoveJoystick(value)),
                new SettingsToggle(
                    new SettingDescriptor(
                        EnablePinchZoomSettingKey,
                        "双指缩放",
                        SettingControlType.Toggle,
                        "ui",
                        order: 2),
                    () => EnablePinchZoom,
                    value => SetEnablePinchZoom(value))
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "界面";
        public int Order => 20;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => toggles;
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings =>
            Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings =>
            Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults() => UIUserSettings.ResetToDefaults();
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        cachedScale = SanitizeScale(PlayerPrefs.GetFloat(ScaleKey, DefaultScale));
        cachedRespectSafeArea = PlayerPrefs.GetInt(RespectSafeAreaKey, 1) != 0;
        cachedFloatingMoveJoystick = PlayerPrefs.GetInt(FloatingMoveJoystickKey, 1) != 0;
        cachedEnablePinchZoom = PlayerPrefs.GetInt(EnablePinchZoomKey, 0) != 0;
        initialized = true;
    }

    private static float SanitizeScale(float value)
    {
        float clamped = Mathf.Clamp(value, MinimumScale, MaximumScale);
        return Mathf.Round(clamped / ScaleStep) * ScaleStep;
    }

    #endregion
}

/// <summary>
/// 把缓存的 UI 缩放与安全区偏好应用到根 Canvas。
/// 设置变更、根 RectTransform 尺寸变化及应用恢复时按事件刷新，不保留常驻 Update。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasScaler))]
public sealed class UIScaleController : MonoBehaviour
{
    private const float MinimumManagedReferenceWidth = 1280f;
    private const float MinimumManagedReferenceHeight = 720f;

    [SerializeField]
    private Vector2 baseReferenceResolution;

    private Canvas rootCanvas;
    private CanvasScaler canvasScaler;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private Rect lastSafeArea;
    private bool isApplying;

    public static UIScaleController Ensure(Transform canvasOrChild)
    {
        if (canvasOrChild == null)
            return null;

        Canvas canvas = canvasOrChild.GetComponent<Canvas>() ??
                        canvasOrChild.GetComponentInParent<Canvas>();
        if (!CanManage(canvas))
            return null;

        UIScaleController controller = canvas.GetComponent<UIScaleController>();
        if (controller == null)
            controller = canvas.gameObject.AddComponent<UIScaleController>();

        controller.ApplyCurrentSettings();
        return controller;
    }

    public static void ApplyToAllLoadedCanvases()
    {
        CanvasScaler[] scalers = UnityEngine.Object.FindObjectsOfType<CanvasScaler>(true);
        for (int i = 0; i < scalers.Length; i++)
        {
            Canvas canvas = scalers[i] != null ? scalers[i].GetComponent<Canvas>() : null;
            if (!CanManage(canvas))
                continue;

            UIScaleController controller = canvas.GetComponent<UIScaleController>();
            if (controller == null)
                controller = canvas.gameObject.AddComponent<UIScaleController>();
            controller.ApplyCurrentSettings();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAfterInitialSceneLoad()
    {
        ApplyToAllLoadedCanvases();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToAllLoadedCanvases();
    }

    private static bool CanManage(Canvas canvas)
    {
        if (canvas == null ||
            !canvas.isRootCanvas ||
            canvas.renderMode == RenderMode.WorldSpace ||
            !canvas.gameObject.scene.IsValid())
        {
            return false;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        return scaler != null &&
               scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
               scaler.referenceResolution.x >= MinimumManagedReferenceWidth &&
               scaler.referenceResolution.y >= MinimumManagedReferenceHeight;
    }

    private void Awake()
    {
        CaptureReferences();
    }

    private void OnEnable()
    {
        UIUserSettings.Changed -= HandleSettingsChanged;
        UIUserSettings.Changed += HandleSettingsChanged;
        CaptureReferences();
        ApplyCurrentSettings();
    }

    private void OnDisable()
    {
        UIUserSettings.Changed -= HandleSettingsChanged;
    }

    /// <summary>分辨率或根 Canvas 尺寸变化时由 Unity 回调，不需要逐帧比较。</summary>
    private void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled && !isApplying)
            ApplyIfDisplayStateChanged();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            ApplyIfDisplayStateChanged();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
            ApplyIfDisplayStateChanged();
    }

    private void HandleSettingsChanged()
    {
        ApplyCurrentSettings();
    }

    private void ApplyIfDisplayStateChanged()
    {
        if (lastScreenWidth != Screen.width ||
            lastScreenHeight != Screen.height ||
            lastSafeArea != Screen.safeArea)
        {
            ApplyCurrentSettings();
        }
    }

    public void ApplyCurrentSettings()
    {
        if (isApplying)
            return;

        CaptureReferences();
        if (canvasScaler == null || rootCanvas == null)
            return;

        isApplying = true;
        try
        {
            // SafeAreaRoot 已经负责裁出可交互区域，CanvasScaler 不应再次缩小整套 UI。
            float effectiveScale = Mathf.Max(0.5f, UIUserSettings.Scale);

            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasScaler.referenceResolution = baseReferenceResolution / effectiveScale;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;
        }
        finally
        {
            isApplying = false;
        }
    }

    private void CaptureReferences()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>();
        if (canvasScaler == null)
            canvasScaler = GetComponent<CanvasScaler>();

        if (baseReferenceResolution.x <= 0f || baseReferenceResolution.y <= 0f)
        {
            baseReferenceResolution = canvasScaler != null &&
                                      canvasScaler.referenceResolution.x > 0f &&
                                      canvasScaler.referenceResolution.y > 0f
                ? canvasScaler.referenceResolution
                : new Vector2(1920f, 1080f);
        }
    }

}
