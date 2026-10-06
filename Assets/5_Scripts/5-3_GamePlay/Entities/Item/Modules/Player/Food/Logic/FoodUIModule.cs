using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 食物 UI 模块：仅为本地玩家创建常驻参数 HUD，负责刷新、位置保存和销毁，
/// 并读取玩家 Mod_DamageReceiver 的权威生命值显示到角色参数面板。
/// UI 只读取运行时状态，不参与营养计算、回血或进食结算。
/// </summary>
public sealed class FoodUIModule : IFoodMechanic, IFoodStateObserver, IDisposable
{
    private const float StatusBarTransitionDuration = 0.24f;

    private struct StatusBarTransition
    {
        public Slider Slider;
        public float StartValue;
        public float TargetValue;
        public float Elapsed;
    }

    private struct DisplayedRange
    {
        public int Current;
        public int Maximum;
        public bool Initialized;
    }

    private readonly IFoodRuntimeContext context;
    private readonly Mod_DamageReceiver damageReceiver;
    private readonly GameObject panelPrefab;
    private readonly Func<GameObject> readPanelInstance;
    private readonly Action<GameObject> writePanelInstance;
    private readonly Func<BasePanel> readPanel;
    private readonly Action<BasePanel> writePanel;
    private readonly Dictionary<Slider, float> statusBarTargets = new Dictionary<Slider, float>();
    private readonly List<StatusBarTransition> statusBarTransitions = new List<StatusBarTransition>(8);
    private Coroutine statusBarTransitionCoroutine;
    private BasePanel boundPanel;
    private Slider carbohydratesSlider;
    private Slider fatSlider;
    private Slider proteinSlider;
    private Slider waterSlider;
    private Slider vitaminsSlider;
    private Slider healthSlider;
    private Slider temperatureSlider;
    private TMPro.TextMeshProUGUI carbohydratesText;
    private TMPro.TextMeshProUGUI fatText;
    private TMPro.TextMeshProUGUI proteinText;
    private TMPro.TextMeshProUGUI waterText;
    private TMPro.TextMeshProUGUI vitaminsText;
    private TMPro.TextMeshProUGUI healthText;
    private TMPro.TextMeshProUGUI temperatureText;
    private Mod_Temperature temperatureModule;
    private DisplayedRange carbohydratesDisplay;
    private DisplayedRange fatDisplay;
    private DisplayedRange proteinDisplay;
    private DisplayedRange waterDisplay;
    private DisplayedRange vitaminsDisplay;
    private DisplayedRange healthDisplay;
    private int temperatureDisplayTenths = int.MinValue;
    private bool temperatureDisplayInitialized;

    public FoodUIModule(
        IFoodRuntimeContext context,
        Mod_DamageReceiver damageReceiver,
        GameObject panelPrefab,
        Func<GameObject> readPanelInstance,
        Action<GameObject> writePanelInstance,
        Func<BasePanel> readPanel,
        Action<BasePanel> writePanel)
    {
        this.context = context;
        this.damageReceiver = damageReceiver;
        this.panelPrefab = panelPrefab;
        this.readPanelInstance = readPanelInstance;
        this.writePanelInstance = writePanelInstance;
        this.readPanel = readPanel;
        this.writePanel = writePanel;
        BindHealthChanged();
    }

    public string MechanicId => "core.ui";
    public int Priority => 1000;

    /// <summary>食物数据变化时刷新已打开的面板。</summary>
    public void OnFoodStateChanged(FoodStateChangedContext _)
    {
        RefreshUI();
    }

    #region 常驻 HUD 生命周期

    /// <summary>本地玩家加载后创建并显示 HUD，普通食物、动物和远端玩家不创建面板。</summary>
    public void Initialize()
    {
        if (!(context.Item is Player player) || !player.IsLocalProfile)
            return;

        if (EnsurePanelExists())
            OpenPanel();
    }

    #endregion

    public void RefreshUI()
    {
        BasePanel panel = ResolvePanel();
        if (panel == null)
            return;
        BindPanelControls(panel);

        if (context.Data?.nutrition != null)
        {
            UpdateNutrition(carbohydratesSlider, carbohydratesText, ref carbohydratesDisplay,
                context.Data.nutrition.Carbohydrates, context.Data.nutrition.Max_Carbohydrates);
            UpdateNutrition(fatSlider, fatText, ref fatDisplay,
                context.Data.nutrition.Fat, context.Data.nutrition.Max_Fat);
            UpdateNutrition(proteinSlider, proteinText, ref proteinDisplay,
                context.Data.nutrition.Protein, context.Data.nutrition.Max_Protein);
            UpdateNutrition(waterSlider, waterText, ref waterDisplay,
                context.Data.nutrition.Water, context.Data.nutrition.Max_Water);
            UpdateNutrition(vitaminsSlider, vitaminsText, ref vitaminsDisplay,
                context.Data.nutrition.Vitamins, context.Data.nutrition.Max_Vitamins);
        }

        UpdateTemperatureUI(panel);
        UpdateHealthUI(panel);
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
        StopStatusBarTransitions();
        ClearPanelBindings();
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
        UnbindHealthChanged();
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
        BindPanelControls(createdPanel);

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
        RefreshUI();
    }

    /// <summary>面板实例固定后一次性缓存高频状态控件，后续刷新不再遍历层级或按名称查找。</summary>
    private void BindPanelControls(BasePanel panel)
    {
        if (panel == null || boundPanel == panel)
            return;

        ClearPanelBindings();
        boundPanel = panel;
        carbohydratesSlider = panel.GetSlider("碳水");
        fatSlider = panel.GetSlider("脂肪");
        proteinSlider = panel.GetSlider("蛋白质");
        waterSlider = panel.GetSlider("水");
        vitaminsSlider = panel.GetSlider("维生素");
        healthSlider = FindSliderOnce(panel, "血量");
        temperatureSlider = panel.GetSlider("体温");
        panel.TryGetText("DataText_碳水", out carbohydratesText);
        panel.TryGetText("DataText_脂肪", out fatText);
        panel.TryGetText("DataText_蛋白质", out proteinText);
        panel.TryGetText("DataText_水", out waterText);
        panel.TryGetText("DataText_维生素", out vitaminsText);
        panel.TryGetText("DataText_血量", out healthText);
        panel.TryGetText("DataText_体温", out temperatureText);
        temperatureModule = context.Item?.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        statusBarTransitionCoroutine = panel.StartCoroutine(AdvanceStatusBarTransitionsCoroutine(panel));
    }

    /// <summary>兼容旧版 Prefab 缺少可选血量条时不输出警告，只在绑定面板时扫描一次。</summary>
    private static Slider FindSliderOnce(BasePanel panel, string name)
    {
        if (panel == null)
            return null;

        Slider[] sliders = panel.GetComponentsInChildren<Slider>(true);
        for (int i = 0; i < sliders.Length; i++)
        {
            Slider slider = sliders[i];
            if (slider != null && string.Equals(slider.name, name, StringComparison.Ordinal))
                return slider;
        }

        return null;
    }

    private void ClearPanelBindings()
    {
        if (statusBarTransitionCoroutine != null && boundPanel != null)
            boundPanel.StopCoroutine(statusBarTransitionCoroutine);
        statusBarTransitionCoroutine = null;
        StopStatusBarTransitions();
        boundPanel = null;
        carbohydratesSlider = null;
        fatSlider = null;
        proteinSlider = null;
        waterSlider = null;
        vitaminsSlider = null;
        healthSlider = null;
        temperatureSlider = null;
        carbohydratesText = null;
        fatText = null;
        proteinText = null;
        waterText = null;
        vitaminsText = null;
        healthText = null;
        temperatureText = null;
        temperatureModule = null;
        carbohydratesDisplay = default;
        fatDisplay = default;
        proteinDisplay = default;
        waterDisplay = default;
        vitaminsDisplay = default;
        healthDisplay = default;
        temperatureDisplayTenths = int.MinValue;
        temperatureDisplayInitialized = false;
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

    private void UpdateNutrition(
        Slider slider,
        TMPro.TextMeshProUGUI text,
        ref DisplayedRange displayed,
        float currentValue,
        float maxValue)
    {
        if (slider != null)
            SetStatusBarValue(slider, 0f, maxValue, currentValue);

        UpdateRangeText(text, ref displayed, currentValue, maxValue);
    }

    /// <summary>TMP 的数值 SetText 最终仍可能生成 backing string；显示整数未变化时跳过文本重建。</summary>
    private static void UpdateRangeText(
        TMPro.TextMeshProUGUI text,
        ref DisplayedRange displayed,
        float currentValue,
        float maxValue)
    {
        if (text == null)
            return;

        int current = Mathf.RoundToInt(currentValue);
        int maximum = Mathf.RoundToInt(maxValue);
        if (displayed.Initialized && displayed.Current == current && displayed.Maximum == maximum)
            return;

        displayed.Current = current;
        displayed.Maximum = maximum;
        displayed.Initialized = true;
        text.SetText("{0:0}/{1:0}", current, maximum);
    }

    /// <summary>把本地玩家的权威生命值同步到常驻参数面板。</summary>
    private void UpdateHealthUI(BasePanel panel)
    {
        Slider slider = healthSlider;
        TMPro.TextMeshProUGUI text = healthText;
        bool showHealth = context.IsPlayer && damageReceiver != null;

        if (slider != null)
            slider.gameObject.SetActive(showHealth);

        if (!showHealth)
            return;

        float maxHp = Mathf.Max(0f, damageReceiver.MaxHp);
        float hp = Mathf.Clamp(damageReceiver.Hp, 0f, maxHp);
        if (slider != null)
            SetStatusBarValue(slider, 0f, Mathf.Max(1f, maxHp), hp);

        UpdateRangeText(text, ref healthDisplay, hp, maxHp);
    }

    /// <summary>监听 Mod_DamageReceiver 的统一状态事件，确保受伤、回血和网络同步都能刷新面板。</summary>
    private void BindHealthChanged()
    {
        if (damageReceiver == null)
            return;

        damageReceiver.OnAction -= HandleHealthChanged;
        damageReceiver.OnAction += HandleHealthChanged;
    }

    /// <summary>解除生命值监听，避免玩家实例销毁后残留回调。</summary>
    private void UnbindHealthChanged()
    {
        if (damageReceiver != null)
            damageReceiver.OnAction -= HandleHealthChanged;
    }

    private void HandleHealthChanged(float _)
    {
        RefreshUI();
    }

    /// <summary>存在体温模块时刷新体温显示，否则显示空值。</summary>
    private void UpdateTemperatureUI(BasePanel panel)
    {
        if (temperatureModule == null)
            temperatureModule = context.Item?.itemMods?.GetMod_ByID<Mod_Temperature>(ModText.Temperature);
        Mod_Temperature temperature = temperatureModule;
        Slider slider = temperatureSlider;
        TMPro.TextMeshProUGUI dataText = temperatureText;
        if (temperature?.Data == null)
        {
            if (dataText != null)
            {
                if (!temperatureDisplayInitialized || temperatureDisplayTenths != int.MinValue)
                    dataText.text = "--";
                temperatureDisplayTenths = int.MinValue;
                temperatureDisplayInitialized = true;
            }
            return;
        }

        float coldStart = temperature.SafeTemperatureMin;
        float hotStart = Mathf.Max(coldStart + 1f, temperature.SafeTemperatureMax);
        float buffer = Mathf.Max(2f, (hotStart - coldStart) * 0.2f);
        if (slider != null)
            SetStatusBarValue(
                slider,
                coldStart - buffer,
                hotStart + buffer,
                temperature.Data.CurrentTemperature);

        if (dataText != null)
        {
            int tenths = Mathf.RoundToInt(temperature.Data.CurrentTemperature * 10f);
            if (!temperatureDisplayInitialized || temperatureDisplayTenths != tenths)
            {
                temperatureDisplayTenths = tenths;
                temperatureDisplayInitialized = true;
                dataText.SetText("{0:1}°C", tenths * 0.1f);
            }
        }
    }

    #region 状态条过渡

    /// <summary>首次绑定直接同步，后续变化用非缩放时间平滑推动填充条左右移动。</summary>
    private void SetStatusBarValue(Slider slider, float minValue, float maxValue, float targetValue)
    {
        float safeMaxValue = Mathf.Max(minValue, maxValue);
        float clampedTarget = Mathf.Clamp(targetValue, minValue, safeMaxValue);
        slider.minValue = minValue;
        slider.maxValue = safeMaxValue;

        if (!statusBarTargets.TryGetValue(slider, out float previousTarget))
        {
            statusBarTargets.Add(slider, clampedTarget);
            slider.SetValueWithoutNotify(clampedTarget);
            return;
        }

        if (Mathf.Approximately(previousTarget, clampedTarget))
            return;

        statusBarTargets[slider] = clampedTarget;
        if (Mathf.Approximately(slider.value, clampedTarget))
        {
            slider.SetValueWithoutNotify(clampedTarget);
            RemoveStatusBarTransition(slider);
            return;
        }

        for (int i = 0; i < statusBarTransitions.Count; i++)
        {
            if (statusBarTransitions[i].Slider != slider)
                continue;

            StatusBarTransition transition = statusBarTransitions[i];
            transition.StartValue = slider.value;
            transition.TargetValue = clampedTarget;
            transition.Elapsed = 0f;
            statusBarTransitions[i] = transition;
            return;
        }

        statusBarTransitions.Add(new StatusBarTransition
        {
            Slider = slider,
            StartValue = slider.value,
            TargetValue = clampedTarget,
            Elapsed = 0f
        });
    }

    private void AdvanceStatusBarTransitions(float unscaledDeltaTime)
    {
        float deltaTime = Mathf.Max(0f, unscaledDeltaTime);
        for (int i = statusBarTransitions.Count - 1; i >= 0; i--)
        {
            StatusBarTransition transition = statusBarTransitions[i];
            if (transition.Slider == null)
            {
                statusBarTransitions.RemoveAt(i);
                continue;
            }

            transition.Elapsed += deltaTime;
            float t = StatusBarTransitionDuration <= 0f
                ? 1f
                : Mathf.Clamp01(transition.Elapsed / StatusBarTransitionDuration);
            float inverse = 1f - t;
            float eased = 1f - inverse * inverse * inverse;
            transition.Slider.SetValueWithoutNotify(Mathf.LerpUnclamped(
                transition.StartValue,
                transition.TargetValue,
                eased));

            if (t >= 1f)
            {
                transition.Slider.SetValueWithoutNotify(transition.TargetValue);
                statusBarTransitions.RemoveAt(i);
            }
            else
            {
                statusBarTransitions[i] = transition;
            }
        }
    }

    /// <summary>常驻 HUD 用一条复用协程逐帧推进状态条，避免为每次刷新创建 Tween 和闭包。</summary>
    private IEnumerator AdvanceStatusBarTransitionsCoroutine(BasePanel owner)
    {
        while (owner != null && boundPanel == owner)
        {
            AdvanceStatusBarTransitions(Time.unscaledDeltaTime);
            yield return null;
        }
    }

    private void RemoveStatusBarTransition(Slider slider)
    {
        for (int i = statusBarTransitions.Count - 1; i >= 0; i--)
        {
            if (statusBarTransitions[i].Slider == slider)
                statusBarTransitions.RemoveAt(i);
        }
    }

    /// <summary>释放面板前终止所有状态条动画，避免销毁后仍访问 Slider。</summary>
    private void StopStatusBarTransitions()
    {
        statusBarTransitions.Clear();
        statusBarTargets.Clear();
    }

    #endregion

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
