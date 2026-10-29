// 按业务职责维护 Prefab，复用公共控件与布局实现。
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 系统状态HUD

    /// <summary>构建左上角的坐标、FPS、环境温度与季节信息，固定行距且不拦截输入。</summary>
    private static GameObject BuildPlayerWorldCoordinateHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.PlayerWorldCoordinate, null);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        SetTopLeft(rootRect, 28f, 28f, 560f, 206f);

        TextMeshProUGUI coordinates = CreateText("坐标文本", root.transform, "X  +0.0    Y  +0.0", 16f, Cream);
        coordinates.fontStyle = FontStyles.Bold;
        coordinates.enableWordWrapping = false;
        coordinates.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(coordinates.rectTransform, 0f, 0f, 560f, 28f);

        TextMeshProUGUI fps = CreateText("FPS文本", root.transform, "FPS  --", 15f, Cream);
        fps.fontStyle = FontStyles.Bold;
        fps.enableWordWrapping = false;
        fps.overflowMode = TextOverflowModes.Ellipsis;
        fps.gameObject.SetActive(PlayerWorldCoordinateDisplayPreferences.DefaultShowFps);
        SetTopLeft(fps.rectTransform, 0f, 30f, 240f, 26f);

        TextMeshProUGUI temperature = CreateText(PlayerWorldCoordinateHUD.AmbientTemperatureTextNodeName,
            root.transform, PlayerWorldCoordinateHUD.AmbientTemperatureUnavailableText, 15f, Cream);
        temperature.fontStyle = FontStyles.Bold;
        temperature.enableWordWrapping = false;
        temperature.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(temperature.rectTransform, 0f, 58f, 360f, 110f);

        TextMeshProUGUI season = CreateText(PlayerWorldCoordinateHUD.SeasonTextNodeName,
            root.transform, "第 1 年 · 春季 · 第 1 天", 15f, Cream);
        season.enableWordWrapping = false;
        season.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(season.rectTransform, 0f, 174f, 290f, 26f);

        return root;
    }

    /// <summary>构建不拦截输入的顶部居中保存状态文本，默认隐藏并由 GameSaveStatusHUD 控制显隐。</summary>
    private static GameObject BuildSaveStatusHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.SaveStatus, null, typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -118f);
        rootRect.sizeDelta = new Vector2(260f, 52f);

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        TextMeshProUGUI status = CreateText("保存状态文本", root.transform, "正在保存…", 16f, Cream);
        status.fontStyle = FontStyles.Bold;
        status.alignment = TextAlignmentOptions.Center;
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(status.rectTransform);
        return root;
    }

    /// <summary>构建屏幕左侧中部上方的透明 Buff 条目容器，避开手机翻滚按钮，超出高度时由 ScrollRect 裁剪。</summary>
    private static GameObject BuildBuffStatusHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.BuffStatus, null, typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0.5f);
        rootRect.anchorMax = new Vector2(0f, 0.5f);
        rootRect.pivot = new Vector2(0f, 0f);
        rootRect.anchoredPosition = new Vector2(32f, 60f);
        rootRect.sizeDelta = new Vector2(230f, 42f);

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        GameObject scrollRoot = CreateUIObject("内容列表", root.transform, typeof(ItemStepScrollRect));
        RectTransform scrollRect = scrollRoot.GetComponent<RectTransform>();
        Stretch(scrollRect);

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(RectMask2D), typeof(Image));
        Stretch(viewport.GetComponent<RectTransform>());
        // 只有 Buff 超过可见行数时允许在透明视口滚动，其余时间 HUD 仍不拦截输入。
        Image scrollHitArea = viewport.GetComponent<Image>();
        scrollHitArea.color = Color.clear;
        scrollHitArea.raycastTarget = true;

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;

        return root;
    }

    /// <summary>构建 Buff 行：图标右下角显示可叠层 Buff 的当前层级，名称与层级分离以保持紧凑易读。</summary>
    private static GameObject BuildBuffStatusItem()
    {
        GameObject root = CreateUIObject(
            RuntimeUIPrefabKeys.BuffStatusItem,
            null,
            typeof(Image),
            typeof(BuffStatusRowView));
        LayoutElement rowElement = root.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 42f;

        Image background = root.GetComponent<Image>();
        // 状态 HUD 保持透明，只保留动态内容。
        background.color = Color.clear;
        background.raycastTarget = false;

        HorizontalLayoutGroup rowLayout = root.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(5, 5, 5, 5);
        rowLayout.spacing = 7f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        GameObject iconObject = CreateUIObject("状态符号底板", root.transform, typeof(Image));
        LayoutElement iconElement = iconObject.AddComponent<LayoutElement>();
        iconElement.preferredWidth = 29f;
        iconElement.preferredHeight = 29f;
        Image icon = iconObject.GetComponent<Image>();
        icon.color = SurfaceRaised;
        icon.raycastTarget = false;
        AddOutline(icon, Border);

        // 独立白色图层保留像素图原色，底板与层数徽标继续由各自节点持有。
        GameObject spriteObject = CreateUIObject("状态图标", iconObject.transform, typeof(UnityEngine.UI.Image));
        UnityEngine.UI.Image spriteImage = spriteObject.GetComponent<UnityEngine.UI.Image>();
        spriteImage.color = Color.white;
        spriteImage.raycastTarget = false;
        spriteImage.preserveAspect = true;
        spriteImage.enabled = false;
        Stretch(spriteImage.rectTransform);

        TextMeshProUGUI placeholder = CreateText("占位符文本", iconObject.transform, "·", 15f, Cream);
        placeholder.fontStyle = FontStyles.Bold;
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.enableWordWrapping = false;
        placeholder.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(placeholder.rectTransform);

        GameObject stackBadge = CreateUIObject("层数徽标", iconObject.transform, typeof(Image));
        RectTransform stackBadgeRect = stackBadge.GetComponent<RectTransform>();
        SetBottomRight(stackBadgeRect, -2f, -2f, 15f, 15f);
        Image stackBadgeImage = stackBadge.GetComponent<Image>();
        stackBadgeImage.color = new Color(0.10f, 0.10f, 0.10f, 0.96f);
        stackBadgeImage.raycastTarget = false;
        AddOutline(stackBadgeImage, new Color(0.92f, 0.92f, 0.92f, 0.34f));

        TextMeshProUGUI stackText = CreateText("层数文本", stackBadge.transform, "1", 9f, Cream);
        stackText.fontStyle = FontStyles.Bold;
        stackText.alignment = TextAlignmentOptions.Center;
        stackText.enableWordWrapping = false;
        stackText.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(stackText.rectTransform);
        AddOutline(stackText, new Color(0.05f, 0.05f, 0.05f, 0.92f));

        // 运行时只对可叠层 Buff 打开，Prefab 默认隐藏避免编辑器预览误导。
        stackBadge.SetActive(false);

        GameObject info = CreateUIObject("状态信息", root.transform);
        LayoutElement infoElement = info.AddComponent<LayoutElement>();
        infoElement.flexibleWidth = 1f;
        VerticalLayoutGroup infoLayout = info.AddComponent<VerticalLayoutGroup>();
        infoLayout.spacing = 1f;
        infoLayout.childAlignment = TextAnchor.MiddleLeft;
        infoLayout.childControlWidth = true;
        infoLayout.childControlHeight = true;
        infoLayout.childForceExpandWidth = true;
        infoLayout.childForceExpandHeight = false;

        TextMeshProUGUI name = CreateText("状态名称", info.transform, "Buff", 14f, Cream);
        name.fontStyle = FontStyles.Bold;
        name.enableAutoSizing = true;
        name.fontSizeMin = 12f;
        name.fontSizeMax = 14f;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.gameObject.AddComponent<LayoutElement>().preferredHeight = 17f;

        TextMeshProUGUI remaining = CreateText("剩余时间", info.transform, "剩余 30s", 12f, Muted);
        remaining.enableAutoSizing = true;
        remaining.fontSizeMin = 10f;
        remaining.fontSizeMax = 12f;
        remaining.enableWordWrapping = false;
        remaining.overflowMode = TextOverflowModes.Ellipsis;
        remaining.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        return root;
    }

    private static void AddNetworkPlayerNameLabel(GameObject root)
    {
        Transform existing = FindTransform(root.transform, "玩家名称");
        TextMeshPro label = existing != null ? existing.GetComponent<TextMeshPro>() : null;
        if (label == null)
        {
            GameObject labelObject = new GameObject("玩家名称", typeof(TextMeshPro));
            labelObject.transform.SetParent(root.transform, false);
            label = labelObject.GetComponent<TextMeshPro>();
        }

        label.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        label.transform.localRotation = Quaternion.identity;
        label.transform.localScale = Vector3.one;
        // 头顶名称使用紧凑单行框，长名字省略，避免选框盖住大块场景。
        label.rectTransform.sizeDelta = new Vector2(4f, 0.7f);
        label.text = "玩家";
        label.font = font;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 4f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.color = Color.white;
        label.sortingOrder = 100;
    }

    /// <summary>确保本地玩家 Prefab 挂载 Buff HUD 控制器；控制器本身只负责实例化正式 UI Prefab。</summary>
    private static void EnsurePlayerBuffStatusHUD(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<PlayerBuffStatusHUD>() == null)
        {
            root.AddComponent<PlayerBuffStatusHUD>();
            EditorUtility.SetDirty(root);
        }
    }

    /// <summary>确保本地玩家 Prefab 挂载手机 HUD 控制器；编辑器模拟必须由 Inspector 显式开启。</summary>
    private static void EnsurePlayerMobileControlsHUD(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<PlayerMobileControlsHUD>() == null)
        {
            root.AddComponent<PlayerMobileControlsHUD>();
            EditorUtility.SetDirty(root);
        }
    }

    /// <summary>在全屏根 Canvas 下固化安全区节点，所有正式面板与手机 HUD 都由 UIManager 挂到这里。</summary>
    private static void EnsureSafeAreaRoot(GameObject root)
    {
        if (root == null)
            return;

        Transform existing = root.transform.Find("SafeAreaRoot");
        GameObject safeAreaObject = existing != null
            ? existing.gameObject
            : new GameObject("SafeAreaRoot", typeof(RectTransform), typeof(SafeAreaRectController));
        if (existing == null)
            safeAreaObject.transform.SetParent(root.transform, false);

        RectTransform rect = safeAreaObject.GetComponent<RectTransform>();
        Stretch(rect);
        if (safeAreaObject.GetComponent<SafeAreaRectController>() == null)
            safeAreaObject.AddComponent<SafeAreaRectController>();
        safeAreaObject.transform.SetAsLastSibling();
        EditorUtility.SetDirty(root);
    }

    #endregion
}
