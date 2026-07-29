using System.Collections.Generic;
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
    private TextMeshProUGUI statusText;
    private bool panelSuspendedInput;
    private bool previousGameplayLock;
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

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
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
            previousGameplayLock =
                gameController != null && gameController.IsGameplayInputLocked;
            gameController?.SetGameplayInputLocked(true);
            bindingService.SuspendGameplayInput();
            panelSuspendedInput = true;
        }

        UpdateDialogSize();
        RefreshRows();
        SetStatus("选择一项后按下新按键；Esc 取消录入。设置会自动保存。");
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
        GameObject rowPrefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.InputBindingRow);
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
        Transform content = FindTransform(bindingPanel.transform, "Content");
        statusText = bindingPanel.GetText("状态文本");

        bindingPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        bindingPanel.GetButton("恢复默认按钮")?.onClick.AddListener(ResetToDefaults);
        bindingPanel.GetButton("完成按钮")?.onClick.AddListener(Close);

        if (dialogRect == null || content == null || statusText == null)
        {
            Debug.LogError("[InputBindingPanelLauncher] 按键绑定 Prefab 控件命名契约不完整。", bindingPanel);
            bindingPanel.Close();
            return;
        }

        CreateRows(content, rowPrefab);
        UpdateDialogSize();
        bindingPanel.Close();
    }







private void CreateRows(Transform content, GameObject rowPrefab)
    {
        rows.Clear();
        IReadOnlyList<InputBindingEntry> entries = bindingService.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingEntry entry = entries[i];
            GameObject rowObject = Instantiate(rowPrefab, content, false);
            rowObject.name = $"绑定项_{entry.Action?.name ?? i.ToString()}";
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

            label.text = entry.DisplayName;
            bindingText.text = bindingService.GetBindingDisplayString(entry);

            BindingRow row = new BindingRow
            {
                Entry = entry,
                BindingText = bindingText,
                RebindButton = rebindButton
            };
            rebindButton.onClick.AddListener(() => BeginRebind(row));
            rows.Add(row);
        }
    }





    private void BeginRebind(BindingRow row)
    {
        if (bindingService == null || row == null)
            return;

        SetRowsInteractable(false);
        row.BindingText.text = "等待输入…";
        SetStatus($"正在修改“{row.Entry.DisplayName}”；按 Esc 取消。");
        bindingService.BeginInteractiveRebind(row.Entry, result =>
        {
            SetRowsInteractable(true);
            RefreshRows();

            switch (result.Status)
            {
                case InputRebindStatus.Completed:
                    SetStatus($"“{row.Entry.DisplayName}”已保存。");
                    break;
                case InputRebindStatus.Canceled:
                    suppressEscapeCloseFrame = Time.frameCount;
                    SetStatus("已取消本次修改。");
                    break;
                case InputRebindStatus.Conflict:
                    SetStatus(
                        $"该按键已用于“{result.ConflictingEntry?.DisplayName ?? "其他操作"}”，未作修改。",
                        true);
                    break;
                default:
                    SetStatus(
                        $"修改失败：{result.Exception?.Message ?? "未知错误"}",
                        true);
                    break;
            }
        });
    }

    private void ResetToDefaults()
    {
        if (bindingService == null)
            return;

        bindingService.ResetToDefaults();
        RefreshRows();
        SetStatus("已恢复默认按键。");
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
        gameController?.SetGameplayInputLocked(previousGameplayLock);
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
