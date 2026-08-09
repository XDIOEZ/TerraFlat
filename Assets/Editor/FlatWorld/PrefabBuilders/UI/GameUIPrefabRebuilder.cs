// AI-Context: 游戏内 UI 的结构级重建器；保留双脚本 UI 的全部业务节点名，只创建 FWUI_ 前缀的美术节点并调整 RectTransform。

using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 将旧的“功能先行”UI 重构为和主界面一致的生存档案风格。
/// 所有业务 Button/TMP/Slider/Image 名称保持不变，确保 BasePanel 的按名绑定契约稳定。
/// </summary>
public static class GameUIPrefabRebuilder
{
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";
    private const string SelectBoxSpritePath = "Assets/6_Art/UI/Inventory_UI/Inventory_select.png";
    private const string InventoryRoot = "Assets/2_Prefabs/2-1_UI/InventoryUI/";
    private const string MenuRoot = "Assets/2_Prefabs/2-1_UI/Menu_UI/";
    private const string ModsRoot = "Assets/2_Prefabs/2-1_UI/ModsUI/";
    private const string CommonRoot = "Assets/2_Prefabs/2-1_UI/";
    private const string SlotPrefabPath = InventoryRoot + "UI_Slot.prefab";

    private static readonly string[] CraftingPreviewPrefabPaths =
    {
        SlotPrefabPath,
        InventoryRoot + "UI_HandCraftTable.prefab",
        InventoryRoot + "UI_MakerTable.prefab",
        InventoryRoot + "UI_FireDrill.prefab",
        InventoryRoot + "UI_FlintStrike.prefab"
    };

    private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.985f);
    private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.985f);
    private static readonly Color Surface = new Color(0.063f, 0.153f, 0.188f, 0.985f);
    private static readonly Color SurfaceRaised = new Color(0.094f, 0.212f, 0.247f, 0.985f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.66f, 0.72f, 0.73f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);
    private static readonly Color Border = new Color(0.55f, 0.68f, 0.70f, 0.22f);

    private static TMP_FontAsset font;

    private static readonly Dictionary<string, string[]> BindingContracts = new Dictionary<string, string[]>
    {
        { InventoryRoot + "UI_Bag.prefab", new[] { "Scroll View", "Content", "关闭" } },
        { InventoryRoot + "UI_Equipment.prefab", new[] { "Scroll View", "Content", "关闭" } },
        { InventoryRoot + "UI_HandCraftTable.prefab", new[] { "输入_1", "输入_2", "输入_3", "输入_4", "输出_1", "合成按钮", "关闭", "Progress" } },
        { InventoryRoot + "UI_MakerTable.prefab", new[] { "输入_1", "输入_9", "输出_1", "输出_2", "合成按钮", "关闭", "Progress" } },
        { InventoryRoot + "UI_Furnace.prefab", new[] { "输入_1", "输入_2", "输入_3", "输出_1", "燃料_1", "熔炼进度条", "燃料显示条", "合成按钮", "关闭" } },
        { InventoryRoot + "UI_BoneFire.prefab", new[] { "输入_1", "输出_1", "燃料_1", "熔炼进度条", "燃料显示条", "合成按钮", "关闭" } },
        { InventoryRoot + "UI_FireDrill.prefab", new[] { "输入_1", "输出_1", "合成按钮", "关闭", "Progress" } },
        { InventoryRoot + "UI_FlintStrike.prefab", new[] { "输入_1", "输出_1", "合成按钮", "关闭", "Progress" } },
        { InventoryRoot + "UI_Death.prefab", new[] { "重生", "回到主菜单" } },
        { InventoryRoot + "UI_GameModuleUI.prefab", new[] { "Dropdown", "Template", "Content" } },
        { MenuRoot + "Info_Button_List.prefab", new[] { "Scroll View", "保存游戏", "保存并回到主界面按钮", "保存并退出游戏按钮", "关闭" } },
        { CommonRoot + "右键菜单.prefab", new[] { "控制面板", "销毁面板", "使用物品", "查看物品信息" } },
        { CommonRoot + "物品信息面板.prefab", new[] { "面板", "信息", "销毁" } },
        { ModsRoot + "UI_Canvas.prefab", new[] { "Panel", "Slider", "关闭页面" } },
        { ModsRoot + "UI_HP.prefab", new[] { "血量模块_世界面板", "背景", "血量" } },
        { ModsRoot + "UI_Food.prefab", new[] { "碳水", "脂肪", "蛋白质", "水", "维生素", "体温", "DataText_体温" } },
        { ModsRoot + "UI_Sleep.prefab", new[] { "ZZZs" } }
    };

    private sealed class BuildTarget
    {
        public string Path;
        public Action<GameObject> Build;

        public BuildTarget(string path, Action<GameObject> build)
        {
            Path = path;
            Build = build;
        }
    }

    [MenuItem("FlatWorld/UI/重构全部游戏内UI")]
    public static void RebuildAllGameUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Game UI] 缺少统一字体：{FontPath}");
            return;
        }

        List<BuildTarget> targets = new List<BuildTarget>
        {
            new BuildTarget(InventoryRoot + "UI_Bag.prefab", root => BuildScrollWindow(root, 706f, 640f, "行囊", "INVENTORY / FIELD KIT", "整理携带物资 · 拖拽交换位置", 5, new Vector2(96f, 96f))),
            new BuildTarget(InventoryRoot + "UI_Equipment.prefab", root => BuildScrollWindow(root, 526f, 566f, "装备", "EQUIPMENT / LOADOUT", "将装备拖入槽位以更新生存配置", 2, new Vector2(112f, 112f))),
            new BuildTarget(InventoryRoot + "UI_CompostBin.prefab", root => BuildScrollWindow(root, 646f, 468f, "堆肥箱", "COMPOST / RESOURCE CYCLE", "投入可腐物 · 等待自然转化", 5, new Vector2(88f, 88f))),
            new BuildTarget(InventoryRoot + "UI_MeatRack.prefab", root => BuildScrollWindow(root, 646f, 468f, "晾肉架", "MEAT RACK / PRESERVATION", "保持通风 · 留意加工进度", 5, new Vector2(88f, 88f))),
            new BuildTarget(InventoryRoot + "UI_HandCraftTable.prefab", root => BuildCraftWindow(root, "手工制作", "CRAFTING / BASIC WORK", 4, false)),
            new BuildTarget(InventoryRoot + "UI_FireDrill.prefab", root => BuildCraftWindow(root, "钻木取火", "FIRECRAFT / FRICTION", 1, true)),
            new BuildTarget(InventoryRoot + "UI_FlintStrike.prefab", root => BuildCraftWindow(root, "燧石取火", "FIRECRAFT / SPARK", 1, true)),
            new BuildTarget(InventoryRoot + "UI_MakerTable.prefab", BuildMakerTable),
            new BuildTarget(InventoryRoot + "UI_Furnace.prefab", root => BuildFurnace(root, false)),
            new BuildTarget(InventoryRoot + "UI_BoneFire.prefab", root => BuildFurnace(root, true)),
            new BuildTarget(InventoryRoot + "UI_Death.prefab", BuildDeathScreen),
            new BuildTarget(InventoryRoot + "UI_HotBar.prefab", BuildHotbar),
            new BuildTarget(InventoryRoot + "UI_Hand.prefab", BuildHandSlot),
            new BuildTarget(InventoryRoot + "UI_GameModuleUI.prefab", BuildGameModuleSelector),
            new BuildTarget(MenuRoot + "Info_Button_List.prefab", BuildActionList),
            new BuildTarget(MenuRoot + "UI_ContextMenu .prefab", BuildSaveContextMenu),
            new BuildTarget(CommonRoot + "右键菜单.prefab", BuildItemContextMenu),
            new BuildTarget(CommonRoot + "物品信息面板.prefab", BuildItemInfo),
            new BuildTarget(CommonRoot + "Hand_Slot_UI.prefab", BuildSlot),
            new BuildTarget(CommonRoot + "Mod_UI_OpenUIButton.prefab", BuildBaseButton),
            new BuildTarget(InventoryRoot + "Button.prefab", BuildBaseButton),
            new BuildTarget(ModsRoot + "UI_Canvas.prefab", BuildSettingsPanel),
            new BuildTarget(ModsRoot + "UI_HP.prefab", BuildHealthWorldPanel),
            new BuildTarget(ModsRoot + "UI_Food.prefab", BuildNutritionHud),
            new BuildTarget(ModsRoot + "UI_Sleep.prefab", BuildSleepHud),
            new BuildTarget(CommonRoot + "InventoryUI/UI_Slot.prefab", BuildSlot),
            new BuildTarget(CommonRoot + "Base_UI/UI_Slider.prefab", BuildBaseSlider),
            new BuildTarget(CommonRoot + "Base_UI/UI_SelectBox.prefab", BuildBaseSelectBox)
        };

        int rebuilt = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (BuildTarget target in targets)
            {
                if (!System.IO.File.Exists(target.Path))
                {
                    Debug.LogWarning($"[Game UI] 未找到 Prefab：{target.Path}");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(target.Path);
                try
                {
                    RemoveGeneratedArt(root.transform);
                    target.Build(root);
                    SetUILayerRecursively(root);
                    NormalizeTypography(root.transform);
                    NormalizeControls(root.transform);
                    FlatWorldUITheme.Apply(root.transform);
                    EditorUtility.SetDirty(root);
                    PrefabUtility.SaveAsPrefabAsset(root, target.Path);
                    rebuilt++;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Game UI] 重构失败：{target.Path}\n{exception}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Game UI] 已完成 {rebuilt}/{targets.Count} 个 Prefab 的结构级重构；业务节点名称全部保留。");
        ValidateRebuiltUI();
    }

    [MenuItem("FlatWorld/UI/修复快捷栏UI")]
    public static void RebuildHotbarUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Game UI] 缺少统一字体：{FontPath}");
            return;
        }

        BuildTarget[] targets =
        {
            new BuildTarget(InventoryRoot + "UI_HotBar.prefab", BuildHotbar),
            new BuildTarget(CommonRoot + "Base_UI/UI_SelectBox.prefab", BuildBaseSelectBox)
        };

        int rebuilt = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (BuildTarget target in targets)
            {
                if (RebuildSinglePrefab(target))
                    rebuilt++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Game UI] 快捷栏修复完成：{rebuilt}/{targets.Length} 个 Prefab。快捷栏直属子节点只保留物品槽。");
    }

    [MenuItem("FlatWorld/UI/修复制作预览图层")]
    public static void RebuildCraftingPreviewLayers()
    {
        int rebuilt = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in CraftingPreviewPrefabPaths)
            {
                if (!System.IO.File.Exists(path))
                {
                    Debug.LogError($"[Crafting Preview Prefab] 未找到 Prefab：{path}");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int repairedSlots = path == SlotPrefabPath
                        ? RepairBaseSlotPreview(root)
                        : EnsureCraftingOutputPreviewLayers(root.transform);
                    if (repairedSlots == 0)
                        throw new MissingReferenceException($"{path} 未找到制作输出槽。");

                    SetUILayerRecursively(root);
                    EditorUtility.SetDirty(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    rebuilt++;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Crafting Preview Prefab] 修复失败：{path}\n{exception}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Crafting Preview Prefab] 已重建 {rebuilt}/{CraftingPreviewPrefabPaths.Length} 个 Prefab。");
        ValidateCraftingPreviewPrefabs();
    }

    [MenuItem("FlatWorld/UI/验证制作预览图层")]
    public static void ValidateCraftingPreviewPrefabs()
    {
        List<string> failures = new List<string>();
        int checkedSlots = 0;
        foreach (string path in CraftingPreviewPrefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (path == SlotPrefabPath)
                {
                    ValidateCraftingPreviewSlot(root.GetComponent<ItemSlot_UI>(), path, failures);
                    checkedSlots++;
                    continue;
                }

                ItemSlot_UI[] slots = root.GetComponentsInChildren<ItemSlot_UI>(true);
                int outputCount = 0;
                foreach (ItemSlot_UI slot in slots)
                {
                    if (slot == null || !slot.name.StartsWith("输出_", StringComparison.Ordinal))
                        continue;

                    outputCount++;
                    checkedSlots++;
                    ValidateCraftingPreviewSlot(slot, path, failures);
                }

                if (outputCount == 0)
                    failures.Add($"{path} 未找到输出槽");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        if (failures.Count == 0)
        {
            Debug.Log($"[Crafting Preview Prefab] 验证通过：{CraftingPreviewPrefabPaths.Length} 个 Prefab、{checkedSlots} 个槽位结构完整。");
            return;
        }

        Debug.LogError("[Crafting Preview Prefab] 验证失败：\n- " + string.Join("\n- ", failures));
    }

    [MenuItem("FlatWorld/UI/居中功能列表按钮")]
    public static void RebuildActionListUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Game UI] 缺少统一字体：{FontPath}");
            return;
        }

        BuildTarget target = new BuildTarget(MenuRoot + "Info_Button_List.prefab", BuildActionList);
        bool rebuilt = RebuildSinglePrefab(target);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(rebuilt
            ? "[Game UI] 功能列表按钮已居中。"
            : "[Game UI] 功能列表按钮重建失败，请检查控制台。");
    }

    [MenuItem("FlatWorld/UI/重建矩形进度条UI")]
    public static void RebuildRectangularProgressBars()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Game UI] 缺少统一字体：{FontPath}");
            return;
        }

        BuildTarget[] targets =
        {
            new BuildTarget(CommonRoot + "Base_UI/UI_Slider.prefab", BuildBaseSlider),
            new BuildTarget(ModsRoot + "UI_Food.prefab", BuildNutritionHud),
            new BuildTarget(InventoryRoot + "UI_Furnace.prefab", root => BuildFurnace(root, false)),
            new BuildTarget(InventoryRoot + "UI_BoneFire.prefab", root => BuildFurnace(root, true)),
            new BuildTarget(ModsRoot + "UI_Canvas.prefab", BuildSettingsPanel)
        };

        int rebuilt = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (BuildTarget target in targets)
            {
                if (RebuildSinglePrefab(target))
                    rebuilt++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Game UI] 矩形进度条重建完成：{rebuilt}/{targets.Length} 个 Prefab；状态条手柄已隐藏。");
    }

    [MenuItem("FlatWorld/UI/重建角色参数面板")]
    public static void RebuildCharacterStatusPanel()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Game UI] 缺少统一字体：{FontPath}");
            return;
        }

        BuildTarget target = new BuildTarget(ModsRoot + "UI_Food.prefab", BuildNutritionHud);
        bool rebuilt = RebuildSinglePrefab(target);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(rebuilt
            ? "[Game UI] 角色参数面板重建完成。"
            : "[Game UI] 角色参数面板重建失败，请检查控制台。");
    }

    private static bool RebuildSinglePrefab(BuildTarget target)
    {
        if (!System.IO.File.Exists(target.Path))
        {
            Debug.LogWarning($"[Game UI] 未找到 Prefab：{target.Path}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(target.Path);
        try
        {
            RemoveGeneratedArt(root.transform);
            target.Build(root);
            SetUILayerRecursively(root);
            NormalizeTypography(root.transform);
            NormalizeControls(root.transform);
            FlatWorldUITheme.Apply(root.transform);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, target.Path);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Game UI] 重构失败：{target.Path}\n{exception}");
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("FlatWorld/UI/验证重构UI绑定契约")]
    public static void ValidateRebuiltUI()
    {
        if (font == null)
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        List<string> failures = new List<string>();
        int checkedPrefabs = 0;
        foreach (KeyValuePair<string, string[]> contract in BindingContracts)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(contract.Key);
            try
            {
                checkedPrefabs++;
                foreach (string requiredName in contract.Value)
                {
                    if (FindTransform(root.transform, requiredName) == null)
                        failures.Add($"{contract.Key} 缺少节点 {requiredName}");
                }

                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (font != null && text.font != font)
                        failures.Add($"{contract.Key} 字体未统一：{BuildPath(text.transform)}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        if (failures.Count == 0)
        {
            Debug.Log($"[Game UI Contract] 通过：{checkedPrefabs} 个核心 Prefab 的业务节点与字体契约完整。");
            return;
        }

        Debug.LogError("[Game UI Contract] 验证失败：\n- " + string.Join("\n- ", failures));
    }

    private static void BuildScrollWindow(
        GameObject root,
        float width,
        float height,
        string title,
        string eyebrow,
        string hint,
        int columns,
        Vector2 cellSize)
    {
        RectTransform frame = PrepareWindow(root, width, height, title, eyebrow, hint);
        AddSection(frame, "CONTENTS", "物资清单", 24f, 104f, width - 48f, height - 184f);

        RectTransform scroll = FindRect(root.transform, "Scroll View");
        if (scroll != null)
            SetTopLeft(scroll, 42f, 146f, width - 84f, height - 246f);

        GridLayoutGroup grid = root.GetComponentInChildren<GridLayoutGroup>(true);
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = cellSize;
            grid.spacing = new Vector2(12f, 12f);
            grid.padding = new RectOffset(12, 12, 12, 18);
            grid.childAlignment = TextAnchor.UpperLeft;
        }

        ScrollRect scrollRect = root.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null)
        {
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 34f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
        }
    }

    private static void BuildCraftWindow(GameObject root, string title, string eyebrow, int inputCount, bool compact)
    {
        float width = compact ? 646f : 726f;
        float height = compact ? 438f : 526f;
        RectTransform frame = PrepareWindow(root, width, height, title, eyebrow, compact ? "按住操作键推进过程 · 松开即可暂停" : "放入材料 · 核对产物 · 开始制作");

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

    private static void BuildMakerTable(GameObject root)
    {
        const float width = 824f;
        const float height = 604f;
        RectTransform frame = PrepareWindow(root, width, height, "制作台", "WORKBENCH / REFINED CRAFT", "组合多种材料 · 产物完成后移入背包");
        AddSection(frame, "MATERIAL MATRIX", "材料矩阵", 24f, 104f, 510f, 400f);
        AddSection(frame, "RESULT", "制作结果", 556f, 104f, 244f, 400f);
        AddFlowArrow(frame, 544f, 280f);

        for (int i = 1; i <= 9; i++)
        {
            int column = (i - 1) % 3;
            int row = (i - 1) / 3;
            RectTransform slot = FindRect(root.transform, $"输入_{i}");
            if (slot != null)
                SetTopLeft(slot, 76f + column * 132f, 168f + row * 112f, 96f, 96f);
        }

        for (int i = 1; i <= 2; i++)
        {
            RectTransform slot = FindRect(root.transform, $"输出_{i}");
            if (slot != null)
                SetTopLeft(slot, 630f, 178f + (i - 1) * 132f, 104f, 104f);
        }

        PlaceProgress(root.transform, 48f, height - 82f, 464f, 14f);
        PlaceActionButton(root.transform, "合成按钮", width, height, "开始制作");
        EnsureCraftingOutputPreviewLayers(root.transform);
    }

    private static void BuildFurnace(GameObject root, bool bonfire)
    {
        float width = bonfire ? 740f : 856f;
        const float height = 592f;
        string title = bonfire ? "篝火" : "熔炉";
        string eyebrow = bonfire ? "BONFIRE / FIRE MANAGEMENT" : "FURNACE / SMELTING OPERATION";
        RectTransform frame = PrepareWindow(root, width, height, title, eyebrow, bonfire ? "维持燃料 · 处理食物与基础材料" : "控制燃料与温度 · 等待冶炼完成");

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
                SetTopLeft(input, 56f, 156f, 168f, 252f);
                ArrangeSlots(input, 3, 1, 82f, 12f);
            }
            if (fuel != null)
            {
                SetTopLeft(fuel, 328f, 318f, 176f, 96f);
                ArrangeSlots(fuel, 3, 3, 72f, 8f);
            }
            if (output != null)
            {
                SetTopLeft(output, width - 218f, 164f, 168f, 236f);
                ArrangeSlots(output, 3, 1, 82f, 12f);
            }
        }

        RectTransform sliders = FindRect(root.transform, "Slider");
        if (sliders != null)
            SetTopLeft(sliders, bonfire ? 270f : 316f, 184f, bonfire ? 202f : 220f, 170f);

        RectTransform illustration = FindDirectRect(root.transform, bonfire ? "Image_1" : "Image_2");
        if (illustration != null)
            SetTopLeft(illustration, bonfire ? 302f : 350f, 152f, 132f, 132f);

        PlaceActionButton(root.transform, "合成按钮", width, height, bonfire ? "开始处理" : "启动熔炼");
    }

    private static void BuildDeathScreen(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect == null)
            return;

        Stretch(rect);
        Image rootImage = EnsureImage(root);
        rootImage.color = new Color(0.015f, 0.026f, 0.034f, 0.90f);
        rootImage.sprite = null;
        rootImage.type = Image.Type.Simple;

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image horizon = CreateImage("FWUI_DeathHorizon", chrome, new Color(0.83f, 0.49f, 0.23f, 0.18f));
        horizon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        horizon.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        horizon.rectTransform.sizeDelta = new Vector2(0f, 1f);

        Image card = CreateImage("FWUI_DeathCard", chrome, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        SetCenter(card.rectTransform, new Vector2(0f, 8f), new Vector2(860f, 430f));
        AddOutline(card, new Color(0.83f, 0.49f, 0.23f, 0.26f));

        TextMeshProUGUI eyebrow = CreateText("FWUI_DeathEyebrow", card.transform, "JOURNEY INTERRUPTED / 生存记录", 17f, Amber, FontStyles.Bold, TextAlignmentOptions.Center);
        SetTopCenter(eyebrow.rectTransform, 42f, 680f, 28f);
        eyebrow.characterSpacing = 4f;

        TextMeshProUGUI title = CreateText("FWUI_Death标题", card.transform, "旅程暂告一段落", 48f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        SetTopCenter(title.rectTransform, 94f, 720f, 72f);

        TextMeshProUGUI copy = CreateText("FWUI_DeathCopy", card.transform, "带着这次留下的经验，再次回到这片世界。", 19f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
        SetTopCenter(copy.rectTransform, 174f, 720f, 38f);

        Image accent = CreateImage("FWUI_DeathAccent", card.transform, Amber);
        SetTopCenter(accent.rectTransform, 232f, 92f, 4f);

        PlaceBottomButton(root.transform, "重生", -118f, 118f, 230f, 62f, "重新醒来");
        PlaceBottomButton(root.transform, "回到主菜单", 132f, 118f, 230f, 62f, "结束本次旅程");
    }

    private static void BuildHotbar(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 28f);
        rect.sizeDelta = new Vector2(890f, 104f);

        Image image = EnsureImage(root);
        image.color = new Color(0.025f, 0.043f, 0.058f, 0.92f);
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.34f));

        GridLayoutGroup grid = root.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(82f, 82f);
            grid.spacing = new Vector2(10f, 0f);
            grid.padding = new RectOffset(18, 18, 12, 10);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 9;
        }

        // 快捷栏内嵌的是旧版槽位实例，必须同时清掉其绿色像素底图，
        // 否则统一色值仍会被底图相乘成棕绿，和行囊动态槽位不一致。
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child.GetComponent("ItemSlot_UI") != null)
                BuildSlot(child.gameObject);
        }
    }

    private static void BuildHandSlot(GameObject root)
    {
        Button slot = root.GetComponentInChildren<Button>(true);
        if (slot != null)
            BuildSlot(slot.gameObject);
        NormalizeTypography(root.transform);
    }

    private static void BuildGameModuleSelector(GameObject root)
    {
        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (rootRect == null)
            return;
        Stretch(rootRect);

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image plate = CreateImage("FWUI_ModulePlate", chrome, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        plate.rectTransform.anchorMin = Vector2.one;
        plate.rectTransform.anchorMax = Vector2.one;
        plate.rectTransform.pivot = Vector2.one;
        plate.rectTransform.anchoredPosition = new Vector2(-24f, -24f);
        plate.rectTransform.sizeDelta = new Vector2(374f, 94f);
        AddOutline(plate, Border);

        Image accent = CreateImage("FWUI_ModuleAccent", chrome, Amber);
        accent.rectTransform.anchorMin = Vector2.one;
        accent.rectTransform.anchorMax = Vector2.one;
        accent.rectTransform.pivot = Vector2.one;
        accent.rectTransform.anchoredPosition = new Vector2(-394f, -24f);
        accent.rectTransform.sizeDelta = new Vector2(4f, 94f);

        TextMeshProUGUI label = CreateText("FWUI_Module标题", chrome, "MODULES  /  界面模块", 12f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        label.rectTransform.anchorMin = Vector2.one;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.pivot = Vector2.one;
        label.rectTransform.anchoredPosition = new Vector2(-42f, -34f);
        label.rectTransform.sizeDelta = new Vector2(334f, 22f);
        label.characterSpacing = 2f;

        RectTransform dropdown = FindDirectRect(root.transform, "Dropdown");
        if (dropdown != null)
        {
            dropdown.anchorMin = Vector2.one;
            dropdown.anchorMax = Vector2.one;
            dropdown.pivot = Vector2.one;
            dropdown.anchoredPosition = new Vector2(-42f, -62f);
            dropdown.sizeDelta = new Vector2(334f, 42f);
            Image image = dropdown.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = SurfaceRaised;
                AddOutline(image, new Color(0.55f, 0.68f, 0.70f, 0.26f));
            }
        }
    }

    private static void BuildSettingsPanel(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;
        ConfigureFloatingCard(panel, 540f, 320f, "界面设置", "INTERFACE / PRESENTATION");
        PlaceTopLeft(panel, "Slider", 38f, 126f, 464f, 30f);
        PlaceTopLeft(panel, "关闭页面", 38f, 240f, 464f, 52f);
        RectTransform close = FindRect(panel, "关闭页面");
        if (close != null)
            ConfigureActionButton(close.gameObject, "完成设置", true);
    }

    private static void BuildHealthWorldPanel(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "血量模块_世界面板");
        if (panel == null)
            return;

        RectTransform background = FindRect(panel, "背景");
        if (background != null)
        {
            background.sizeDelta = new Vector2(236f, 56f);
            Image image = background.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = new Color(0.025f, 0.043f, 0.058f, 0.86f);
                AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.30f));
            }

            Image accent = CreateImage("FWUI_Chrome", background, Amber);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(4f, 0f);
            accent.transform.SetAsFirstSibling();
        }

        RectTransform healthText = FindRect(panel, "血量");
        if (healthText != null)
        {
            healthText.anchorMin = new Vector2(0.5f, 0.5f);
            healthText.anchorMax = new Vector2(0.5f, 0.5f);
            healthText.pivot = new Vector2(0.5f, 0.5f);
            healthText.anchoredPosition = Vector2.zero;
            healthText.sizeDelta = new Vector2(208f, 40f);
            TMP_Text text = healthText.GetComponent<TMP_Text>();
            if (text != null)
            {
                text.font = font;
                text.fontSize = 18f;
                text.fontStyle = FontStyles.Bold;
                text.color = Cream;
                text.alignment = TextAlignmentOptions.Center;
            }
        }
    }

    private static void BuildActionList(GameObject root)
    {
        RectTransform frame = PrepareWindow(root, 430f, 584f, "功能列表", "ACTIONS / QUICK ENTRY", "选择一项操作继续");
        AddSection(frame, "AVAILABLE", "可用操作", 24f, 104f, 382f, 390f);
        RectTransform scroll = FindRect(root.transform, "Scroll View");
        if (scroll != null)
        {
            SetTopLeft(scroll, 42f, 148f, 346f, 322f);

            RectTransform content = FindRect(scroll, "Content");
            if (content != null)
            {
                ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    UnityEngine.Object.DestroyImmediate(fitter);

                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 0.5f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = Vector2.zero;

                GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
                if (grid != null)
                {
                    grid.padding = new RectOffset(0, 0, 0, 0);
                    grid.childAlignment = TextAnchor.MiddleCenter;
                    grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                    grid.startAxis = GridLayoutGroup.Axis.Vertical;
                    grid.cellSize = new Vector2(264f, 52f);
                    grid.spacing = new Vector2(0f, 14f);
                    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    grid.constraintCount = 1;
                }
            }
        }
        RectTransform oldInfo = FindDirectRect(root.transform, "信息");
        if (oldInfo != null)
            oldInfo.gameObject.SetActive(false);
    }

    private static void BuildSaveContextMenu(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;

        ConfigureFloatingCard(panel, 324f, 356f, "存档操作", "SAVE / ACTIONS");
        PlaceTopLeft(panel, "InputField (TMP)", 24f, 102f, 276f, 52f);
        PlaceTopLeft(panel, "Button_ReName", 24f, 174f, 276f, 50f);
        PlaceTopLeft(panel, "删除按钮", 24f, 236f, 276f, 50f);
        PlaceTopLeft(panel, "Button_Close", 24f, 298f, 276f, 40f);
    }

    private static void BuildItemContextMenu(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "控制面板");
        if (panel == null)
            return;

        ConfigureFloatingCard(panel, 310f, 408f, "物品操作", "ITEM / ACTIONS");
        RectTransform scroll = FindRect(panel, "Scroll View");
        if (scroll != null)
            SetTopLeft(scroll, 20f, 94f, 270f, 230f);
        RectTransform destroy = FindRect(panel, "销毁面板");
        if (destroy != null)
            SetTopLeft(destroy, 20f, 338f, 270f, 50f);
    }

    private static void BuildItemInfo(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "面板");
        if (panel == null)
            return;

        ConfigureFloatingCard(panel, 468f, 590f, "物品详情", "ITEM / FIELD NOTES");
        RectTransform panelRect = panel as RectTransform;
        if (panelRect != null)
            panelRect.localScale = Vector3.one;

        PlaceTopLeft(panel, "Image", 30f, 112f, 112f, 112f);
        PlaceTopLeft(panel, "信息", 164f, 112f, 270f, 380f);
        PlaceTopLeft(panel, "销毁", 30f, 516f, 404f, 50f);

        TextMeshProUGUI infoText = FindTransform(panel, "信息")?.GetComponent<TextMeshProUGUI>();
        if (infoText != null)
        {
            infoText.font = font;
            infoText.fontSize = 16f;
            infoText.enableAutoSizing = true;
            infoText.fontSizeMin = 13f;
            infoText.fontSizeMax = 16f;
            infoText.fontStyle = FontStyles.Normal;
            infoText.alignment = TextAlignmentOptions.TopLeft;
            infoText.enableWordWrapping = true;
            infoText.overflowMode = TextOverflowModes.Page;
            infoText.rectTransform.localScale = Vector3.one;
        }
    }

    private static void BuildNutritionHud(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        RectTransform panelRect = panel as RectTransform;
        if (panelRect == null)
            return;

        if (rootRect != null)
            Stretch(rootRect);

        EnsureNutritionTemperatureRow(panel);
        SetCenter(panelRect, new Vector2(-480f, 120f), new Vector2(382f, 324f));
        Image panelImage = EnsureImage(panel.gameObject);
        panelImage.color = new Color(0.025f, 0.043f, 0.058f, 0.90f);
        panelImage.sprite = null;
        panelImage.type = Image.Type.Simple;
        AddOutline(panelImage, Border);
        BuildCardHeader(panel, "角色参数", "SURVIVAL / VITALS", 382f);

        string[] sliders = { "碳水", "脂肪", "蛋白质", "水", "维生素", "体温" };
        for (int i = 0; i < sliders.Length; i++)
        {
            RectTransform slider = FindRect(panel, sliders[i]);
            if (slider != null)
            {
                SetTopLeft(slider, 28f, 86f + i * 38f, 326f, 26f);
                Slider sliderComponent = slider.GetComponent<Slider>();
                if (sliderComponent != null)
                    sliderComponent.interactable = false;
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }

    private static void EnsureNutritionTemperatureRow(Transform panel)
    {
        if (FindTransform(panel, "体温") != null)
            return;

        RectTransform source = FindRect(panel, "维生素");
        if (source == null)
            throw new InvalidOperationException("UI_Food 缺少可复用的维生素状态行。");

        GameObject temperatureRow = UnityEngine.Object.Instantiate(source.gameObject, panel, false);
        temperatureRow.name = "体温";

        TextMeshProUGUI[] labels = temperatureRow.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI label = labels[i];
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            if (label.name.StartsWith("DataText_", StringComparison.Ordinal))
            {
                label.name = "DataText_体温";
                label.text = "36.5°C";
            }
            else if (label.text == "维生素")
            {
                label.text = "体温";
            }
        }
    }

    private static void BuildSleepHud(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect == null)
            return;
        Stretch(rect);

        Image veil = FindDirectRect(root.transform, "Image")?.GetComponent<Image>();
        if (veil != null)
            veil.color = new Color(0.012f, 0.028f, 0.045f, 0.72f);

        RectTransform zzzs = FindDirectRect(root.transform, "ZZZs");
        if (zzzs != null)
        {
            zzzs.anchorMin = new Vector2(0.5f, 0.5f);
            zzzs.anchorMax = new Vector2(0.5f, 0.5f);
            zzzs.pivot = new Vector2(0.5f, 0.5f);
            zzzs.anchoredPosition = new Vector2(0f, 30f);
            zzzs.sizeDelta = new Vector2(640f, 220f);
        }

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();
        TextMeshProUGUI copy = CreateText("FWUI_SleepCopy", chrome, "RESTING / 世界在篝火外继续流动", 17f, new Color(Cream.r, Cream.g, Cream.b, 0.82f), FontStyles.Bold, TextAlignmentOptions.Center);
        copy.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        copy.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        copy.rectTransform.pivot = new Vector2(0.5f, 0f);
        copy.rectTransform.anchoredPosition = new Vector2(0f, 68f);
        copy.rectTransform.sizeDelta = new Vector2(680f, 30f);
        copy.characterSpacing = 3f;
    }

    private static void BuildSlot(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect != null && (rect.sizeDelta.x < 72f || rect.sizeDelta.y < 72f))
            rect.sizeDelta = new Vector2(82f, 82f);

        Image image = EnsureImage(root);
        image.color = new Color(0.10f, 0.17f, 0.18f, 0.98f);
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.30f));

        Button button = root.GetComponent<Button>();
        if (button != null)
        {
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.18f, 1.10f, 0.94f, 1f);
            colors.pressedColor = new Color(0.76f, 0.82f, 0.80f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        RuntimeUIPrefabBuilder.AddCraftingPreviewLayers(root);
    }

    private static int RepairBaseSlotPreview(GameObject root)
    {
        RuntimeUIPrefabBuilder.AddCraftingPreviewLayers(root);
        return 1;
    }

    private static int EnsureCraftingOutputPreviewLayers(Transform root)
    {
        int repairedSlots = 0;
        ItemSlot_UI[] slots = root.GetComponentsInChildren<ItemSlot_UI>(true);
        foreach (ItemSlot_UI slot in slots)
        {
            if (slot == null || !slot.name.StartsWith("输出_", StringComparison.Ordinal))
                continue;

            RuntimeUIPrefabBuilder.AddCraftingPreviewLayers(slot.gameObject);
            repairedSlots++;
        }

        return repairedSlots;
    }

    private static void ValidateCraftingPreviewSlot(ItemSlot_UI slot, string prefabPath, List<string> failures)
    {
        if (slot == null)
        {
            failures.Add($"{prefabPath} 缺少 ItemSlot_UI");
            return;
        }

        Image reference = slot.image;
        Image ghost = FindNamedImage(slot.transform, "Crafting Output Ghost");
        Image reveal = FindNamedImage(slot.transform, "Crafting Output Reveal");
        string context = $"{prefabPath}/{slot.name}";

        if (reference == null)
            failures.Add($"{context} 缺少物品图标引用");
        if (ghost == null)
            failures.Add($"{context} 缺少 Crafting Output Ghost");
        if (reveal == null)
            failures.Add($"{context} 缺少 Crafting Output Reveal");
        if (reference == null || ghost == null || reveal == null)
            return;

        if (ghost.gameObject.activeSelf || reveal.gameObject.activeSelf)
            failures.Add($"{context} 预览图层默认必须隐藏");
        if (ghost.raycastTarget || reveal.raycastTarget)
            failures.Add($"{context} 预览图层不得拦截射线");
        if (!ghost.preserveAspect || !reveal.preserveAspect)
            failures.Add($"{context} 预览图层必须保持宽高比");
        if (reveal.type != Image.Type.Filled ||
            reveal.fillMethod != Image.FillMethod.Vertical ||
            reveal.fillOrigin != (int)Image.OriginVertical.Bottom)
        {
            failures.Add($"{context} Reveal 必须由下向上填充");
        }

        if (ghost.transform.parent != reference.transform.parent ||
            reveal.transform.parent != reference.transform.parent)
        {
            failures.Add($"{context} 预览图层必须与物品图标同级");
        }
    }

    private static Image FindNamedImage(Transform root, string name)
    {
        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image != null && image.name == name)
                return image;
        }

        return null;
    }

    private static void BuildBaseSlider(GameObject root)
    {
        Slider slider = root.GetComponent<Slider>();
        if (slider == null)
            slider = root.GetComponentInChildren<Slider>(true);
        if (slider == null)
            return;

        RectTransform rect = slider.GetComponent<RectTransform>();
        if (rect != null)
            rect.sizeDelta = new Vector2(Mathf.Max(240f, rect.sizeDelta.x), 26f);

        Image background = slider.GetComponent<Image>();
        if (background != null)
            background.color = InkSoft;
        if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill))
            fill.color = Amber;
        if (slider.handleRect != null && slider.handleRect.TryGetComponent(out Image handle))
        {
            handle.color = Cream;
            AddOutline(handle, new Color(0f, 0f, 0f, 0.45f));
        }
    }

    private static void BuildBaseButton(GameObject root)
    {
        Image image = EnsureImage(root);
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = SurfaceRaised;
        AddOutline(image, Border);

        Button button = root.GetComponent<Button>();
        if (button != null)
        {
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.16f, 1.10f, 1f);
            colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
            colors.fadeDuration = 0.10f;
            button.colors = colors;
            if (button.GetComponent<FlatWorldUIFeedback>() == null)
                button.gameObject.AddComponent<FlatWorldUIFeedback>();
        }

        TMP_Text label = root.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.font = font;
            label.fontSize = 17f;
            label.fontStyle = FontStyles.Bold;
            label.color = Cream;
            label.alignment = TextAlignmentOptions.Center;
        }
    }

    private static void BuildBaseSelectBox(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(90f, 90f);
        }

        Image image = EnsureImage(root);
        image.color = Color.white;
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SelectBoxSpritePath);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.34f));
        TMP_Text label = root.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.color = Cream;
            label.font = font;
        }
    }

    private static RectTransform PrepareWindow(GameObject root, float width, float height, string title, string eyebrow, string footerHint)
    {
        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (rootRect == null)
            throw new InvalidOperationException($"{root.name} 缺少 RectTransform");

        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = new Vector2(width, height);

        Image rootImage = EnsureImage(root);
        rootImage.color = Ink;
        rootImage.sprite = null;
        rootImage.type = Image.Type.Simple;
        AddOutline(rootImage, new Color(0.83f, 0.49f, 0.23f, 0.30f));

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image shadow = CreateImage("FWUI_Shadow", chrome, new Color(0.005f, 0.012f, 0.016f, 0.60f));
        Stretch(shadow.rectTransform);
        shadow.rectTransform.offsetMin = new Vector2(12f, -12f);
        shadow.rectTransform.offsetMax = new Vector2(12f, -12f);

        Image body = CreateImage("FWUI_Body", chrome, Ink);
        Stretch(body.rectTransform);

        Image inner = CreateImage("FWUI_InnerField", chrome, new Color(0.045f, 0.075f, 0.095f, 0.74f));
        SetTopLeft(inner.rectTransform, 14f, 92f, width - 28f, height - 158f);

        Image header = CreateImage("FWUI_Header", chrome, new Color(0.075f, 0.18f, 0.215f, 0.96f));
        SetTopLeft(header.rectTransform, 0f, 0f, width, 78f);

        Image accent = CreateImage("FWUI_AccentRail", chrome, Amber);
        SetTopLeft(accent.rectTransform, 0f, 0f, 5f, 78f);

        TextMeshProUGUI eyebrowText = CreateText("FWUI_眉题", chrome, eyebrow, 13f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(eyebrowText.rectTransform, 24f, 13f, width - 120f, 22f);
        eyebrowText.characterSpacing = 3f;

        TextMeshProUGUI titleText = CreateText("FWUI_标题", chrome, title, 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(titleText.rectTransform, 24f, 33f, width - 130f, 38f);

        Image rule = CreateImage("FWUI_HeaderRule", chrome, new Color(0.83f, 0.49f, 0.23f, 0.30f));
        SetTopLeft(rule.rectTransform, 0f, 77f, width, 1f);

        Image footer = CreateImage("FWUI_Footer", chrome, new Color(0.035f, 0.072f, 0.088f, 0.98f));
        footer.rectTransform.anchorMin = Vector2.zero;
        footer.rectTransform.anchorMax = new Vector2(1f, 0f);
        footer.rectTransform.pivot = Vector2.zero;
        footer.rectTransform.anchoredPosition = Vector2.zero;
        footer.rectTransform.sizeDelta = new Vector2(0f, 58f);

        TextMeshProUGUI footerText = CreateText("FWUI_FooterHint", chrome, footerHint, 13f, new Color(Muted.r, Muted.g, Muted.b, 0.86f), FontStyles.Normal, TextAlignmentOptions.Left);
        footerText.rectTransform.anchorMin = Vector2.zero;
        footerText.rectTransform.anchorMax = Vector2.zero;
        footerText.rectTransform.pivot = Vector2.zero;
        footerText.rectTransform.anchoredPosition = new Vector2(24f, 16f);
        footerText.rectTransform.sizeDelta = new Vector2(Mathf.Max(250f, width - 310f), 24f);

        AddCornerTicks(chrome, width, height);
        PlaceCloseButton(root.transform, width);
        return chrome;
    }

    private static void AddSection(RectTransform chrome, string eyebrow, string title, float x, float y, float width, float height)
    {
        Image surface = CreateImage("FWUI_Section_" + Sanitize(eyebrow), chrome, new Color(0.063f, 0.153f, 0.188f, 0.70f));
        SetTopLeft(surface.rectTransform, x, y, width, height);
        AddOutline(surface, new Color(0.55f, 0.68f, 0.70f, 0.15f));

        Image marker = CreateImage("FWUI_SectionMarker_" + Sanitize(eyebrow), chrome, new Color(0.26f, 0.61f, 0.57f, 0.72f));
        SetTopLeft(marker.rectTransform, x + 12f, y + 15f, 3f, 27f);

        TextMeshProUGUI eyebrowText = CreateText("FWUI_SectionEyebrow_" + Sanitize(eyebrow), chrome, eyebrow, 10f, Teal, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(eyebrowText.rectTransform, x + 24f, y + 10f, width - 36f, 16f);
        eyebrowText.characterSpacing = 2f;

        TextMeshProUGUI titleText = CreateText("FWUI_SectionTitle_" + Sanitize(eyebrow), chrome, title, 16f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(titleText.rectTransform, x + 24f, y + 26f, width - 36f, 24f);

        Image rule = CreateImage("FWUI_SectionRule_" + Sanitize(eyebrow), chrome, new Color(0.55f, 0.68f, 0.70f, 0.14f));
        SetTopLeft(rule.rectTransform, x + 12f, y + 52f, width - 24f, 1f);
    }

    private static void AddFlowArrow(RectTransform chrome, float x, float y)
    {
        TextMeshProUGUI arrow = CreateText("FWUI_FlowArrow_" + Mathf.RoundToInt(x), chrome, "→", 28f, Amber, FontStyles.Bold, TextAlignmentOptions.Center);
        SetTopLeft(arrow.rectTransform, x - 22f, y, 44f, 40f);
    }

    private static void PlaceProgress(Transform root, float x, float y, float width, float height)
    {
        RectTransform background = FindDirectRect(root, "Image_1");
        RectTransform progress = FindDirectRect(root, "Progress");
        if (background != null)
            SetTopLeft(background, x, y, width, height);
        if (progress != null)
            SetTopLeft(progress, x, y, width, height);
    }

    private static void PlaceActionButton(Transform root, string name, float width, float height, string caption)
    {
        RectTransform buttonRect = FindRect(root, name);
        if (buttonRect == null)
            return;
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.zero;
        buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(width - 252f, 12f);
        buttonRect.sizeDelta = new Vector2(228f, 46f);
        ConfigureActionButton(buttonRect.gameObject, caption, true);
    }

    private static void PlaceCloseButton(Transform root, float width)
    {
        RectTransform close = FindDirectRect(root, "关闭");
        if (close == null)
            return;

        close.anchorMin = new Vector2(1f, 1f);
        close.anchorMax = new Vector2(1f, 1f);
        close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-16f, -16f);
        close.sizeDelta = new Vector2(44f, 44f);
        ConfigureActionButton(close.gameObject, "×", false);
    }

    private static void PlaceBottomButton(Transform root, string name, float x, float y, float width, float height, string caption)
    {
        RectTransform rect = FindRect(root, name);
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        ConfigureActionButton(rect.gameObject, caption, name == "重生");
    }

    private static void ConfigureActionButton(GameObject gameObject, string caption, bool primary)
    {
        Image image = EnsureImage(gameObject);
        image.color = primary ? new Color(0.70f, 0.36f, 0.16f, 0.98f) : SurfaceRaised;
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, primary ? new Color(1f, 0.71f, 0.38f, 0.40f) : Border);

        Button button = gameObject.GetComponent<Button>();
        if (button != null)
        {
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? new Color(1.13f, 1.08f, 0.98f, 1f) : new Color(1.18f, 1.16f, 1.10f, 1f);
            colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
            colors.selectedColor = FlatWorldUITheme.Selection;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.fadeDuration = 0.10f;
            button.colors = colors;
            if (gameObject.GetComponent<FlatWorldUIFeedback>() == null)
                gameObject.AddComponent<FlatWorldUIFeedback>();
        }

        TMP_Text label = gameObject.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = caption;
            label.font = font;
            label.fontSize = primary ? 18f : 20f;
            label.fontStyle = FontStyles.Bold;
            label.color = Cream;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
        }
    }

    private static void ConfigureFloatingCard(Transform panel, float width, float height, string title, string eyebrow)
    {
        RectTransform rect = panel as RectTransform;
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);

        Image image = EnsureImage(panel.gameObject);
        image.color = Ink;
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.30f));
        BuildCardHeader(panel, title, eyebrow, width);
    }

    private static void BuildCardHeader(Transform panel, string title, string eyebrow, float width)
    {
        Transform previous = panel.Find("FWUI_Chrome");
        if (previous != null)
            UnityEngine.Object.DestroyImmediate(previous.gameObject);

        RectTransform chrome = CreateRect("FWUI_Chrome", panel);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image header = CreateImage("FWUI_CardHeader", chrome, SurfaceRaised);
        SetTopLeft(header.rectTransform, 0f, 0f, width, 76f);
        Image accent = CreateImage("FWUI_CardAccent", chrome, Amber);
        SetTopLeft(accent.rectTransform, 0f, 0f, 4f, 76f);

        TextMeshProUGUI eyebrowText = CreateText("FWUI_CardEyebrow", chrome, eyebrow, 10f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(eyebrowText.rectTransform, 18f, 12f, width - 36f, 18f);
        eyebrowText.characterSpacing = 2f;
        TextMeshProUGUI titleText = CreateText("FWUI_Card标题", chrome, title, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(titleText.rectTransform, 18f, 32f, width - 36f, 32f);
    }

    private static void AddCornerTicks(RectTransform chrome, float width, float height)
    {
        Image top = CreateImage("FWUI_TickTop", chrome, Amber);
        SetTopLeft(top.rectTransform, width - 92f, 0f, 68f, 2f);
        Image bottom = CreateImage("FWUI_TickBottom", chrome, Teal);
        bottom.rectTransform.anchorMin = Vector2.zero;
        bottom.rectTransform.anchorMax = Vector2.zero;
        bottom.rectTransform.pivot = Vector2.zero;
        bottom.rectTransform.anchoredPosition = new Vector2(24f, 0f);
        bottom.rectTransform.sizeDelta = new Vector2(48f, 2f);
    }

    private static void ArrangeSlots(RectTransform container, int expectedCount, int columns, float size, float spacing)
    {
        List<RectTransform> slots = new List<RectTransform>();
        for (int i = 0; i < container.childCount; i++)
        {
            RectTransform child = container.GetChild(i) as RectTransform;
            if (child != null && child.GetComponent<Button>() != null)
                slots.Add(child);
        }

        int count = Mathf.Min(expectedCount, slots.Count);
        for (int i = 0; i < count; i++)
        {
            int column = i % Mathf.Max(1, columns);
            int row = i / Mathf.Max(1, columns);
            SetTopLeft(slots[i], column * (size + spacing), row * (size + spacing), size, size);
        }
    }

    private static void PlaceRect(Transform root, string name, float x, float y, float width, float height)
    {
        RectTransform rect = FindRect(root, name);
        if (rect != null)
            SetTopLeft(rect, x, y, width, height);
    }

    private static void PlaceTopLeft(Transform root, string name, float x, float y, float width, float height)
    {
        RectTransform rect = FindRect(root, name);
        if (rect != null)
            SetTopLeft(rect, x, y, width, height);
    }

    private static void RemoveGeneratedArt(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child.name == "UITheme_Chrome" || child.name.StartsWith("FWUI_", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = descendants.Length - 1; i >= 0; i--)
        {
            Transform descendant = descendants[i];
            if (descendant == null || descendant == root)
                continue;
            if (descendant.name == "UITheme_Chrome" || descendant.name.StartsWith("FWUI_", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(descendant.gameObject);
        }
    }

    private static void NormalizeTypography(Transform root)
    {
        if (font == null)
            return;

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            text.font = font;
    }

    private static void NormalizeControls(Transform root)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            bool isItemSlot = button.GetComponent("ItemSlot_UI") != null ||
                              button.name.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              button.name.Contains("槽");
            if (isItemSlot)
                continue;

            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
            }

            if (button.GetComponent<FlatWorldUIFeedback>() == null)
                button.gameObject.AddComponent<FlatWorldUIFeedback>();
        }

        foreach (ScrollRect scrollRect in root.GetComponentsInChildren<ScrollRect>(true))
        {
            Image image = scrollRect.GetComponent<Image>();
            if (image == null)
                continue;
            image.sprite = null;
            image.type = Image.Type.Simple;
        }
    }

    private static RectTransform FindRect(Transform root, string name)
    {
        Transform found = FindTransform(root, name);
        return found as RectTransform;
    }

    private static RectTransform FindDirectRect(Transform root, string name)
    {
        Transform found = root.Find(name);
        return found as RectTransform;
    }

    private static Transform FindTransform(Transform root, string name)
    {
        if (root.name == name)
            return root;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in all)
        {
            if (candidate.name == name)
                return candidate;
        }
        return null;
    }

    private static Image EnsureImage(GameObject gameObject)
    {
        Image image = gameObject.GetComponent<Image>();
        if (image == null)
            image = gameObject.AddComponent<Image>();
        image.raycastTarget = true;
        return image;
    }

    private static void AddOutline(Graphic graphic, Color color)
    {
        Outline outline = graphic.GetComponent<Outline>();
        if (outline == null)
            outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetTopCenter(RectTransform rect, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static string Sanitize(string value)
    {
        return value.Replace(" ", string.Empty).Replace("/", "_").Replace("—", "_");
    }

    private static string BuildPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private static void SetUILayerRecursively(GameObject root)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
            uiLayer = 5;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = uiLayer;
    }
}
