// AI-Context: NetworkModeUIController 的动态视觉树分部；UI 状态与绑定位于 NetworkModeUIController.UI.cs。

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 联机 UI：动态构建视觉树、绑定控件并更新显示状态。
    /// </summary>
    public sealed partial class NetworkModeUIController
    {
        public const string NetworkPanelKey = "UI_NetworkMode";

        private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.98f);
        private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.98f);
        private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
        private static readonly Color Muted = new Color(0.64f, 0.70f, 0.71f, 1f);
        private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
        private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);
        private static TMP_FontAsset preferredFont;

        #region 动态视觉树

        private static BasePanel CreateNetworkPanel(Transform parent)
        {
            GameObject panelObject = new GameObject(
                NetworkPanelKey,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(GraphicRaycaster),
                typeof(BasePanel));

            panelObject.layer = LayerMask.NameToLayer("UI");
            panelObject.transform.SetParent(parent, false);

            Canvas panelCanvas = panelObject.GetComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 500;

            BuildVisualTree(panelObject);
            BasePanel basePanel = panelObject.GetComponent<BasePanel>();
            basePanel.PanelName = NetworkPanelKey;
            basePanel.Init();
            UIManager.Instance.RegisterPanel(basePanel, NetworkPanelKey);
            panelObject.transform.SetAsLastSibling();
            return basePanel;
        }

        private static void BuildVisualTree(GameObject panelObject)
        {
            preferredFont = ResolvePreferredFont();

            RectTransform root = panelObject.GetComponent<RectTransform>();
            Stretch(root);

            Image scrim = panelObject.GetComponent<Image>();
            scrim.color = new Color(0.006f, 0.016f, 0.024f, 0.68f);
            scrim.raycastTarget = true;

            Image shadow = CreateImage("面板投影", panelObject.transform, new Color(0f, 0f, 0f, 0.38f));
            SetRect(shadow.rectTransform, new Vector2(12f, -14f), new Vector2(920f, 680f), new Vector2(0.5f, 0.5f));
            shadow.raycastTarget = false;

            Image card = CreateImage("联机主卡片", panelObject.transform, Ink);
            SetRect(card.rectTransform, Vector2.zero, new Vector2(920f, 680f), new Vector2(0.5f, 0.5f));

            Outline cardOutline = card.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.32f);
            cardOutline.effectDistance = new Vector2(1f, -1f);
            cardOutline.useGraphicAlpha = true;

            Image accent = CreateImage("卡片强调线", card.transform, Amber);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
            accent.raycastTarget = false;

            BuildHeader(card.transform);
            BuildConnectionForm(card.transform);
            BuildSessionSummary(card.transform);
            BuildActions(card.transform);
        }

        private static void BuildHeader(Transform card)
        {
            TextMeshProUGUI eyebrow = CreateText(
                "联机眉题",
                card,
                "MULTIPLAYER  /  网络会话",
                16f,
                Amber,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(eyebrow.rectTransform, new Vector2(42f, -34f), new Vector2(560f, 26f), new Vector2(0f, 1f));
            eyebrow.characterSpacing = 3f;

            TextMeshProUGUI title = CreateText(
                "标题",
                card,
                "联机模式",
                42f,
                Cream,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(title.rectTransform, new Vector2(42f, -70f), new Vector2(500f, 58f), new Vector2(0f, 1f));

            TextMeshProUGUI description = CreateText(
                "说明",
                card,
                "创建你的世界，或输入好友的连接信息加入旅程。",
                19f,
                Muted,
                FontStyles.Normal,
                TextAlignmentOptions.Left);
            SetRect(description.rectTransform, new Vector2(42f, -130f), new Vector2(720f, 34f), new Vector2(0f, 1f));

            AddButton(
                card,
                "关闭按钮",
                "×",
                new Vector2(-34f, -34f),
                new Vector2(48f, 48f),
                new Color(0.08f, 0.11f, 0.13f, 0.96f),
                new Color(0.64f, 0.70f, 0.71f, 0.28f),
                28f,
                new Vector2(1f, 1f));

            Image divider = CreateImage("标题分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
            divider.rectTransform.anchorMin = new Vector2(0f, 1f);
            divider.rectTransform.anchorMax = new Vector2(1f, 1f);
            divider.rectTransform.pivot = new Vector2(0.5f, 1f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, -178f);
            divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
            divider.raycastTarget = false;
        }

        private static void BuildConnectionForm(Transform card)
        {
            TextMeshProUGUI heading = CreateText(
                "连接设置标题",
                card,
                "连接设置",
                22f,
                Cream,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(heading.rectTransform, new Vector2(42f, -208f), new Vector2(470f, 34f), new Vector2(0f, 1f));

            AddInput(
                card,
                "玩家名称输入框",
                "玩家名称",
                "你在联机世界中的显示名称",
                $"玩家_{Random.Range(1000, 9999)}",
                new Vector2(42f, -258f),
                new Vector2(500f, 66f));

            AddInput(
                card,
                "地址输入框",
                "主机地址",
                "例如 127.0.0.1",
                "127.0.0.1",
                new Vector2(42f, -358f),
                new Vector2(342f, 66f));

            AddInput(
                card,
                "端口输入框",
                "端口",
                "7777",
                "7777",
                new Vector2(400f, -358f),
                new Vector2(142f, 66f),
                TMP_InputField.ContentType.IntegerNumber);

            Image notice = CreateImage("同步说明底板", card, new Color(0.07f, 0.105f, 0.125f, 0.92f));
            SetRect(notice.rectTransform, new Vector2(42f, -458f), new Vector2(500f, 78f), new Vector2(0f, 1f));

            Image noticeAccent = CreateImage("同步说明强调线", notice.transform, Teal);
            noticeAccent.rectTransform.anchorMin = new Vector2(0f, 0f);
            noticeAccent.rectTransform.anchorMax = new Vector2(0f, 1f);
            noticeAccent.rectTransform.pivot = new Vector2(0f, 0.5f);
            noticeAccent.rectTransform.anchoredPosition = Vector2.zero;
            noticeAccent.rectTransform.sizeDelta = new Vector2(4f, 0f);
            noticeAccent.raycastTarget = false;

            TextMeshProUGUI noticeTitle = CreateText(
                "同步说明标题",
                notice.transform,
                "世界同步",
                17f,
                Teal,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(noticeTitle.rectTransform, new Vector2(18f, -10f), new Vector2(180f, 26f), new Vector2(0f, 1f));

            TextMeshProUGUI noticeText = CreateText(
                "同步说明文字",
                notice.transform,
                "主机负责地图与世界状态；加入者会自动接收当前世界。",
                15f,
                Muted,
                FontStyles.Normal,
                TextAlignmentOptions.Left);
            SetRect(noticeText.rectTransform, new Vector2(18f, -38f), new Vector2(458f, 26f), new Vector2(0f, 1f));
        }

        private static void BuildSessionSummary(Transform card)
        {
            Image summary = CreateImage("会话状态卡", card, new Color(0.035f, 0.06f, 0.075f, 0.98f));
            SetRect(summary.rectTransform, new Vector2(-42f, -208f), new Vector2(292f, 328f), new Vector2(1f, 1f));

            Outline outline = summary.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.18f);
            outline.effectDistance = new Vector2(1f, -1f);

            TextMeshProUGUI heading = CreateText(
                "会话状态标题",
                summary.transform,
                "会话状态",
                21f,
                Cream,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(heading.rectTransform, new Vector2(24f, -24f), new Vector2(220f, 32f), new Vector2(0f, 1f));

            Image statusPill = CreateImage("状态底板", summary.transform, new Color(0.07f, 0.16f, 0.15f, 1f));
            SetRect(statusPill.rectTransform, new Vector2(24f, -70f), new Vector2(244f, 56f), new Vector2(0f, 1f));

            Image statusDot = CreateImage("状态指示点", statusPill.transform, Teal);
            SetRect(statusDot.rectTransform, new Vector2(18f, 0f), new Vector2(10f, 10f), new Vector2(0f, 0.5f));
            statusDot.raycastTarget = false;

            TextMeshProUGUI status = CreateText(
                "状态文本",
                statusPill.transform,
                "离线",
                16f,
                new Color(0.58f, 0.88f, 0.79f, 1f),
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                true);
            status.rectTransform.anchorMin = new Vector2(0f, 0f);
            status.rectTransform.anchorMax = new Vector2(1f, 1f);
            status.rectTransform.offsetMin = new Vector2(38f, 8f);
            status.rectTransform.offsetMax = new Vector2(-12f, -8f);

            TextMeshProUGUI playersLabel = CreateText(
                "玩家数量标签",
                summary.transform,
                "当前连接",
                14f,
                Muted,
                FontStyles.Normal,
                TextAlignmentOptions.Left);
            SetRect(playersLabel.rectTransform, new Vector2(24f, -146f), new Vector2(210f, 24f), new Vector2(0f, 1f));

            TextMeshProUGUI players = CreateText(
                "玩家数量文本",
                summary.transform,
                "玩家：0 / 2",
                28f,
                Cream,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(players.rectTransform, new Vector2(24f, -174f), new Vector2(220f, 42f), new Vector2(0f, 1f));

            Image divider = CreateImage("状态分隔线", summary.transform, new Color(0.55f, 0.64f, 0.65f, 0.16f));
            SetRect(divider.rectTransform, new Vector2(24f, -232f), new Vector2(244f, 1f), new Vector2(0f, 1f));
            divider.raycastTarget = false;

            TextMeshProUGUI controls = CreateText(
                "操作提示",
                summary.transform,
                "移动  WASD / 方向键\n关闭  使用右上角按钮",
                14f,
                Muted,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                true);
            SetRect(controls.rectTransform, new Vector2(24f, -252f), new Vector2(244f, 58f), new Vector2(0f, 1f));
            controls.lineSpacing = 6f;
        }

        private static void BuildActions(Transform card)
        {
            Image divider = CreateImage("操作区分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
            divider.rectTransform.anchorMin = new Vector2(0f, 0f);
            divider.rectTransform.anchorMax = new Vector2(1f, 0f);
            divider.rectTransform.pivot = new Vector2(0.5f, 0f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, 112f);
            divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
            divider.raycastTarget = false;

            AddButton(
                card,
                "创建主机按钮",
                "创建主机",
                new Vector2(42f, 34f),
                new Vector2(214f, 62f),
                new Color(0.70f, 0.36f, 0.16f, 1f),
                new Color(1f, 0.71f, 0.38f, 0.38f),
                20f,
                Vector2.zero);

            AddButton(
                card,
                "加入游戏按钮",
                "加入好友",
                new Vector2(272f, 34f),
                new Vector2(214f, 62f),
                new Color(0.08f, 0.29f, 0.29f, 1f),
                new Color(0.36f, 0.78f, 0.72f, 0.34f),
                20f,
                Vector2.zero);

            AddButton(
                card,
                "断开按钮",
                "断开连接",
                new Vector2(-42f, 34f),
                new Vector2(194f, 62f),
                new Color(0.25f, 0.075f, 0.075f, 0.96f),
                new Color(0.78f, 0.34f, 0.29f, 0.30f),
                18f,
                new Vector2(1f, 0f));

        }

        private static TMP_InputField AddInput(
            Transform parent,
            string objectName,
            string labelValue,
            string placeholderValue,
            string initialValue,
            Vector2 position,
            Vector2 size,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
        {
            TextMeshProUGUI label = CreateText(
                objectName + "_标签",
                parent,
                labelValue,
                15f,
                Muted,
                FontStyles.Bold,
                TextAlignmentOptions.Left);
            SetRect(label.rectTransform, position, new Vector2(size.x, 24f), new Vector2(0f, 1f));

            GameObject inputObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TMP_InputField));
            inputObject.layer = LayerMask.NameToLayer("UI");
            inputObject.transform.SetParent(parent, false);

            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            SetRect(inputRect, position + new Vector2(0f, -26f), size, new Vector2(0f, 1f));

            Image background = inputObject.GetComponent<Image>();
            background.color = InkSoft;

            Outline outline = inputObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.22f);
            outline.effectDistance = new Vector2(1f, -1f);

            GameObject viewportObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.layer = LayerMask.NameToLayer("UI");
            viewportObject.transform.SetParent(inputObject.transform, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);
            viewport.offsetMin = new Vector2(16f, 7f);
            viewport.offsetMax = new Vector2(-16f, -7f);

            TextMeshProUGUI placeholder = AddInputText(viewportObject.transform, objectName + "_占位文字", placeholderValue);
            placeholder.color = new Color(0.53f, 0.59f, 0.60f, 0.80f);

            TextMeshProUGUI valueText = AddInputText(viewportObject.transform, objectName + "_输入文字", initialValue);

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = viewport;
            input.textComponent = valueText;
            input.placeholder = placeholder;
            input.contentType = contentType;
            input.text = initialValue;
            input.caretColor = Cream;
            input.selectionColor = new Color(0.83f, 0.49f, 0.23f, 0.42f);
            input.customCaretColor = true;

            ColorBlock colors = input.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.08f, 1f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.58f, 0.60f, 0.60f, 0.55f);
            colors.fadeDuration = 0.12f;
            input.colors = colors;
            return input;
        }

        private static TextMeshProUGUI AddInputText(Transform parent, string objectName, string value)
        {
            TextMeshProUGUI text = CreateText(
                objectName,
                parent,
                value,
                19f,
                Cream,
                FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);
            return text;
        }

        private static Button AddButton(
            Transform parent,
            string objectName,
            string caption,
            Vector2 position,
            Vector2 size,
            Color color,
            Color outlineColor,
            float fontSize,
            Vector2 pivot)
        {
            GameObject buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.layer = LayerMask.NameToLayer("UI");
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            SetRect(rect, position, size, pivot);

            Image image = buttonObject.GetComponent<Image>();
            image.color = color;

            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(1f, -1f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.13f, 1.04f, 1f);
            colors.pressedColor = new Color(0.72f, 0.76f, 0.78f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.42f, 0.43f, 0.44f, 0.56f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            TextMeshProUGUI label = CreateText(
                objectName + "_文字",
                buttonObject.transform,
                caption,
                fontSize,
                Cream,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            return button;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string value,
            float fontSize,
            Color color,
            FontStyles style,
            TextAlignmentOptions alignment,
            bool wordWrapping = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            if (preferredFont != null)
                text.font = preferredFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = wordWrapping;
            text.overflowMode = wordWrapping ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = pivot;
            rect.anchorMax = pivot;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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

        #endregion
    }
}
