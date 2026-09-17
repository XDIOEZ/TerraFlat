using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 公共 uGUI 控件库与显式迁移入口。使用真正的嵌套 Prefab/Variant 保持继承关系，
/// 迁移保留匹配对象的引用、业务事件与外部布局，只移除公共外观的实例覆盖。
/// 不在编辑器启动或脚本重载时自动改写资源。
/// </summary>
public static partial class RuntimeUIPrefabBuilder
{
    #region 控件库

    public const string SharedControlsRoot = PrefabRoot + "Common/Controls/";
    private static bool buildingSharedControls;
    private static readonly string[] SharedControlNames =
    {
        "UI_Button", "UI_CloseButton", "UI_TabButton", "UI_Toggle",
        "UI_SwitchOption", "UI_Switch", "UI_SliderControl", "UI_ProgressBar",
        "UI_Dropdown", "UI_InputField"
    };

    /// <summary>只补建缺失的公共资产；已由开发者编辑的 Prefab 永远不被默认构建覆盖。</summary>
    [MenuItem("FlatWorld/UI/Shared Controls/Create Missing Prefabs")]
    public static void EnsureSharedControlPrefabs()
    {
        if (buildingSharedControls)
            return;

        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException("公共 UI 控件缺少项目字体：" + FontPath);

        buildingSharedControls = true;
        try
        {
            Directory.CreateDirectory(SharedControlsRoot);
            CreateSharedAsset("UI_Button", () => CreateButton("UI_Button", null, string.Empty, 160f, 64f, false).gameObject);
            CreateSharedAsset("UI_Toggle", () => CreateToggle("UI_Toggle", null).gameObject);
            CreateSharedAsset("UI_SliderControl", () => CreateSlider("UI_SliderControl", null).gameObject);
            CreateSharedAsset("UI_Dropdown", () => CreateDropdown("UI_Dropdown", null).gameObject);
            CreateSharedAsset("UI_InputField", () => CreateInputField("UI_InputField", null, string.Empty).gameObject);
            CreateSharedVariant("UI_CloseButton", "UI_Button", ConfigureCloseButton);
            CreateSharedVariant("UI_TabButton", "UI_Button", ConfigureTabButton);
            CreateSharedVariant("UI_ProgressBar", "UI_SliderControl", ConfigureProgressBar);
            CreateSharedVariant("UI_SwitchOption", "UI_Toggle", ConfigureSwitchOption);
            CreateSharedAsset("UI_Switch", BuildSharedSwitch);
        }
        finally
        {
            buildingSharedControls = false;
        }
    }

    /// <summary>实例化公共资产而不是复制层级；窗口保存后仍保留 Prefab 连接。</summary>
    private static T InstantiateSharedControl<T>(string key, string name, Transform parent) where T : Component
    {
        string path = SharedControlsRoot + key + ".prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            EnsureSharedControlPrefabs();
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        if (asset == null)
            throw new InvalidOperationException("找不到公共控件：" + path);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        instance.name = name;
        instance.transform.localScale = Vector3.one;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        T control = instance.GetComponent<T>();
        if (control == null)
            throw new InvalidOperationException(path + " 缺少 " + typeof(T).Name);
        return control;
    }

    /// <summary>首次创建基础控件并登记稳定 Addressables 键。</summary>
    private static void CreateSharedAsset(string key, Func<GameObject> factory)
    {
        string path = SharedControlsRoot + key + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            return;

        GameObject root = factory();
        try
        {
            root.name = key;
            root.SetActive(false);
            FlatWorldUITheme.Apply(root.transform);
            ConfigureSharedDefaults(root);
            if (root.GetComponent<ReusableUIControl>() == null)
                root.AddComponent<ReusableUIControl>();
            SetUILayerRecursively(root);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            EnsureRuntimePrefabAddressable(path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>特殊按钮、进度条继承基础控件，保持共同底色和边框可继续向下传播。</summary>
    private static void CreateSharedVariant(string key, string baseKey, Action<GameObject> configure)
    {
        string path = SharedControlsRoot + key + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            return;
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SharedControlsRoot + baseKey + ".prefab");
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        try
        {
            instance.name = key;
            configure(instance);
            RecordSharedInstance(instance);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            EnsureRuntimePrefabAddressable(path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>首次生成时设置移动端触控尺寸与灰阶主题；后续以资产自身的编辑为准。</summary>
    private static void ConfigureSharedDefaults(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(240f, 64f);
        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.fontSize = 20f;
            text.color = FlatWorldUITheme.TextPrimary;
            text.raycastTarget = false;
        }
        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            ColorBlock colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = FlatWorldUITheme.Selection;
            colors.disabledColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            selectable.colors = colors;
            selectable.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        }

        if (root.TryGetComponent(out Slider slider))
            ConfigureSharedSlider(slider);
        if (root.TryGetComponent(out Toggle toggle))
        {
            rect.sizeDelta = new Vector2(76f, 60f);
            LayoutElement element = root.GetComponent<LayoutElement>();
            element.preferredWidth = 76f;
            element.preferredHeight = 60f;
            toggle.isOn = true;
        }
        if (root.TryGetComponent(out TMP_Dropdown dropdown))
        {
            dropdown.ClearOptions();
            dropdown.captionText.text = string.Empty;
            dropdown.itemText.text = string.Empty;
            dropdown.itemText.transform.parent.GetComponent<LayoutElement>().preferredHeight = 60f;
            ((RectTransform)dropdown.itemText.transform.parent).sizeDelta = new Vector2(0f, 60f);
            dropdown.template.sizeDelta = new Vector2(0f, 360f);
        }
    }

    /// <summary>滑块的 60 像素命中区与轨道分离，手柄拥有明确非零高度。</summary>
    private static void ConfigureSharedSlider(Slider slider)
    {
        Image hitArea = slider.GetComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.raycastTarget = true;
        Outline rootOutline = slider.GetComponent<Outline>();
        if (rootOutline != null)
            Object.DestroyImmediate(rootOutline);
        RectTransform root = (RectTransform)slider.transform;
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(320f, 60f);
        LayoutElement layout = slider.GetComponent<LayoutElement>();
        layout.preferredHeight = 60f;
        layout.minWidth = 100f;

        Image background = slider.transform.Find("Background")?.GetComponent<Image>();
        if (background == null)
            background = CreateImage("Background", slider.transform, FlatWorldUITheme.SurfaceLow);
        background.transform.SetAsFirstSibling();
        background.raycastTarget = false;
        SetSharedTrackRect(background.rectTransform, 0f);
        AddOutline(background, Border);
        SetSharedTrackRect((RectTransform)slider.fillRect.parent, 4f);
        slider.fillRect.sizeDelta = Vector2.zero;
        slider.fillRect.GetComponent<Image>().color = FlatWorldUITheme.Accent;
        RectTransform handleArea = (RectTransform)slider.handleRect.parent;
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);
        slider.handleRect.anchorMin = slider.handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        slider.handleRect.sizeDelta = new Vector2(14f, 38f);
        slider.handleRect.gameObject.SetActive(true);
        slider.handleRect.GetComponent<Image>().raycastTarget = false;
        slider.value = 1f;
    }

    /// <summary>固定轨道厚度，不随父容器高度放大。</summary>
    private static void SetSharedTrackRect(RectTransform rect, float inset)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-inset * 2f, 18f - inset * 2f);
    }

    private static void ConfigureCloseButton(GameObject root)
    {
        root.GetComponent<LayoutElement>().preferredWidth = 96f;
        root.GetComponentInChildren<TextMeshProUGUI>(true).text = "关闭";
    }

    private static void ConfigureTabButton(GameObject root)
    {
        root.GetComponent<LayoutElement>().preferredWidth = 140f;
        root.AddComponent<ReusableUITabVisual>().Configure(
            root.GetComponent<Image>(), root.GetComponentInChildren<TextMeshProUGUI>(true));
    }

    private static void ConfigureProgressBar(GameObject root)
    {
        Slider slider = root.GetComponent<Slider>();
        slider.interactable = false;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.handleRect.gameObject.SetActive(false);
        root.GetComponent<Image>().raycastTarget = false;
        root.GetComponent<LayoutElement>().preferredHeight = 26f;
        ((RectTransform)root.transform).sizeDelta = new Vector2(320f, 26f);
    }

    /// <summary>互斥选项继承开关，选中标记改为底部细线，文字由使用者填写。</summary>
    private static void ConfigureSwitchOption(GameObject root)
    {
        Toggle toggle = root.GetComponent<Toggle>();
        RectTransform mark = (RectTransform)toggle.graphic.transform;
        mark.anchorMin = Vector2.zero;
        mark.anchorMax = new Vector2(1f, 0f);
        mark.anchoredPosition = new Vector2(0f, 5f);
        mark.sizeDelta = new Vector2(-16f, 3f);
        root.GetComponent<LayoutElement>().preferredWidth = 140f;
        TextMeshProUGUI text = CreateText("Text (TMP)", root.transform, string.Empty, 20f, Cream);
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);
    }

    /// <summary>组合两个标准 Toggle 选项；原生 ToggleGroup 提供互斥，不引入业务依赖。</summary>
    private static GameObject BuildSharedSwitch()
    {
        GameObject root = CreateUIObject("UI_Switch", null, typeof(HorizontalLayoutGroup), typeof(ToggleGroup));
        HorizontalLayoutGroup layout = root.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        ToggleGroup group = root.GetComponent<ToggleGroup>();
        for (int index = 0; index < 2; index++)
        {
            Toggle option = InstantiateSharedControl<Toggle>("UI_SwitchOption", "Option" + index, root.transform);
            option.group = group;
            option.SetIsOnWithoutNotify(index == 0);
            RecordSharedInstance(option.gameObject);
        }
        return root;
    }

    #endregion

    #region 显式原位迁移

    /// <summary>构建器统一保存入口：记录业务差异，但不把临时主题结果烘焙成外观覆盖。</summary>
    private static void PrepareSharedControlsForSave(GameObject root)
    {
        if (buildingSharedControls)
            return;
        ConvertCompatibleControls(root);
        foreach (ReusableUIControl control in root.GetComponentsInChildren<ReusableUIControl>(true))
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(control.gameObject) ||
                (control.transform.parent != null && ReusableUIControl.OwnsVisuals(control.transform.parent)))
                continue;
            RecordSharedInstance(control.gameObject);
            RemoveSharedVisualOverrides(control.gameObject);
        }
    }

    /// <summary>迁移所有兼容的正式 UI 控件；不重建面板，不触碰不兼容的物品图标等专用层级。</summary>
    [MenuItem("FlatWorld/UI/Shared Controls/Migrate Existing UI")]
    public static void MigrateExistingUIToSharedControls()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 Play Mode 再迁移正式 UI 资产。");
        EnsureSharedControlPrefabs();
        int count = 0;
        var migrated = new List<string>();
        var failures = new List<string>();
        string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => AssetDatabase.GetDependencies(path).Length).ToArray();
        foreach (string path in paths)
        {
            if (IsSharedLibraryAsset(path))
                continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int converted = ConvertCompatibleControls(root);
                foreach (ReusableUIControl control in root.GetComponentsInChildren<ReusableUIControl>(true))
                    converted += RemoveSharedVisualOverrides(control.gameObject);
                if (converted == 0)
                    continue;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                count += converted;
                migrated.Add(path + " : " + converted);
            }
            catch (Exception exception)
            {
                failures.Add(path + " : " + exception.Message);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Temp/SharedUI");
        File.WriteAllText("Temp/SharedUI/migration.txt", string.Join("\n", migrated) + "\nFAILURES\n" + string.Join("\n", failures));
        Debug.Log($"[Shared UI] 已迁移 {count} 个控件 / {migrated.Count} 个 Prefab；失败 {failures.Count}。详见 Temp/SharedUI/migration.txt");
        if (failures.Count > 0)
            Debug.LogError("[Shared UI] 部分资产未保存：" + string.Join("\n", failures));
    }

    /// <summary>保存构建结果前嵌套化兼容控件，避免未来重建重新产生复制件。</summary>
    public static int ConvertCompatibleControls(GameObject root)
    {
        if (root == null || buildingSharedControls || IsSharedLibraryAsset(AssetDatabase.GetAssetPath(root)))
            return 0;
        EnsureSharedControlPrefabs();
        int converted = 0;
        Selectable[] controls = root.GetComponentsInChildren<Selectable>(true);
        foreach (Selectable control in controls)
        {
            if (control == null || control.gameObject == root || ReusableUIControl.OwnsVisuals(control) ||
                PrefabUtility.IsPartOfPrefabInstance(control))
                continue;
            string key = GetCompatibleSharedKey(control);
            if (key == null)
                continue;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SharedControlsRoot + key + ".prefab");
            Object[] existingObjects = control.GetComponentsInChildren<Component>(true).Cast<Object>()
                .Concat(control.GetComponentsInChildren<Transform>(true).Select(value => (Object)value.gameObject)).ToArray();
            PrefabUtility.ConvertToPrefabInstance(control.gameObject, asset, new ConvertToPrefabInstanceSettings
            {
                // 按完整层级匹配才会保留未匹配的原有组件/节点，且不混淆重复的 Label 名。
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                changeRootNameToAssetName = false,
                recordPropertyOverridesOfMatches = true,
                componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true
            }, InteractionMode.AutomatedAction);
            if (existingObjects.Any(value => value == null))
                throw new InvalidOperationException("控件转换未保留所有原对象，已取消该面板保存：" + control.name);
            RemoveSharedVisualOverrides(control.gameObject);
            if (control is Slider slider && IsAudioSlider(slider.name))
            {
                LayoutElement layout = slider.GetComponent<LayoutElement>();
                layout.preferredHeight = 60f;
                LayoutElement row = slider.transform.parent.GetComponent<LayoutElement>();
                if (row != null)
                    row.preferredHeight = Mathf.Max(row.preferredHeight, 72f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            }
            converted++;
        }
        return converted;
    }

    /// <summary>只迁移已知的标准层级，不把槽位、专用图标、多文本卡片强行替换成普通按钮。</summary>
    private static string GetCompatibleSharedKey(Selectable control)
    {
        Transform root = control.transform;
        if (control is TMP_Dropdown)
            return root.Find("Label") != null && root.Find("Template/Viewport/Content/Item/Item Label") != null ? "UI_Dropdown" : null;
        if (control is TMP_InputField)
            return root.Find("Text Area/Text") != null && root.Find("Text Area/Placeholder") != null ? "UI_InputField" : null;
        if (control.GetComponentInParent<TMP_Dropdown>(true) != null || control.GetComponentsInChildren<Selectable>(true).Length != 1)
            return null;
        if (control is Slider slider)
        {
            if (root.Find("Fill Area/Fill") == null || root.Find("Handle Slide Area/Handle") == null)
                return null;
            return slider.interactable && slider.handleRect != null && slider.handleRect.gameObject.activeSelf ? "UI_SliderControl" : "UI_ProgressBar";
        }
        if (control is Toggle)
            return root.childCount == 1 && root.Find("Checkmark") != null ? "UI_Toggle" : null;
        if (control is Button)
        {
            if (root.childCount != 1 || root.Find("Text (TMP)") == null ||
                root.GetComponentsInChildren<Image>(true).Length != 1)
                return null;
            if (control.name.Contains("关闭") || control.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                return "UI_CloseButton";
            if (control.name.Contains("分页") || control.name.Contains("切换") || control.name.Contains("页签"))
                return "UI_TabButton";
            return "UI_Button";
        }
        return null;
    }

    /// <summary>删除公共外观覆盖；业务事件、设置值、文案、节点名及控件外部布局仍归使用面板所有。</summary>
    private static int RemoveSharedVisualOverrides(GameObject instance)
    {
        PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instance);
        if (modifications == null)
            return 0;
        PropertyModification[] retained = modifications.Where(KeepScopedControlOverride).ToArray();
        if (retained.Length == modifications.Length)
            return 0;
        PrefabUtility.SetPropertyModifications(instance, retained);
        return 1;
    }

    /// <summary>嵌套实例的 API 可能返回整个外层面板的覆盖；绝不能清除公共控件边界之外的数据。</summary>
    private static bool KeepScopedControlOverride(PropertyModification modification)
    {
        Transform target = modification.target is GameObject gameObject ? gameObject.transform :
            (modification.target as Component)?.transform;
        ReusableUIControl owner = target != null ? target.GetComponentInParent<ReusableUIControl>(true) : null;
        return owner == null || KeepControlOverride(modification, owner.gameObject);
    }

    private static bool KeepControlOverride(PropertyModification modification, GameObject source)
    {
        Object target = modification.target;
        string path = modification.propertyPath;
        if (target == null)
            return false;
        if (target is GameObject gameObject)
            return path == "m_Name" || (path == "m_IsActive" && (gameObject == source || gameObject.name == "Template"));
        if (target is RectTransform rect)
            return rect.gameObject == source;
        if (target is LayoutElement element)
            return element.gameObject == source;
        if (target is TMP_Text)
            return path == "m_text";
        if (target is Image || target is Outline || target is Shadow || target is CanvasRenderer)
            return false;
        if (target is Selectable)
        {
            if (path.StartsWith("m_Colors") || path.StartsWith("m_SpriteState") || path.StartsWith("m_AnimationTriggers") ||
                path == "m_Transition" || path == "m_TargetGraphic" || path == "m_FillRect" || path == "m_HandleRect" ||
                path == "m_Graphic" || path == "m_CaptionText" || path == "m_ItemText" || path == "m_Template")
                return false;
        }
        return true;
    }

    /// <summary>保存 Variant 和已实例化控件的显式差异。</summary>
    private static void RecordSharedInstance(GameObject root)
    {
        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(node.gameObject);
            foreach (Component component in node.GetComponents<Component>())
                if (component != null)
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }

    private static bool IsSharedLibraryAsset(string path)
    {
        return SharedControlNames.Any(key => path == SharedControlsRoot + key + ".prefab");
    }

    private static bool IsAudioSlider(string name)
    {
        return name == "MasterVolume" || name == "MusicVolume" || name == "SfxVolume" ||
            name == "UIVolume" || name == "AmbientVolume" || name == "VoiceVolume";
    }

    #endregion
}
