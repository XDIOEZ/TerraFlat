// AI-Context: 游戏内 UI 的结构级重建器；保留双脚本 UI 的全部业务节点名，只创建 FWUI_ 前缀的美术节点并调整 RectTransform。

using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

/// <summary>
/// 将旧的“功能先行”UI 重构为和主界面一致的生存档案风格。
/// 所有业务 Button/TMP/Slider/Image 名称保持不变，确保 BasePanel 的按名绑定契约稳定。
/// </summary>
public static partial class GameUIPrefabRebuilder
{
    #region 路径与共享配置

    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";
    private const string SelectBoxSpritePath = "Assets/6_Art/UI/Inventory_UI/UI_HotBar_SelectionFrame.png";
    private const string PrefabRoot = "Assets/2_Prefabs/2-1_UI/";
    private const string CommonControlsRoot = PrefabRoot + "Common/Controls/";
    private const string MainMenuCoreRoot = PrefabRoot + "MainMenu/Core/";
    private const string MainMenuSaveRoot = PrefabRoot + "MainMenu/Save/";
    private const string CraftingRoot = PrefabRoot + "Gameplay/Crafting/";
    private const string HudRoot = PrefabRoot + "Gameplay/HUD/";
    private const string InventoryComponentsRoot = PrefabRoot + "Gameplay/Inventory/Components/";
    private const string InventoryPanelsRoot = PrefabRoot + "Gameplay/Inventory/Panels/";
    private const string ScreensRoot = PrefabRoot + "Gameplay/Screens/";
    private const string PlayerStatusRoot = PrefabRoot + "Gameplay/Status/Player/";
    private const string CombatStatusRoot = PrefabRoot + "Gameplay/Status/Combat/";
    private const string BagPrefabPath = InventoryPanelsRoot + "UI_Bag.prefab";
    private const string SlotPrefabPath = InventoryComponentsRoot + "UI_Slot.prefab";

    private static readonly string[] CraftingPreviewPrefabPaths =
    {
        SlotPrefabPath,
        CraftingRoot + "UI_HandCraftTable.prefab",
        CraftingRoot + "UI_MakerTable.prefab",
        CraftingRoot + "UI_Anvil.prefab",
        CraftingRoot + "UI_FireDrill.prefab",
        CraftingRoot + "UI_FlintStrike.prefab"
    };

    private static Color Ink => FlatWorldUITheme.Canvas;
    private static Color InkSoft => FlatWorldUITheme.SurfaceLow;
    private static Color Surface => FlatWorldUITheme.Surface;
    private static Color SurfaceRaised => FlatWorldUITheme.SurfaceRaised;
    private static Color Cream => FlatWorldUITheme.TextPrimary;
    private static Color Muted => FlatWorldUITheme.TextSecondary;
    private static Color Amber => FlatWorldUITheme.Accent;
    private static Color Teal => FlatWorldUITheme.Teal;
    private static Color Border => FlatWorldUITheme.Border;

    // 行囊保留原布局，但视觉跟随全局灰阶主题，不再使用独立彩色槽位皮肤。
    private static Color ModularBagOuter => FlatWorldUITheme.Canvas;
    private static Color ModularBagSurface => FlatWorldUITheme.Surface;
    private static Color ModularBagField => FlatWorldUITheme.SurfaceLow;
    private static Color ModularBagLine => FlatWorldUITheme.Border;
    private static Color ModularBagText => FlatWorldUITheme.TextPrimary;

    private static TMP_FontAsset font;

    private static readonly Dictionary<string, string[]> BindingContracts = new Dictionary<string, string[]>
    {
        { BagPrefabPath, new[] { "Scroll View", "Content", "关闭" } },
        { InventoryPanelsRoot + "UI_Equipment.prefab", new[] { "UI_Content", "关闭" } },
        { CraftingRoot + "UI_HandCraftTable.prefab", new[] { "输入_1", "输入_4", "输出_1", "输出_2", CraftingStationController.CandidateContentName, CraftingStationController.CandidateTemplateName, CraftingStationController.CandidateOutputAmountName, CraftingStationController.CandidateMaterialAmountName, "合成按钮", "关闭" } },
        { CraftingRoot + "UI_MakerTable.prefab", new[] { "输入_1", "输入_6", "输出_1", "输出_2", CraftingStationController.CandidateContentName, CraftingStationController.CandidateTemplateName, CraftingStationController.CandidateOutputAmountName, CraftingStationController.CandidateMaterialAmountName, "合成按钮", "关闭" } },
        { CraftingRoot + "UI_Anvil.prefab", new[] { "工具_1", "输入_1", "输出_1", CraftingStationController.CandidateContentName, CraftingStationController.CandidateTemplateName, "拆回建筑按钮", "合成按钮", "关闭" } },
        { CraftingRoot + "UI_Furnace.prefab", new[] { "输入_1", "输入_2", "输入_3", "输出_1", "燃料_1", "燃料显示条", "FWUI_FurnaceTemperatureValue", "FWUI_FurnaceInputCapacity", "FWUI_InductionTargetInput", "FWUI_ApplyInductionTarget", "合成按钮", "关闭" } },
        { CraftingRoot + "UI_Bonfire.prefab", new[] { "输入_1", "输出_1", "燃料_1", "燃料显示条", "合成按钮", "关闭" } },
        { CraftingRoot + "UI_FireDrill.prefab", new[] { "输入_1", "输出_1", "合成按钮", "关闭", "Progress" } },
        { CraftingRoot + "UI_FlintStrike.prefab", new[] { "输入_1", "输出_1", "合成按钮", "关闭", "Progress" } },
        { ScreensRoot + "UI_Death.prefab", new[] { "重生", "回到主菜单" } },
        { PlayerStatusRoot + "UI_ModuleList.prefab", new[] { "Dropdown", "Template", "Content" } },
        { MainMenuCoreRoot + "UI_ActionList.prefab", new[]
            {
                "Scroll View",
                UIText.SaveButton,
                UIText.ReturnToMainMenuButton,
                UIText.ReturnToDesktopButton,
                SettingsExitConfirmationController.LayerName,
                SettingsExitConfirmationController.CancelButtonName,
                SettingsExitConfirmationController.ConfirmButtonName,
                "关闭"
            }
        },
        { PlayerStatusRoot + "UI_ModuleSettings.prefab", new[] { "Panel", "Slider", "关闭页面" } },
        { PlayerStatusRoot + "UI_Health.prefab", new[] { "血量模块_世界面板", "背景", "血量" } },
        { CombatStatusRoot + "UI_DamageReceiverStatus.prefab", new[] { "整体血量", "部位耐久列表", "部位耐久模板" } },
        { PlayerStatusRoot + "UI_Food.prefab", new[] { "血量", "碳水", "脂肪", "蛋白质", "水", "维生素", "体温", "DataText_血量", "DataText_体温" } },
        { PlayerStatusRoot + "UI_Sleep.prefab", new[] { "ZZZs" } }
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

    #endregion

    #region 重建入口

    [MenuItem("FlatWorld/其他工具/UI/重构全部游戏内UI")]
    public static void RebuildAllGameUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        List<BuildTarget> targets = new List<BuildTarget>
        {
            new BuildTarget(BagPrefabPath, BuildModularInventoryBagPreview),
            new BuildTarget(InventoryPanelsRoot + "UI_Equipment.prefab", BuildEquipmentWindow),
            new BuildTarget(CraftingRoot + "UI_CompostBin.prefab", root => BuildScrollWindow(root, 646f, 468f, "堆肥箱", string.Empty, string.Empty, 5, new Vector2(88f, 88f))),
            new BuildTarget(CraftingRoot + "UI_MeatRack.prefab", root => BuildScrollWindow(root, 646f, 468f, "晾肉架", string.Empty, string.Empty, 5, new Vector2(88f, 88f))),
            new BuildTarget(CraftingRoot + "UI_HandCraftTable.prefab", root => BuildCraftingSelectionWindow(root, "手工制作", string.Empty, 4)),
            new BuildTarget(CraftingRoot + "UI_FireDrill.prefab", root => BuildCraftWindow(root, "钻木取火", string.Empty, 1, true)),
            new BuildTarget(CraftingRoot + "UI_FlintStrike.prefab", root => BuildCraftWindow(root, "燧石取火", string.Empty, 1, true)),
            new BuildTarget(CraftingRoot + "UI_MakerTable.prefab", BuildMakerTable),
            new BuildTarget(CraftingRoot + "UI_Anvil.prefab", BuildAnvil),
            new BuildTarget(CraftingRoot + "UI_Furnace.prefab", root => BuildFurnace(root, false)),
            new BuildTarget(CraftingRoot + "UI_Bonfire.prefab", root => BuildFurnace(root, true)),
            new BuildTarget(ScreensRoot + "UI_Death.prefab", BuildDeathScreen),
            new BuildTarget(HudRoot + "UI_HotBar.prefab", BuildHotbar),
            new BuildTarget(InventoryPanelsRoot + "UI_Hand.prefab", BuildHandSlot),
            new BuildTarget(PlayerStatusRoot + "UI_ModuleList.prefab", BuildGameModuleSelector),
            new BuildTarget(MainMenuCoreRoot + "UI_ActionList.prefab", BuildActionList),
            new BuildTarget(MainMenuSaveRoot + "UI_SaveContextMenu.prefab", BuildSaveContextMenu),
            new BuildTarget(InventoryComponentsRoot + "UI_HandSlot.prefab", BuildHandSlotComponent),
            new BuildTarget(PlayerStatusRoot + "UI_ModuleOpenButton.prefab", BuildBaseButton),
            new BuildTarget(PlayerStatusRoot + "UI_ModuleButton.prefab", BuildBaseButton),
            new BuildTarget(PlayerStatusRoot + "UI_ModuleSettings.prefab", BuildSettingsPanel),
            new BuildTarget(PlayerStatusRoot + "UI_Health.prefab", BuildHealthWorldPanel),
            new BuildTarget(CombatStatusRoot + "UI_DamageReceiverStatus.prefab", BuildDamageReceiverStatusPanel),
            new BuildTarget(PlayerStatusRoot + "UI_Food.prefab", BuildNutritionHud),
            new BuildTarget(PlayerStatusRoot + "UI_Sleep.prefab", BuildSleepHud),
            new BuildTarget(InventoryComponentsRoot + "UI_Slot.prefab", BuildSlot),
            new BuildTarget(CommonControlsRoot + "UI_Slider.prefab", BuildBaseSlider),
            new BuildTarget(CommonControlsRoot + "UI_SelectBox.prefab", BuildBaseSelectBox)
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
                    NormalizeCanvasLayers(root.transform);
                    NormalizeTypography(root.transform);
                    NormalizeControls(root.transform);
                    FlatWorldUITheme.Apply(root.transform);
                    if (string.Equals(target.Path, BagPrefabPath, StringComparison.Ordinal))
                        FinalizeModularInventoryBag(root);
                    if (string.Equals(target.Path, PlayerStatusRoot + "UI_Food.prefab", StringComparison.Ordinal))
                        FinalizeNutritionHud(root);
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

    /// <summary>只重构玩家行囊的灰阶简约外观，不影响通用槽位和其他游戏内 UI。</summary>
    [MenuItem("FlatWorld/其他工具/UI/重构灰阶简约行囊")]
    public static void RebuildModularInventoryBag()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException($"缺少统一字体：{FontPath}");

        GameObject root = PrefabUtility.LoadPrefabContents(BagPrefabPath);
        try
        {
            RemoveGeneratedArt(root.transform);
            BuildModularInventoryBagPreview(root);
            SetUILayerRecursively(root);
            NormalizeCanvasLayers(root.transform);
            NormalizeTypography(root.transform);
            NormalizeControls(root.transform);
            FlatWorldUITheme.Apply(root.transform);
            FinalizeModularInventoryBag(root);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, BagPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Game UI] 已按灰阶简约主题重构玩家行囊；动态槽位回归通用 UI_Slot。");
    }

    /// <summary>只向现有 UI_Bag Footer 增补重量/体积显示，不重排其它行囊结构。</summary>
    [MenuItem("FlatWorld/其他工具/UI/同步行囊携带容量显示")]
    public static void SyncBagCarryCapacityFooter()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException($"缺少统一字体：{FontPath}");

        GameObject root = PrefabUtility.LoadPrefabContents(BagPrefabPath);
        try
        {
            EnsureBagCarryCapacityFooter(root.transform);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, BagPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Game UI] 已同步 UI_Bag 的重量/体积携带容量显示。");
    }

    /// <summary>仅重构通用物品槽位，便于独立调整背包/装备等库存界面的槽位视觉。</summary>
    [MenuItem("FlatWorld/其他工具/UI/重构基础物品槽位")]
    public static void RebuildBaseInventorySlot()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException($"缺少统一字体：{FontPath}");

        GameObject root = PrefabUtility.LoadPrefabContents(SlotPrefabPath);
        try
        {
            BuildSlot(root);
            SetUILayerRecursively(root);
            NormalizeTypography(root.transform);
            NormalizeControls(root.transform);
            FlatWorldUITheme.Apply(root.transform);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, SlotPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Game UI] 已按灰阶简约风格重构基础物品槽位。");
    }

    /// <summary>仅更新快捷栏名称提示，保留十格槽位和现有快捷栏样式。</summary>
    [MenuItem("FlatWorld/其他工具/UI/重建手持物名称提示")]
    public static void RebuildHotbarItemNameHUD()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new InvalidOperationException($"缺少统一字体：{FontPath}");

        string path = HudRoot + "UI_HotBar.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            BuildHotbarItemName(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log("[Game UI] 已更新快捷栏上方的手持物名称提示。");
    }

    [MenuItem("FlatWorld/其他工具/UI/修复快捷栏UI")]
    public static void RebuildHotbarUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        BuildTarget[] targets =
        {
            new BuildTarget(HudRoot + "UI_HotBar.prefab", BuildHotbar),
            new BuildTarget(CommonControlsRoot + "UI_SelectBox.prefab", BuildBaseSelectBox)
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

    [MenuItem("FlatWorld/其他工具/UI/修复制作预览图层")]
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
                    NormalizeCanvasLayers(root.transform);
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

    [MenuItem("FlatWorld/其他工具/UI/验证制作预览图层")]
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

    [MenuItem("FlatWorld/其他工具/UI/居中功能列表按钮")]
    public static void RebuildActionListUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        BuildTarget target = new BuildTarget(MainMenuCoreRoot + "UI_ActionList.prefab", BuildActionList);
        bool rebuilt = RebuildSinglePrefab(target);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(rebuilt
            ? "[Game UI] 功能列表按钮已居中。"
            : "[Game UI] 功能列表按钮重建失败，请检查控制台。");
    }

    [MenuItem("FlatWorld/其他工具/UI/同步工作台工具柜共享槽")]
    public static void RebuildWorkbenchSharedInputUI()
    {
        const string path = CraftingRoot + "UI_MakerTable.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // 只补第六格并排好材料槽，保留现有界面的视觉和绑定身份。
            EnsureNamedSlot(root.transform, "输入_1", "输入_6");
            LayoutCraftingSlots(root.transform, "输入_", 6, 20f, 168f, 376f, 112f, 3);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("FlatWorld/其他工具/UI/重构合成候选界面")]
    public static void RebuildCraftingSelectionUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        BuildTarget[] targets =
        {
            new BuildTarget(
                CraftingRoot + "UI_HandCraftTable.prefab",
                root => BuildCraftingSelectionWindow(root, "手工制作", string.Empty, 4)),
            new BuildTarget(CraftingRoot + "UI_MakerTable.prefab", BuildMakerTable),
            new BuildTarget(CraftingRoot + "UI_Anvil.prefab", BuildAnvil)
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
        Debug.Log($"[Game UI] 合成候选界面重建完成：{rebuilt}/{targets.Length} 个 Prefab。");
        ValidateRebuiltUI();
    }

    [MenuItem("FlatWorld/其他工具/UI/重建矩形进度条UI")]
    public static void RebuildRectangularProgressBars()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        BuildTarget[] targets =
        {
            new BuildTarget(CommonControlsRoot + "UI_Slider.prefab", BuildBaseSlider),
            new BuildTarget(PlayerStatusRoot + "UI_Food.prefab", BuildNutritionHud),
            new BuildTarget(CraftingRoot + "UI_Furnace.prefab", root => BuildFurnace(root, false)),
            new BuildTarget(CraftingRoot + "UI_Bonfire.prefab", root => BuildFurnace(root, true)),
            new BuildTarget(PlayerStatusRoot + "UI_ModuleSettings.prefab", BuildSettingsPanel)
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

    /// <summary>只重建熔炉面板，便于校正其专用布局而不触碰其它游戏内 UI。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Apply Furnace Layout")]
    public static void RebuildFurnaceUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        BuildTarget target = new BuildTarget(CraftingRoot + "UI_Furnace.prefab", root => BuildFurnace(root, false));
        bool rebuilt = RebuildSinglePrefab(target);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(rebuilt
            ? "[Game UI] 熔炉面板重建完成。"
            : "[Game UI] 熔炉面板重建失败，请检查控制台。");
    }

    [MenuItem("FlatWorld/其他工具/UI/重建角色参数面板")]
    public static void RebuildCharacterStatusPanel()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Game UI", out font))
            return;

        BuildTarget target = new BuildTarget(PlayerStatusRoot + "UI_Food.prefab", BuildNutritionHud);
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
            NormalizeCanvasLayers(root.transform);
            NormalizeTypography(root.transform);
            NormalizeControls(root.transform);
            FlatWorldUITheme.Apply(root.transform);
            if (string.Equals(target.Path, PlayerStatusRoot + "UI_Food.prefab", StringComparison.Ordinal))
                FinalizeNutritionHud(root);
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

    [MenuItem("FlatWorld/其他工具/UI/验证重构UI绑定契约")]
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

    /// <summary>
    /// 构建灰阶简约行囊版式。
    /// 使用 1004×560 窗口和十列滚动布局，只清理旧彩色装饰并统一内容区层级。
    /// </summary>
    private static void BuildModularInventoryBagPreview(GameObject root)
    {
        const float width = 1004f;
        const float height = 560f;

        RectTransform chrome = PrepareWindow(root, width, height, "行囊", string.Empty, string.Empty, true);
        RectTransform rootRect = (RectTransform)root.transform;
        SafeAreaScaleGroup scale = root.GetComponent<SafeAreaScaleGroup>() ?? root.AddComponent<SafeAreaScaleGroup>();
        scale.Configure(rootRect, new[] { rootRect }, Vector2.zero);
        UIDragResizer resizer = root.GetComponent<UIDragResizer>();
        if (resizer != null)
        {
            resizer.minWidth = width;
            resizer.minHeight = 288f;
        }

        // 背包只在安全区不足时缩小，拖边保留十列槽位与至少一行正文的空间。
        foreach (string headerName in new[] { "FWUI_Header", "FWUI_HeaderRule" })
        {
            RectTransform header = FindRect(chrome, headerName);
            header.anchorMax = new Vector2(1f, 1f);
            header.sizeDelta = new Vector2(0f, header.sizeDelta.y);
        }

        // 简约主题不保留额外图标、眉题和装饰性英文。
        DestroyGeneratedElement(chrome, "FWUI_眉题");
        DestroyGeneratedElement(chrome, "FWUI_FooterHint");
        DestroyGeneratedElement(chrome, "FWUI_TickTop");
        DestroyGeneratedElement(chrome, "FWUI_TickBottom");
        DestroyGeneratedElement(chrome, "FWUI_BagIconPlate");
        DestroyGeneratedElement(chrome, "FWUI_BagIcon");

        RectTransform title = FindRect(chrome, "FWUI_标题");
        if (title != null)
        {
            SetTopLeft(title, 26f, 18f, 168f, 42f);
            TMP_Text titleText = title.GetComponent<TMP_Text>();
            if (titleText != null)
            {
                titleText.fontSize = 30f;
                titleText.color = ModularBagText;
            }
        }

        // 内容区使用低对比灰底，槽位只保留轻微边界。
        RectTransform innerField = FindRect(chrome, "FWUI_InnerField");
        if (innerField != null)
        {
            innerField.anchorMin = Vector2.zero;
            innerField.anchorMax = Vector2.one;
            innerField.offsetMin = new Vector2(24f, 64f);
            innerField.offsetMax = new Vector2(-24f, -92f);
            Image fieldImage = innerField.GetComponent<Image>();
            if (fieldImage != null)
            {
                fieldImage.sprite = null;
                fieldImage.type = Image.Type.Simple;
                fieldImage.color = ModularBagField;
                fieldImage.raycastTarget = false;
            }
            innerField.SetSiblingIndex(Mathf.Min(3, innerField.parent.childCount - 1));
        }

        RectTransform scroll = FindRect(root.transform, "Scroll View");
        if (scroll != null)
        {
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(48f, 80f);
            scroll.offsetMax = new Vector2(-48f, -112f);
            Image scrollImage = scroll.GetComponent<Image>();
            if (scrollImage != null)
            {
                scrollImage.sprite = null;
                scrollImage.color = Color.clear;
                scrollImage.raycastTarget = false;
            }
        }

        GridLayoutGroup grid = root.GetComponentInChildren<GridLayoutGroup>(true);
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;
            grid.cellSize = new Vector2(80f, 80f);
            grid.spacing = new Vector2(4f, 4f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.childAlignment = TextAnchor.UpperLeft;
        }

        ScrollRect scrollRect = root.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null)
        {
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 34f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalScrollbar = root.GetComponentInChildren<Scrollbar>(true);
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scrollRect.verticalScrollbar.gameObject.SetActive(true);
            ConfigureInventoryScrollLayout(scrollRect);
        }

        EnsureBagCarryCapacityFooter(root.transform);

        RectTransform sortButton = FindDirectRect(root.transform, "整理");
        if (sortButton != null)
        {
            sortButton.anchorMin = new Vector2(1f, 0f);
            sortButton.anchorMax = new Vector2(1f, 0f);
            sortButton.pivot = new Vector2(1f, 0f);
            sortButton.anchoredPosition = new Vector2(-22f, 8f);
            sortButton.sizeDelta = new Vector2(148f, 50f);
            TMP_Text label = sortButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "整理";
                label.fontSize = 17f;
                label.color = Surface;
            }
        }

        RectTransform closeButton = FindDirectRect(root.transform, "关闭");
        if (closeButton != null)
        {
            closeButton.anchorMin = Vector2.one;
            closeButton.anchorMax = Vector2.one;
            closeButton.pivot = Vector2.one;
            closeButton.anchoredPosition = new Vector2(-16f, -13f);
            closeButton.sizeDelta = new Vector2(52f, 52f);
            TMP_Text label = closeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "×";
                label.fontSize = 22f;
                label.color = Surface;
            }
        }
    }

    #endregion
}
