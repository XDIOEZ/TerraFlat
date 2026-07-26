// AI-Context: 全局 UI 用户偏好与 CanvasScaler 应用器；缩放通过参考分辨率实现，保持锚点语义并使用 Expand 防止宽高比导致界面出框。

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class UIUserSettings
{
    private const string ScaleKey = "FlatWorld.UI.Scale";
    private const string RespectSafeAreaKey = "FlatWorld.UI.RespectSafeArea";

    public const float DefaultScale = 1f;
    public const float MinimumScale = 0.75f;
    public const float MaximumScale = 1.2f;
    public const float ScaleStep = 0.05f;

    public static float Scale =>
        SanitizeScale(PlayerPrefs.GetFloat(ScaleKey, DefaultScale));

    public static bool RespectSafeArea =>
        PlayerPrefs.GetInt(RespectSafeAreaKey, 1) != 0;

    public static float SetScale(float value)
    {
        float sanitized = SanitizeScale(value);
        PlayerPrefs.SetFloat(ScaleKey, sanitized);
        PlayerPrefs.Save();
        UIScaleController.ApplyToAllLoadedCanvases();
        return sanitized;
    }

    public static void SetRespectSafeArea(bool value)
    {
        PlayerPrefs.SetInt(RespectSafeAreaKey, value ? 1 : 0);
        PlayerPrefs.Save();
        UIScaleController.ApplyToAllLoadedCanvases();
    }

    public static void ResetToDefaults()
    {
        PlayerPrefs.SetFloat(ScaleKey, DefaultScale);
        PlayerPrefs.SetInt(RespectSafeAreaKey, 1);
        PlayerPrefs.Save();
        UIScaleController.ApplyToAllLoadedCanvases();
    }

    private static float SanitizeScale(float value)
    {
        float clamped = Mathf.Clamp(value, MinimumScale, MaximumScale);
        return Mathf.Round(clamped / ScaleStep) * ScaleStep;
    }
}

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
    private float lastRequestedScale = -1f;
    private bool lastRespectSafeArea;

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
        CanvasScaler[] scalers = Object.FindObjectsOfType<CanvasScaler>(true);
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
        ApplyCurrentSettings();
    }

    private void OnEnable()
    {
        CaptureReferences();
        ApplyCurrentSettings();
    }

    private void Update()
    {
        if (lastScreenWidth != Screen.width ||
            lastScreenHeight != Screen.height ||
            lastSafeArea != Screen.safeArea ||
            !Mathf.Approximately(lastRequestedScale, UIUserSettings.Scale) ||
            lastRespectSafeArea != UIUserSettings.RespectSafeArea)
        {
            ApplyCurrentSettings();
        }
    }

    public void ApplyCurrentSettings()
    {
        CaptureReferences();
        if (canvasScaler == null || rootCanvas == null)
            return;

        float requestedScale = UIUserSettings.Scale;
        float safeAreaFactor = UIUserSettings.RespectSafeArea
            ? CalculateSafeAreaFactor()
            : 1f;
        float effectiveScale = Mathf.Max(0.5f, requestedScale * safeAreaFactor);

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasScaler.referenceResolution = baseReferenceResolution / effectiveScale;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastSafeArea = Screen.safeArea;
        lastRequestedScale = requestedScale;
        lastRespectSafeArea = UIUserSettings.RespectSafeArea;

        Canvas.ForceUpdateCanvases();
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

    private static float CalculateSafeAreaFactor()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
            return 1f;

        float widthRatio = Screen.safeArea.width / Screen.width;
        float heightRatio = Screen.safeArea.height / Screen.height;
        return Mathf.Clamp01(Mathf.Min(widthRatio, heightRatio));
    }
}
