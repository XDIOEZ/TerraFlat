using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 独立于 UIManager 与 Addressables 的运行时日志悬浮窗；折叠时只保留右上角入口，展开后显示有界日志快照。
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeDebugOverlay : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    #region 限制

    /// <summary>调试页最多展示的字符数。</summary>
    private const int DisplayCharacterLimit = 48 * 1024;

    /// <summary>首次运行时默认复制最近的日志条数。</summary>
    private const int DefaultCopyEntryCount = 50;

    /// <summary>复制条数设置的持久化键。</summary>
    private const string CopyEntryCountPreferenceKey = "FlatWorld.RuntimeDebugOverlay.CopyEntryCount";

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

    /// <summary>玩家输入的最近日志复制条数。</summary>
    [SerializeField] private TMP_InputField copyEntryCountInput;

    #endregion

    #region 运行时状态

    /// <summary>上一次呈现的内存日志版本。</summary>
    private int presentedLogVersion = -1;

    /// <summary>当前是否正在拖动常驻日志入口。</summary>
    private bool isDraggingToggle;

    /// <summary>按下位置到按钮轴心的父级局部坐标偏移。</summary>
    private Vector2 toggleDragOffset;

    /// <summary>当前生效的最近日志复制条数。</summary>
    private int copyEntryCount;

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
        // 独立 Canvas 不经过 UIManager 面板动画，由自身建立可见缩放不变量。
        transform.localScale = Vector3.one;

        Canvas rootCanvas = GetComponent<Canvas>();
        if (rootCanvas != null)
            rootCanvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 20;

        copyEntryCount = Mathf.Clamp(
            PlayerPrefs.GetInt(CopyEntryCountPreferenceKey, DefaultCopyEntryCount),
            1,
            GameLogManager.RuntimeLogCapacity);
        RefreshCopyEntryCountInput();
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
        BindCopyEntryCountInput();
    }

    /// <summary>解除悬浮窗按钮事件，避免禁用或销毁后的重复回调。</summary>
    private void OnDisable()
    {
        isDraggingToggle = false;
        UnbindButton(toggleButton, TogglePanel);
        UnbindButton(copyButton, CopyLogs);
        UnbindButton(clearButton, ClearLogs);
        UnbindButton(closeButton, ClosePanel);
        UnbindCopyEntryCountInput();
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

    #region 悬浮入口拖拽

    /// <summary>只接受从日志入口开始的左键或单指拖拽，并取消本次点击以免误展开。</summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!TryGetToggleDragContext(eventData, eventData?.pressPosition ?? Vector2.zero, true,
                out RectTransform toggleRect, out RectTransform boundsRect, out Vector2 pointerPosition))
        {
            return;
        }

        isDraggingToggle = true;
        toggleDragOffset = GetPivotPositionInParent(toggleRect, boundsRect) - pointerPosition;
        eventData.eligibleForClick = false;
    }

    /// <summary>让日志入口跟随指针移动，并始终限制在设备安全区内。</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!isDraggingToggle ||
            !TryGetToggleDragContext(eventData, eventData.position, false,
                out RectTransform toggleRect, out RectTransform boundsRect, out Vector2 pointerPosition))
        {
            return;
        }

        SetPivotPositionInParent(toggleRect, boundsRect, pointerPosition + toggleDragOffset);
        ClampToggleToBounds(toggleRect, boundsRect);
    }

    /// <summary>结束拖拽并再次校正边界；普通点击仍由 Button 负责展开页面。</summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDraggingToggle)
            return;

        isDraggingToggle = false;
        if (toggleButton == null || !(toggleButton.transform is RectTransform toggleRect) ||
            !(toggleRect.parent is RectTransform boundsRect))
        {
            return;
        }

        ClampToggleToBounds(toggleRect, boundsRect);
    }

    /// <summary>解析日志入口、安全区与指针局部坐标，拒绝其它面板控件发起的拖拽。</summary>
    private bool TryGetToggleDragContext(
        PointerEventData eventData,
        Vector2 screenPosition,
        bool requirePressInside,
        out RectTransform toggleRect,
        out RectTransform boundsRect,
        out Vector2 pointerPosition)
    {
        toggleRect = toggleButton != null ? toggleButton.transform as RectTransform : null;
        boundsRect = toggleRect != null ? toggleRect.parent as RectTransform : null;
        pointerPosition = default;
        return eventData != null &&
               eventData.button == PointerEventData.InputButton.Left &&
               toggleRect != null &&
               boundsRect != null &&
               (!requirePressInside ||
                RectTransformUtility.RectangleContainsScreenPoint(
                    toggleRect,
                    eventData.pressPosition,
                    eventData.pressEventCamera)) &&
               RectTransformUtility.ScreenPointToLocalPointInRectangle(
                   boundsRect,
                   screenPosition,
                   eventData.pressEventCamera,
                   out pointerPosition);
    }

    /// <summary>计算按钮轴心在安全区父节点中的局部位置。</summary>
    private static Vector2 GetPivotPositionInParent(RectTransform rect, RectTransform parent)
    {
        return GetAnchorPositionInParent(rect, parent) + rect.anchoredPosition;
    }

    /// <summary>把目标轴心位置换算回 RectTransform 的锚点坐标。</summary>
    private static void SetPivotPositionInParent(RectTransform rect, RectTransform parent, Vector2 pivotPosition)
    {
        rect.anchoredPosition = pivotPosition - GetAnchorPositionInParent(rect, parent);
    }

    /// <summary>取得固定锚点在父节点局部坐标中的位置。</summary>
    private static Vector2 GetAnchorPositionInParent(RectTransform rect, RectTransform parent)
    {
        Rect parentRect = parent.rect;
        return new Vector2(
            Mathf.Lerp(parentRect.xMin, parentRect.xMax, rect.anchorMin.x),
            Mathf.Lerp(parentRect.yMin, parentRect.yMax, rect.anchorMin.y));
    }

    /// <summary>把按钮完整限制在安全区内，不允许拖到屏幕外。</summary>
    private static void ClampToggleToBounds(RectTransform rect, RectTransform parent)
    {
        Rect bounds = parent.rect;
        Rect localRect = rect.rect;
        Vector2 pivotPosition = GetPivotPositionInParent(rect, parent);
        float width = localRect.width * Mathf.Abs(rect.localScale.x);
        float height = localRect.height * Mathf.Abs(rect.localScale.y);
        pivotPosition.x = Mathf.Clamp(
            pivotPosition.x,
            bounds.xMin + width * rect.pivot.x,
            bounds.xMax - width * (1f - rect.pivot.x));
        pivotPosition.y = Mathf.Clamp(
            pivotPosition.y,
            bounds.yMin + height * rect.pivot.y,
            bounds.yMax - height * (1f - rect.pivot.y));
        SetPivotPositionInParent(rect, parent, pivotPosition);
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

    /// <summary>按照玩家设置复制最近若干条完整日志。</summary>
    private void CopyLogs()
    {
        ApplyCopyEntryCount(copyEntryCountInput != null ? copyEntryCountInput.text : copyEntryCount.ToString());
        string snapshot = GameLogManager.GetRuntimeLogSnapshotByEntryCount(
            copyEntryCount,
            out int copiedEntries,
            out int collapsedEntries,
            out bool truncated);
        GUIUtility.systemCopyBuffer = snapshot;
        if (statusText != null)
        {
            string collapsedSummary = collapsedEntries > 0
                ? $"，合并 {collapsedEntries} 条重复记录"
                : string.Empty;
            statusText.text = truncated
                ? $"已复制最近 {copiedEntries} 个日志槽位{collapsedSummary}，较早类型已省略。"
                : $"已复制当前 {copiedEntries} 个日志槽位{collapsedSummary}。";
        }
    }

    /// <summary>校验、保存并回显玩家设置的日志复制条数。</summary>
    private void ApplyCopyEntryCount(string rawValue)
    {
        int selectedCount = copyEntryCount;
        if (int.TryParse(rawValue, out int parsedCount))
            selectedCount = parsedCount;

        copyEntryCount = Mathf.Clamp(selectedCount, 1, GameLogManager.RuntimeLogCapacity);
        RefreshCopyEntryCountInput();
        PlayerPrefs.SetInt(CopyEntryCountPreferenceKey, copyEntryCount);
        PlayerPrefs.Save();
        if (statusText != null)
            statusText.text = $"复制时将取最近 {copyEntryCount} 个去重日志槽位。";
    }

    /// <summary>把当前复制条数同步到输入框且不触发重复回调。</summary>
    private void RefreshCopyEntryCountInput()
    {
        if (copyEntryCountInput != null)
            copyEntryCountInput.SetTextWithoutNotify(copyEntryCount.ToString());
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
            statusText.text = "页面只显示最近内容；复制条数可在顶部手动设置。";

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

    /// <summary>幂等绑定复制条数输入事件。</summary>
    private void BindCopyEntryCountInput()
    {
        if (copyEntryCountInput == null)
            return;

        copyEntryCountInput.onEndEdit.RemoveListener(ApplyCopyEntryCount);
        copyEntryCountInput.onEndEdit.AddListener(ApplyCopyEntryCount);
    }

    /// <summary>解除复制条数输入事件。</summary>
    private void UnbindCopyEntryCountInput()
    {
        if (copyEntryCountInput != null)
            copyEntryCountInput.onEndEdit.RemoveListener(ApplyCopyEntryCount);
    }

    #endregion
}
