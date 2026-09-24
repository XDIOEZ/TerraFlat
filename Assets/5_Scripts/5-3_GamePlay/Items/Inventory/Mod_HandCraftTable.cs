using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 玩家随身制作入口：读取与世界工作台相同的普通合成配方，按手工点击基准完成制作。
/// 输入和输出库存均保留末尾空槽，正式面板根据库存槽位数量扩展滚动网格。
/// </summary>
public class Mod_HandCraftTable : Module, IInventory, IInstanceUI
{
#region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

#endregion

#region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();
    [Tooltip("手工制作输入容器；末格占用后自动增加空槽")]
    public Inventory inputInventory;
    [Tooltip("手工制作输出容器；末格占用后自动增加空槽")]
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
    [Tooltip("徒手制作的基础点击次数；世界工作台按此基准减少30%")]
    public int baseClickCount = 6;
    [Tooltip("每升1级减少的点击次数")]
    public int clickReductionPerLevel = 1;
    [Tooltip("每次合成最少需要点击次数")]
    public int minClickCount = 1;

    private CraftingStationController _craftingController;
    private GameController _inputController;
    private InputAction _toggleAction;
    private Action<InputAction.CallbackContext> _toggleCallback;
    private Inventory_Data observedInputData;
    private Inventory_Data observedOutputData;
    private static readonly CraftingCapabilities Capabilities = new CraftingCapabilities
    {
        RecipeType = RecipeType.Crafting,
        StationId = "handcraft",
        CompatibleStationIds = new[] { "workbench" },
        InputSlotLimit = 0,
        AllowOutputIntoInput = false
    };

    /// <summary>手工制作点击基准，供制作控制器与 MOD 扩展读取。</summary>
    public int GetRequiredClickCount() => Mathf.Max(1, Mathf.Max(minClickCount,
        baseClickCount - (Mathf.Max(1, workbenchLevel) - 1) * clickReductionPerLevel));

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
                !CanToggleFromMobileMenu() &&
                !CanOpenAlongsidePlayerBag())
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

    /// <summary>仅当玩法输入锁全部来自当前玩家的主背包时，允许继续打开手工制作面板。</summary>
    private bool CanOpenAlongsidePlayerBag()
    {
        if (_inputController == null)
            return false;

        return !_inputController.HasBlockingGameplayInputLock(owner =>
            owner is Inventory inventory &&
            ReferenceEquals(inventory.item, item) &&
            string.Equals(inventory.Data?.Name, ModText.Bag, StringComparison.Ordinal));
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
        UnbindDynamicSlotEvents();
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
        inputInventory.Data.SetUnlimitedSlots(true);
        outputInventory.Data.SetUnlimitedSlots(true);
        InitializeInventoryData(inputInventory, nameof(inputInventory));
        InitializeInventoryData(outputInventory, nameof(outputInventory));
        BindDynamicSlotEvents();
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
            GetRequiredClickCount,
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
        BindSlots(inputInventory, "输入", "输入槽内容");
    }

    private void BindOutputSlots()
    {
        BindSlots(outputInventory, "输出", "输出槽内容");
    }

    /// <summary>正式面板保留原始槽作为模板，库存增长时只克隆所需数量。</summary>
    private void BindSlots(Inventory inventory, string prefix, string contentName)
    {
        RectTransform content = FindSlotContent(contentName);
        ItemSlot_UI[] slots = content.GetComponentsInChildren<ItemSlot_UI>(true);
        if (slots.Length == 0)
            throw new InvalidOperationException($"[Mod_HandCraftTable] {contentName} 缺少初始槽位模板");

        inventory.itemSlot_UI.Clear();
        int count = inventory.Data.itemSlots.Count;
        ItemSlot_UI template = slots[0];
        for (int index = 0; index < count; index++)
        {
            ItemSlot_UI slotUI = index < slots.Length
                ? slots[index]
                : Instantiate(template, content, false);
            slotUI.name = $"{prefix}_{index + 1}";
            slotUI.gameObject.SetActive(true);
            inventory.BindSlotUI(slotUI, index);
        }

        for (int index = count; index < slots.Length; index++)
            slots[index].gameObject.SetActive(false);
    }

    /// <summary>仅从正式 Prefab 的槽位内容节点读取布局，不在运行时拼装滚动视图。</summary>
    private RectTransform FindSlotContent(string contentName)
    {
        foreach (RectTransform rect in basePanel.GetComponentsInChildren<RectTransform>(true))
            if (string.Equals(rect.name, contentName, StringComparison.Ordinal))
                return rect;

        throw new InvalidOperationException($"[Mod_HandCraftTable] 面板缺少 {contentName}");
    }

    /// <summary>库存数据先补空格，再同步新增 UI 与输出预览绑定。</summary>
    private void SyncDynamicSlotUI(Inventory inventory, string prefix, string contentName)
    {
        if (basePanel == null || inventory?.Data?.itemSlots == null ||
            inventory.itemSlot_UI.Count == inventory.Data.itemSlots.Count)
            return;

        BindSlots(inventory, prefix, contentName);
        inventory.SyncData();
        basePanel.RefreshUIComponents();
        if (ReferenceEquals(inventory, outputInventory))
            _craftingController?.RefreshOutputSlotBindings();
    }

    private void OnInputInventoryChanged(ItemSlot _) => SyncDynamicSlotUI(inputInventory, "输入", "输入槽内容");
    private void OnOutputInventoryChanged(ItemSlot _) => SyncDynamicSlotUI(outputInventory, "输出", "输出槽内容");

    /// <summary>存档恢复可能替换库存数据引用，因此订阅始终跟随当前数据实例。</summary>
    private void BindDynamicSlotEvents()
    {
        UnbindDynamicSlotEvents();
        observedInputData = inputInventory.Data;
        observedOutputData = outputInventory.Data;
        observedInputData.Event_OnDataChanged += OnInputInventoryChanged;
        observedOutputData.Event_OnDataChanged += OnOutputInventoryChanged;
    }

    private void UnbindDynamicSlotEvents()
    {
        if (observedInputData != null)
            observedInputData.Event_OnDataChanged -= OnInputInventoryChanged;
        if (observedOutputData != null)
            observedOutputData.Event_OnDataChanged -= OnOutputInventoryChanged;
        observedInputData = null;
        observedOutputData = null;
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

        if (inputInventory.Data.itemSlots == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] 输入库存缺少槽位列表");

        if (outputInventory.Data.itemSlots == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] 输出库存缺少槽位列表");
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
