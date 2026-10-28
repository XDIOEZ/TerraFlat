// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class GameUIPrefabRebuilder
{
    #region 制作与加工界面

    private static void BuildCraftWindow(GameObject root, string title, string eyebrow, int inputCount, bool compact)
    {
        float width = compact ? 646f : 726f;
        float height = compact ? 438f : 526f;
        RectTransform frame = PrepareWindow(root, width, height, title, eyebrow, compact ? "连续点击推进取火 · 停止操作时进度会缓慢回落" : string.Empty, true);

        float sectionHeight = height - 206f;
        float inputWidth = compact ? 220f : 398f;
        AddSection(frame, "INPUT", "投入材料", 24f, 104f, inputWidth, sectionHeight);
        AddSection(frame, "OUTPUT", "预期产物", width - 238f, 104f, 214f, sectionHeight);
        AddFlowArrow(frame, width * 0.61f, 214f);

        RectTransform input = FindRect(root.transform, "输入");
        if (input != null)
        {
            SetTopLeft(input, 48f, 158f, inputWidth - 48f, sectionHeight - 88f);
            ArrangeSlots(input, inputCount, inputCount > 2 ? 2 : 1, compact ? 90f : 94f, 16f);
        }

        RectTransform output = FindRect(root.transform, "输出");
        if (output != null)
        {
            SetTopLeft(output, width - 190f, 174f, 118f, 118f);
            ArrangeSlots(output, 1, 1, 106f, 0f);
        }

        PlaceProgress(root.transform, 38f, height - 100f, width - 312f, 14f);
        PlaceActionButton(root.transform, "合成按钮", width, height, compact ? "执行" : "开始制作");
        EnsureCraftingOutputPreviewLayers(root.transform);
    }

    /// <summary>
    /// 构建移动端合成选择面板：左侧放大材料槽，中间滚动展示全部可制作配方，右侧显示双产物预览。
    /// 面板按 1920×1080 参考画布的 70% 设计，边框只保留 1 像素，主要点击目标不低于 60 逻辑像素。
    /// </summary>
    private static void BuildCraftingSelectionWindow(GameObject root, string title, string eyebrow, int inputCount, int outputCount = 2)
    {
        UnwrapCraftingContent(root);
        const float width = 1344f;
        const float height = 756f;
        const float sectionTop = 98f;
        const float sectionHeight = 568f;

        RemoveNamedSlots(root.transform, "输入_", inputCount);
        RemoveNamedSlots(root.transform, "输出_", outputCount);
        for (int index = 2; index <= inputCount; index++)
            EnsureNamedSlot(root.transform, "输入_1", $"输入_{index}");
        if (outputCount > 1) EnsureNamedSlot(root.transform, "输出_1", "输出_2");

        RectTransform frame = PrepareWindow(
            root,
            width,
            height,
            title,
            eyebrow,
            string.Empty, true);
        ResizeCraftingFooter(frame, width, height);

        AddSection(frame, "INPUT", "输入材料", 20f, sectionTop, 376f, sectionHeight);
        AddSection(frame, "RECIPES", "可制作配方", 408f, sectionTop, 524f, sectionHeight);
        AddSection(frame, "OUTPUT", "制作产物", 944f, sectionTop, 380f, sectionHeight);

        int inputColumns = inputCount <= 4 ? 2 : 3;
        LayoutCraftingSlots(root.transform, "输入_", inputCount, 20f, 168f, 376f, inputCount == 2 ? 136f : 112f, inputColumns);
        LayoutCraftingSlots(root.transform, "输出_", outputCount, 944f, 168f, 380f, 136f, 2);
        BuildRecipeCandidateScroll(frame, 424f, 164f, 492f, 486f);

        SetLegacyCraftingProgressActive(root.transform, false);
        PlaceCraftingActionButton(root.transform, "开始制作");
        PlaceCraftingCloseButton(root.transform);
        EnsureCraftingOutputPreviewLayers(root.transform);
        EnsureCraftingSafeArea(root);
    }

    /// <summary>只给两张制作面板增加适配外壳，保留所有原有业务节点和引用。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Adapt Crafting Panels Safe Area Only")]
    public static void AdaptCraftingPanelsSafeAreaOnly()
    {
        foreach (string name in new[] { "UI_HandCraftTable", "UI_MakerTable", "UI_Anvil" })
        {
            string path = CraftingRoot + name + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureCraftingSafeArea(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>内容以 1344×756 排版；外层占满安全区，缩放组件仅向下限幅。</summary>
    private static void EnsureCraftingSafeArea(GameObject root)
    {
        RectTransform outer = root.GetComponent<RectTransform>();
        RectTransform content = root.transform.Find("CraftingContent") as RectTransform;
        if (content == null)
        {
            content = CreateRect("CraftingContent", root.transform);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(1344f, 756f);
            content.anchoredPosition = Vector2.zero;
            Image source = root.GetComponent<Image>();
            if (source != null)
            {
                Image background = content.gameObject.AddComponent<Image>();
                EditorUtility.CopySerialized(source, background);
                background.enabled = true;
                source.enabled = false;
            }
            foreach (Outline outline in root.GetComponents<Outline>())
            {
                Outline copy = content.gameObject.AddComponent<Outline>();
                EditorUtility.CopySerialized(outline, copy);
                copy.enabled = true;
                outline.enabled = false;
            }
            List<Transform> children = new List<Transform>();
            foreach (Transform child in root.transform)
                if (child != content && child.name != "FWUI_DragSurface")
                    children.Add(child);
            foreach (Transform child in children)
                child.SetParent(content, false);
        }
        content.localScale = Vector3.one;
        content.GetComponent<Image>().raycastTarget = false;
        UIWindowDragSurface dragSurface = root.GetComponentInChildren<UIWindowDragSurface>(true);
        if (dragSurface != null)
            dragSurface.Configure(content);
        Stretch(outer);
        outer.localScale = Vector3.one;
        SafeAreaScaleGroup scale = root.GetComponent<SafeAreaScaleGroup>();
        if (scale == null)
            scale = root.AddComponent<SafeAreaScaleGroup>();
        // 制作台整体缩到八成，为底部快捷栏留出空间。
        scale.Configure(content, new[] { content }, new Vector2(24f, 24f), root.name == "UI_HandCraftTable" ? 0.6f : 0.8f);
    }

    /// <summary>重建前还原旧构建坐标空间，避免重复包裹或改变内部绝对布局。</summary>
    private static void UnwrapCraftingContent(GameObject root)
    {
        Transform content = root.transform.Find("CraftingContent");
        if (content == null)
            return;
        while (content.childCount > 0)
            content.GetChild(0).SetParent(root.transform, false);
        UnityEngine.Object.DestroyImmediate(content.gameObject);
        Image image = root.GetComponent<Image>();
        if (image != null)
            image.enabled = true;
        foreach (Outline outline in root.GetComponents<Outline>())
            outline.enabled = true;
    }

    private static void BuildMakerTable(GameObject root)
    {
        BuildCraftingSelectionWindow(root, "制作台", string.Empty, 6);
        RemoveMakerFlowArrow(root.transform);
    }

    /// <summary>砧使用独立工具与材料槽，拆除按钮和提示共用有明确边界的底栏。</summary>
    private static void BuildAnvil(GameObject root)
    {
        BuildCraftingSelectionWindow(root, "铁砧", string.Empty, 1, 1);
        RectTransform content = (RectTransform)root.transform.Find("CraftingContent");
        EnsureNamedSlot(content, "输入_1", "工具_1");
        SetTopLeft(FindRect(content, "工具_1"), 64f, 208f, 136f, 136f);
        SetTopLeft(FindRect(content, "输入_1"), 216f, 208f, 136f, 136f);
        SetTopLeft(FindRect(content, "输出_1"), 1066f, 208f, 136f, 136f);
        TextMeshProUGUI tool = CreateText("FWUI_AnvilToolLabel", content, "金属锤", 20f, Cream, FontStyles.Normal, TextAlignmentOptions.Center);
        TextMeshProUGUI material = CreateText("FWUI_AnvilMaterialLabel", content, "材料", 20f, Cream, FontStyles.Normal, TextAlignmentOptions.Center);
        SetTopLeft(tool.rectTransform, 64f, 170f, 136f, 28f);
        SetTopLeft(material.rectTransform, 216f, 170f, 136f, 28f);
        RectTransform action = FindRect(content, "合成按钮");
        RectTransform dismantle = FindRect(content, "拆回建筑按钮");
        if (dismantle == null)
        {
            dismantle = UnityEngine.Object.Instantiate(action, content, false);
            dismantle.name = "拆回建筑按钮";
        }
        dismantle.anchorMin = dismantle.anchorMax = dismantle.pivot = new Vector2(1f, 0f);
        dismantle.anchoredPosition = new Vector2(-356f, 8f);
        dismantle.sizeDelta = new Vector2(180f, 60f);
        ConfigureActionButton(dismantle.gameObject, "拆除", false);
        FindRect(content, "FWUI_FooterHint").sizeDelta = new Vector2(752f, 26f);
        bool active = root.activeSelf;
        root.SetActive(false);
        BuildingPanelActions actions = root.GetComponent<BuildingPanelActions>() ?? root.AddComponent<BuildingPanelActions>();
        actions.DismantleButton = dismantle.GetComponent<Button>();
        actions.Title = FindRect(content, "FWUI_标题").GetComponent<TMP_Text>();
        root.SetActive(active);
        RemoveMakerFlowArrow(root.transform);
    }

    /// <summary>制作台不再显示旧的输入到输出方向箭头；仅清理该面板根节点上的历史视觉。</summary>
    private static void RemoveMakerFlowArrow(Transform root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            bool isLegacyRootArrow = transform != null &&
                                     (transform.parent == root || transform.parent == root.Find("CraftingContent")) &&
                                     string.Equals(transform.name, "Image", StringComparison.Ordinal);
            bool isGeneratedFlowArrow = transform != null &&
                                        transform.name.StartsWith("FWUI_FlowArrow_", StringComparison.Ordinal);
            if (isLegacyRootArrow || isGeneratedFlowArrow)
                UnityEngine.Object.DestroyImmediate(transform.gameObject);
        }
    }

    private static void BuildFurnace(GameObject root, bool bonfire)
    {
        // 篝火旧版方向装饰已由三列布局替代，仅移除已知的无交互箭头。
        Transform legacyArrow = root.transform.Find("Image_1");
        if (legacyArrow != null && legacyArrow.TryGetComponent(out Image arrowImage) &&
            !arrowImage.raycastTarget && arrowImage.sprite != null && arrowImage.sprite.name == "cursor-direction-15")
            UnityEngine.Object.DestroyImmediate(legacyArrow.gameObject);
        float width = bonfire ? 740f : 856f;
        const float height = 592f;
        string title = bonfire ? "篝火" : "熔炉";
        string eyebrow = string.Empty;

        RectTransform legacyProgress = FindRect(root.transform, "熔炼进度条");
        if (legacyProgress != null) UnityEngine.Object.DestroyImmediate(legacyProgress.gameObject);
        RectTransform legacyProgressLabel = FindRect(root.transform, "FWUI_FurnaceProgressLabel");
        if (legacyProgressLabel != null) UnityEngine.Object.DestroyImmediate(legacyProgressLabel.gameObject);

        if (!bonfire)
            RemoveLegacyFurnaceVisuals(root.transform);

        RectTransform frame = PrepareWindow(root, width, height, title, eyebrow, string.Empty, true);

        AddSection(frame, "INPUT", "投入", 24f, 104f, bonfire ? 188f : 232f, 366f);
        AddSection(frame, "PROCESS", "作业状态", bonfire ? 232f : 276f, 104f, bonfire ? 276f : 296f, 366f);
        AddSection(frame, "OUTPUT", "产出", width - (bonfire ? 192f : 260f), 104f, bonfire ? 168f : 236f, 366f);

        if (bonfire)
        {
            PlaceRect(root.transform, "输入_1", 68f, 178f, 96f, 96f);
            PlaceRect(root.transform, "燃料_1", 68f, 316f, 96f, 96f);
            PlaceRect(root.transform, "输出_1", width - 156f, 216f, 104f, 104f);
        }
        else
        {
            RectTransform input = FindRect(root.transform, "输入");
            RectTransform fuel = FindRect(root.transform, "燃料");
            RectTransform output = FindRect(root.transform, "输出");
            if (input != null)
            {
                SetTopLeft(input, 56f, 156f, 168f, 270f);
                ArrangeSlotsCentered(input, 3, 1, 82f, 12f);
            }
            if (fuel != null)
            {
                // 三格燃料横排完整收进作业区，不再靠容器外溢维持旧布局。
                SetTopLeft(fuel, 308f, 390f, 232f, 72f);
                ArrangeSlotsCentered(fuel, 3, 3, 72f, 8f);
            }
            if (output != null)
            {
                SetTopLeft(output, width - 218f, 164f, 168f, 236f);
                ArrangeSlotsCentered(output, 3, 1, 82f, 12f);
            }
        }

        RectTransform sliders = FindRect(root.transform, "Slider");
        if (sliders != null)
        {
            if (bonfire)
            {
                // 篝火的两个状态条统一为横向并下移，避免旧旋转燃料条穿过流程箭头。
                SetTopLeft(sliders, 274f, 272f, 192f, 92f);
                sliders.localScale = Vector3.one;

                RectTransform fuelBar = FindRect(sliders, "燃料显示条");
                if (fuelBar != null)
                {
                    fuelBar.localScale = Vector3.one;
                    fuelBar.localRotation = Quaternion.identity;
                    SetTopLeft(fuelBar, 0f, 44f, 192f, 16f);
                }
            }
            else
            {
                // 熔炉状态条统一回到 1 倍缩放并显式占位，清掉旧 Stretch 偏移造成的错位。
                SetTopLeft(sliders, 316f, 306f, 220f, 70f);
                sliders.localScale = Vector3.one;

                RectTransform fuelBar = FindRect(sliders, "燃料显示条");
                if (fuelBar != null)
                {
                    fuelBar.localScale = Vector3.one;
                    fuelBar.localRotation = Quaternion.identity;
                    SetTopLeft(fuelBar, 0f, 52f, 220f, 18f);
                    if (fuelBar.TryGetComponent(out Slider fuelSlider))
                        fuelSlider.interactable = false;
                }

                AddFurnaceProcessLabel(frame, "FWUI_FurnaceFuelLabel", "燃料余量", 316f, 336f, 220f);
                AddFurnaceTemperatureLabel(frame);
                TextMeshProUGUI capacity = CreateText("FWUI_FurnaceInputCapacity", frame, string.Empty,
                    13f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
                SetTopLeft(capacity.rectTransform, 316f, 286f, 220f, 42f);
                capacity.raycastTarget = false;
            }
        }

        if (!bonfire) AddInductionTemperatureControls(frame);

        RectTransform illustration = bonfire ? FindDirectRect(root.transform, "Image_1") : null;
        if (illustration != null)
        {
            SetTopLeft(illustration, 314f, 184f, 112f, 64f);
            if (illustration.TryGetComponent(out Image illustrationImage))
            {
                illustrationImage.preserveAspect = true;
                illustrationImage.raycastTarget = false;
            }
        }

        PlaceActionButton(root.transform, "合成按钮", width, height, "点火");
        foreach (ItemSlot_UI slot in root.GetComponentsInChildren<ItemSlot_UI>(true))
            if (slot.name.StartsWith("燃料_", StringComparison.Ordinal) || slot.name.StartsWith("燃料 ", StringComparison.Ordinal))
                EnsureSlotProcessingProgress(slot);

        // 交互控件必须压在装饰层与业务槽位之上，关闭按钮最后置顶。
        FindDirectRect(root.transform, "合成按钮")?.SetAsLastSibling();
        FindDirectRect(root.transform, "关闭")?.SetAsLastSibling();
    }

    private static void EnsureSlotProcessingProgress(ItemSlot_UI slot)
    {
        const string nodeName = "Slot Processing Progress";
        Transform existing = slot.transform.Find(nodeName);
        GameObject layer = existing != null ? existing.gameObject :
            new GameObject(nodeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        layer.transform.SetParent(slot.transform, false);
        layer.transform.SetAsLastSibling();
        Image image = layer.GetComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = new Color(.8f, .8f, .8f, .45f);
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 0f);
        rect.offsetMin = new Vector2(2f, 2f);
        rect.offsetMax = new Vector2(-2f, -2f);
        // 无 Sprite 的纯色 Image 直接缩放矩形，避免 Filled 模式无法裁切白色遮罩。
        slot.ConfigureProcessingProgress(rect);
    }

    /// <summary>移除熔炉旧版整块遮挡图和重复标签；这些节点不属于运行时绑定契约。</summary>
    private static void RemoveLegacyFurnaceVisuals(Transform root)
    {
        RectTransform legacyOverlay = FindDirectRect(root, "Image");
        if (legacyOverlay != null)
            UnityEngine.Object.DestroyImmediate(legacyOverlay.gameObject);

        RectTransform processArrow = FindDirectRect(root, "Image_2");
        if (processArrow != null)
            UnityEngine.Object.DestroyImmediate(processArrow.gameObject);

        string[] legacyLabels = { "Text (TMP)", "Text (TMP)_1", "Text (TMP)_2" };
        foreach (string labelName in legacyLabels)
        {
            RectTransform label = FindDirectRect(root, labelName);
            if (label != null)
                UnityEngine.Object.DestroyImmediate(label.gameObject);
        }
    }

    /// <summary>给熔炉作业区状态条补充轻量说明，不参与射线。</summary>
    private static void AddFurnaceProcessLabel(RectTransform frame, string name, string text, float x, float y, float width)
    {
        TextMeshProUGUI label = CreateText(name, frame, text, 13f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetTopLeft(label.rectTransform, x, y, width, 18f);
        label.raycastTarget = false;
    }

    /// <summary>炉温反馈只保留百位精度，因此只预留粗略数值文本，不创建精确刻度。</summary>
    private static void AddFurnaceTemperatureLabel(RectTransform frame)
    {
        TextMeshProUGUI label = CreateText(
            "FWUI_FurnaceTemperatureValue",
            frame,
            "当前炉温 0°C",
            13f,
            Muted,
            FontStyles.Normal,
            TextAlignmentOptions.Left);
        SetTopLeft(label.rectTransform, 316f, 262f, 220f, 18f);
        label.raycastTarget = false;
    }

    private static void AddInductionTemperatureControls(RectTransform frame)
    {
        // 共用炉面板只预置独立温控区，电磁炉会话按能力启用。
        RectTransform controls = CreateRect("FWUI_InductionTemperatureControls", frame);
        SetTopLeft(controls, 316f, 174f, 220f, 288f);
        TextMeshProUGUI target = CreateText("FWUI_InductionTargetValue", controls, string.Empty,
            13f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetTopLeft(target.rectTransform, 0f, 0f, 220f, 18f);

        GameObject inputRoot = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(CommonControlsRoot + "UI_InputField.prefab"), controls);
        inputRoot.name = "FWUI_InductionTargetInput";
        TMP_InputField input = inputRoot.GetComponent<TMP_InputField>();
        SetTopLeft((RectTransform)input.transform, 0f, 26f, 132f, 40f);
        input.contentType = TMP_InputField.ContentType.DecimalNumber;
        input.characterLimit = 12;
        if (input.placeholder is TMP_Text placeholder) placeholder.text = string.Empty;

        GameObject buttonRoot = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(CommonControlsRoot + "UI_Button.prefab"), controls);
        buttonRoot.name = "FWUI_ApplyInductionTarget";
        SetTopLeft((RectTransform)buttonRoot.transform, 144f, 26f, 76f, 40f);
        buttonRoot.GetComponentInChildren<TMP_Text>(true).text = "应用";

        TextMeshProUGUI feedback = CreateText("FWUI_InductionSettingFeedback", controls, string.Empty,
            13f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetTopLeft(feedback.rectTransform, 0f, 224f, 220f, 64f);
        feedback.enableWordWrapping = true;
        foreach (Transform child in controls.GetComponentsInChildren<Transform>(true))
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(child)) continue;
            PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            foreach (Component component in child.GetComponents<Component>())
                if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        controls.gameObject.SetActive(false);
    }

    /// <summary>将普通合成面板底栏扩大到可容纳 60 像素触控按钮，并给内容区留出明确间距。</summary>
    private static void ResizeCraftingFooter(RectTransform chrome, float width, float height)
    {
        RectTransform inner = FindRect(chrome, "FWUI_InnerField");
        if (inner != null)
            SetTopLeft(inner, 14f, 92f, width - 28f, height - 176f);

        RectTransform footer = FindRect(chrome, "FWUI_Footer");
        if (footer != null)
            footer.sizeDelta = new Vector2(0f, 76f);

        RectTransform hint = FindRect(chrome, "FWUI_FooterHint");
        if (hint != null)
        {
            hint.anchoredPosition = new Vector2(24f, 24f);
            hint.sizeDelta = new Vector2(width - 390f, 26f);
        }
    }

    /// <summary>删除超过站点能力的旧槽位；开发期配置直接服从当前结构，不保留旧九宫格。</summary>
    private static void RemoveNamedSlots(Transform root, string prefix, int keepCount)
    {
        ItemSlot_UI[] slots = root.GetComponentsInChildren<ItemSlot_UI>(true);
        foreach (ItemSlot_UI slot in slots)
        {
            if (slot == null || !slot.name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string suffix = slot.name.Substring(prefix.Length);
            if (int.TryParse(suffix, out int index) && index > keepCount)
                UnityEngine.Object.DestroyImmediate(slot.gameObject);
        }
    }

    /// <summary>从同组正式槽位复制缺少的节点，保留 ItemSlot_UI 的完整图层与引用。</summary>
    private static void EnsureNamedSlot(Transform root, string sourceName, string targetName)
    {
        if (FindRect(root, targetName) != null)
            return;

        RectTransform source = FindRect(root, sourceName);
        if (source == null)
            throw new MissingReferenceException($"{root.name} 缺少可复制的槽位 {sourceName}");

        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, source.parent, false);
        clone.name = targetName;
        clone.SetActive(true);
    }

    /// <summary>把业务槽位归一到面板根节点，避免旧 LayoutGroup 再次改写移动端布局。</summary>
    private static void LayoutCraftingSlots(
        Transform root,
        string prefix,
        int count,
        float areaX,
        float top,
        float areaWidth,
        float size,
        int maxColumns)
    {
        const float spacing = 16f;
        int columns = Mathf.Clamp(maxColumns, 1, count);
        Transform content = root.Find("CraftingContent") ?? root;

        for (int index = 1; index <= count; index++)
        {
            RectTransform slot = FindRect(root, prefix + index);
            if (slot == null)
                throw new MissingReferenceException($"{root.name} 缺少槽位 {prefix}{index}");

            int zeroBased = index - 1;
            int row = zeroBased / columns;
            int column = zeroBased % columns;
            int itemsInRow = Mathf.Min(columns, count - row * columns);
            float rowWidth = itemsInRow * size + Mathf.Max(0, itemsInRow - 1) * spacing;
            float left = areaX + (areaWidth - rowWidth) * 0.5f;
            slot.SetParent(content, false);
            slot.localScale = Vector3.one;
            slot.SetAsLastSibling();
            SetTopLeft(slot, left + column * (size + spacing), top + row * (size + spacing), size, size);
        }
    }

    /// <summary>创建候选配方滚动容器及隐藏行模板；运行时只克隆该模板并绑定实际配方。</summary>
    private static void BuildRecipeCandidateScroll(
        RectTransform chrome,
        float x,
        float y,
        float width,
        float height)
    {
        Image listSurface = CreateImage("FWUI_RecipeScroll", chrome, new Color(0.025f, 0.060f, 0.077f, 0.86f));
        SetTopLeft(listSurface.rectTransform, x, y, width, height);
        listSurface.raycastTarget = true;
        AddOutline(listSurface, new Color(0.55f, 0.68f, 0.70f, 0.14f));

        ScrollRect scroll = listSurface.gameObject.AddComponent<ItemStepScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 46f;

        Image viewportImage = CreateImage("FWUI_RecipeViewport", listSurface.transform, new Color(1f, 1f, 1f, 0.001f));
        SetStretchWithMargins(viewportImage.rectTransform, 8f, 8f, 8f, 8f);
        viewportImage.raycastTarget = true;
        viewportImage.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect(CraftingStationController.CandidateContentName, viewportImage.transform);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image templateImage = CreateImage(
            CraftingStationController.CandidateTemplateName,
            content,
            SurfaceRaised);
        templateImage.raycastTarget = true;
        AddOutline(templateImage, new Color(0.55f, 0.68f, 0.70f, 0.18f));
        LayoutElement templateLayout = templateImage.gameObject.AddComponent<LayoutElement>();
        templateLayout.minHeight = 84f;
        templateLayout.preferredHeight = 84f;

        Button templateButton = templateImage.gameObject.AddComponent<Button>();
        templateButton.targetGraphic = templateImage;
        templateButton.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = templateButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.10f, 1.04f, 1f);
        colors.pressedColor = new Color(0.76f, 0.82f, 0.80f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.fadeDuration = 0.08f;
        templateButton.colors = colors;
        templateImage.gameObject.AddComponent<FlatWorldUIFeedback>();

        Image icon = CreateImage(CraftingStationController.CandidateIconName, templateImage.transform, Color.white);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(10f, 0f);
        icon.rectTransform.sizeDelta = new Vector2(52f, 52f);
        icon.preserveAspect = true;

        TextMeshProUGUI label = CreateText(
            CraftingStationController.CandidateLabelName,
            templateImage.transform,
            string.Empty,
            20f,
            Cream,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        label.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        label.rectTransform.anchorMax = new Vector2(0.54f, 0.5f);
        label.rectTransform.offsetMin = new Vector2(72f, -30f);
        label.rectTransform.offsetMax = new Vector2(-50f, 30f);
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI outputAmount = CreateText(
            CraftingStationController.CandidateOutputAmountName,
            templateImage.transform,
            string.Empty,
            18f,
            Amber,
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        outputAmount.rectTransform.anchorMin = outputAmount.rectTransform.anchorMax = new Vector2(0.54f, 0.5f);
        outputAmount.rectTransform.anchoredPosition = new Vector2(-26f, 0f);
        outputAmount.rectTransform.sizeDelta = new Vector2(48f, 44f);
        ConfigureCandidateAmountText(outputAmount);

        RectTransform materials = CreateRect(CraftingStationController.CandidateMaterialsName, templateImage.transform);
        materials.anchorMin = new Vector2(0.54f, 0f);
        materials.anchorMax = Vector2.one;
        materials.offsetMin = new Vector2(8f, 8f);
        materials.offsetMax = new Vector2(-8f, -8f);
        GridLayoutGroup materialGrid = materials.gameObject.AddComponent<GridLayoutGroup>();
        materialGrid.cellSize = new Vector2(56f, 64f);
        materialGrid.spacing = new Vector2(4f, 4f);
        materialGrid.childAlignment = TextAnchor.UpperRight;
        materialGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        materialGrid.constraintCount = 3;
        RectTransform materialTemplate = CreateRect(CraftingStationController.CandidateMaterialTemplateName, materials);
        materialTemplate.sizeDelta = materialGrid.cellSize;
        Image materialIcon = CreateImage(CraftingStationController.CandidateMaterialIconName, materialTemplate, Color.white);
        SetTopLeft(materialIcon.rectTransform, 8f, 0f, 40f, 40f);
        materialIcon.preserveAspect = true;
        materialIcon.raycastTarget = false;
        TextMeshProUGUI materialAmount = CreateText(
            CraftingStationController.CandidateMaterialAmountName,
            materialTemplate,
            string.Empty,
            18f,
            Teal,
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        SetTopLeft(materialAmount.rectTransform, 0f, 40f, 56f, 24f);
        ConfigureCandidateAmountText(materialAmount);
        materialTemplate.gameObject.SetActive(false);

        templateImage.gameObject.SetActive(false);
        scroll.viewport = viewportImage.rectTransform;
        scroll.content = content;
        scroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>候选数量使用固定预算并自动缩字，保证 ×10、×100 等多位数不会挤入相邻区域。</summary>
    private static void ConfigureCandidateAmountText(TextMeshProUGUI text)
    {
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 11f;
        text.fontSizeMax = 18f;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
    }

    /// <summary>旧版单配方进度条已由双输出槽预览取代。</summary>
    private static void SetLegacyCraftingProgressActive(Transform root, bool active)
    {
        RectTransform progress = FindDirectRect(root, "Progress");
        if (progress != null)
            progress.gameObject.SetActive(active);

        RectTransform background = FindDirectRect(root, "Image_1");
        if (background != null)
            background.gameObject.SetActive(active);
    }

    private static void PlaceCraftingActionButton(Transform root, string caption)
    {
        RectTransform button = FindRect(root, "合成按钮");
        if (button == null)
            throw new MissingReferenceException($"{root.name} 缺少合成按钮");

        button.anchorMin = new Vector2(1f, 0f);
        button.anchorMax = new Vector2(1f, 0f);
        button.pivot = new Vector2(1f, 0f);
        button.anchoredPosition = new Vector2(-20f, 8f);
        button.sizeDelta = new Vector2(320f, 60f);
        button.SetAsLastSibling();
        ConfigureActionButton(button.gameObject, caption, true);
    }

    private static void PlaceCraftingCloseButton(Transform root)
    {
        RectTransform close = FindDirectRect(root, "关闭");
        if (close == null)
            throw new MissingReferenceException($"{root.name} 缺少关闭按钮");

        close.anchorMin = Vector2.one;
        close.anchorMax = Vector2.one;
        close.pivot = Vector2.one;
        close.anchoredPosition = new Vector2(-12f, -9f);
        close.sizeDelta = new Vector2(60f, 60f);
        close.SetAsLastSibling();
        ConfigureActionButton(close.gameObject, "×", false);
    }

    #endregion
}
