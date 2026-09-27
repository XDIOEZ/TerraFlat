// AI-Context: 全局 UI 用户偏好；缩放设置由 UIScaleController 应用到根 Canvas。

using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 全局 UI 用户偏好缓存。PlayerPrefs 只在首次访问和显式写入时读取/保存，
/// 运行时消费者通过 Changed 事件更新，不在逐帧路径访问磁盘偏好。
/// </summary>
public static class UIUserSettings
{
    #region 键与默认值

    private const string ScaleKey = "FlatWorld.UI.Scale";
    private const string AnimationSpeedKey = "FlatWorld.UI.AnimationSpeed";
    private const string HotbarBottomSpacingKey = "FlatWorld.UI.HotbarBottomSpacing";
    private const string ScrollRowsPerWheelTickKey = "FlatWorld.UI.ScrollRowsPerWheelTick";
    private const string RespectSafeAreaKey = "FlatWorld.UI.RespectSafeArea";
    private const string FloatingMoveJoystickKey = "FlatWorld.Mobile.FloatingMoveJoystick";
    private const string TouchControlsOpacityKey = "FlatWorld.Mobile.TouchControlsOpacity";
    private const string PinchZoomSensitivityKey = "FlatWorld.Mobile.PinchZoomSensitivity";
    private const string LeftControlZoneRatioKey = "FlatWorld.Mobile.LeftControlZoneRatio";
    private const string RightControlZoneRatioKey = "FlatWorld.Mobile.RightControlZoneRatio";
    private const string MobileControlLayoutPrefix = "FlatWorld.Mobile.ControlLayout.";
    public const float MinimumMobileControlSize = 0.5f;
    public const float MaximumMobileControlSize = 2f;

    public const string MobileMoveControlId = "move";
    public const string MobileAttackControlId = "attack";
    public const string MobileInteractControlId = "interact";
    public const string MobileUseControlId = "use";
    public const string MobileRunControlId = "run";

    private static readonly string[] BuiltInMobileControlIds =
    {
        MobileMoveControlId,
        MobileAttackControlId,
        MobileInteractControlId,
        MobileUseControlId,
        MobileRunControlId
    };

    /// <summary>新配置与恢复默认时采用的界面缩放倍率。</summary>
    public const float DefaultScale = 1.5f;

    /// <summary>界面缩放允许的最小倍率。</summary>
    public const float MinimumScale = 0.25f;

    /// <summary>界面缩放允许的最大倍率。</summary>
    public const float MaximumScale = 3.25f;

    /// <summary>界面缩放每次调整的倍率步长。</summary>
    public const float ScaleStep = 0.05f;
    /// <summary>UI 动画默认播放倍率。</summary>
    public const float DefaultAnimationSpeed = 1f;
    /// <summary>UI 动画允许的最慢播放倍率，避免 0 倍速让面板视觉过渡永久停住。</summary>
    public const float MinimumAnimationSpeed = 0.25f;
    /// <summary>UI 动画允许的最快播放倍率。</summary>
    public const float MaximumAnimationSpeed = 3f;
    /// <summary>UI 动画速度滑动条步长。</summary>
    public const float AnimationSpeedStep = 0.05f;
    public const float DefaultHotbarBottomSpacing = 28f;
    public const float MinimumHotbarBottomSpacing = -128f;
    public const float MaximumHotbarBottomSpacing = 128f;
    public const float HotbarBottomSpacingStep = 1f;
    public const int DefaultScrollRowsPerWheelTick = 1;
    public const int MinimumScrollRowsPerWheelTick = 1;
    public const int MaximumScrollRowsPerWheelTick = 10;
    public const int ScrollRowsPerWheelTickStep = 1;
    public const float DefaultLeftControlZoneRatio = 0.33f;
    public const float DefaultRightControlZoneRatio = 0.33f;
    public const float MinimumControlZoneRatio = 0.2f;
    public const float MaximumControlZoneRatio = 0.4f;
    public const float ControlZoneRatioStep = 0.01f;
    public const float DefaultPinchZoomSensitivity = 100f;
    public const float MinimumPinchZoomSensitivity = 0f;
    public const float MaximumPinchZoomSensitivity = 300f;
    public const float PinchZoomSensitivityStep = 1f;
    public const float DefaultTouchControlsOpacityPercent = 100f;
    public const float MinimumTouchControlsOpacityPercent = 0f;
    public const float MaximumTouchControlsOpacityPercent = 100f;
    public const float TouchControlsOpacityPercentStep = 1f;

    public const string SettingsProviderId = "ui";
    public const string ScaleSettingKey = "ui.scale";
    public const string AnimationSpeedSettingKey = "ui.animationSpeed";
    public const string HotbarBottomSpacingSettingKey = "ui.hotbarBottomSpacing";
    public const string ScrollRowsPerWheelTickSettingKey = "ui.scrollRowsPerWheelTick";
    public const string RespectSafeAreaSettingKey = "ui.respectSafeArea";
    public const string FloatingMoveJoystickSettingKey = "ui.floatingMoveJoystick";
    public const string TouchControlsOpacitySettingKey = "ui.touchControlsOpacity";
    public const string PinchZoomSensitivitySettingKey = "ui.pinchZoomSensitivity";
    public const string LeftControlZoneRatioSettingKey = "ui.leftControlZoneRatio";
    public const string RightControlZoneRatioSettingKey = "ui.rightControlZoneRatio";

    #endregion

    #region 缓存与事件

    private static bool initialized;
    private static float cachedScale = DefaultScale;
    private static float cachedAnimationSpeed = DefaultAnimationSpeed;
    private static float cachedHotbarBottomSpacing = DefaultHotbarBottomSpacing;
    private static int cachedScrollRowsPerWheelTick = DefaultScrollRowsPerWheelTick;
    private static bool cachedRespectSafeArea = true;
    private static bool cachedFloatingMoveJoystick = true;
    private static float cachedTouchControlsOpacityPercent = DefaultTouchControlsOpacityPercent;
    private static float cachedPinchZoomSensitivity = DefaultPinchZoomSensitivity;
    private static float cachedLeftControlZoneRatio = DefaultLeftControlZoneRatio;
    private static float cachedRightControlZoneRatio = DefaultRightControlZoneRatio;

    /// <summary>界面缩放或安全区偏好实际改变后广播一次。</summary>
    public static event Action Changed;

    /// <summary>手机摇杆模式或左右触控分区改变时广播，避免无关 UI 设置触发摇杆重配。</summary>
    public static event Action MobileControlsChanged;

    /// <summary>触屏玩法控件透明度改变时广播，不影响控件射线与交互状态。</summary>
    public static event Action TouchControlsOpacityChanged;

    /// <summary>快捷栏底部间距改变时广播，桌面与手机布局各自重新应用安全边距。</summary>
    public static event Action HotbarLayoutChanged;

    /// <summary>触屏玩法控件的自定义位置改变时广播，HUD 立即按安全区重新投影。</summary>
    public static event Action MobileControlLayoutChanged;

    /// <summary>通知手机控件布局偏好已恢复。</summary>
    private static void NotifyMobileControlLayoutChanged()
    {
        MobileControlLayoutChanged?.Invoke();
    }

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

    /// <summary>统一 UI 面板动画播放倍率；1 为正常速度。</summary>
    public static float AnimationSpeed
    {
        get
        {
            EnsureInitialized();
            return cachedAnimationSpeed;
        }
    }

    /// <summary>快捷栏相对可用屏幕底边的额外间距，单位为 UI 参考像素。</summary>
    public static float HotbarBottomSpacing
    {
        get
        {
            EnsureInitialized();
            return cachedHotbarBottomSpacing;
        }
    }

    /// <summary>每个滚轮刻度带动所有列表移动的条目行数。</summary>
    public static int ScrollRowsPerWheelTick
    {
        get
        {
            EnsureInitialized();
            return cachedScrollRowsPerWheelTick;
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

    /// <summary>触屏玩法控件透明度百分比；0 为完全透明，100 为完全不透明。</summary>
    public static float TouchControlsOpacityPercent
    {
        get
        {
            EnsureInitialized();
            return cachedTouchControlsOpacityPercent;
        }
    }

    /// <summary>供 CanvasGroup 使用的归一化触屏控件透明度。</summary>
    public static float TouchControlsOpacity => TouchControlsOpacityPercent / 100f;

    /// <summary>双指缩放灵敏度；0 表示关闭，数值越大响应越快。</summary>
    public static float PinchZoomSensitivity
    {
        get
        {
            EnsureInitialized();
            return cachedPinchZoomSensitivity;
        }
    }

    /// <summary>安全区域内由左侧移动摇杆接收触点的屏幕宽度比例。</summary>
    public static float LeftControlZoneRatio
    {
        get
        {
            EnsureInitialized();
            return cachedLeftControlZoneRatio;
        }
    }

    /// <summary>安全区域内由右侧普通指向摇杆接收触点的屏幕宽度比例。</summary>
    public static float RightControlZoneRatio
    {
        get
        {
            EnsureInitialized();
            return cachedRightControlZoneRatio;
        }
    }

    /// <summary>中间预留操作区使用左右区域之外的剩余宽度。</summary>
    public static float CenterControlZoneRatio
    {
        get
        {
            EnsureInitialized();
            return Mathf.Max(0f, 1f - cachedLeftControlZoneRatio - cachedRightControlZoneRatio);
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

    /// <summary>保存 UI 动画播放倍率，并立即应用到当前由 UIAnimationManager 管理的过渡。</summary>
    public static float SetAnimationSpeed(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizeAnimationSpeed(value);
        if (Mathf.Approximately(cachedAnimationSpeed, sanitized))
        {
            UIAnimationManager.Instance.SetPlaybackSpeed(cachedAnimationSpeed);
            return cachedAnimationSpeed;
        }

        cachedAnimationSpeed = sanitized;
        PlayerPrefs.SetFloat(AnimationSpeedKey, sanitized);
        PlayerPrefs.Save();
        UIAnimationManager.Instance.SetPlaybackSpeed(sanitized);
        return sanitized;
    }

    /// <summary>保存快捷栏底部额外间距，并立即刷新桌面与手机快捷栏布局。</summary>
    public static float SetHotbarBottomSpacing(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizeHotbarBottomSpacing(value);
        if (Mathf.Approximately(cachedHotbarBottomSpacing, sanitized))
            return cachedHotbarBottomSpacing;

        cachedHotbarBottomSpacing = sanitized;
        PlayerPrefs.SetFloat(HotbarBottomSpacingKey, sanitized);
        PlayerPrefs.Save();
        HotbarLayoutChanged?.Invoke();
        return sanitized;
    }

    /// <summary>保存每个滚轮刻度对应的列表行数。</summary>
    public static int SetScrollRowsPerWheelTick(float value)
    {
        EnsureInitialized();
        int sanitized = SanitizeScrollRowsPerWheelTick(value);
        if (cachedScrollRowsPerWheelTick == sanitized)
            return cachedScrollRowsPerWheelTick;

        cachedScrollRowsPerWheelTick = sanitized;
        PlayerPrefs.SetInt(ScrollRowsPerWheelTickKey, sanitized);
        PlayerPrefs.Save();
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

    /// <summary>保存触屏玩法控件透明度，并即时刷新手机 HUD 与快捷栏视觉。</summary>
    public static float SetTouchControlsOpacityPercent(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizeTouchControlsOpacityPercent(value);
        if (Mathf.Approximately(cachedTouchControlsOpacityPercent, sanitized))
            return cachedTouchControlsOpacityPercent;

        cachedTouchControlsOpacityPercent = sanitized;
        PlayerPrefs.SetFloat(TouchControlsOpacityKey, sanitized);
        PlayerPrefs.Save();
        TouchControlsOpacityChanged?.Invoke();
        return sanitized;
    }

    /// <summary>保存双指缩放灵敏度；0 表示关闭。</summary>
    public static float SetPinchZoomSensitivity(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizePinchZoomSensitivity(value);
        if (Mathf.Approximately(cachedPinchZoomSensitivity, sanitized))
            return cachedPinchZoomSensitivity;

        cachedPinchZoomSensitivity = sanitized;
        PlayerPrefs.SetFloat(PinchZoomSensitivityKey, sanitized);
        PlayerPrefs.Save();
        return sanitized;
    }

    /// <summary>保存左侧移动摇杆触控区比例并即时刷新手机 HUD。</summary>
    public static float SetLeftControlZoneRatio(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizeControlZoneRatio(value);
        if (Mathf.Approximately(cachedLeftControlZoneRatio, sanitized))
            return cachedLeftControlZoneRatio;

        cachedLeftControlZoneRatio = sanitized;
        PlayerPrefs.SetFloat(LeftControlZoneRatioKey, sanitized);
        PlayerPrefs.Save();
        MobileControlsChanged?.Invoke();
        return sanitized;
    }

    /// <summary>保存右侧普通指向触控区比例并即时刷新手机 HUD。</summary>
    public static float SetRightControlZoneRatio(float value)
    {
        EnsureInitialized();
        float sanitized = SanitizeControlZoneRatio(value);
        if (Mathf.Approximately(cachedRightControlZoneRatio, sanitized))
            return cachedRightControlZoneRatio;

        cachedRightControlZoneRatio = sanitized;
        PlayerPrefs.SetFloat(RightControlZoneRatioKey, sanitized);
        PlayerPrefs.Save();
        MobileControlsChanged?.Invoke();
        return sanitized;
    }

    /// <summary>读取一个内置触屏玩法控件保存的安全区归一化中心位置。</summary>
    public static bool TryGetMobileControlLayoutPosition(
        string controlId,
        out Vector2 normalizedPosition)
    {
        EnsureInitialized();
        normalizedPosition = default;
        if (!IsBuiltInMobileControlId(controlId))
            return false;

        string xKey = GetMobileControlLayoutKey(controlId, "X");
        string yKey = GetMobileControlLayoutKey(controlId, "Y");
        if (!PlayerPrefs.HasKey(xKey) || !PlayerPrefs.HasKey(yKey))
            return false;

        float x = PlayerPrefs.GetFloat(xKey);
        float y = PlayerPrefs.GetFloat(yKey);
        if (float.IsNaN(x) || float.IsInfinity(x) ||
            float.IsNaN(y) || float.IsInfinity(y))
        {
            return false;
        }

        normalizedPosition = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        return true;
    }

    /// <summary>批量保存触屏玩法控件位置，只在实际变化后落盘并广播一次。</summary>
    public static void SetMobileControlLayoutPositions(
        IReadOnlyDictionary<string, Vector2> positions)
    {
        SetMobileControlLayout(positions, null);
    }

    /// <summary>读取控件独立尺寸乘区，不使用全局 UI 缩放键。</summary>
    public static float GetMobileControlSize(string controlId)
    {
        float value = PlayerPrefs.GetFloat(GetMobileControlLayoutKey(controlId, "Size"), 1f);
        return SanitizeMobileControlSize(value);
    }

    public static float SanitizeMobileControlSize(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 1f :
            Mathf.Clamp(value, MinimumMobileControlSize, MaximumMobileControlSize);
    }

    /// <summary>一次提交位置和独立倍率，并只广播一次布局变化以释放旧触点。</summary>
    public static void SetMobileControlLayout(
        IReadOnlyDictionary<string, Vector2> positions,
        IReadOnlyDictionary<string, float> sizes)
    {
        EnsureInitialized();
        if (positions == null || positions.Count == 0)
            return;

        bool changed = false;
        for (int index = 0; index < BuiltInMobileControlIds.Length; index++)
        {
            string controlId = BuiltInMobileControlIds[index];
            if (sizes != null && sizes.TryGetValue(controlId, out float size))
            {
                float sanitizedSize = SanitizeMobileControlSize(size);
                if (!Mathf.Approximately(GetMobileControlSize(controlId), sanitizedSize))
                {
                    PlayerPrefs.SetFloat(GetMobileControlLayoutKey(controlId, "Size"), sanitizedSize);
                    changed = true;
                }
            }
            if (!positions.TryGetValue(controlId, out Vector2 position))
                continue;

            Vector2 sanitized = new Vector2(
                Mathf.Clamp01(position.x),
                Mathf.Clamp01(position.y));
            bool hasPrevious = TryGetMobileControlLayoutPosition(
                controlId,
                out Vector2 previous);
            if (hasPrevious && Vector2.SqrMagnitude(previous - sanitized) <= 0.000001f)
                continue;

            PlayerPrefs.SetFloat(GetMobileControlLayoutKey(controlId, "X"), sanitized.x);
            PlayerPrefs.SetFloat(GetMobileControlLayoutKey(controlId, "Y"), sanitized.y);
            changed = true;
        }

        if (!changed)
            return;

        PlayerPrefs.Save();
        MobileControlLayoutChanged?.Invoke();
    }

    /// <summary>清除所有内置触屏玩法控件的位置覆盖，恢复正式 Prefab 的默认布局。</summary>
    public static void ResetMobileControlLayoutToDefaults()
    {
        EnsureInitialized();
        if (!ClearMobileControlLayoutPreferences())
            return;

        PlayerPrefs.Save();
        MobileControlLayoutChanged?.Invoke();
    }

    /// <summary>只恢复界面页可见设置，不改动已经拆到镜头控制页的双指缩放灵敏度。</summary>
    public static void ResetInterfaceToDefaults()
    {
        EnsureInitialized();
        bool mobileLayoutChanged = HasMobileControlLayoutPreferences();
        bool visualChanged = !Mathf.Approximately(cachedScale, DefaultScale) ||
                             !cachedRespectSafeArea;
        bool animationSpeedChanged = !Mathf.Approximately(
            cachedAnimationSpeed,
            DefaultAnimationSpeed);
        bool hotbarLayoutChanged = !Mathf.Approximately(
            cachedHotbarBottomSpacing,
            DefaultHotbarBottomSpacing);
        bool scrollRowsChanged = cachedScrollRowsPerWheelTick != DefaultScrollRowsPerWheelTick;
        bool touchOpacityChanged = !Mathf.Approximately(
            cachedTouchControlsOpacityPercent,
            DefaultTouchControlsOpacityPercent);
        bool mobileChanged = !cachedFloatingMoveJoystick ||
                             !Mathf.Approximately(
                                 cachedLeftControlZoneRatio,
                                 DefaultLeftControlZoneRatio) ||
                             !Mathf.Approximately(
                                 cachedRightControlZoneRatio,
                                 DefaultRightControlZoneRatio);
        if (!visualChanged && !animationSpeedChanged && !hotbarLayoutChanged && !scrollRowsChanged &&
            !mobileChanged && !touchOpacityChanged && !mobileLayoutChanged)
            return;

        cachedScale = DefaultScale;
        cachedAnimationSpeed = DefaultAnimationSpeed;
        cachedHotbarBottomSpacing = DefaultHotbarBottomSpacing;
        cachedScrollRowsPerWheelTick = DefaultScrollRowsPerWheelTick;
        cachedRespectSafeArea = true;
        cachedFloatingMoveJoystick = true;
        cachedTouchControlsOpacityPercent = DefaultTouchControlsOpacityPercent;
        cachedLeftControlZoneRatio = DefaultLeftControlZoneRatio;
        cachedRightControlZoneRatio = DefaultRightControlZoneRatio;
        PlayerPrefs.SetFloat(ScaleKey, DefaultScale);
        PlayerPrefs.SetFloat(AnimationSpeedKey, DefaultAnimationSpeed);
        PlayerPrefs.SetFloat(HotbarBottomSpacingKey, DefaultHotbarBottomSpacing);
        PlayerPrefs.SetInt(ScrollRowsPerWheelTickKey, DefaultScrollRowsPerWheelTick);
        PlayerPrefs.SetInt(RespectSafeAreaKey, 1);
        PlayerPrefs.SetInt(FloatingMoveJoystickKey, 1);
        PlayerPrefs.SetFloat(TouchControlsOpacityKey, DefaultTouchControlsOpacityPercent);
        PlayerPrefs.SetFloat(LeftControlZoneRatioKey, DefaultLeftControlZoneRatio);
        PlayerPrefs.SetFloat(RightControlZoneRatioKey, DefaultRightControlZoneRatio);
        ClearMobileControlLayoutPreferences();
        PlayerPrefs.Save();
        if (animationSpeedChanged)
            UIAnimationManager.Instance.SetPlaybackSpeed(DefaultAnimationSpeed);
        if (visualChanged)
            Changed?.Invoke();
        if (hotbarLayoutChanged)
            HotbarLayoutChanged?.Invoke();
        if (mobileChanged)
            MobileControlsChanged?.Invoke();
        if (touchOpacityChanged)
            TouchControlsOpacityChanged?.Invoke();
        if (mobileLayoutChanged)
            MobileControlLayoutChanged?.Invoke();
    }

    /// <summary>只恢复镜头控制页的双指缩放灵敏度。</summary>
    public static void ResetPinchZoomSensitivityToDefault()
    {
        SetPinchZoomSensitivity(DefaultPinchZoomSensitivity);
    }

    public static void ResetToDefaults()
    {
        EnsureInitialized();
        bool mobileLayoutChanged = HasMobileControlLayoutPreferences();
        bool changed = !Mathf.Approximately(cachedScale, DefaultScale) ||
                       !Mathf.Approximately(cachedAnimationSpeed, DefaultAnimationSpeed) ||
                       !Mathf.Approximately(cachedHotbarBottomSpacing, DefaultHotbarBottomSpacing) ||
                       cachedScrollRowsPerWheelTick != DefaultScrollRowsPerWheelTick ||
                       !cachedRespectSafeArea ||
                       !cachedFloatingMoveJoystick ||
                       !Mathf.Approximately(
                           cachedTouchControlsOpacityPercent,
                           DefaultTouchControlsOpacityPercent) ||
                       !Mathf.Approximately(
                           cachedLeftControlZoneRatio,
                           DefaultLeftControlZoneRatio) ||
                       !Mathf.Approximately(
                           cachedRightControlZoneRatio,
                           DefaultRightControlZoneRatio) ||
                       !Mathf.Approximately(
                           cachedPinchZoomSensitivity,
                           DefaultPinchZoomSensitivity) ||
                       mobileLayoutChanged;
        if (!changed)
            return;

        cachedScale = DefaultScale;
        bool animationSpeedChanged = !Mathf.Approximately(
            cachedAnimationSpeed,
            DefaultAnimationSpeed);
        cachedAnimationSpeed = DefaultAnimationSpeed;
        bool hotbarLayoutChanged = !Mathf.Approximately(
            cachedHotbarBottomSpacing,
            DefaultHotbarBottomSpacing);
        cachedHotbarBottomSpacing = DefaultHotbarBottomSpacing;
        cachedScrollRowsPerWheelTick = DefaultScrollRowsPerWheelTick;
        cachedRespectSafeArea = true;
        bool mobileControlsChanged = !cachedFloatingMoveJoystick ||
                                     !Mathf.Approximately(
                                         cachedLeftControlZoneRatio,
                                         DefaultLeftControlZoneRatio) ||
                                     !Mathf.Approximately(
                                         cachedRightControlZoneRatio,
                                         DefaultRightControlZoneRatio);
        bool touchOpacityChanged = !Mathf.Approximately(
            cachedTouchControlsOpacityPercent,
            DefaultTouchControlsOpacityPercent);
        cachedFloatingMoveJoystick = true;
        cachedTouchControlsOpacityPercent = DefaultTouchControlsOpacityPercent;
        cachedPinchZoomSensitivity = DefaultPinchZoomSensitivity;
        cachedLeftControlZoneRatio = DefaultLeftControlZoneRatio;
        cachedRightControlZoneRatio = DefaultRightControlZoneRatio;
        PlayerPrefs.SetFloat(ScaleKey, DefaultScale);
        PlayerPrefs.SetFloat(AnimationSpeedKey, DefaultAnimationSpeed);
        PlayerPrefs.SetFloat(HotbarBottomSpacingKey, DefaultHotbarBottomSpacing);
        PlayerPrefs.SetInt(ScrollRowsPerWheelTickKey, DefaultScrollRowsPerWheelTick);
        PlayerPrefs.SetInt(RespectSafeAreaKey, 1);
        PlayerPrefs.SetInt(FloatingMoveJoystickKey, 1);
        PlayerPrefs.SetFloat(TouchControlsOpacityKey, DefaultTouchControlsOpacityPercent);
        PlayerPrefs.SetFloat(PinchZoomSensitivityKey, DefaultPinchZoomSensitivity);
        PlayerPrefs.SetFloat(LeftControlZoneRatioKey, DefaultLeftControlZoneRatio);
        PlayerPrefs.SetFloat(RightControlZoneRatioKey, DefaultRightControlZoneRatio);
        ClearMobileControlLayoutPreferences();
        PlayerPrefs.Save();
        if (animationSpeedChanged)
            UIAnimationManager.Instance.SetPlaybackSpeed(DefaultAnimationSpeed);
        Changed?.Invoke();
        if (hotbarLayoutChanged)
            HotbarLayoutChanged?.Invoke();
        if (mobileControlsChanged)
            MobileControlsChanged?.Invoke();
        if (touchOpacityChanged)
            TouchControlsOpacityChanged?.Invoke();
        if (mobileLayoutChanged)
            MobileControlLayoutChanged?.Invoke();
    }

    #endregion

    #region 初始化与校验

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        cachedScale = DefaultScale;
        cachedAnimationSpeed = DefaultAnimationSpeed;
        cachedHotbarBottomSpacing = DefaultHotbarBottomSpacing;
        cachedScrollRowsPerWheelTick = DefaultScrollRowsPerWheelTick;
        cachedRespectSafeArea = true;
        cachedFloatingMoveJoystick = true;
        cachedTouchControlsOpacityPercent = DefaultTouchControlsOpacityPercent;
        cachedPinchZoomSensitivity = DefaultPinchZoomSensitivity;
        cachedLeftControlZoneRatio = DefaultLeftControlZoneRatio;
        cachedRightControlZoneRatio = DefaultRightControlZoneRatio;
        Changed = null;
        MobileControlsChanged = null;
        TouchControlsOpacityChanged = null;
        HotbarLayoutChanged = null;
        MobileControlLayoutChanged = null;
    }

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    /// <summary>在场景加载前恢复持久化倍率，确保首个面板动画也使用玩家设置。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyAnimationSpeedOnLoad()
    {
        EnsureInitialized();
        UIAnimationManager.Instance.SetPlaybackSpeed(cachedAnimationSpeed);
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

    private sealed class UISettingsProvider : ISettingsProvider, ISettingsEditSessionParticipant
    {
        private readonly IReadOnlyList<ISettingsToggle> toggles;
        private readonly IReadOnlyList<ISettingsSlider> sliders;

        private sealed class MobileControlLayoutState
        {
            /// <summary>手机 HUD 控件的稳定 ID。</summary>
            public string ControlId;

            /// <summary>快照时 X 坐标是否有自定义值。</summary>
            public bool HasX;

            /// <summary>快照时保存的 X 坐标。</summary>
            public float X;

            /// <summary>快照时 Y 坐标是否有自定义值。</summary>
            public bool HasY;

            /// <summary>快照时保存的 Y 坐标。</summary>
            public float Y;

            /// <summary>快照时控件尺寸是否有自定义值。</summary>
            public bool HasSize;

            /// <summary>快照时保存的尺寸倍率。</summary>
            public float Size;
        }

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
                    value => SetScale(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        AnimationSpeedSettingKey,
                        "UI 动画速度",
                        SettingControlType.Slider,
                        "ui",
                        order: 1),
                    MinimumAnimationSpeed,
                    MaximumAnimationSpeed,
                    AnimationSpeedStep,
                    () => AnimationSpeed,
                    value => SetAnimationSpeed(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        HotbarBottomSpacingSettingKey,
                        "快捷栏底部间距",
                        SettingControlType.Slider,
                        "ui",
                        order: 2),
                    MinimumHotbarBottomSpacing,
                    MaximumHotbarBottomSpacing,
                    HotbarBottomSpacingStep,
                    () => HotbarBottomSpacing,
                    value => SetHotbarBottomSpacing(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        ScrollRowsPerWheelTickSettingKey,
                        "滚轮每次翻动行数",
                        SettingControlType.Slider,
                        "ui",
                        order: 3),
                    MinimumScrollRowsPerWheelTick,
                    MaximumScrollRowsPerWheelTick,
                    ScrollRowsPerWheelTickStep,
                    () => ScrollRowsPerWheelTick,
                    value => SetScrollRowsPerWheelTick(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        TouchControlsOpacitySettingKey,
                        "触屏控件透明度",
                        SettingControlType.Slider,
                        "mobile",
                        order: 2),
                    MinimumTouchControlsOpacityPercent,
                    MaximumTouchControlsOpacityPercent,
                    TouchControlsOpacityPercentStep,
                    () => TouchControlsOpacityPercent,
                    value => SetTouchControlsOpacityPercent(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        LeftControlZoneRatioSettingKey,
                        "左侧触控区比例",
                        SettingControlType.Slider,
                        "mobile",
                        order: 3),
                    MinimumControlZoneRatio,
                    MaximumControlZoneRatio,
                    ControlZoneRatioStep,
                    () => LeftControlZoneRatio,
                    value => SetLeftControlZoneRatio(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        RightControlZoneRatioSettingKey,
                        "右侧触控区比例",
                        SettingControlType.Slider,
                        "mobile",
                        order: 4),
                    MinimumControlZoneRatio,
                    MaximumControlZoneRatio,
                    ControlZoneRatioStep,
                    () => RightControlZoneRatio,
                    value => SetRightControlZoneRatio(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        PinchZoomSensitivitySettingKey,
                        "双指缩放灵敏度",
                        SettingControlType.Slider,
                        "mobile",
                        order: 5),
                    MinimumPinchZoomSensitivity,
                    MaximumPinchZoomSensitivity,
                    PinchZoomSensitivityStep,
                    () => PinchZoomSensitivity,
                    value => SetPinchZoomSensitivity(value))
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

        /// <summary>快照未通过通用控件契约暴露的手机控件位置与尺寸。</summary>
        public object CaptureSettingsEditSessionState()
        {
            var states = new List<MobileControlLayoutState>(BuiltInMobileControlIds.Length);
            for (int index = 0; index < BuiltInMobileControlIds.Length; index++)
            {
                string controlId = BuiltInMobileControlIds[index];
                string xKey = GetMobileControlLayoutKey(controlId, "X");
                string yKey = GetMobileControlLayoutKey(controlId, "Y");
                string sizeKey = GetMobileControlLayoutKey(controlId, "Size");
                states.Add(new MobileControlLayoutState
                {
                    ControlId = controlId,
                    HasX = PlayerPrefs.HasKey(xKey),
                    X = PlayerPrefs.GetFloat(xKey),
                    HasY = PlayerPrefs.HasKey(yKey),
                    Y = PlayerPrefs.GetFloat(yKey),
                    HasSize = PlayerPrefs.HasKey(sizeKey),
                    Size = PlayerPrefs.GetFloat(sizeKey)
                });
            }

            return states;
        }

        /// <summary>恢复全部内置手机控件布局，并通知运行时 HUD 重新投影。</summary>
        public void RestoreSettingsEditSessionState(object state)
        {
            if (!(state is List<MobileControlLayoutState> states))
                return;

            for (int index = 0; index < states.Count; index++)
            {
                MobileControlLayoutState layout = states[index];
                string xKey = GetMobileControlLayoutKey(layout.ControlId, "X");
                string yKey = GetMobileControlLayoutKey(layout.ControlId, "Y");
                string sizeKey = GetMobileControlLayoutKey(layout.ControlId, "Size");
                PlayerPrefs.DeleteKey(xKey);
                PlayerPrefs.DeleteKey(yKey);
                PlayerPrefs.DeleteKey(sizeKey);
                if (layout.HasX)
                    PlayerPrefs.SetFloat(xKey, layout.X);
                if (layout.HasY)
                    PlayerPrefs.SetFloat(yKey, layout.Y);
                if (layout.HasSize)
                    PlayerPrefs.SetFloat(sizeKey, layout.Size);
            }

            PlayerPrefs.Save();
            NotifyMobileControlLayoutChanged();
        }
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        cachedScale = SanitizeScale(PlayerPrefs.GetFloat(ScaleKey, DefaultScale));
        cachedAnimationSpeed = SanitizeAnimationSpeed(
            PlayerPrefs.GetFloat(AnimationSpeedKey, DefaultAnimationSpeed));
        cachedHotbarBottomSpacing = SanitizeHotbarBottomSpacing(
            PlayerPrefs.GetFloat(HotbarBottomSpacingKey, DefaultHotbarBottomSpacing));
        cachedScrollRowsPerWheelTick = SanitizeScrollRowsPerWheelTick(
            PlayerPrefs.GetInt(ScrollRowsPerWheelTickKey, DefaultScrollRowsPerWheelTick));
        cachedRespectSafeArea = PlayerPrefs.GetInt(RespectSafeAreaKey, 1) != 0;
        cachedFloatingMoveJoystick = PlayerPrefs.GetInt(FloatingMoveJoystickKey, 1) != 0;
        cachedTouchControlsOpacityPercent = SanitizeTouchControlsOpacityPercent(
            PlayerPrefs.GetFloat(TouchControlsOpacityKey, DefaultTouchControlsOpacityPercent));
        cachedPinchZoomSensitivity = SanitizePinchZoomSensitivity(
            PlayerPrefs.GetFloat(PinchZoomSensitivityKey, DefaultPinchZoomSensitivity));
        cachedLeftControlZoneRatio = SanitizeControlZoneRatio(
            PlayerPrefs.GetFloat(LeftControlZoneRatioKey, DefaultLeftControlZoneRatio));
        cachedRightControlZoneRatio = SanitizeControlZoneRatio(
            PlayerPrefs.GetFloat(RightControlZoneRatioKey, DefaultRightControlZoneRatio));
        initialized = true;
    }

    private static float SanitizeScale(float value)
    {
        float clamped = Mathf.Clamp(value, MinimumScale, MaximumScale);
        return Mathf.Round(clamped / ScaleStep) * ScaleStep;
    }

    /// <summary>把 UI 动画倍率限制到设置页允许的安全范围。</summary>
    private static float SanitizeAnimationSpeed(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = DefaultAnimationSpeed;

        float clamped = Mathf.Clamp(
            value,
            MinimumAnimationSpeed,
            MaximumAnimationSpeed);
        return Mathf.Round(clamped / AnimationSpeedStep) * AnimationSpeedStep;
    }

    /// <summary>把快捷栏底部间距限制到界面设置页允许的整数参考像素范围。</summary>
    private static float SanitizeHotbarBottomSpacing(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = DefaultHotbarBottomSpacing;

        float clamped = Mathf.Clamp(
            value,
            MinimumHotbarBottomSpacing,
            MaximumHotbarBottomSpacing);
        return Mathf.Round(clamped / HotbarBottomSpacingStep) * HotbarBottomSpacingStep;
    }

    /// <summary>把滚轮行数限制在设置页提供的整数范围。</summary>
    private static int SanitizeScrollRowsPerWheelTick(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = DefaultScrollRowsPerWheelTick;

        return Mathf.Clamp(
            Mathf.RoundToInt(value),
            MinimumScrollRowsPerWheelTick,
            MaximumScrollRowsPerWheelTick);
    }

    /// <summary>把双指缩放灵敏度限制为设置页显示的整数范围。</summary>
    private static float SanitizePinchZoomSensitivity(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = DefaultPinchZoomSensitivity;

        float clamped = Mathf.Clamp(
            value,
            MinimumPinchZoomSensitivity,
            MaximumPinchZoomSensitivity);
        return Mathf.Round(clamped / PinchZoomSensitivityStep) * PinchZoomSensitivityStep;
    }

    /// <summary>把触屏控件透明度限制为设置页显示的 0–100 整数百分比。</summary>
    private static float SanitizeTouchControlsOpacityPercent(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = DefaultTouchControlsOpacityPercent;

        float clamped = Mathf.Clamp(
            value,
            MinimumTouchControlsOpacityPercent,
            MaximumTouchControlsOpacityPercent);
        return Mathf.Round(clamped / TouchControlsOpacityPercentStep) *
               TouchControlsOpacityPercentStep;
    }

    /// <summary>把左右触控区限制为稳定百分比，确保中间始终至少保留 20% 宽度。</summary>
    private static float SanitizeControlZoneRatio(float value)
    {
        float clamped = Mathf.Clamp(value, MinimumControlZoneRatio, MaximumControlZoneRatio);
        return Mathf.Round(clamped / ControlZoneRatioStep) * ControlZoneRatioStep;
    }

    /// <summary>触屏布局只接受正式 HUD 已声明的稳定控件 ID。</summary>
    private static bool IsBuiltInMobileControlId(string controlId)
    {
        if (string.IsNullOrWhiteSpace(controlId))
            return false;

        for (int index = 0; index < BuiltInMobileControlIds.Length; index++)
        {
            if (string.Equals(BuiltInMobileControlIds[index], controlId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>组合触屏布局的 PlayerPrefs 稳定键。</summary>
    private static string GetMobileControlLayoutKey(string controlId, string axis)
    {
        return MobileControlLayoutPrefix + controlId + "." + axis;
    }

    /// <summary>判断是否存在任一内置触屏控件位置覆盖。</summary>
    private static bool HasMobileControlLayoutPreferences()
    {
        for (int index = 0; index < BuiltInMobileControlIds.Length; index++)
        {
            string controlId = BuiltInMobileControlIds[index];
            if (PlayerPrefs.HasKey(GetMobileControlLayoutKey(controlId, "X")) ||
                PlayerPrefs.HasKey(GetMobileControlLayoutKey(controlId, "Y")) ||
                PlayerPrefs.HasKey(GetMobileControlLayoutKey(controlId, "Size")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>删除全部内置触屏控件的位置覆盖；返回是否真的删除过数据。</summary>
    private static bool ClearMobileControlLayoutPreferences()
    {
        bool changed = false;
        for (int index = 0; index < BuiltInMobileControlIds.Length; index++)
        {
            string controlId = BuiltInMobileControlIds[index];
            string xKey = GetMobileControlLayoutKey(controlId, "X");
            string yKey = GetMobileControlLayoutKey(controlId, "Y");
            string sizeKey = GetMobileControlLayoutKey(controlId, "Size");
            if (PlayerPrefs.HasKey(sizeKey))
            {
                PlayerPrefs.DeleteKey(sizeKey);
                changed = true;
            }
            if (PlayerPrefs.HasKey(xKey))
            {
                PlayerPrefs.DeleteKey(xKey);
                changed = true;
            }
            if (PlayerPrefs.HasKey(yKey))
            {
                PlayerPrefs.DeleteKey(yKey);
                changed = true;
            }
        }

        return changed;
    }

    #endregion
}
