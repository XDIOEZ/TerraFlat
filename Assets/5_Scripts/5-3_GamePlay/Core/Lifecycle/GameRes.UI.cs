using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 资源启动阶段的 UGUI 呈现：直接使用 WorldManager 引用的 Prefab，避免加载面板依赖尚未就绪的资源目录。
/// </summary>
public partial class GameRes
{
    #region 控件命名契约

    /// <summary>资源加载标题节点名。</summary>
    public const string ResourceLoadingTitleKey = "资源加载标题";

    /// <summary>资源加载状态节点名。</summary>
    public const string ResourceLoadingStatusKey = "资源加载状态";

    /// <summary>资源加载进度条节点名。</summary>
    public const string ResourceLoadingProgressKey = "资源加载进度";

    /// <summary>资源加载百分比节点名。</summary>
    public const string ResourceLoadingProgressTextKey = "资源加载进度文本";

    /// <summary>资源加载进度填充节点名。</summary>
    public const string ResourceLoadingProgressFillKey = "资源加载进度填充";

    #endregion

    #region 序列化配置

    /// <summary>启动阶段直接实例化的资源加载面板。</summary>
    [Header("资源加载界面")]
    [SerializeField] private GameObject resourceLoadingPrefab;

    #endregion

    #region 运行时状态

    /// <summary>已实例化的资源加载面板。</summary>
    private GameObject resourceLoadingView;

    /// <summary>资源加载面板的顶层画布。</summary>
    private Canvas resourceLoadingCanvas;

    /// <summary>资源加载面板的交互与透明度控制器。</summary>
    private CanvasGroup resourceLoadingCanvasGroup;

    /// <summary>资源加载标题文本。</summary>
    private TextMeshProUGUI resourceLoadingTitle;

    /// <summary>资源加载状态文本。</summary>
    private TextMeshProUGUI resourceLoadingStatus;

    /// <summary>资源加载百分比文本。</summary>
    private TextMeshProUGUI resourceLoadingProgressText;

    /// <summary>资源加载进度条。</summary>
    private Slider resourceLoadingProgress;

    /// <summary>资源加载进度填充图像。</summary>
    private Image resourceLoadingProgressFill;

    /// <summary>是否已经报告过一次 Prefab 配置错误。</summary>
    private bool resourceLoadingConfigurationErrorLogged;

    /// <summary>上一次呈现的失败状态。</summary>
    private bool lastPresentedFailure;

    /// <summary>上一次呈现的加载进度。</summary>
    private float lastPresentedProgress = -1f;

    /// <summary>上一次呈现的加载文案。</summary>
    private string lastPresentedStatus = string.Empty;

    #endregion

    #region 生命周期

    /// <summary>创建启动加载界面；视觉资源通过 Prefab 直接引用，不经过 GameRes 资源字典。</summary>
    private void InitializeResourceLoadingPresentation()
    {
        EnsureResourceLoadingView();
    }

    /// <summary>销毁加载界面并释放单例引用。</summary>
    protected override void OnDestroy()
    {
        DisposeResourceLoadingPresentation();
        base.OnDestroy();
    }

    #endregion

    #region 呈现更新

    /// <summary>仅在显示状态、进度或文字变化时刷新 UGUI，避免无意义的布局更新。</summary>
    private void RefreshResourceLoadingPresentation()
    {
        if (!showLoadingGUI)
        {
            if (resourceLoadingView != null && resourceLoadingView.activeSelf)
                resourceLoadingView.SetActive(false);
            return;
        }

        if (!EnsureResourceLoadingView())
            return;

        bool stateChanged = lastPresentedFailure != resourceLoadFailed;
        bool progressChanged = !Mathf.Approximately(lastPresentedProgress, loadingProgress);
        bool statusChanged = !string.Equals(lastPresentedStatus, loadingText, System.StringComparison.Ordinal);
        if (!resourceLoadingView.activeSelf)
            resourceLoadingView.SetActive(true);

        resourceLoadingCanvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 10;
        resourceLoadingCanvasGroup.alpha = 1f;
        resourceLoadingCanvasGroup.interactable = true;
        resourceLoadingCanvasGroup.blocksRaycasts = true;

        if (!stateChanged && !progressChanged && !statusChanged)
            return;

        Color accent = resourceLoadFailed
            ? new Color(0.76f, 0.33f, 0.29f, 1f)
            : new Color(0.26f, 0.61f, 0.57f, 1f);
        resourceLoadingTitle.text = resourceLoadFailed ? "资源加载失败" : "正在准备游戏";
        resourceLoadingTitle.color = resourceLoadFailed
            ? new Color(1f, 0.72f, 0.65f, 1f)
            : new Color(0.95f, 0.91f, 0.81f, 1f);
        resourceLoadingStatus.text = string.IsNullOrWhiteSpace(loadingText)
            ? "正在加载资源…"
            : loadingText;
        resourceLoadingProgress.value = Mathf.Clamp01(loadingProgress);
        resourceLoadingProgressText.text = $"{Mathf.RoundToInt(loadingProgress * 100f)}%";
        resourceLoadingProgressText.color = accent;
        resourceLoadingProgressFill.color = accent;

        lastPresentedFailure = resourceLoadFailed;
        lastPresentedProgress = loadingProgress;
        lastPresentedStatus = loadingText ?? string.Empty;
    }

    /// <summary>实例化并校验资源加载 Prefab 的控件命名契约。</summary>
    private bool EnsureResourceLoadingView()
    {
        if (resourceLoadingView != null)
            return true;

        if (resourceLoadingPrefab == null)
        {
            LogResourceLoadingConfigurationError("GameRes 未绑定 UI_ResourceLoading.prefab。");
            return false;
        }

        resourceLoadingView = Instantiate(resourceLoadingPrefab);
        resourceLoadingCanvas = resourceLoadingView.GetComponent<Canvas>();
        resourceLoadingCanvasGroup = resourceLoadingView.GetComponent<CanvasGroup>();
        resourceLoadingTitle = FindResourceLoadingChild(ResourceLoadingTitleKey)?.GetComponent<TextMeshProUGUI>();
        resourceLoadingStatus = FindResourceLoadingChild(ResourceLoadingStatusKey)?.GetComponent<TextMeshProUGUI>();
        resourceLoadingProgress = FindResourceLoadingChild(ResourceLoadingProgressKey)?.GetComponent<Slider>();
        resourceLoadingProgressText = FindResourceLoadingChild(ResourceLoadingProgressTextKey)?.GetComponent<TextMeshProUGUI>();
        resourceLoadingProgressFill = FindResourceLoadingChild(ResourceLoadingProgressFillKey)?.GetComponent<Image>();

        if (resourceLoadingCanvas == null || resourceLoadingCanvasGroup == null ||
            resourceLoadingTitle == null || resourceLoadingStatus == null ||
            resourceLoadingProgress == null || resourceLoadingProgressText == null ||
            resourceLoadingProgressFill == null)
        {
            Destroy(resourceLoadingView);
            resourceLoadingView = null;
            LogResourceLoadingConfigurationError("UI_ResourceLoading.prefab 的控件命名契约不完整。");
            return false;
        }

        DontDestroyOnLoad(resourceLoadingView);
        resourceLoadingConfigurationErrorLogged = false;
        return true;
    }

    /// <summary>按节点名查找资源加载 Prefab 的子控件。</summary>
    private Transform FindResourceLoadingChild(string childName)
    {
        if (resourceLoadingView == null)
            return null;

        Transform[] children = resourceLoadingView.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == childName)
                return children[i];
        }

        return null;
    }

    /// <summary>只记录一次加载界面配置错误，避免每帧刷屏。</summary>
    private void LogResourceLoadingConfigurationError(string message)
    {
        if (resourceLoadingConfigurationErrorLogged)
            return;

        resourceLoadingConfigurationErrorLogged = true;
        Debug.LogError($"[GameRes] {message}", this);
    }

    /// <summary>销毁独立加载界面，避免退出或编辑器重载后残留。</summary>
    private void DisposeResourceLoadingPresentation()
    {
        if (resourceLoadingView != null)
            Destroy(resourceLoadingView);

        resourceLoadingView = null;
        resourceLoadingCanvas = null;
        resourceLoadingCanvasGroup = null;
        resourceLoadingTitle = null;
        resourceLoadingStatus = null;
        resourceLoadingProgressText = null;
        resourceLoadingProgress = null;
        resourceLoadingProgressFill = null;
    }

    #endregion
}
