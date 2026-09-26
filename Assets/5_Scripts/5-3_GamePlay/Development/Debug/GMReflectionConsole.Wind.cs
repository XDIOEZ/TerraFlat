using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GM 世界页的风力调试入口；0～100% 对应星球风力 0～1，
/// 通过 WeatherMgr 的权威接口同步草、树叶和其他共用风场的表现。
/// </summary>
public sealed partial class GMReflectionConsole
{
    #region 世界风力调试

    private Slider worldWindSlider; // 调整当前星球风力的百分比滑条
    private TextMeshProUGUI worldWindStrengthText; // 显示当前设定风力

    /// <summary>构建可即时观察植被摆动的风力滑条。</summary>
    private void CreateWorldWindControl(Transform parent)
    {
        GameObject control = CreateUiObject("World Wind Strength", parent);
        control.AddComponent<Image>().color = GmSurface;
        Outline outline = control.AddComponent<Outline>();
        StyleGmOutline(outline);
        control.AddComponent<LayoutElement>().preferredHeight = 60f;

        HorizontalLayoutGroup layout = control.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 0, 0);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        worldWindStrengthText = CreateText(control.transform, "世界风力 --", 15f, GmTextPrimary);
        worldWindStrengthText.alignment = TextAlignmentOptions.Center;
        worldWindStrengthText.enableWordWrapping = false;
        worldWindStrengthText.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

        GameRes resources = GameRes.ExistingInstance;
        GameObject sliderPrefab = resources != null
            ? resources.GetPrefab(RuntimeUIPrefabKeys.SliderControl, false)
            : null;
        if (sliderPrefab == null)
        {
            Debug.LogError($"[GM] 缺少全局滑动条 Prefab：{RuntimeUIPrefabKeys.SliderControl}");
            return;
        }

        GameObject sliderObject = Instantiate(sliderPrefab, control.transform, false);
        sliderObject.name = "Wind Slider";
        LayoutElement sliderLayout = sliderObject.GetComponent<LayoutElement>();
        if (sliderLayout == null)
            sliderLayout = sliderObject.AddComponent<LayoutElement>();
        sliderLayout.minWidth = 80f;
        sliderLayout.preferredWidth = -1f;
        sliderLayout.flexibleWidth = 1f;
        worldWindSlider = sliderObject.GetComponent<Slider>();
        if (worldWindSlider == null)
        {
            Debug.LogError($"[GM] 全局滑动条 Prefab 缺少 Slider 组件：{RuntimeUIPrefabKeys.SliderControl}");
            Destroy(sliderObject);
            return;
        }

        worldWindSlider.minValue = 0f;
        worldWindSlider.maxValue = 100f;
        worldWindSlider.wholeNumbers = true;
        worldWindSlider.direction = Slider.Direction.LeftToRight;
        worldWindSlider.SetValueWithoutNotify(0f);
        worldWindSlider.onValueChanged.AddListener(SetWorldWindStrength);
        RegisterSearchEntry(GmPageId.World, "世界风力", "风 风力 强度 植被 草 树叶 wind sway",
            (RectTransform)control.transform);
        AddPageHint(parent, "0% 无风，100% 最强；拖动可观察草和树叶。联机由房主调整。", 24f);
    }

    /// <summary>从当前星球同步滑条，普通客户端只查看风力。</summary>
    private void RefreshWorldWindControl()
    {
        if (worldWindSlider == null || worldWindStrengthText == null)
            return;

        bool inWorld = GameManager.Instance != null && GameManager.Instance.IsInGameWorld;
        WeatherMgr weather = inWorld ? WeatherMgr.Instance : null;
        PlanetData planetData = weather != null ? weather.GetActivePlanetData() : null;
        worldWindSlider.interactable = planetData != null && GameNetwork.HasStateAuthority;
        if (planetData == null)
        {
            worldWindStrengthText.text = "世界风力 --";
            worldWindSlider.SetValueWithoutNotify(0f);
            return;
        }

        float strength = Mathf.Clamp01(planetData.WindStrength);
        worldWindStrengthText.SetText("世界风力 {0:0}%", strength * 100f);
        worldWindSlider.SetValueWithoutNotify(strength * 100f);
    }

    /// <summary>通过天气管理器修改星球权威风力并立即刷新共用 GPU 风场。</summary>
    private void SetWorldWindStrength(float percent)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
        {
            SetStatus("请先进入游戏世界，再调整风力。", Color.yellow);
            RefreshWorldWindControl();
            return;
        }

        WeatherMgr weather = WeatherMgr.Instance;
        if (weather.GetActivePlanetData() == null || !GameNetwork.HasStateAuthority)
        {
            SetStatus("当前没有可修改的星球风力，联机时请由房主操作。", Color.yellow);
            RefreshWorldWindControl();
            return;
        }

        weather.SetWindStrength(percent / 100f);
        RefreshWorldWindControl();
    }

    #endregion
}
