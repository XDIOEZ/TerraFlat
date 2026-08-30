using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Mod_HandCraftTable : Module, IInventory, IInstanceUI
{
#region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

#endregion

#region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();
    [Tooltip("手工制作输入容器（输入_1~输入_2）")]
    public Inventory inputInventory;
    [Tooltip("手工制作输出容器（输出_1~输出_2）")]
    public Inventory outputInventory;
    public BasePanel basePanel;
    public GameObject InventoryPanel_Prefab;
    [Tooltip("手工合成台UI预制体名，Inspector未手动拖拽时会按此名称从GameRes回填")]
    public string InventoryPanelPrefabName = "UI_HandCraftTable";

    [Header("交互组件")]
    [Tooltip("合成按钮")]
    public Button workButton;
    [Tooltip("打开/关闭手工合成台的 InputAction 名称")]
    public string ToggleActionName = "H";
    [Tooltip("工作台等级，等级越高需要点击次数越少")]
    public int workbenchLevel = 1;
    [Tooltip("1级工作台每次合成需要的基础点击次数")]
    public int baseClickCount = 6;
    [Tooltip("每升1级减少的点击次数")]
    public int clickReductionPerLevel = 1;
    [Tooltip("每次合成最少需要点击次数")]
    public int minClickCount = 1;

    private CraftingStationController _craftingController;
    private GameController _inputController;
    private InputAction _toggleAction;
    private Action<InputAction.CallbackContext> _toggleCallback;
    private static readonly CraftingCapabilities Capabilities = new CraftingCapabilities
    {
        RecipeType = RecipeType.Crafting,
        InputSlotLimit = InputSlotCount,
        AllowOutputIntoInput = false
    };

    private int RequiredClickCount => Mathf.Max(minClickCount, baseClickCount - (Mathf.Max(1, workbenchLevel) - 1) * clickReductionPerLevel);

    private const int InputSlotCount = 2;
    private const int OutputSlotCount = 2;
    private const string InputInventorySaveKey = "handcraft.input";
    private const string OutputInventorySaveKey = "handcraft.output";
    [Header("调试")]
    [Tooltip("是否输出手工合成详细调试日志")]
    public bool EnableCraftDebug = true;

#endregion

#region 生命周期

    public void OnValidate()
    {
        _Data.Name = $"{ModText.WorkBench}_手工";
    }

    public override void Load()
    {
        RestoreInventoryState();
        InitData();
        BindToggleInput();
    }

    public override void Save()
    {
        SaveInventoryState();
    }

    private void BindToggleInput()
    {
        _inputController = item?.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
        _inputController ??= item != null ? item.GetComponent<GameController>() : null;
        if (_inputController == null || _inputController._inputActions == null)
            return;

        _toggleAction = _inputController._inputActions.FindAction(ToggleActionName);
        if (_toggleAction == null)
        {
            Debug.LogError($"[Mod_HandCraftTable] 找不到输入动作 '{ToggleActionName}'。", this);
            return;
        }

        _toggleCallback = context =>
        {
            if (!_inputController.IsGameplayInputAllowed(context))
                return;

            if (_inputController.IsGameplayInputLocked &&
                (basePanel == null || !basePanel.IsOpen()) &&
                !CanToggleFromMobileMenu())
            {
                return;
            }

            TogglePanelByKey();
        };
        _toggleAction.performed += _toggleCallback;
    }

    /// <summary>手机菜单抽屉内的制作按钮允许在背包面板打开时切换制作面板。</summary>
    private bool CanToggleFromMobileMenu()
    {
        return _inputController != null &&
               _inputController.IsUsingMobile &&
               PlayerMobileControlsHUD.IsActiveDrawerOpen;
    }

#endregion

#region 库存存档

    /// <summary>读取制作面板输入/输出槽位；没有新格式数据时保留预制体初始库存。</summary>
    private void RestoreInventoryState()
    {
        Inventory_ModuleData savedData = InventoryModuleDataPersistence.TryRead(ModSaveData);
        InventoryModuleDataPersistence.TryRestore(inputInventory, savedData, InputInventorySaveKey);
        InventoryModuleDataPersistence.TryRestore(outputInventory, savedData, OutputInventorySaveKey);
    }

    /// <summary>保存制作面板输入/输出槽位，避免关闭世界后只保存了模块空壳。</summary>
    private void SaveInventoryState()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        Inventory_ModuleData savedData = new Inventory_ModuleData
        {
            Name = ModSaveData.Name,
            ID = ModSaveData.ID
        };
        InventoryModuleDataPersistence.Capture(savedData, InputInventorySaveKey, inputInventory);
        InventoryModuleDataPersistence.Capture(savedData, OutputInventorySaveKey, outputInventory);
        InventoryModuleDataPersistence.Write(ModSaveData, savedData);
    }

#endregion

#region UI与交互

    private void TogglePanelByKey()
    {
        EnsurePanelCreated();
        if (basePanel == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 面板为空，无法切换显示");
            return;
        }

        if (basePanel.IsOpen())
        {
            basePanel.Close();
            inputInventory.SyncQuickTransferTarget(basePanel);
            inputInventory.DefaultTarget_Inventory = null;
            outputInventory.DefaultTarget_Inventory = null;
            return;
        }

        var handInv = GetPlayerHandInventory();
        if (handInv == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 玩家手部容器为空，无法打开手工合成台");
            return;
        }

        inputInventory.DefaultTarget_Inventory = handInv;
        outputInventory.DefaultTarget_Inventory = handInv;
        basePanel.Open();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    public bool EnsurePanelCreated()
    {
        if (basePanel != null)
            return false;

        EnsureInventoryPanelPrefabAssigned();
        if (InventoryPanel_Prefab == null)
        {
            Debug.LogError($"[Mod_HandCraftTable] InventoryPanel_Prefab 未设置，且无法通过 {InventoryPanelPrefabName} 回填面板预制体");
            return false;
        }

        basePanel = UIManager.Instance.CreatePanelFromGameObject(InventoryPanel_Prefab).GetComponentInChildren<BasePanel>();
        if (basePanel == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 创建面板失败，未找到 BasePanel");
            return false;
        }

        if (basePanel.TryGetText("窗口信息", out TextMeshProUGUI titleText))
            titleText.text = _Data.Name;

        RectTransform panelRect = basePanel.Dragger != null
            ? basePanel.Dragger.rectTransform
            : basePanel.rectTransform;
        InventoryPanelLayout.ApplyDefaultCraftingPosition(panelRect);

        InitUI();
        basePanel.PrepareForGamepadNavigation();
        basePanel.Opened += AcquirePanelInputLock;
        basePanel.Closed += ReleasePanelInputLock;
        basePanel.Close();
        return true;
    }

    private void AcquirePanelInputLock()
    {
        _inputController?.AcquireGameplayInputLock(this);
    }

    private void ReleasePanelInputLock()
    {
        _inputController?.ReleaseGameplayInputLock(this);
    }

    private void OnDestroy()
    {
        Unload();
    }

    public override void Unload()
    {
        _craftingController?.Dispose();
        _craftingController = null;
        inputInventory?.UnbindSlotDataEvents();
        outputInventory?.UnbindSlotDataEvents();

        if (_toggleAction != null && _toggleCallback != null)
            _toggleAction.performed -= _toggleCallback;

        if (basePanel != null)
        {
            basePanel.Opened -= AcquirePanelInputLock;
            basePanel.Closed -= ReleasePanelInputLock;
        }

        ReleasePanelInputLock();
    }

    private void EnsureInventoryPanelPrefabAssigned()
    {
        if (InventoryPanel_Prefab != null)
            return;

        if (GameRes.Instance == null)
            return;

        if (!string.IsNullOrWhiteSpace(InventoryPanelPrefabName))
            InventoryPanel_Prefab = GameRes.Instance.GetPrefab(InventoryPanelPrefabName);
    }

    public void InitData()
    {
        ValidateInventoryConfig();
        InitializeInventoryData(inputInventory, nameof(inputInventory));
        InitializeInventoryData(outputInventory, nameof(outputInventory));
    }

    public void InitUI()
    {
        BindInputSlots();
        BindOutputSlots();

        inputInventory.SyncData();
        outputInventory.SyncData();

        workButton = basePanel.GetButton("合成按钮");
        if (workButton == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 未找到合成按钮");
            return;
        }

        _craftingController?.Dispose();
        _craftingController = new CraftingStationController(
            basePanel,
            inputInventory,
            outputInventory,
            Capabilities,
            () => RequiredClickCount,
            ResolveCraftActor,
            LogCraftDebug);
        LogCraftDebug(
            $"线路绑定完成：输入槽={inputInventory.Data.itemSlots.Count}，" +
            $"输出槽={outputInventory.Data.itemSlots.Count}，按钮={workButton.name}，" +
            $"运行时配方={GameRes.Instance?.recipeById?.Count ?? 0}");

        inputInventory.RefreshUI();
        outputInventory.RefreshUI();
    }

    private void BindInputSlots()
    {
        inputInventory.itemSlot_UI.Clear();

        for (int i = 1; i <= InputSlotCount; i++)
        {
            var button = basePanel.GetButton($"输入_{i}");
            if (button == null)
                throw new System.NullReferenceException($"[Mod_HandCraftTable] 未找到输入按钮 输入_{i}");

            var slotUI = button.GetComponent<ItemSlot_UI>();
            if (slotUI == null)
                throw new System.NullReferenceException($"[Mod_HandCraftTable] 输入_{i} 缺少 ItemSlot_UI");

            inputInventory.BindSlotUI(slotUI, i - 1);
        }
    }

    private void BindOutputSlots()
    {
        outputInventory.itemSlot_UI.Clear();
        for (int i = 1; i <= OutputSlotCount; i++)
        {
            Button button = basePanel.GetButton($"输出_{i}");
            if (button == null)
                throw new System.NullReferenceException($"[Mod_HandCraftTable] 未找到输出按钮 输出_{i}");

            ItemSlot_UI slotUI = button.GetComponent<ItemSlot_UI>();
            if (slotUI == null)
                throw new System.NullReferenceException($"[Mod_HandCraftTable] 输出_{i} 缺少 ItemSlot_UI");

            outputInventory.BindSlotUI(slotUI, i - 1);
        }
    }

    private Inventory GetPlayerHandInventory()
    {
        var handMod = item.GetComponentInChildren<Mod_Hand>();
        if (handMod == null)
            return null;

        return handMod.HandInventory;
    }

    private static void InitializeInventoryData(Inventory inventory, string inventoryName)
    {
        if (inventory == null || inventory.Data == null)
            throw new System.NullReferenceException($"[Mod_HandCraftTable] {inventoryName} 或 Data 为空");

        inventory.UnbindSlotDataEvents();
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            inventory.Data.itemSlots[i].Index = i;
            inventory.Data.itemSlots[i].SlotMaxVolume = 100;
        }

        inventory.Data.Event_RefreshUI = new();
        inventory.Data.Event_RefreshUI += inventory.RefreshUI;
    }

    private void ValidateInventoryConfig()
    {
        if (inputInventory == null || inputInventory.Data == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] inputInventory 未配置");

        if (outputInventory == null || outputInventory.Data == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] outputInventory 未配置");

        if (inputInventory.Data.itemSlots.Count != InputSlotCount)
            throw new System.InvalidOperationException($"[Mod_HandCraftTable] 输入槽位必须为 {InputSlotCount} 个");

        if (outputInventory.Data.itemSlots.Count != OutputSlotCount)
            throw new System.InvalidOperationException($"[Mod_HandCraftTable] 输出槽位必须为 {OutputSlotCount} 个");
    }

    public Inventory GetDefaultTargetInventory()
    {
        return inputInventory;
    }

    public void I_ShowPanel()
    {
        EnsurePanelCreated();
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] basePanel 为空，无法打开面板");

        var handInv = GetPlayerHandInventory();
        if (handInv == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] 玩家手部容器为空，无法打开面板");

        inputInventory.DefaultTarget_Inventory = handInv;
        outputInventory.DefaultTarget_Inventory = handInv;
        basePanel.Open();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    public void I_ClosePanel()
    {
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] basePanel 为空，无法关闭面板");

        basePanel.Close();
        inputInventory.SyncQuickTransferTarget(basePanel);
        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
    }

    public void I_TogglePanel()
    {
        TogglePanelByKey();
    }

#endregion

    #region 合成日志

    private Player ResolveCraftActor()
    {
        return item as Player ?? item?.Owner as Player ?? item?.GetComponentInParent<Player>();
    }

    private void LogCraftDebug(string message)
    {
        if (!EnableCraftDebug)
            return;

        Debug.Log($"[Mod_HandCraftTable][Debug] {message}");
    }

#endregion
}
