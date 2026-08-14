using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏内简易手柄键盘，接管 TMP_InputField 的英文、数字和常用符号输入。
/// 键盘本身使用 BasePanel，因此确认、取消、焦点高亮和返回栈与其它 UI 保持一致。
/// </summary>
public static class GamepadVirtualKeyboardController
{
    #region 状态

    private static readonly string[] KeyRows =
    {
        "1234567890",
        "QWERTYUIOP",
        "ASDFGHJKL",
        "ZXCVBNM"
    };

    private static TMP_InputField currentInputField;
    private static BasePanel currentPanel;
    private static GameObject currentRoot;
    private static string originalText;
    private static bool closing;

    public static bool IsOpen => currentPanel != null && currentRoot != null;
    public static event Action<TMP_InputField> Closed;

    #endregion

    #region 对外接口

    /// <summary>
    /// 为当前选中的输入框打开虚拟键盘。
    /// </summary>
    public static bool Show(TMP_InputField inputField)
    {
        if (inputField == null || !inputField.isActiveAndEnabled || !inputField.interactable)
            return false;

        if (IsOpen)
            return ReferenceEquals(currentInputField, inputField);

        Transform panelRoot = FindPanelRoot();
        if (panelRoot == null)
            return false;

        currentInputField = inputField;
        originalText = inputField.text;
        closing = false;

        currentRoot = CreateKeyboardRoot(panelRoot);
        currentPanel = currentRoot.GetComponent<BasePanel>();
        BuildKeyboard(currentRoot.transform);

        CanvasGroup canvasGroup = currentPanel.canvasGroup;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        currentPanel.Init();
        currentPanel.RefreshUIComponents();
        currentPanel.Closed += HandlePanelClosed;
        currentPanel.PrepareForGamepadNavigation("Key_1", true, true);
        currentPanel.Open();
        return true;
    }

    /// <summary>
    /// 接受当前文本并关闭虚拟键盘。
    /// </summary>
    public static void Confirm()
    {
        Close(true);
    }

    /// <summary>
    /// 恢复打开键盘前的文本并关闭虚拟键盘。
    /// </summary>
    public static void Cancel()
    {
        Close(false);
    }

    #endregion

    #region 键盘构建

    private static GameObject CreateKeyboardRoot(Transform panelRoot)
    {
        GameObject root = new GameObject(
            "GamepadVirtualKeyboard",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(Image),
            typeof(BasePanel));
        root.transform.SetParent(panelRoot, false);

        RectTransform rectTransform = root.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot = new Vector2(0.5f, 0f);
        rectTransform.anchoredPosition = new Vector2(0f, 24f);
        rectTransform.sizeDelta = new Vector2(980f, 510f);

        Image background = root.GetComponent<Image>();
        background.color = FlatWorldUITheme.Surface;
        background.raycastTarget = true;

        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = FlatWorldUITheme.SelectionOutline;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
        return root;
    }

    private static void BuildKeyboard(Transform root)
    {
        TextMeshProUGUI title = CreateLabel(root, "键盘标题", "手柄输入");
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(-36f, 28f);
        title.alignment = TMPro.TextAlignmentOptions.Center;
        title.fontSize = 18f;

        GameObject rowsObject = new GameObject("KeyboardRows", typeof(RectTransform), typeof(VerticalLayoutGroup));
        rowsObject.transform.SetParent(root, false);
        RectTransform rowsRect = rowsObject.GetComponent<RectTransform>();
        rowsRect.anchorMin = Vector2.zero;
        rowsRect.anchorMax = Vector2.one;
        rowsRect.offsetMin = new Vector2(18f, 18f);
        rowsRect.offsetMax = new Vector2(-18f, -54f);

        VerticalLayoutGroup rowsLayout = rowsObject.GetComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 6f;
        rowsLayout.childAlignment = TextAnchor.UpperCenter;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;

        for (int rowIndex = 0; rowIndex < KeyRows.Length; rowIndex++)
        {
            CreateCharacterRow(rowsObject.transform, KeyRows[rowIndex], rowIndex);
        }

        CreateCharacterRow(rowsObject.transform, "-_.", KeyRows.Length);
        CreateActionRow(rowsObject.transform);
    }

    private static void CreateCharacterRow(Transform parent, string characters, int rowIndex)
    {
        GameObject row = CreateRow(parent, $"KeyboardRow_{rowIndex}");
        for (int i = 0; i < characters.Length; i++)
        {
            string character = characters[i].ToString();
            CreateButton(row.transform, $"Key_{character}", character, () => AppendText(character), 52f);
        }
    }

    private static void CreateActionRow(Transform parent)
    {
        GameObject row = CreateRow(parent, "KeyboardActions");
        CreateButton(row.transform, "Key_Backspace", "退格", Backspace, 120f);
        CreateButton(row.transform, "Key_Space", "空格", () => AppendText(" "), 190f);
        CreateButton(row.transform, "Key_Clear", "清空", ClearText, 120f);
        CreateButton(row.transform, "Key_Cancel", "取消", Cancel, 120f);
        CreateButton(row.transform, "Key_Confirm", "确认", Confirm, 120f);
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        LayoutElement rowElement = row.GetComponent<LayoutElement>();
        rowElement.preferredHeight = 50f;
        rowElement.minHeight = 50f;

        HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 6f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;
        return row;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string label,
        UnityEngine.Events.UnityAction action,
        float width)
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = FlatWorldUITheme.SurfaceRaised;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        LayoutElement element = buttonObject.GetComponent<LayoutElement>();
        element.preferredWidth = width;
        element.minWidth = width;
        element.preferredHeight = 44f;
        element.minHeight = 44f;

        TextMeshProUGUI text = CreateLabel(buttonObject.transform, "Label", label);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.fontSize = 15f;
        return button;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string value)
    {
        GameObject labelObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(parent, false);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.font = TMP_Settings.defaultFontAsset;
        label.color = FlatWorldUITheme.TextPrimary;
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static Transform FindPanelRoot()
    {
        UIManager manager = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (manager != null && manager.panelRoot != null)
            return manager.panelRoot;

        return GameObject.Find("PanelRoot")?.transform;
    }

    #endregion

    #region 文本操作

    private static void AppendText(string value)
    {
        if (currentInputField == null || string.IsNullOrEmpty(value))
            return;

        if (!CanAppend(value))
            return;

        currentInputField.text += value;
        currentInputField.caretPosition = currentInputField.text.Length;
    }

    private static bool CanAppend(string value)
    {
        if (currentInputField.characterLimit > 0 &&
            currentInputField.text.Length + value.Length > currentInputField.characterLimit)
        {
            return false;
        }

        if (currentInputField.contentType == TMP_InputField.ContentType.IntegerNumber)
            return value == "-" || int.TryParse(value, out _);

        if (currentInputField.contentType == TMP_InputField.ContentType.DecimalNumber)
            return value == "-" || value == "." || decimal.TryParse(value, out _);

        return true;
    }

    private static void Backspace()
    {
        if (currentInputField == null || currentInputField.text.Length == 0)
            return;

        currentInputField.text = currentInputField.text.Substring(0, currentInputField.text.Length - 1);
        currentInputField.caretPosition = currentInputField.text.Length;
    }

    private static void ClearText()
    {
        if (currentInputField == null)
            return;

        currentInputField.text = string.Empty;
        currentInputField.caretPosition = 0;
    }

    #endregion

    #region 关闭与清理

    private static void Close(bool accept)
    {
        if (currentPanel == null || closing)
            return;

        TMP_InputField inputField = currentInputField;
        BasePanel panel = currentPanel;
        closing = true;

        if (!accept && inputField != null && inputField.isActiveAndEnabled)
            inputField.text = originalText;

        if (accept && inputField != null && inputField.isActiveAndEnabled)
        {
            // onSubmit 会被聊天控制器等业务监听，用于完成提交和关闭业务面板。
            inputField.onSubmit?.Invoke(inputField.text);
        }

        if (panel != null && panel.IsOpen())
            panel.Close();
        else
            Cleanup(inputField);
    }

    private static void HandlePanelClosed()
    {
        TMP_InputField inputField = currentInputField;
        if (!closing && inputField != null && inputField.isActiveAndEnabled)
            inputField.text = originalText;

        closing = true;
        Cleanup(inputField);
    }

    private static void Cleanup(TMP_InputField inputField)
    {
        GameObject root = currentRoot;
        BasePanel panel = currentPanel;
        currentRoot = null;
        currentPanel = null;
        currentInputField = null;
        originalText = string.Empty;
        closing = false;

        if (panel != null)
            panel.Closed -= HandlePanelClosed;
        if (root != null)
            UnityEngine.Object.Destroy(root);

        Closed?.Invoke(inputField);
    }

    #endregion
}
