#if UNITY_EDITOR
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 公共进度条资产与过渡采样

    [InitializeOnLoadMethod]
    private static void RegisterSharedProgressBarRules() =>
        Register("ui_settings", "shared_progress_bars", CaptureSharedProgressBars);

    private static JObject CaptureSharedProgressBars(JObject args)
    {
        const string uiRoot = "Assets/2_Prefabs/2-1_UI/";
        const string sharedPath = uiRoot + "Common/Controls/UI_ProgressBar.prefab";
        string[][] targets =
        {
            new[] { "Gameplay/Loading/UI_ResourceLoading.prefab", "资源加载进度" },
            new[] { "Gameplay/Loading/UI_WorldLoading.prefab", "加载进度" },
            new[] { "Gameplay/Loading/UI_DimensionLoading.prefab", "维度加载进度" },
            new[] { "Gameplay/Inventory/Panels/UI_Bag.prefab", "FWUI_CarryWeightProgress", "FWUI_CarryVolumeProgress" },
            new[] { "Gameplay/Player/UI_BodyPartTreatment.prefab", "引导进度" },
            new[] { "Gameplay/Containers/UI_WaterVessel.prefab", "FWUI_WaterCapacity" },
            new[] { "Gameplay/Containers/UI_FuelInteraction.prefab", "燃料进度" },
            new[] { "Gameplay/Crafting/UI_Cultivator.prefab", "FWUI_WaterCapacity" },
            new[] { "Gameplay/Crafting/UI_FluidTank.prefab", "FWUI_GasCapacity" },
            new[] { "Gameplay/Crafting/UI_IndustrialFluid.prefab", "FWUI_GasCapacity", "FWUI_WaterCapacity", "FWUI_FuelRemaining" },
            new[] { "Gameplay/Crafting/UI_Mechanical.prefab", "FWUI_BatteryCharge" },
            new[] { "Gameplay/Crafting/UI_Bonfire.prefab", "燃料显示条" },
            new[] { "Gameplay/Crafting/UI_Furnace.prefab", "燃料显示条" },
            new[] { "Gameplay/Status/Player/UI_Food.prefab", "血量", "水", "维生素", "体温", "FoodTrack" },
            new[] { "Gameplay/Status/Creatures/UI_CreatureHealth.prefab", "血量" },
            new[] { "Gameplay/Status/Player/UI_Stamina.prefab", "体力进度" }
        };
        var unshared = new JArray();
        var brokenVisuals = new JArray();
        foreach (string[] target in targets)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(uiRoot + target[0]);
            for (int i = 1; i < target.Length; i++)
            {
                Slider slider = asset != null ? asset.GetComponentsInChildren<Slider>(true)
                    .FirstOrDefault(value => value.name == target[i]) : null;
                if (slider == null || !UsesSharedProgressBar(slider.gameObject, sharedPath) ||
                    slider.GetComponent<SmoothProgressBarVisual>() == null)
                    unshared.Add(target[0] + "/" + target[i]);
                else if (target[i] != "FoodTrack" &&
                    (slider.fillRect == null || !slider.fillRect.IsChildOf(slider.transform) ||
                     !slider.fillRect.gameObject.activeSelf || !slider.fillRect.parent.gameObject.activeSelf ||
                     ((target[i] == "FWUI_WaterCapacity" || target[i] == "FWUI_GasCapacity" ||
                       target[i] == "FWUI_BatteryCharge") && slider.direction != Slider.Direction.BottomToTop)))
                    brokenVisuals.Add(target[0] + "/" + target[i]);
            }
        }

        string[][] bindings =
        {
            new[] { "Gameplay/Containers/UI_WaterVessel.prefab", "WaterVesselPanel", "WaterCapacity", "FWUI_WaterCapacity" },
            new[] { "Gameplay/Crafting/UI_Cultivator.prefab", "MechanicalPanelView", "WaterCapacity", "FWUI_WaterCapacity" },
            new[] { "Gameplay/Crafting/UI_FluidTank.prefab", "MechanicalPanelView", "GasCapacity", "FWUI_GasCapacity" },
            new[] { "Gameplay/Crafting/UI_IndustrialFluid.prefab", "MechanicalPanelView", "GasCapacity", "FWUI_GasCapacity" },
            new[] { "Gameplay/Crafting/UI_IndustrialFluid.prefab", "MechanicalPanelView", "WaterCapacity", "FWUI_WaterCapacity" },
            new[] { "Gameplay/Crafting/UI_IndustrialFluid.prefab", "MechanicalPanelView", "FuelRemaining", "FWUI_FuelRemaining" },
            new[] { "Gameplay/Crafting/UI_Mechanical.prefab", "MechanicalPanelView", "BatteryCharge", "FWUI_BatteryCharge" },
            new[] { "Gameplay/Player/UI_BodyPartTreatment.prefab", "BodyPartTreatmentPanel", "Progress", "引导进度" }
        };
        var brokenBindings = new JArray();
        foreach (string[] binding in bindings)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(uiRoot + binding[0]);
            MonoBehaviour owner = asset.GetComponentsInChildren<MonoBehaviour>(true)
                .FirstOrDefault(value => value != null && value.GetType().Name == binding[1]);
            SerializedProperty field = owner != null ? new SerializedObject(owner).FindProperty(binding[2]) : null;
            Slider slider = field?.objectReferenceValue as Slider;
            if (slider == null || slider.name != binding[3])
                brokenBindings.Add(binding[0] + "/" + binding[2]);
        }

        GameObject human = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Gameplay/Player/Human.prefab");
        PlayerOxygenWorldBar oxygen = human.GetComponentInChildren<PlayerOxygenWorldBar>(true);
        var oxygenRect = (RectTransform)new SerializedObject(oxygen).FindProperty("fillRect").objectReferenceValue;
        Mod_Stamina stamina = human.GetComponentInChildren<Mod_Stamina>(true);
        if (oxygenRect == null || !UsesSharedProgressBar(oxygenRect.gameObject, sharedPath) ||
            oxygenRect.GetComponent<SmoothProgressBarVisual>() == null)
            unshared.Add("Human/氧气");
        if (stamina.Prefab_UI == null || AssetDatabase.GetAssetPath(stamina.Prefab_UI) !=
            uiRoot + "Gameplay/Status/Player/UI_Stamina.prefab")
            unshared.Add("Human/体力");

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sharedPath);
        var scene = CreateGameplayTestProbeScene("SharedProgressBarRules");
        GameObject instance = Object.Instantiate(source);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
        instance.SetActive(false);
        try
        {
            Slider slider = instance.GetComponent<Slider>();
            var visual = EnableGameplayTestProbeLifecycleCallbacks(instance.GetComponent<SmoothProgressBarVisual>());
            var samples = new JArray();
            foreach (Slider.Direction direction in System.Enum.GetValues(typeof(Slider.Direction)))
            {
                samples.Add(CaptureProgressTransitionSample(instance, slider, visual, direction, false));
                samples.Add(CaptureProgressTransitionSample(instance, slider, visual, direction, true));
            }
            GameObject interactive = AssetDatabase.LoadAssetAtPath<GameObject>(uiRoot + "Common/Controls/UI_Slider.prefab");
            return new JObject { ["unshared"] = unshared, ["brokenVisuals"] = brokenVisuals,
                ["brokenBindings"] = brokenBindings, ["samples"] = samples,
                ["interactiveSliderUnchanged"] = interactive.GetComponent<SmoothProgressBarVisual>() == null };
        }
        finally
        {
            Object.DestroyImmediate(instance);
            CloseGameplayTestProbeScene(scene);
        }
    }

    private static bool UsesSharedProgressBar(GameObject value, string sharedPath)
    {
        for (Object current = value; current != null; current = PrefabUtility.GetCorrespondingObjectFromSource(current))
            if (AssetDatabase.GetAssetPath(current) == sharedPath)
                return true;
        return false;
    }

    private static JObject CaptureProgressTransitionSample(GameObject instance, Slider slider,
        SmoothProgressBarVisual visual, Slider.Direction direction, bool filledImage)
    {
        instance.SetActive(false);
        slider.direction = direction;
        Image fill = slider.fillRect.GetComponent<Image>();
        fill.type = filledImage ? Image.Type.Filled : Image.Type.Simple;
        if (filledImage)
            fill.fillMethod = Image.FillMethod.Horizontal;
        slider.SetValueWithoutNotify(.2f);
        instance.SetActive(true);
        visual.AdvanceProgressTransition(0f);
        float initial = ReadProgressVisual(slider, fill);
        bool vertical = direction == Slider.Direction.BottomToTop || direction == Slider.Direction.TopToBottom;
        bool reverse = direction == Slider.Direction.RightToLeft || direction == Slider.Direction.TopToBottom;
        bool directionCorrect = !filledImage ||
            (fill.fillMethod == (vertical ? Image.FillMethod.Vertical : Image.FillMethod.Horizontal) &&
             fill.fillOrigin == (reverse ? 1 : 0));
        int events = 0;
        UnityEngine.Events.UnityAction<float> onChanged = _ => events++;
        slider.onValueChanged.AddListener(onChanged);
        slider.SetValueWithoutNotify(.8f);
        visual.AdvanceProgressTransition(.12f);
        float increasing = ReadProgressVisual(slider, fill);
        float target = slider.normalizedValue;
        int silentEvents = events;
        slider.value = .1f;
        visual.AdvanceProgressTransition(.12f);
        float reversing = ReadProgressVisual(slider, fill);
        visual.AdvanceProgressTransition(.24f);
        float completed = ReadProgressVisual(slider, fill);
        instance.SetActive(false);
        slider.SetValueWithoutNotify(.7f);
        instance.SetActive(true);
        visual.AdvanceProgressTransition(0f);
        float reopened = ReadProgressVisual(slider, fill);
        slider.onValueChanged.RemoveListener(onChanged);
        return new JObject { ["initial"] = initial, ["increasing"] = increasing, ["target"] = target,
            ["reversing"] = reversing, ["completed"] = completed, ["reopened"] = reopened,
            ["silentEvents"] = silentEvents, ["changeEvents"] = events, ["directionCorrect"] = directionCorrect };
    }

    private static float ReadProgressVisual(Slider slider, Image fill)
    {
        if (fill.type == Image.Type.Filled)
            return fill.fillAmount;
        return slider.direction switch
        {
            Slider.Direction.RightToLeft => 1f - slider.fillRect.anchorMin.x,
            Slider.Direction.BottomToTop => slider.fillRect.anchorMax.y,
            Slider.Direction.TopToBottom => 1f - slider.fillRect.anchorMin.y,
            _ => slider.fillRect.anchorMax.x
        };
    }

    #endregion
}
#endif
