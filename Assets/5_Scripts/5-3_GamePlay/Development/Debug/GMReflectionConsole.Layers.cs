using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 层级显示分页

    private GMWorldLayerOverlay worldLayerOverlay; // 独立于 GM 窗口显隐的世界观察层
    private Button temperatureOverlayButton; // 温度层开关
    private Button contaminationOverlayButton; // 污染层开关
    private Button navigationOverlayButton; // 怪物到本地玩家的逐格流场开关
    private TextMeshProUGUI worldLayerTransparencyText; // 透明度百分比

    private void BuildLayersPage()
    {
        GmPageView page = CreatePage(GmPageId.Layers);
        AddPageIntro(page.Content, "层级显示", "切换温度、污染或怪物导航观察层；关闭 GM 窗口后，当前观察层仍会保留在世界中。");
        GameObject controls = CreateUiObject("World Layer Controls", page.Content);
        controls.AddComponent<LayoutElement>().preferredHeight = 48f;
        HorizontalLayoutGroup controlsLayout = controls.AddComponent<HorizontalLayoutGroup>();
        controlsLayout.spacing = 12f;
        controlsLayout.childControlWidth = true;
        controlsLayout.childControlHeight = true;
        controlsLayout.childForceExpandWidth = false;
        controlsLayout.childForceExpandHeight = true;
        temperatureOverlayButton = CreateSearchableButton(
            controls.transform, GmPageId.Layers, "显示温度：关", "温度 热力图 冷 热 temperature heatmap overlay", ToggleTemperatureOverlay, 48f);
        LayoutElement buttonLayout = temperatureOverlayButton.GetComponent<LayoutElement>();
        buttonLayout.preferredWidth = 0f;
        buttonLayout.flexibleWidth = 1f;
        contaminationOverlayButton = CreateSearchableButton(
            controls.transform, GmPageId.Layers, "显示污染：关", "污染 脏污 病原 热力图 contamination pollution pathogen overlay", ToggleContaminationOverlay, 48f);
        LayoutElement contaminationButtonLayout = contaminationOverlayButton.GetComponent<LayoutElement>();
        contaminationButtonLayout.preferredWidth = 0f;
        contaminationButtonLayout.flexibleWidth = 1f;
        CreateWorldLayerTransparencyControl(controls.transform);

        // 导航独占一行，不挤占原有温度、污染按钮与透明度滑轨。
        navigationOverlayButton = CreateSearchableButton(
            page.Content, GmPageId.Layers, "显示怪物导航：关",
            "怪物 玩家 导航 寻路 流场 箭头 路径 monster player navigation path flow field arrow",
            ToggleNavigationOverlay, 60f);

        GameObject legend = CreateUiObject("Temperature Legend", page.Content);
        legend.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup layout = legend.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        for (int i = 0; i < 5; i++)
        {
            float temperature = -20f + i * 20f;
            Color color = GMWorldLayerOverlay.EvaluateTemperatureColor(temperature);
            color.a = 1f;
            TextMeshProUGUI label = CreateText(legend.transform, $"■  {temperature:0}℃", 18f, color);
            label.alignment = TextAlignmentOptions.Center;
        }
        GameObject contaminationLegend = CreateUiObject("Contamination Legend", page.Content);
        contaminationLegend.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup contaminationLayout = contaminationLegend.AddComponent<HorizontalLayoutGroup>();
        contaminationLayout.spacing = 12f;
        contaminationLayout.childControlWidth = true;
        contaminationLayout.childControlHeight = true;
        contaminationLayout.childForceExpandWidth = true;
        for (int i = 0; i < 5; i++)
        {
            float load = i / 4f;
            Color color = GMWorldLayerOverlay.EvaluateContaminationColor(load);
            color.a = 1f;
            TextMeshProUGUI label = CreateText(contaminationLegend.transform, $"■  {load * 100f:0}%", 18f, color);
            label.alignment = TextAlignmentOptions.Center;
        }
        AddPageHint(page.Content,
            "温度：蓝色冷、红色热，固定 -20～60℃。污染：绿色清洁、红色高负荷；每格取所有已注册污染类型（包含 MOD）的最高归一化负荷。未加载区域透明。\n怪物导航：每个可达格显示朝本地玩家寻路的下一步箭头；蓝点为玩家目标格，红叉为不可达格，障碍和未加载格不画。读取实际共享流场，不代表所有怪物都在追击；目标尚未就绪时不显示。\n透明度越高，地表越清晰：0% 完全覆盖，100% 完全透明。",
            132f);
        RefreshWorldLayerOverlayButtons();
    }

    /// <summary>右侧占两份宽度，标签定宽、滑轨自适应；透明度为 0～100%，默认 42%。</summary>
    private void CreateWorldLayerTransparencyControl(Transform parent)
    {
        GameObject control = CreateUiObject("World Layer Transparency", parent);
        Image background = control.AddComponent<Image>();
        background.color = GmSurface;
        background.raycastTarget = false;
        LayoutElement element = control.AddComponent<LayoutElement>();
        element.minWidth = 0f;
        element.preferredWidth = 0f;
        element.flexibleWidth = 2f;
        HorizontalLayoutGroup layout = control.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 8, 0, 0);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        worldLayerTransparencyText = CreateText(control.transform, string.Empty, 18f, GmTextPrimary);
        worldLayerTransparencyText.enableWordWrapping = false;
        LayoutElement labelLayout = worldLayerTransparencyText.gameObject.AddComponent<LayoutElement>();
        labelLayout.minWidth = 120f;
        labelLayout.preferredWidth = 120f;

        GameObject sliderObject = CreateUiObject("Transparency Slider", control.transform);
        sliderObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // 整个滑轨区域接收点击；装饰图形不阻挡滑动和触摸。
        sliderObject.AddComponent<Image>().color = Color.clear;
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        slider.direction = Slider.Direction.LeftToRight;

        RectTransform range = (RectTransform)CreateUiObject("Slide Area", sliderObject.transform).transform;
        range.anchorMin = Vector2.zero;
        range.anchorMax = Vector2.one;
        range.offsetMin = new Vector2(10f, 0f);
        range.offsetMax = new Vector2(-10f, 0f);
        Image track = CreateUiObject("Track", range).AddComponent<Image>();
        track.color = GmSurfaceLow;
        track.raycastTarget = false;
        track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        track.rectTransform.sizeDelta = new Vector2(0f, 8f);
        Image fill = CreateUiObject("Fill", track.transform).AddComponent<Image>();
        fill.color = GmAccent;
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.sizeDelta = Vector2.zero;
        Image handle = CreateUiObject("Handle", range).AddComponent<Image>();
        handle.color = GmTextPrimary;
        handle.raycastTarget = false;
        handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        handle.rectTransform.sizeDelta = new Vector2(20f, 28f);
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.SetValueWithoutNotify(worldLayerOverlay.Transparency * 100f);
        worldLayerTransparencyText.SetText("透明度  {0:0}%", slider.value);
        slider.onValueChanged.AddListener(SetWorldLayerTransparency);

        // 拖动只更新材质与内存偏好，松开或键盘移走焦点时统一落盘。
        EventTrigger trigger = sliderObject.AddComponent<EventTrigger>();
        foreach (EventTriggerType eventType in new[] { EventTriggerType.PointerUp, EventTriggerType.Deselect })
        {
            EventTrigger.Entry entry = new() { eventID = eventType };
            entry.callback.AddListener(_ => GMConsolePreferences.SavePendingChanges());
            trigger.triggers.Add(entry);
        }
        RegisterSearchEntry(GmPageId.Layers, "图层透明度", "温度 污染 导航 箭头 透明度 热力图 transparency opacity", (RectTransform)control.transform);
    }

    /// <summary>即时修改覆盖层材质，不触发地块重采样或纹理上传。</summary>
    private void SetWorldLayerTransparency(float percent)
    {
        worldLayerOverlay.SetTransparency(percent / 100f);
        worldLayerTransparencyText.SetText("透明度  {0:0}%", percent);
        GMConsolePreferences.SetWorldLayerOverlayTransparency(worldLayerOverlay.Transparency);
    }

    /// <summary>温度按钮再次点击时关闭；切入温度会自动替换污染观察层。</summary>
    private void ToggleTemperatureOverlay()
    {
        GmWorldLayerMode mode = worldLayerOverlay.Mode == GmWorldLayerMode.Temperature
            ? GmWorldLayerMode.Off
            : GmWorldLayerMode.Temperature;
        SetWorldLayerMode(mode);
        SetStatus(mode == GmWorldLayerMode.Temperature ? "温度层已开启，关闭 GM 窗口后仍会显示。" : "温度层已关闭。",
            GmAccentHover);
    }

    /// <summary>污染按钮显示所有已注册污染定义中的最高归一化负荷，MOD 定义无需额外接 UI。</summary>
    private void ToggleContaminationOverlay()
    {
        GmWorldLayerMode mode = worldLayerOverlay.Mode == GmWorldLayerMode.Contamination
            ? GmWorldLayerMode.Off
            : GmWorldLayerMode.Contamination;
        SetWorldLayerMode(mode);
        SetStatus(mode == GmWorldLayerMode.Contamination ? "污染层已开启，关闭 GM 窗口后仍会显示。" : "污染层已关闭。",
            GmAccentHover);
    }

    /// <summary>显示怪物实际使用的玩家共享流场；再次点击关闭，不改变 AI 的目标或行为。</summary>
    private void ToggleNavigationOverlay()
    {
        GmWorldLayerMode mode = worldLayerOverlay.Mode == GmWorldLayerMode.Navigation
            ? GmWorldLayerMode.Off
            : GmWorldLayerMode.Navigation;
        SetWorldLayerMode(mode);
        SetStatus(mode == GmWorldLayerMode.Navigation
            ? "怪物导航层已开启；共享目标就绪后逐格显示，关闭 GM 窗口后仍会显示。"
            : "怪物导航层已关闭。", GmAccentHover);
    }

    /// <summary>统一切换世界观察层并保存本地 GM 偏好。</summary>
    private void SetWorldLayerMode(GmWorldLayerMode mode)
    {
        worldLayerOverlay.SetMode(mode);
        GMConsolePreferences.SetWorldLayerOverlayMode(mode);
        RefreshWorldLayerOverlayButtons();
    }

    /// <summary>同步温度、污染和导航按钮的互斥状态。</summary>
    private void RefreshWorldLayerOverlayButtons()
    {
        GmWorldLayerMode mode = worldLayerOverlay != null ? worldLayerOverlay.Mode : GmWorldLayerMode.Off;
        RefreshWorldLayerOverlayButton(temperatureOverlayButton, "显示温度", mode == GmWorldLayerMode.Temperature);
        RefreshWorldLayerOverlayButton(contaminationOverlayButton, "显示污染", mode == GmWorldLayerMode.Contamination);
        RefreshWorldLayerOverlayButton(navigationOverlayButton, "显示怪物导航", mode == GmWorldLayerMode.Navigation);
    }

    /// <summary>刷新一个观察层按钮的文字与颜色。</summary>
    private static void RefreshWorldLayerOverlayButton(Button button, string label, bool active)
    {
        if (button == null)
            return;
        button.GetComponentInChildren<TextMeshProUGUI>(true).text = active ? $"{label}：开" : $"{label}：关";
        SetGmButtonVisual(button, active ? GmSelection : GmSurfaceRaised, active);
    }

    #endregion
}
