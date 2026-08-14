using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 管理设置页的按键绑定入口、模态面板和可复用绑定行。
/// 绑定行只在容量不足时实例化；取消按键仅在面板打开期间轮询，关闭时立即释放玩法输入租约。
/// </summary>
[DisallowMultipleComponent]
public sealed class InputBindingPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "按键绑定";
    private static readonly Vector2 PreferredDialogSize = new Vector2(920f, 820f);
    private const float DialogSafeMargin = 64f;

    private sealed class BindingRow
    {
        public GameObject Root;
        public InputBindingEntry Entry;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI BindingText;
        public Button RebindButton;
        public Button ClearButton;
    }

    private readonly List<BindingRow> rows = new List<BindingRow>();
    private readonly Stack<BindingRow> pooledRows = new Stack<BindingRow>();

    private GameController gameController;
    private InputBindingService bindingService;
    private Button entryButton;
    private BasePanel bindingPanel;
    private RectTransform dialogRect;
    private Transform content;
    private GameObject rowPrefab;
    private TextMeshProUGUI statusText;
    private Button keyboardMouseTabButton;
    private Button gamepadTabButton;
    private InputBindingDeviceGroup currentDeviceGroup = InputBindingDeviceGroup.KeyboardMouse;
    private bool panelSuspendedInput;
    private int suppressEscapeCloseFrame = -1;

    /// <summary>当前页面实际显示的绑定行数量。</summary>
    public int ActiveRowCount => rows.Count;

    /// <summary>当前保留的绑定行总数，供 Profiler 检查是否发生重复实例化。</summary>
    public int RetainedRowCount => rows.Count + pooledRows.Count;

    #region 初始化与面板生命周期

    public static InputBindingPanelLauncher Ensure(
        Transform settingsPanel,
        GameController gameController)
    {
        if (settingsPanel == null)
            return null;

        InputBindingPanelLauncher launcher =
            settingsPanel.GetComponent<InputBindingPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<InputBindingPanelLauncher>();

        launcher.Initialize(gameController);
        launcher.EnsureEntryButton();
        // 入口按钮仍可调用禁用组件的方法；只有子面板打开期间才需要轮询取消按键。
        launcher.enabled = launcher.bindingPanel != null && launcher.bindingPanel.IsVisible();
        return launcher;
    }

    private void Initialize(GameController controller)
    {
        InputBindingService nextService = controller != null ? controller.InputBindings : null;
        if (ReferenceEquals(gameController, controller) &&
            ReferenceEquals(bindingService, nextService))
        {
            return;
        }

        if (bindingService != null)
            bindingService.BindingsChanged -= RefreshRows;

        ReleaseInputLock();
        gameController = controller;
        bindingService = nextService;

        if (bindingService != null)
            bindingService.BindingsChanged += RefreshRows;

        if (bindingPanel != null && bindingPanel.IsVisible())
            RebuildRows();
    }

    private void EnsureEntryButton()
    {
        if (entryButton == null)
            entryButton = FindButton(transform, EntryButtonName);

        if (entryButton == null)
        {
            Debug.LogError(
                $"[InputBindingPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

    private void Update()
    {
        if (bindingPanel == null || !bindingPanel.IsVisible() || bindingService == null)
            return;

        if (bindingService.IsRebinding || Time.frameCount == suppressEscapeCloseFrame)
            return;

        bool keyboardCanceled = Keyboard.current != null &&
                                Keyboard.current.escapeKey.wasPressedThisFrame;
        bool gamepadCanceled = Gamepad.current != null &&
                               Gamepad.current.buttonEast.wasPressedThisFrame;
        if (keyboardCanceled || gamepadCanceled)
            Close();
    }

    private void Open()
    {
        if (bindingService == null)
        {
            Debug.LogError(
                "[InputBindingPanelLauncher] GameController 尚未准备好按键绑定服务。",
                this);
            return;
        }

        EnsurePanel();
        if (bindingPanel == null)
            return;

        if (!panelSuspendedInput)
        {
            gameController?.AcquireGameplayInputLock(this);
            bindingService.SuspendGameplayInput();
            panelSuspendedInput = true;
        }

        UpdateDialogSize();
        RebuildRows();
        SetStatus(GetDevicePageHint());
        bindingPanel.Open();
        bindingPanel.transform.SetAsLastSibling();
        RequestLocalLayoutRebuild();
        enabled = true;
    }

    private void Close()
    {
        if (bindingService != null && bindingService.IsRebinding)
            bindingService.CancelActiveRebind();

        bindingPanel?.Close();
        ReleaseInputLock();
        enabled = false;
    }

    private void EnsurePanel()
    {
        if (bindingPanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.InputBindingSettings);
        rowPrefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.InputBindingRow);
        if (prefab == null || rowPrefab == null)
        {
            Debug.LogError(
                $"[InputBindingPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.InputBindingSettings} / {RuntimeUIPrefabKeys.InputBindingRow}。",
                this);
            return;
        }

        bindingPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.InputBindingSettings);
        dialogRect = FindTransform(bindingPanel.transform, "按键绑定面板") as RectTransform;
        content = FindTransform(bindingPanel.transform, "Content");
        statusText = bindingPanel.GetText("状态文本");
        keyboardMouseTabButton = bindingPanel.GetButton("键鼠分页按钮");
        gamepadTabButton = bindingPanel.GetButton("手柄分页按钮");

        bindingPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        bindingPanel.GetButton("恢复默认按钮")?.onClick.AddListener(ResetToDefaults);
        bindingPanel.GetButton("完成按钮")?.onClick.AddListener(Close);
        keyboardMouseTabButton?.onClick.AddListener(ShowKeyboardMouseBindings);
        gamepadTabButton?.onClick.AddListener(ShowGamepadBindings);
        bindingPanel.Closed += HandlePanelClosed;

        if (dialogRect == null || content == null || statusText == null ||
            keyboardMouseTabButton == null || gamepadTabButton == null)
        {
            Debug.LogError("[InputBindingPanelLauncher] 按键绑定 Prefab 控件命名契约不完整。", bindingPanel);
            bindingPanel.Close();
            return;
        }

        SetDeviceGroup(InputBindingDeviceGroup.KeyboardMouse);
        bindingPanel.PrepareForGamepadNavigation("键鼠分页按钮", false);
        UpdateDialogSize();
        bindingPanel.Close();
    }

    #endregion

    #region 绑定行复用

    /// <summary>按当前设备页复用现有行；切页只改变数据和显隐，不再销毁 GameObject。</summary>
    private void RebuildRows()
    {
        ReleaseActiveRows();
        if (bindingService == null || content == null || rowPrefab == null)
            return;

        IReadOnlyList<InputBindingEntry> entries =
            bindingService.GetEntries(currentDeviceGroup);
        bool createdRow = false;
        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingEntry entry = entries[i];
            BindingRow row = AcquireRow(ref createdRow);
            if (row == null)
                continue;

            row.Entry = entry;
            row.Root.name =
                $"绑定项_{currentDeviceGroup}_{entry.Action?.name ?? i.ToString()}_{entry.BindingIndex}";
            row.Root.transform.SetSiblingIndex(i);
            row.Label.text = FlatWorldLocalizationService.GetUiText(entry.DisplayName);
            row.BindingText.text = bindingService.GetBindingDisplayString(entry);
            row.Root.SetActive(true);
            rows.Add(row);
        }

        UpdateTabVisuals();
        RequestLocalLayoutRebuild();

        // 只有新增槽位才重建层级快照；纯复用只刷新导航状态。
        if (createdRow)
            bindingPanel?.RefreshUIComponents();
        else
            bindingPanel?.RefreshGamepadNavigationState();

        if (bindingPanel != null && bindingPanel.IsVisible())
            bindingPanel.PrepareForGamepadNavigation("修改按钮", false);
    }

    private BindingRow AcquireRow(ref bool createdRow)
    {
        BindingRow row = null;
        while (pooledRows.Count > 0 && row == null)
        {
            BindingRow candidate = pooledRows.Pop();
            if (candidate?.Root != null)
                row = candidate;
        }

        if (row == null)
        {
            row = CreateRow();
            createdRow |= row != null;
        }

        if (row?.Root == null)
            return null;

        row.Root.transform.SetParent(content, false);
        return row;
    }

    private BindingRow CreateRow()
    {
        GameObject rowObject = Instantiate(rowPrefab, content, false);
        rowObject.SetActive(false);

        BindingRow row = new BindingRow
        {
            Root = rowObject,
            Label = FindText(rowObject.transform, "操作名称"),
            BindingText = FindText(rowObject.transform, "绑定值"),
            RebindButton = FindButton(rowObject.transform, "修改按钮"),
            ClearButton = FindButton(rowObject.transform, "清除按钮")
        };
        if (row.Label == null || row.BindingText == null ||
            row.RebindButton == null || row.ClearButton == null)
        {
            Debug.LogError(
                "[InputBindingPanelLauncher] 按键绑定行 Prefab 控件命名契约不完整。",
                rowObject);
            Destroy(rowObject);
            return null;
        }

        row.RebindButton.onClick.AddListener(() => BeginRebind(row));
        row.ClearButton.onClick.AddListener(() => ClearBinding(row));
        return row;
    }

    private void ReleaseActiveRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            BindingRow row = rows[i];
            if (row?.Root == null)
                continue;

            row.Root.SetActive(false);
            row.Entry = null;
            pooledRows.Push(row);
        }

        rows.Clear();
    }

    private void RequestLocalLayoutRebuild()
    {
        if (content is RectTransform contentRect)
            LayoutRebuilder.MarkLayoutForRebuild(contentRect);
        if (dialogRect != null)
            LayoutRebuilder.MarkLayoutForRebuild(dialogRect);
    }

    #endregion

    #region 分页与绑定操作

    private void ShowKeyboardMouseBindings()
    {
        SetDeviceGroup(InputBindingDeviceGroup.KeyboardMouse);
    }

    private void ShowGamepadBindings()
    {
        SetDeviceGroup(InputBindingDeviceGroup.Gamepad);
    }

    private void SetDeviceGroup(InputBindingDeviceGroup deviceGroup)
    {
        if (bindingService != null && bindingService.IsRebinding)
            return;

        currentDeviceGroup = deviceGroup;
        RebuildRows();
        SetStatus(GetDevicePageHint());
    }

    private void UpdateTabVisuals()
    {
        SetTabVisual(
            keyboardMouseTabButton,
            currentDeviceGroup == InputBindingDeviceGroup.KeyboardMouse);
        SetTabVisual(
            gamepadTabButton,
            currentDeviceGroup == InputBindingDeviceGroup.Gamepad);
    }

    private static void SetTabVisual(Button button, bool selected)
    {
        if (button?.targetGraphic != null)
        {
            button.targetGraphic.color = selected
                ? FlatWorldUITheme.Accent
                : FlatWorldUITheme.Surface;
        }
    }

    private void BeginRebind(BindingRow row)
    {
        if (bindingService == null || row == null)
            return;

        SetRowsInteractable(false);
        row.BindingText.text = FlatWorldLocalizationService.GetUiText("等待输入…");
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "正在修改“{0}”；Esc / 手柄 B 取消，绑定该键时改用 Backspace / Start。",
                FlatWorldLocalizationService.GetUiText(row.Entry.DisplayName)));
        bindingService.BeginInteractiveRebind(row.Entry, result =>
        {
            SetRowsInteractable(true);
            RefreshRows();

            switch (result.Status)
            {
                case InputRebindStatus.Completed:
                    SetStatus(
                        FlatWorldLocalizationService.GetUiFormat(
                            "“{0}”已保存。",
                            FlatWorldLocalizationService.GetUiText(row.Entry.DisplayName)));
                    break;
                case InputRebindStatus.Canceled:
                    suppressEscapeCloseFrame = Time.frameCount;
                    SetStatus(FlatWorldLocalizationService.GetUiText("已取消本次修改。"));
                    break;
                case InputRebindStatus.Conflict:
                    SetStatus(
                        FlatWorldLocalizationService.GetUiFormat(
                            "该按键已用于“{0}”，未作修改。",
                            FlatWorldLocalizationService.GetUiText(
                                result.ConflictingEntry?.DisplayName ?? "其他操作")),
                        true);
                    break;
                default:
                    SetStatus(
                        FlatWorldLocalizationService.GetUiFormat(
                            "修改失败：{0}",
                            result.Exception?.Message ??
                            FlatWorldLocalizationService.GetUiText("未知错误")),
                        true);
                    break;
            }
        });
    }

    /// <summary>清除当前设备页选中操作的绑定，并让空绑定立即显示为未绑定。</summary>
    private void ClearBinding(BindingRow row)
    {
        if (bindingService == null || row == null || bindingService.IsRebinding)
            return;

        if (!bindingService.ClearBinding(row.Entry))
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiText("清除绑定失败。"),
                true);
            return;
        }

        RefreshRows();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "“{0}”的绑定已清除。",
                FlatWorldLocalizationService.GetUiText(row.Entry.DisplayName)));
    }

    private void ResetToDefaults()
    {
        if (bindingService == null)
            return;

        bindingService.ResetToDefaults(currentDeviceGroup);
        RefreshRows();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "{0}绑定已恢复默认值。",
                FlatWorldLocalizationService.GetUiText(GetDevicePageName())));
    }

    private void RefreshRows()
    {
        if (bindingService == null)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            BindingRow row = rows[i];
            if (row?.BindingText != null)
            {
                row.BindingText.text =
                    bindingService.GetBindingDisplayString(row.Entry);
            }
        }
    }

    private void SetRowsInteractable(bool interactable)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i]?.RebindButton != null)
                rows[i].RebindButton.interactable = interactable;
            if (rows[i]?.ClearButton != null)
                rows[i].ClearButton.interactable = interactable;
        }

        if (keyboardMouseTabButton != null)
            keyboardMouseTabButton.interactable = interactable;
        if (gamepadTabButton != null)
            gamepadTabButton.interactable = interactable;
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError
            ? new Color(1f, 0.48f, 0.35f)
            : new Color(0.69f, 0.78f, 0.79f);
    }

    private string GetDevicePageHint()
    {
        return FlatWorldLocalizationService.GetUiFormat(
            "当前：{0}。选择一项后输入新控制；冲突会被拦截并自动保存。",
            FlatWorldLocalizationService.GetUiText(GetDevicePageName()));
    }

    private string GetDevicePageName()
    {
        return currentDeviceGroup == InputBindingDeviceGroup.Gamepad
            ? "手柄"
            : "键鼠";
    }

    #endregion

    #region 布局、输入租约与清理

    private void UpdateDialogSize()
    {
        if (dialogRect == null)
            return;

        Canvas canvas = dialogRect.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null
            ? canvas.transform as RectTransform
            : null;
        Vector2 canvasSize = canvasRect != null
            ? canvasRect.rect.size
            : new Vector2(Screen.width, Screen.height);
        Vector2 available = new Vector2(
            Mathf.Max(1f, canvasSize.x - DialogSafeMargin),
            Mathf.Max(1f, canvasSize.y - DialogSafeMargin));

        dialogRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Min(PreferredDialogSize.x, available.x));
        dialogRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            Mathf.Min(PreferredDialogSize.y, available.y));
    }

    private void ReleaseInputLock()
    {
        if (!panelSuspendedInput)
            return;

        bindingService?.ResumeGameplayInput();
        gameController?.ReleaseGameplayInputLock(this);
        panelSuspendedInput = false;
    }

    /// <summary>面板被全局取消路由关闭时也必须释放玩法输入租约。</summary>
    private void HandlePanelClosed()
    {
        ReleaseInputLock();
        enabled = false;
    }

    private void OnDestroy()
    {
        if (bindingService != null)
        {
            bindingService.BindingsChanged -= RefreshRows;
            if (bindingService.IsRebinding)
                bindingService.CancelActiveRebind();
        }

        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (keyboardMouseTabButton != null)
            keyboardMouseTabButton.onClick.RemoveListener(ShowKeyboardMouseBindings);
        if (gamepadTabButton != null)
            gamepadTabButton.onClick.RemoveListener(ShowGamepadBindings);
        if (bindingPanel != null)
            bindingPanel.Closed -= HandlePanelClosed;
        ReleaseInputLock();
        if (bindingPanel != null)
            Destroy(bindingPanel.gameObject);
    }

    #endregion

    #region 组件查询

    private static Transform FindTransform(Transform root, string objectName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == objectName)
                return transforms[i];
        }

        return null;
    }

    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == buttonName)
                return buttons[i];
        }

        return null;
    }

    private static TextMeshProUGUI FindText(Transform root, string textName)
    {
        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == textName)
                return texts[i];
        }

        return null;
    }

    #endregion
}
