using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理内嵌按键绑定页与可复用绑定行。
/// 浏览页面复用主设置面板的玩法输入锁，只有交互式重绑由 InputBindingService 暂停 ActionMap。
/// </summary>
[DisallowMultipleComponent]
public sealed class InputBindingPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    /// <summary>保存一条可复用绑定行的控件引用。</summary>
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
    private BasePanel parentPanel;
    private RectTransform pageRect;
    private Transform content;
    private GameObject rowPrefab;
    private TextMeshProUGUI statusText;
    private TMP_Dropdown controlModeDropdown;
    private Button keyboardMouseTabButton;
    private Button gamepadTabButton;
    private Button resetButton;
    private InputBindingDeviceGroup currentDeviceGroup = InputBindingDeviceGroup.KeyboardMouse;
    private bool controlsBound;

    /// <summary>当前页面实际显示的绑定行数量。</summary>
    public int ActiveRowCount => rows.Count;

    /// <summary>当前保留的绑定行总数，供 Profiler 检查是否发生重复实例化。</summary>
    public int RetainedRowCount => rows.Count + pooledRows.Count;

    #region 初始化与页面生命周期

    /// <summary>在指定内嵌页上建立唯一按键绑定器。</summary>
    public static InputBindingPanelLauncher Ensure(
        Transform pageRoot,
        BasePanel ownerPanel,
        GameController controller)
    {
        if (pageRoot == null)
            return null;

        InputBindingPanelLauncher launcher =
            pageRoot.GetComponent<InputBindingPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<InputBindingPanelLauncher>();

        launcher.Initialize(ownerPanel, controller);
        return launcher;
    }

    /// <summary>绑定本页控件，并接入当前玩家的输入绑定服务。</summary>
    private void Initialize(BasePanel ownerPanel, GameController controller)
    {
        parentPanel = ownerPanel;
        BindPageControls();

        InputBindingService nextService = controller != null ? controller.InputBindings : null;
        if (!ReferenceEquals(bindingService, nextService))
        {
            if (bindingService != null)
                bindingService.BindingsChanged -= RefreshRows;

            if (bindingService != null && bindingService.IsRebinding)
                bindingService.CancelActiveRebind();

            gameController = controller;
            bindingService = nextService;
            if (bindingService != null)
                bindingService.BindingsChanged += RefreshRows;
        }
        else
        {
            gameController = controller;
        }

        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
    }

    /// <summary>只在按键页根节点内取得控件，避免命中下拉模板的同名节点。</summary>
    private void BindPageControls()
    {
        if (controlsBound)
            return;

        controlsBound = true;
        pageRect = transform as RectTransform;
        Transform bindingList = FindTransform(transform, "绑定列表");
        ScrollRect bindingScrollRect = bindingList != null
            ? bindingList.GetComponent<ScrollRect>()
            : null;
        content = bindingScrollRect != null ? bindingScrollRect.content : null;
        statusText = FindText(transform, "状态文本");
        controlModeDropdown = FindDropdown(transform, "控制模式下拉列表");
        keyboardMouseTabButton = FindButton(transform, "键鼠分页按钮");
        gamepadTabButton = FindButton(transform, "手柄分页按钮");
        resetButton = FindButton(transform, "恢复默认按钮");
        rowPrefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.InputBindingRow);

        controlModeDropdown?.onValueChanged.AddListener(HandleControlModeChanged);
        keyboardMouseTabButton?.onClick.AddListener(ShowKeyboardMouseBindings);
        gamepadTabButton?.onClick.AddListener(ShowGamepadBindings);
        resetButton?.onClick.AddListener(ResetToDefaults);

        if (bindingList == null || bindingScrollRect == null || content == null ||
            statusText == null || controlModeDropdown == null ||
            keyboardMouseTabButton == null || gamepadTabButton == null ||
            resetButton == null)
        {
            Debug.LogError(
                "[InputBindingPanelLauncher] 内嵌按键绑定页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>页面显示时刷新当前控制方式、绑定行与导航快照。</summary>
    public void OnSettingsPageShown()
    {
        if (bindingService == null)
        {
            Debug.LogError(
                "[InputBindingPanelLauncher] GameController 尚未准备好按键绑定服务。",
                this);
            return;
        }

        if (rowPrefab == null)
            rowPrefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.InputBindingRow);
        if (rowPrefab == null)
        {
            Debug.LogError(
                $"[InputBindingPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.InputBindingRow}。",
                this);
            return;
        }

        RebuildRows();
        RefreshControlModeDropdown();
        SetStatus(GetDevicePageHint());
        RequestLocalLayoutRebuild();
    }

    /// <summary>页面隐藏或总设置关闭时取消重绑并恢复页内交互。</summary>
    public void OnSettingsPageHidden()
    {
        if (bindingService != null && bindingService.IsRebinding)
            bindingService.CancelActiveRebind();

        SetRowsInteractable(true);
    }

    #endregion

    #region 绑定行复用

    /// <summary>按当前设备页复用现有行；切页只改变数据和显隐。</summary>
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

        // 新增行会改变控件集合，纯复用只需刷新现有导航状态。
        if (createdRow)
            parentPanel?.RefreshUIComponents();
        else
            parentPanel?.RefreshGamepadNavigationState();
    }

    /// <summary>优先从池中取得绑定行，容量不足时再实例化。</summary>
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

    /// <summary>从现有行 Prefab 创建并绑定一条可复用记录。</summary>
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

    /// <summary>隐藏当前绑定行并放回页面私有对象池。</summary>
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

    /// <summary>标记按键列表和分页根节点重新布局。</summary>
    private void RequestLocalLayoutRebuild()
    {
        if (content is RectTransform contentRect)
            LayoutRebuilder.MarkLayoutForRebuild(contentRect);
        if (pageRect != null)
            LayoutRebuilder.MarkLayoutForRebuild(pageRect);
    }

    #endregion

    #region 分页与绑定操作

    /// <summary>按当前语言重建控制方式选项，并保持已保存的手动选择。</summary>
    private void RefreshControlModeDropdown()
    {
        if (controlModeDropdown == null)
            return;

        controlModeDropdown.ClearOptions();
        controlModeDropdown.AddOptions(new List<string>
        {
            FlatWorldLocalizationService.GetUiText("电脑键鼠控制"),
            FlatWorldLocalizationService.GetUiText("手柄控制"),
            FlatWorldLocalizationService.GetUiText("手机触屏控制")
        });

        int selectedIndex = gameController != null
            ? (int)gameController.PreferredInputDevice
            : 0;
        controlModeDropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, 2));
        controlModeDropdown.RefreshShownValue();
    }

    /// <summary>下拉选择立即切换玩法绑定并写入 PlayerPrefs。</summary>
    private void HandleControlModeChanged(int selectedIndex)
    {
        if (gameController == null || selectedIndex < 0 || selectedIndex > 2)
            return;

        gameController.SetPreferredInputDevice((GameController.InputDeviceType)selectedIndex);
        RefreshControlModeDropdown();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "控制方式已切换为：{0}。",
                controlModeDropdown.options[controlModeDropdown.value].text));
        parentPanel?.RefreshGamepadNavigationState();
    }

    /// <summary>语言变化时刷新控制方式下拉框。</summary>
    private void HandleLanguageChanged(string localeCode)
    {
        RefreshControlModeDropdown();
    }

    /// <summary>切换到键鼠绑定页。</summary>
    private void ShowKeyboardMouseBindings()
    {
        SetDeviceGroup(InputBindingDeviceGroup.KeyboardMouse);
    }

    /// <summary>切换到手柄绑定页。</summary>
    private void ShowGamepadBindings()
    {
        SetDeviceGroup(InputBindingDeviceGroup.Gamepad);
    }

    /// <summary>切换绑定设备分组并复用行刷新内容。</summary>
    private void SetDeviceGroup(InputBindingDeviceGroup deviceGroup)
    {
        if (bindingService != null && bindingService.IsRebinding)
            return;

        currentDeviceGroup = deviceGroup;
        RebuildRows();
        SetStatus(GetDevicePageHint());
    }

    /// <summary>刷新键鼠与手柄页签的选中色。</summary>
    private void UpdateTabVisuals()
    {
        SetTabVisual(
            keyboardMouseTabButton,
            currentDeviceGroup == InputBindingDeviceGroup.KeyboardMouse);
        SetTabVisual(
            gamepadTabButton,
            currentDeviceGroup == InputBindingDeviceGroup.Gamepad);
    }

    /// <summary>设置一个设备页签的选中颜色。</summary>
    private static void SetTabVisual(Button button, bool selected)
    {
        if (button?.targetGraphic != null)
        {
            button.targetGraphic.color = selected
                ? FlatWorldUITheme.Accent
                : FlatWorldUITheme.Surface;
        }
    }

    /// <summary>开始交互式重绑，并在回调中恢复当前页交互。</summary>
    private void BeginRebind(BindingRow row)
    {
        if (bindingService == null || row?.Entry == null)
            return;

        string displayName = row.Entry.DisplayName;
        SetRowsInteractable(false);
        row.BindingText.text = FlatWorldLocalizationService.GetUiText("等待输入…");
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "正在修改“{0}”；Esc / 手柄 B 取消，绑定该键时改用 Backspace / Start。",
                FlatWorldLocalizationService.GetUiText(displayName)));
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
                            FlatWorldLocalizationService.GetUiText(displayName)));
                    break;
                case InputRebindStatus.Canceled:
                    UIManager.ExistingInstance?.NotifyCancelHandled();
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

    /// <summary>清除当前设备页选中操作的绑定。</summary>
    private void ClearBinding(BindingRow row)
    {
        if (bindingService == null || row?.Entry == null || bindingService.IsRebinding)
            return;

        if (!bindingService.ClearBinding(row.Entry))
        {
            SetStatus(FlatWorldLocalizationService.GetUiText("清除绑定失败。"), true);
            return;
        }

        RefreshRows();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "“{0}”的绑定已清除。",
                FlatWorldLocalizationService.GetUiText(row.Entry.DisplayName)));
    }

    /// <summary>恢复当前设备页的默认绑定。</summary>
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

    /// <summary>刷新所有已显示行的绑定文本。</summary>
    private void RefreshRows()
    {
        if (bindingService == null)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            BindingRow row = rows[i];
            if (row?.BindingText != null && row.Entry != null)
                row.BindingText.text = bindingService.GetBindingDisplayString(row.Entry);
        }
    }

    /// <summary>统一锁定或恢复页内重绑相关控件。</summary>
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
        if (controlModeDropdown != null)
            controlModeDropdown.interactable = interactable;
        if (resetButton != null)
            resetButton.interactable = interactable;
    }

    /// <summary>显示当前操作结果或错误。</summary>
    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError
            ? new Color(1f, 0.48f, 0.35f)
            : new Color(0.69f, 0.78f, 0.79f);
    }

    /// <summary>取得当前设备分页的操作提示。</summary>
    private string GetDevicePageHint()
    {
        return FlatWorldLocalizationService.GetUiFormat(
            "当前：{0}。选择一项后输入新控制；冲突会被拦截并自动保存。",
            FlatWorldLocalizationService.GetUiText(GetDevicePageName()));
    }

    /// <summary>取得当前设备分页名称。</summary>
    private string GetDevicePageName()
    {
        return currentDeviceGroup == InputBindingDeviceGroup.Gamepad
            ? "手柄"
            : "键鼠";
    }

    #endregion

    #region 清理与局部查找

    /// <summary>解除页面事件，并保证销毁时没有残留重绑操作。</summary>
    private void OnDestroy()
    {
        if (bindingService != null)
        {
            bindingService.BindingsChanged -= RefreshRows;
            if (bindingService.IsRebinding)
                bindingService.CancelActiveRebind();
        }

        keyboardMouseTabButton?.onClick.RemoveListener(ShowKeyboardMouseBindings);
        gamepadTabButton?.onClick.RemoveListener(ShowGamepadBindings);
        resetButton?.onClick.RemoveListener(ResetToDefaults);
        controlModeDropdown?.onValueChanged.RemoveListener(HandleControlModeChanged);
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
    }

    /// <summary>在当前页面内按名称查找 Transform。</summary>
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

    /// <summary>在当前页面内按名称查找按钮。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        return FindComponent<Button>(root, buttonName);
    }

    /// <summary>在当前页面内按名称查找 TMP 文本。</summary>
    private static TextMeshProUGUI FindText(Transform root, string textName)
    {
        return FindComponent<TextMeshProUGUI>(root, textName);
    }

    /// <summary>在当前页面内按名称查找 TMP 下拉框。</summary>
    private static TMP_Dropdown FindDropdown(Transform root, string dropdownName)
    {
        return FindComponent<TMP_Dropdown>(root, dropdownName);
    }

    /// <summary>在当前页面内按名称查找指定组件。</summary>
    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        if (root == null)
            return null;

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].name == objectName)
                return components[i];
        }

        return null;
    }

    #endregion
}
