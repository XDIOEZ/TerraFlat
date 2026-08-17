using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 食物 UI 模块：负责食物参数面板的创建、显示、刷新、拖拽位置保存和销毁。
/// UI 只读取食物运行时上下文，不参与营养计算、回血或进食结算。
/// </summary>
public sealed class FoodUIModule : IFoodMechanic, IFoodStateObserver, IDisposable
{
    private readonly IFoodRuntimeContext context;
    private readonly GameObject panelPrefab;
    private readonly Func<GameObject> readPanelInstance;
    private readonly Action<GameObject> writePanelInstance;
    private readonly Func<BasePanel> readPanel;
    private readonly Action<BasePanel> writePanel;

    public FoodUIModule(
        IFoodRuntimeContext context,
        GameObject panelPrefab,
        Func<GameObject> readPanelInstance,
        Action<GameObject> writePanelInstance,
        Func<BasePanel> readPanel,
        Action<BasePanel> writePanel)
    {
        this.context = context;
        this.panelPrefab = panelPrefab;
        this.readPanelInstance = readPanelInstance;
        this.writePanelInstance = writePanelInstance;
        this.readPanel = readPanel;
        this.writePanel = writePanel;
    }

    public string MechanicId => "core.ui";
    public int Priority => 1000;

    /// <summary>食物数据变化时刷新已打开的面板。</summary>
    public void OnFoodStateChanged(FoodStateChangedContext _)
    {
        RefreshUI();
    }

    public void ShowPanel()
    {
        if (!EnsurePanelExists())
            return;

        OpenPanel();
    }

    public void HidePanel()
    {
        BasePanel panel = ResolvePanel();
        if (panel == null)
            return;

        panel.Close();
        context.Data.ShowCanvas = false;
        SavePanelPosition();
    }

    public void TogglePanel()
    {
        if (context.Item != null)
        {
            GameController controller = context.Item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            if (controller != null && controller.IsGameplayInputLocked)
                return;
        }

        BasePanel panel = ResolvePanel();
        if (panel == null)
        {
            ShowPanel();
            return;
        }

        if (panel.IsOpen())
            HidePanel();
        else
            OpenPanel();
    }

    public void RefreshUI()
    {
        BasePanel panel = ResolvePanel();
        if (panel == null || context.Data?.nutrition == null)
            return;

        UpdateNutrition(panel, "碳水", context.Data.nutrition.Carbohydrates, context.Data.nutrition.Max_Carbohydrates);
        UpdateNutrition(panel, "脂肪", context.Data.nutrition.Fat, context.Data.nutrition.Max_Fat);
        UpdateNutrition(panel, "蛋白质", context.Data.nutrition.Protein, context.Data.nutrition.Max_Protein);
        UpdateNutrition(panel, "水", context.Data.nutrition.Water, context.Data.nutrition.Max_Water);
        UpdateNutrition(panel, "维生素", context.Data.nutrition.Vitamins, context.Data.nutrition.Max_Vitamins);
        UpdateTemperatureUI(panel);
    }

    public void SavePanelPosition()
    {
        GameObject panelInstance = readPanelInstance?.Invoke();
        if (panelInstance == null)
            return;

        UI_Drag dragComponent = panelInstance.GetComponentInChildren<UI_Drag>();
        if (dragComponent != null)
        {
            context.Data.PanelPosition = dragComponent.rectTransform.anchoredPosition;
            return;
        }

        RectTransform panelRectTransform = panelInstance.GetComponent<RectTransform>();
        if (panelRectTransform != null)
            context.Data.PanelPosition = panelRectTransform.anchoredPosition;
    }

    public void DestroyPanel()
    {
        GameObject panelInstance = readPanelInstance?.Invoke();
        writePanel?.Invoke(null);
        writePanelInstance?.Invoke(null);

        if (panelInstance == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(panelInstance);
        else
            UnityEngine.Object.DestroyImmediate(panelInstance);
    }

    public void Dispose()
    {
        DestroyPanel();
    }

    private bool EnsurePanelExists()
    {
        if (ResolvePanel() != null)
            return true;

        if (panelPrefab == null)
        {
            Debug.LogError("[FoodPanel] 食物参数面板 Prefab 未配置。");
            return false;
        }

        if (UIManager.Instance == null)
        {
            Debug.LogError("[FoodPanel] UIManager 未初始化，无法创建食物参数面板。");
            return false;
        }

        BasePanel createdPanel = UIManager.Instance.CreatePanelFromGameObject(panelPrefab);
        if (createdPanel == null)
        {
            Debug.LogError("[FoodPanel] 创建食物参数面板失败。");
            return false;
        }

        createdPanel.SetEscapeShortcutEnabled(false);
        createdPanel.SetGameplayInputBlocking(false);
        writePanelInstance?.Invoke(createdPanel.gameObject);
        writePanel?.Invoke(createdPanel);

        LayoutRebuilder.ForceRebuildLayoutImmediate(createdPanel.rectTransform);
        RestorePanelPosition();
        RefreshUI();
        return true;
    }

    private BasePanel ResolvePanel()
    {
        BasePanel panel = readPanel?.Invoke();
        if (panel != null)
            return panel;

        GameObject panelInstance = readPanelInstance?.Invoke();
        if (panelInstance == null)
            return null;

        panel = panelInstance.GetComponent<BasePanel>();
        if (panel != null)
            writePanel?.Invoke(panel);
        return panel;
    }

    private void OpenPanel()
    {
        BasePanel panel = ResolvePanel();
        if (panel == null)
            return;

        panel.Open();
        SetStatusHudInputTransparent(panel);
        context.Data.ShowCanvas = true;
        RefreshUI();
    }

    private void RestorePanelPosition()
    {
        GameObject panelInstance = readPanelInstance?.Invoke();
        if (panelInstance == null)
            return;

        UI_Drag dragComponent = panelInstance.GetComponentInChildren<UI_Drag>(true);
        RectTransform movableRect = dragComponent != null
            ? dragComponent.rectTransform
            : panelInstance.GetComponent<RectTransform>();
        if (movableRect == null)
            return;

        if (context.Data.PanelPosition != Vector2.zero)
            movableRect.anchoredPosition = context.Data.PanelPosition;

        Canvas.ForceUpdateCanvases();
        ClampInsideCanvas(movableRect, 20f);
    }

    private void SetStatusHudInputTransparent(BasePanel panel)
    {
        if (panel == null)
            return;

        if (panel.canvasGroup != null)
        {
            panel.canvasGroup.interactable = false;
            panel.canvasGroup.blocksRaycasts = false;
        }

        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    private static void UpdateNutrition(BasePanel panel, string name, float currentValue, float maxValue)
    {
        Slider slider = panel.GetSlider(name);
        if (slider != null)
        {
            slider.maxValue = maxValue;
            slider.value = currentValue;
        }

        TMPro.TextMeshProUGUI text = panel.GetText($"DataText_{name}");
        if (text != null)
            text.text = $"{Mathf.RoundToInt(currentValue)}/{Mathf.RoundToInt(maxValue)}";
    }

    private void UpdateTemperatureUI(BasePanel panel)
    {
        Mod_Temperature temperature = null;
        context.Item?.itemMods?.GetMod_ByID(ModText.Temperature, out temperature);
        Slider slider = panel.GetSlider("体温");
        TMPro.TextMeshProUGUI dataText = panel.GetText("DataText_体温");
        if (temperature?.Data == null)
        {
            if (dataText != null)
                dataText.text = "--";
            return;
        }

        float coldStart = temperature.Data.ColdDamageStart;
        float hotStart = Mathf.Max(coldStart + 1f, temperature.Data.HotDamageStart);
        float buffer = Mathf.Max(2f, (hotStart - coldStart) * 0.2f);
        if (slider != null)
        {
            slider.minValue = coldStart - buffer;
            slider.maxValue = hotStart + buffer;
            slider.value = temperature.Data.CurrentTemperature;
        }

        if (dataText != null)
            dataText.text = $"{temperature.Data.CurrentTemperature:0.0}°C";
    }

    private static void ClampInsideCanvas(RectTransform panelRect, float margin)
    {
        Canvas canvas = panelRect != null ? panelRect.GetComponentInParent<Canvas>() : null;
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (panelRect == null || canvasRect == null)
            return;

        Vector3[] worldCorners = new Vector3[4];
        panelRect.GetWorldCorners(worldCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < worldCorners.Length; i++)
        {
            Vector3 local = canvasRect.InverseTransformPoint(worldCorners[i]);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        Rect canvasBounds = canvasRect.rect;
        Vector2 correction = Vector2.zero;
        float safeMinX = canvasBounds.xMin + margin;
        float safeMaxX = canvasBounds.xMax - margin;
        float safeMinY = canvasBounds.yMin + margin;
        float safeMaxY = canvasBounds.yMax - margin;
        if (min.x < safeMinX)
            correction.x = safeMinX - min.x;
        else if (max.x > safeMaxX)
            correction.x = safeMaxX - max.x;
        if (min.y < safeMinY)
            correction.y = safeMinY - min.y;
        else if (max.y > safeMaxY)
            correction.y = safeMaxY - max.y;
        if (correction == Vector2.zero)
            return;

        Vector3 worldCorrection = canvasRect.TransformVector(correction);
        Vector3 parentCorrection = panelRect.parent != null
            ? panelRect.parent.InverseTransformVector(worldCorrection)
            : worldCorrection;
        panelRect.anchoredPosition += new Vector2(parentCorrection.x, parentCorrection.y);
    }
}
