using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 层级显示分页

    private GMTemperatureOverlay temperatureOverlay; // 独立于 GM 窗口显隐的温度眼镜
    private Button temperatureOverlayButton; // 温度层开关

    private void BuildLayersPage()
    {
        GmPageView page = CreatePage(GmPageId.Layers);
        AddPageIntro(page.Content, "层级显示", "开启温度层后，关闭 GM 窗口即可透过半透明热力图查看地表温度。");
        Transform grid = CreateActionGrid(page.Content, 3, 300f, 48f, 1);
        temperatureOverlayButton = CreateSearchableButton(
            grid, GmPageId.Layers, "显示温度：关", "温度 热力图 冷 热 temperature heatmap overlay", ToggleTemperatureOverlay, 48f);

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
        AddPageHint(page.Content, "蓝色冷、红色热；色标固定为 -20～60℃，超出范围使用两端颜色。未加载区域透明。", 42f);
        RefreshTemperatureOverlayButton();
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
