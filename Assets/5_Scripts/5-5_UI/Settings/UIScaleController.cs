// AI-Context: 根 Canvas 的统一界面缩放应用器，基准分辨率固定由正式 UI 根预制体提供。

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 把缓存的 UI 缩放与安全区偏好应用到根 Canvas。
/// 正式 UI 根固定以 1920×1080 为基准；首次自动接管只识别参考分辨率至少 1280×720 的屏幕画布。
/// 设置变更、根 RectTransform 尺寸变化及应用恢复时按事件刷新，不保留常驻 Update。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasScaler))]
public sealed class UIScaleController : MonoBehaviour
{
    #region 根画布缩放

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
        if (canvas == null)
            return null;

        UIScaleController controller = canvas.GetComponent<UIScaleController>();
        if (controller == null)
        {
            if (!CanManage(canvas))
                return null;
            controller = canvas.gameObject.AddComponent<UIScaleController>();
        }

        controller.ApplyCurrentSettings();
        return controller;
    }

    public static void ApplyToAllLoadedCanvases()
    {
        CanvasScaler[] scalers = UnityEngine.Object.FindObjectsOfType<CanvasScaler>(true);
        for (int i = 0; i < scalers.Length; i++)
        {
            Canvas canvas = scalers[i] != null ? scalers[i].GetComponent<Canvas>() : null;
            if (canvas == null)
                continue;

            UIScaleController controller = canvas.GetComponent<UIScaleController>();
            if (controller == null)
            {
                if (!CanManage(canvas))
                    continue;
                controller = canvas.gameObject.AddComponent<UIScaleController>();
            }
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

    /// <summary>把当前界面偏好应用到所属根 Canvas。</summary>
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
            float effectiveScale = UIUserSettings.Scale;

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

    #endregion
}
