using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InputBindingPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "按键绑定";

    private sealed class BindingRow
    {
        public GameObject Root;
        public InputBindingEntry Entry;
        public TextMeshProUGUI BindingText;
        public Button RebindButton;
    }

    private readonly List<BindingRow> rows = new List<BindingRow>();

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
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
    }

private void Close()
    {
        if (bindingService != null && bindingService.IsRebinding)
            bindingService.CancelActiveRebind();

        bindingPanel?.Close();
        ReleaseInputLock();
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







    private void RebuildRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i]?.Root == null)
                continue;

            rows[i].Root.SetActive(false);
            Destroy(rows[i].Root);
        }

        rows.Clear();
        if (bindingService == null || content == null || rowPrefab == null)
            return;

        IReadOnlyList<InputBindingEntry> entries =
            bindingService.GetEntries(currentDeviceGroup);
        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingEntry entry = entries[i];
            GameObject rowObject = Instantiate(rowPrefab, content, false);
            rowObject.name =
                $"绑定项_{currentDeviceGroup}_{entry.Action?.name ?? i.ToString()}_{entry.BindingIndex}";
            rowObject.SetActive(true);

            TextMeshProUGUI label = FindText(rowObject.transform, "操作名称");
            TextMeshProUGUI bindingText = FindText(rowObject.transform, "绑定值");
            Button rebindButton = FindButton(rowObject.transform, "修改按钮");
            if (label == null || bindingText == null || rebindButton == null)
            {
                Debug.LogError("[InputBindingPanelLauncher] 按键绑定行 Prefab 控件命名契约不完整。", rowObject);
                Destroy(rowObject);
                continue;
            }

            label.text = FlatWorldLocalizationService.GetUiText(entry.DisplayName);
            bindingText.text = bindingService.GetBindingDisplayString(entry);

            BindingRow row = new BindingRow
            {
                Root = rowObject,
                Entry = entry,
                BindingText = bindingText,
                RebindButton = rebindButton
            };
            rebindButton.onClick.AddListener(() => BeginRebind(row));
            rows.Add(row);
        }

        UpdateTabVisuals();

        if (bindingPanel != null && bindingPanel.IsVisible())
            bindingPanel.PrepareForGamepadNavigation("修改按钮", false);
    }

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

        if (dialogRect != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
        }
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

    private void ResetToDefaults()
    {
        if (bindingService == null)
            return;

        bindingService.ResetToDefaults(currentDeviceGroup);
        RebuildRows();
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
            Mathf.Max(1f, canvasSize.x - 64f),
            Mathf.Max(1f, canvasSize.y - 64f));

        dialogRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Min(760f, available.x));
        dialogRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            Mathf.Min(720f, available.y));
    }

    private void ReleaseInputLock()
    {
        if (!panelSuspendedInput)
            return;

        bindingService?.ResumeGameplayInput();
        gameController?.ReleaseGameplayInputLock(this);
        panelSuspendedInput = false;
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
        ReleaseInputLock();
        if (bindingPanel != null)
            Destroy(bindingPanel.gameObject);
    }


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
}
