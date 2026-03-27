using System;
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

    private InputAction openPanelAction;
    private Action<InputAction.CallbackContext> openPanelCallback;

    #endregion

    #region 触发绑定重载

    protected override void BindOpenPanelTrigger()
    {
        var controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null || controller._inputActions == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] GameController 或输入资产为空，无法绑定动作。物体: {name}");
            return;
        }

        if (string.IsNullOrEmpty(OpenPanelActionName))
        {
            Debug.LogError($"[Mod_Equipment_Player] OpenPanelActionName 为空。物体: {name}");
            return;
        }

        openPanelAction = controller._inputActions.FindAction(OpenPanelActionName);
        if (openPanelAction == null)
        {
            Debug.LogError($"[Mod_Equipment_Player] 找不到输入动作 '{OpenPanelActionName}'。物体: {name}");
            return;
        }

        openPanelCallback = _ => ToggleEquipmentPanelFromInput();
        openPanelAction.performed += openPanelCallback;
    }

    protected override void UnbindOpenPanelTrigger()
    {
        if (openPanelAction != null && openPanelCallback != null)
            openPanelAction.performed -= openPanelCallback;

        openPanelAction = null;
        openPanelCallback = null;
    }

    #endregion

    #region 面板逻辑

    private void ToggleEquipmentPanelFromInput()
    {
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

    #endregion
}
