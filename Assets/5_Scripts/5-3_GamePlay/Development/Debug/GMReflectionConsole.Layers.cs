using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 层级显示分页

    private GMTemperatureOverlay temperatureOverlay; // 独立于 GM 窗口显隐的温度眼镜
    private Button temperatureOverlayButton; // 温度层开关
    private TextMeshProUGUI temperatureTransparencyText; // 透明度百分比

    private void BuildLayersPage()
    {
        GmPageView page = CreatePage(GmPageId.Layers);
        AddPageIntro(page.Content, "层级显示", "开启温度层后，关闭 GM 窗口即可透过半透明热力图查看地表温度。");
        GameObject controls = CreateUiObject("Temperature Controls", page.Content);
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
        CreateTemperatureTransparencyControl(controls.transform);

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
            Color color = GMTemperatureOverlay.EvaluateColor(temperature);
            color.a = 1f;
            TextMeshProUGUI label = CreateText(legend.transform, $"■  {temperature:0}℃", 18f, color);
            label.alignment = TextAlignmentOptions.Center;
        }
        AddPageHint(page.Content, "蓝色冷、红色热；色标固定为 -20～60℃，超出范围使用两端颜色。未加载区域透明。\n透明度越高，地表越清晰：0% 完全覆盖，100% 完全透明。", 42f);
        RefreshTemperatureOverlayButton();
    }

    /// <summary>右侧占两份宽度，标签定宽、滑轨自适应；透明度为 0～100%，默认 42%。</summary>
    private void CreateTemperatureTransparencyControl(Transform parent)
    {
        GameObject control = CreateUiObject("Temperature Transparency", parent);
        Image background = control.AddComponent<Image>();
        background.color = new Color(0.043f, 0.112f, 0.139f, 1f);
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

        temperatureTransparencyText = CreateText(control.transform, string.Empty, 18f, new Color(0.95f, 0.91f, 0.84f));
        temperatureTransparencyText.enableWordWrapping = false;
        LayoutElement labelLayout = temperatureTransparencyText.gameObject.AddComponent<LayoutElement>();
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
        track.color = new Color(0.18f, 0.28f, 0.30f, 1f);
        track.raycastTarget = false;
        track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        track.rectTransform.sizeDelta = new Vector2(0f, 8f);
        Image fill = CreateUiObject("Fill", track.transform).AddComponent<Image>();
        fill.color = new Color(0.16f, 0.70f, 0.64f, 1f);
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.sizeDelta = Vector2.zero;
        Image handle = CreateUiObject("Handle", range).AddComponent<Image>();
        handle.color = new Color(0.95f, 0.91f, 0.84f, 1f);
        handle.raycastTarget = false;
        handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        handle.rectTransform.sizeDelta = new Vector2(20f, 28f);
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.SetValueWithoutNotify(temperatureOverlay.Transparency * 100f);
        temperatureTransparencyText.SetText("透明度  {0:0}%", slider.value);
        slider.onValueChanged.AddListener(SetTemperatureTransparency);

        // 拖动只更新材质与内存偏好，松开或键盘移走焦点时统一落盘。
        EventTrigger trigger = sliderObject.AddComponent<EventTrigger>();
        foreach (EventTriggerType eventType in new[] { EventTriggerType.PointerUp, EventTriggerType.Deselect })
        {
            EventTrigger.Entry entry = new() { eventID = eventType };
            entry.callback.AddListener(_ => GMConsolePreferences.SavePendingChanges());
            trigger.triggers.Add(entry);
        }
        RegisterSearchEntry(GmPageId.Layers, "温度层透明度", "透明度 热力图 transparency opacity", (RectTransform)control.transform);
    }

    /// <summary>即时修改覆盖层材质，不触发地块重采样或纹理上传。</summary>
    private void SetTemperatureTransparency(float percent)
    {
        temperatureOverlay.SetTransparency(percent / 100f);
        temperatureTransparencyText.SetText("透明度  {0:0}%", percent);
        GMConsolePreferences.SetTemperatureOverlayTransparency(temperatureOverlay.Transparency);
    }

    private void ToggleTemperatureOverlay()
    {
        temperatureOverlay.SetVisible(!temperatureOverlay.Visible);
        GMConsolePreferences.SetTemperatureOverlayVisible(temperatureOverlay.Visible);
        RefreshTemperatureOverlayButton();
        SetStatus(temperatureOverlay.Visible ? "温度层已开启，关闭 GM 窗口后仍会显示。" : "温度层已关闭。",
            new Color(0.35f, 0.95f, 0.85f));
    }

    private void RefreshTemperatureOverlayButton()
    {
        if (temperatureOverlayButton == null)
            return;
        bool visible = temperatureOverlay != null && temperatureOverlay.Visible;
        temperatureOverlayButton.GetComponentInChildren<TextMeshProUGUI>(true).text = visible
            ? "显示温度：开" : "显示温度：关";
        temperatureOverlayButton.GetComponent<Image>().color = visible
            ? new Color(0.10f, 0.45f, 0.31f, 1f)
            : new Color(0.094f, 0.212f, 0.251f, 1f);
    }

    #endregion
}
