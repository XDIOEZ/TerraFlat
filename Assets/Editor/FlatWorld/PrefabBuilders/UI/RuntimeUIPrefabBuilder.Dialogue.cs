// 按业务职责维护 Prefab，复用公共控件与布局实现。
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 对话界面

    private static GameObject BuildPlayerChatInput()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.PlayerChatInput,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline),
            typeof(TMP_InputField));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 42f);
        rect.sizeDelta = new Vector2(780f, 54f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.03f, 0.03f, 0.03f, 0.88f);
        AddOutline(background, new Color(1f, 1f, 1f, 0.32f));

        GameObject textArea = CreateUIObject("Text Area", root.transform, typeof(RectMask2D));
        RectTransform areaRect = textArea.GetComponent<RectTransform>();
        Stretch(areaRect);
        areaRect.offsetMin = new Vector2(16f, 7f);
        areaRect.offsetMax = new Vector2(-16f, -7f);

        TextMeshProUGUI placeholder = CreateInputText(
            "Placeholder",
            textArea.transform,
            "输入消息，按 Enter 发送（/ 开头可用于命令）",
            new Color(0.70f, 0.70f, 0.70f, 0.82f));
        TextMeshProUGUI valueText = CreateInputText("Text", textArea.transform, string.Empty, Color.white);

        TMP_InputField field = root.GetComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = areaRect;
        field.textComponent = valueText;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = TMP_InputField.ContentType.Standard;
        field.characterLimit = 160;
        field.richText = false;
        field.customCaretColor = true;
        field.caretColor = Color.white;
        field.selectionColor = new Color(0.35f, 0.55f, 1f, 0.55f);
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        return root;
    }

    private static GameObject BuildSpeechBubble()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.CharacterSpeechBubble,
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Shadow));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(260f, 72f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.10f, 0.94f);
        background.raycastTarget = false;
        Shadow shadow = root.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        shadow.effectDistance = new Vector2(3f, -3f);

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        Image tail = CreateImage("Tail", root.transform, background.color);
        tail.raycastTarget = false;
        RectTransform tailRect = tail.rectTransform;
        tailRect.anchorMin = new Vector2(0.5f, 0f);
        tailRect.anchorMax = new Vector2(0.5f, 0f);
        tailRect.pivot = new Vector2(0.5f, 0.5f);
        tailRect.anchoredPosition = new Vector2(0f, -6f);
        tailRect.sizeDelta = new Vector2(18f, 18f);
        tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

        TextMeshProUGUI message = CreateText("Message", root.transform, string.Empty, 28f, new Color(0.96f, 0.94f, 0.88f, 1f));
        message.enableAutoSizing = true;
        message.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(28f);
        message.fontSizeMax = 28f;
        message.enableWordWrapping = true;
        message.overflowMode = TextOverflowModes.Ellipsis;
        message.alignment = TextAlignmentOptions.Center;
        Stretch(message.rectTransform);
        message.rectTransform.offsetMin = new Vector2(18f, 12f);
        message.rectTransform.offsetMax = new Vector2(-18f, -12f);
        return root;
    }

    #endregion
}
