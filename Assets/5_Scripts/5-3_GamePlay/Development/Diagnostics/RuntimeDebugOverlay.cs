using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 独立于 UIManager 与 Addressables 的运行时日志悬浮窗；折叠时只保留右上角入口，展开后显示有界日志快照。
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeDebugOverlay : MonoBehaviour
{
    #region 限制

    /// <summary>调试页最多展示的字符数。</summary>
    private const int DisplayCharacterLimit = 48 * 1024;

    /// <summary>单次复制到系统剪贴板的最大字符数。</summary>
    private const int ClipboardCharacterLimit = 12 * 1024;

    #endregion

    #region 单例状态

    /// <summary>当前跨场景存活的调试悬浮窗。</summary>
    private static RuntimeDebugOverlay instance;

    /// <summary>当前是否已经存在有效的调试悬浮窗。</summary>
    public static bool HasInstance => instance != null;

    #endregion

    #region Prefab 引用

    /// <summary>展开后的调试页面。</summary>
    [SerializeField] private GameObject debugPanel;

    /// <summary>展开或收起调试页面的悬浮按钮。</summary>
    [SerializeField] private Button toggleButton;

    /// <summary>复制当前日志快照的按钮。</summary>
    [SerializeField] private Button copyButton;

    /// <summary>清空内存日志的按钮。</summary>
    [SerializeField] private Button clearButton;

    /// <summary>关闭调试页面的按钮。</summary>
    [SerializeField] private Button closeButton;

    /// <summary>悬浮按钮上的日志数量文本。</summary>
    [SerializeField] private TextMeshProUGUI toggleLabel;

    /// <summary>调试页头部的日志级别摘要。</summary>
    [SerializeField] private TextMeshProUGUI summaryText;

    /// <summary>滚动区域内的日志正文。</summary>
    [SerializeField] private TextMeshProUGUI logText;

    /// <summary>复制、清空等操作的结果反馈。</summary>
    [SerializeField] private TextMeshProUGUI statusText;

    /// <summary>日志正文滚动视图。</summary>
    [SerializeField] private ScrollRect logScrollRect;

    #endregion

    #region 运行时状态

    /// <summary>上一次呈现的内存日志版本。</summary>
    private int presentedLogVersion = -1;

    #endregion

    #region 生命周期

    /// <summary>建立跨场景唯一实例，并在首次渲染前隐藏展开页。</summary>
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        Canvas rootCanvas = GetComponent<Canvas>();
        if (rootCanvas != null)
            rootCanvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 20;

        SetPanelVisible(false);
        RefreshPresentation(force: true);
    }

    /// <summary>绑定悬浮窗按钮事件，重复启用时先移除旧监听。</summary>
    private void OnEnable()
    {
        BindButton(toggleButton, TogglePanel);
        BindButton(copyButton, CopyLogs);
        BindButton(clearButton, ClearLogs);
        BindButton(closeButton, ClosePanel);
    }

    /// <summary>解除悬浮窗按钮事件，避免禁用或销毁后的重复回调。</summary>
    private void OnDisable()
    {
        UnbindButton(toggleButton, TogglePanel);
        UnbindButton(copyButton, CopyLogs);
        UnbindButton(clearButton, ClearLogs);
        UnbindButton(closeButton, ClosePanel);
    }

    /// <summary>清理跨场景单例引用。</summary>
    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>仅在日志版本变化时刷新摘要；正文只在页面展开时重建。</summary>
    private void Update()
    {
        RefreshPresentation(force: false);
    }

    #endregion

    #region 按钮行为

    /// <summary>切换调试页面的展开状态。</summary>
    private void TogglePanel()
    {
        SetPanelVisible(debugPanel == null || !debugPanel.activeSelf);
    }

    /// <summary>关闭展开页但保留悬浮入口。</summary>
    private void ClosePanel()
    {
        SetPanelVisible(false);
    }

    /// <summary>复制最近日志，并严格执行单次剪贴板字符上限。</summary>
    private void CopyLogs()
    {
        string snapshot = GameLogManager.GetRuntimeLogSnapshot(ClipboardCharacterLimit, out bool truncated);
        GUIUtility.systemCopyBuffer = snapshot;
        if (statusText != null)
        {
            statusText.text = truncated
                ? $"已复制最近 {snapshot.Length} 个字符，较早内容已省略。"
                : $"已复制 {snapshot.Length} 个字符。";
        }
    }

    /// <summary>清空面板内存缓冲并保留磁盘会话日志。</summary>
    private void ClearLogs()
    {
        GameLogManager.ClearRuntimeLogBuffer();
        if (statusText != null)
            statusText.text = "面板日志已清空，磁盘日志仍然保留。";
        RefreshPresentation(force: true);
    }

    #endregion

    #region 呈现

    /// <summary>切换页面显隐，并在展开时立即生成最新日志正文。</summary>
    private void SetPanelVisible(bool visible)
    {
        if (debugPanel == null)
            return;

        debugPanel.SetActive(visible);
        if (visible)
            RefreshPresentation(force: true);
    }

    /// <summary>刷新悬浮入口、数量摘要以及展开状态下的日志正文。</summary>
    private void RefreshPresentation(bool force)
    {
        int currentVersion = GameLogManager.RuntimeLogVersion;
        if (!force && currentVersion == presentedLogVersion)
            return;

        presentedLogVersion = currentVersion;
        GameLogManager.GetRuntimeLogCounts(out int total, out int warnings, out int errors);
        if (toggleLabel != null)
        {
            toggleLabel.text = errors > 0
                ? $"日志  E:{errors}"
                : warnings > 0
                    ? $"日志  W:{warnings}"
                    : $"日志  {total}";
        }

        if (summaryText != null)
            summaryText.text = $"共 {total} 条    错误 {errors}    警告 {warnings}";

        if (debugPanel != null && debugPanel.activeSelf)
            RefreshLogText();
    }

    /// <summary>重建有界日志正文并把滚动位置移动到最新内容。</summary>
    private void RefreshLogText()
    {
        if (logText == null)
            return;

        logText.text = GameLogManager.GetRuntimeLogSnapshot(DisplayCharacterLimit, out bool truncated);
        if (truncated && statusText != null)
            statusText.text = "页面只显示最近日志；复制按钮最多复制最近 12 KiB。";

        LayoutRebuilder.MarkLayoutForRebuild(logText.rectTransform);
        if (logScrollRect != null)
            logScrollRect.verticalNormalizedPosition = 0f;
    }

    #endregion

    #region 事件绑定

    /// <summary>幂等绑定按钮事件。</summary>
    private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    /// <summary>解除指定按钮事件。</summary>
    private static void UnbindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.RemoveListener(action);
    }

    #endregion
}
