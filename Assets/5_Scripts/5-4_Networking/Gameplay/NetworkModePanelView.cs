using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 双脚本 UI 的视图脚本：仅负责 UGUI 结构与 BasePanel 控件收集。
    /// </summary>
    public sealed class NetworkModePanelView : BasePanel
    {
        public const string PanelKey = "UI_NetworkMode";
        private static TMP_FontAsset preferredFont;

        public static bool IsProjectFontReady => FindProjectFont() != null;

        public static NetworkModePanelView Create(Transform parent)
        {
            GameObject panelObject = new GameObject(
                PanelKey,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(GraphicRaycaster),
                typeof(NetworkModePanelView));

            panelObject.transform.SetParent(parent, false);
            Canvas panelCanvas = panelObject.GetComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 500;
            NetworkModePanelView view = panelObject.GetComponent<NetworkModePanelView>();
            view.BuildVisualTree();
            view.PanelName = PanelKey;
            view.Init();
            UIManager.Instance.RegisterPanel(view, PanelKey);
            panelObject.transform.SetAsLastSibling();
            return view;
        }

        private void BuildVisualTree()
        {
            preferredFont = ResolvePreferredFont();

            RectTransform root = GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(640f, 540f);
            root.anchoredPosition = Vector2.zero;

            Image background = GetComponent<Image>();
            background.color = new Color(0.055f, 0.075f, 0.11f, 0.97f);

            VerticalLayoutGroup layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 28, 28);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            AddText(transform, "标题", "联机模式", 38f, 58f, FontStyles.Bold);
            AddText(transform, "说明", "主机同步地图数据；WASD / 方向键移动", 20f, 38f, FontStyles.Normal,
                new Color(0.72f, 0.8f, 0.92f));
            AddInput(transform, "玩家名称输入框", "玩家名称", $"玩家_{Random.Range(1000, 9999)}");
            AddInput(transform, "地址输入框", "主机地址", "127.0.0.1");
            AddInput(transform, "端口输入框", "端口", "7777", TMP_InputField.ContentType.IntegerNumber);
            AddText(transform, "状态文本", "离线", 20f, 34f, FontStyles.Normal, new Color(0.45f, 0.95f, 0.72f));
            AddText(transform, "玩家数量文本", "玩家：0 / 2", 18f, 30f, FontStyles.Normal,
                new Color(0.82f, 0.86f, 0.92f));

            GameObject buttons = new GameObject("操作按钮组", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(transform, false);
            HorizontalLayoutGroup buttonLayout = buttons.GetComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = 12f;
            buttonLayout.childControlHeight = true;
            buttonLayout.childControlWidth = true;
            buttonLayout.childForceExpandWidth = true;
            buttons.GetComponent<LayoutElement>().preferredHeight = 54f;

            AddButton(buttons.transform, "创建主机按钮", "创建主机", new Color(0.18f, 0.55f, 0.38f));
            AddButton(buttons.transform, "加入游戏按钮", "加入游戏", new Color(0.18f, 0.4f, 0.68f));
            AddButton(buttons.transform, "断开按钮", "断开", new Color(0.62f, 0.25f, 0.25f));
            AddButton(transform, "关闭按钮", "关闭界面", new Color(0.22f, 0.25f, 0.32f), 44f);
        }

        private static TextMeshProUGUI AddText(
            Transform parent,
            string objectName,
            string value,
            float fontSize,
            float height,
            FontStyles style,
            Color? color = null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            if (preferredFont != null)
                text.font = preferredFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color ?? Color.white;
            text.enableWordWrapping = false;
            textObject.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private static TMP_InputField AddInput(
            Transform parent,
            string objectName,
            string placeholderValue,
            string initialValue,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
        {
            GameObject inputObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(parent, false);
            inputObject.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 1f);
            inputObject.GetComponent<LayoutElement>().preferredHeight = 48f;

            GameObject viewportObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(inputObject.transform, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(16f, 6f);
            viewport.offsetMax = new Vector2(-16f, -6f);

            TextMeshProUGUI placeholder = AddInputText(viewportObject.transform, "Placeholder", placeholderValue);
            placeholder.color = new Color(0.55f, 0.6f, 0.68f, 0.85f);
            TextMeshProUGUI valueText = AddInputText(viewportObject.transform, "Text", initialValue);

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = valueText;
            input.placeholder = placeholder;
            input.contentType = contentType;
            input.text = initialValue;
            input.caretColor = Color.white;
            input.selectionColor = new Color(0.25f, 0.55f, 0.85f, 0.5f);
            return input;
        }

        private static TextMeshProUGUI AddInputText(Transform parent, string objectName, string value)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            if (preferredFont != null)
                text.font = preferredFont;
            text.fontSize = 22f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = Color.white;
            text.enableWordWrapping = false;
            return text;
        }

        private static Button AddButton(Transform parent, string objectName, string caption, Color color, float height = 54f)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = color;
            buttonObject.GetComponent<LayoutElement>().preferredHeight = height;

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            colors.disabledColor = new Color(0.2f, 0.2f, 0.22f, 0.6f);
            button.colors = colors;

            TextMeshProUGUI label = AddInputText(buttonObject.transform, "按钮文字", caption);
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.fontSize = 20f;
            return button;
        }

        private static TMP_FontAsset ResolvePreferredFont()
        {
            TMP_FontAsset projectFont = FindProjectFont();
            if (projectFont != null)
                return projectFont;

            TextMeshProUGUI[] existingTexts = FindObjectsOfType<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI existingText in existingTexts)
            {
                if (existingText.font != null)
                    return existingText.font;
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static TMP_FontAsset FindProjectFont()
        {
            TextMeshProUGUI[] existingTexts = FindObjectsOfType<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI existingText in existingTexts)
            {
                TMP_FontAsset font = existingText.font;
                if (font != null && font.name.Contains("fusion-pixel"))
                    return font;
            }

            return null;
        }
    }
}
