using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家专属装备模块：挂在玩家子对象下，通过 GameController 的 InputAction 打开装备面板。
/// </summary>
public class Mod_Equipment_Player : Mod_Equipment
{
    #region 玩家输入配置

    [Header("玩家装备面板输入")]
    [Tooltip("用于开关装备面板的 InputAction 名称（来自 PlayerInputActions/Win10）")]
    public string OpenPanelActionName = "H";

    [Tooltip("同时打开合成、装备和背包面板的 InputAction 名称")]
    public string OpenPrimaryPanelsActionName = "Tab";

    [Range(0.5f, 1f)]
    [Tooltip("玩家组合面板的显示缩放比例")]
    public float PrimaryPanelScale = 0.75f;

    private InputAction openPanelAction;
    private Action<InputAction.CallbackContext> openPanelCallback;
    private InputAction openPrimaryPanelsAction;
    private Action<InputAction.CallbackContext> openPrimaryPanelsCallback;
    private GameController inputController;
    private readonly HashSet<BasePanel> compactedPanels = new();

    #endregion

    #region 触发绑定重载

    protected override void BindOpenPanelTrigger()
    {
        inputController = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (inputController == null || inputController._inputActions == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] GameController 或输入资产为空，无法绑定动作。物体: {name}");
            return;
        }

        if (string.IsNullOrEmpty(OpenPanelActionName))
        {
            Debug.LogError($"[Mod_Equipment_Player] OpenPanelActionName 为空。物体: {name}");
            return;
        }

        openPanelAction = inputController._inputActions.FindAction(OpenPanelActionName);
        if (openPanelAction == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] 找不到输入动作 '{OpenPanelActionName}'。物体: {name}");
            return;
        }

        openPanelCallback = context =>
        {
            if (inputController.IsGameplayInputAllowed(context))
                ToggleEquipmentPanelFromInput();
        };
        openPanelAction.performed += openPanelCallback;

        openPrimaryPanelsAction = inputController._inputActions.FindAction(OpenPrimaryPanelsActionName);
        if (openPrimaryPanelsAction == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] 找不到输入动作 '{OpenPrimaryPanelsActionName}'。物体: {name}");
            return;
        }

        openPrimaryPanelsCallback = context =>
        {
            if (inputController.IsGameplayInputAllowed(context))
                TogglePrimaryPanelsFromInput();
        };
        openPrimaryPanelsAction.performed += openPrimaryPanelsCallback;
    }

    protected override void UnbindOpenPanelTrigger()
    {
        if (openPanelAction != null && openPanelCallback != null)
            openPanelAction.performed -= openPanelCallback;

        if (openPrimaryPanelsAction != null && openPrimaryPanelsCallback != null)
            openPrimaryPanelsAction.performed -= openPrimaryPanelsCallback;

        openPanelAction = null;
        openPanelCallback = null;
        openPrimaryPanelsAction = null;
        openPrimaryPanelsCallback = null;
        inputController = null;
        compactedPanels.Clear();
    }

    #endregion

    #region 面板逻辑

    private void ToggleEquipmentPanelFromInput()
    {
        if (inputController != null && inputController.IsGameplayInputLocked &&
            (EquipmentInventory?.basePanel == null || !EquipmentInventory.basePanel.IsOpen()))
        {
            return;
        }

        if (EquipmentInventory == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] EquipmentInventory 为空，无法开关面板。物体: {name}");
            return;
        }

        bool createdNow = EquipmentInventory.EnsurePanelCreated();
        if (EquipmentInventory.basePanel == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] 装备面板创建失败。物体: {name}");
            return;
        }

        // 同步默认交互目标到玩家手部背包
        var handInv = item.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInv != null)
            EquipmentInventory.DefaultTarget_Inventory = handInv;

        if (createdNow)
        {
            EquipmentInventory.basePanel.Open();
            return;
        }

        EquipmentInventory.basePanel.Toggle();
    }

    /// <summary>统一切换玩家的合成、装备、背包三块主要面板。</summary>
    private void TogglePrimaryPanelsFromInput()
    {
        if (inputController != null && inputController.IsGameplayInputLocked && !HasAnyPrimaryPanelOpen())
            return;

        Mod_HandCraftTable craftTable = ResolveCraftTable();
        Mod_Inventory bagModule = ResolveBagModule();
        if (craftTable == null || bagModule == null || EquipmentInventory == null)
        {
            Debug.LogWarning("[Mod_Equipment_Player] 面板组缺少合成、装备或背包模块，无法统一打开");
            return;
        }

        if (ArePrimaryPanelsOpen())
        {
            craftTable.I_ClosePanel();
            I_ClosePanel();
            bagModule.I_ClosePanel();
            return;
        }

        craftTable.I_ShowPanel();
        ShowEquipmentPanel();
        bagModule.I_ShowPanel();

        CompactPanel(craftTable.basePanel);
        CompactPanel(EquipmentInventory.basePanel);
        CompactPanel(bagModule.inventory?.basePanel);
    }

    private bool ArePrimaryPanelsOpen()
    {
        Mod_HandCraftTable craftTable = ResolveCraftTable();
        Mod_Inventory bagModule = ResolveBagModule();
        return craftTable?.basePanel?.IsOpen() == true &&
               EquipmentInventory?.basePanel?.IsOpen() == true &&
               bagModule?.inventory?.basePanel?.IsOpen() == true;
    }

    private bool HasAnyPrimaryPanelOpen()
    {
        Mod_HandCraftTable craftTable = ResolveCraftTable();
        Mod_Inventory bagModule = ResolveBagModule();
        return craftTable?.basePanel?.IsOpen() == true ||
               EquipmentInventory?.basePanel?.IsOpen() == true ||
               bagModule?.inventory?.basePanel?.IsOpen() == true;
    }

    private void ShowEquipmentPanel()
    {
        EquipmentInventory.EnsurePanelCreated();
        if (EquipmentInventory.basePanel == null)
            return;

        Inventory handInventory = item?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInventory != null)
            EquipmentInventory.DefaultTarget_Inventory = handInventory;

        EquipmentInventory.basePanel.Open();
    }

    private void CompactPanel(BasePanel panel)
    {
        if (panel == null || !compactedPanels.Add(panel))
            return;

        panel.transform.localScale *= Mathf.Clamp(PrimaryPanelScale, 0.5f, 1f);
    }

    private Mod_HandCraftTable ResolveCraftTable()
    {
        Mod_HandCraftTable[] modules = item?.GetComponentsInChildren<Mod_HandCraftTable>(true);
        return modules != null && modules.Length > 0 ? modules[0] : null;
    }

    private Mod_Inventory ResolveBagModule()
    {
        Mod_Inventory bagModule = item?.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bagModule != null)
            return bagModule;

        Mod_Inventory[] modules = item?.GetComponentsInChildren<Mod_Inventory>(true);
        if (modules == null)
            return null;

        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i]?.inventory?.Data?.ToggleActionName == "B")
                return modules[i];
        }

        return null;
    }

    #endregion
}
