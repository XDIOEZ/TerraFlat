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
    [Tooltip("2x2输入容器（输入_1~输入_4）")]
    public Inventory inputInventory;
    [Tooltip("输出容器（仅输出_1）")]
    public Inventory outputInventory;
    public BasePanel basePanel;
    public GameObject InventoryPanel_Prefab;
    [Tooltip("手工合成台UI预制体名，Inspector未手动拖拽时会按此名称从GameRes回填")]
    public string InventoryPanelPrefabName = "UI_WorkBench";

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

    private int _currentClickProgress;
    private CraftingOutputPreview _outputPreview;
    private string _lastCraftPreviewMessage;
    private bool? _lastCanCraft;
    private TextMeshProUGUI _craftingHintText;
    private GameController _inputController;
    private InputAction _toggleAction;
    private Action<InputAction.CallbackContext> _toggleCallback;
    private static readonly CraftingCapabilities Capabilities = new CraftingCapabilities
    {
        RecipeType = RecipeType.Crafting,
        InputSlotLimit = InputSlotCount,
        MaxRecipeWidth = 2,
        MaxRecipeHeight = 2,
        AllowCompactGrid = false,
        AllowOutputIntoInput = true
    };

    private int RequiredClickCount => Mathf.Max(minClickCount, baseClickCount - (Mathf.Max(1, workbenchLevel) - 1) * clickReductionPerLevel);

    private const int InputSlotCount = 4;
    private const int OutputSlotCount = 1;
    private const string CraftingHintName = "FWUI_FooterHint";
    private const string InputInventorySaveKey = "handcraft.input";
    private const string OutputInventorySaveKey = "handcraft.output";
    private const string WaitingHint = "放入材料 · 核对配方 · 开始制作";
    private const string ReadyHint = "配方已匹配 · 点击开始制作";
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
        UnbindCraftPreview();
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

    /// <summary>对象销毁时解除制作预览监听；保存过程不得改变运行时事件线路。</summary>
    private void UnbindCraftPreview()
    {
        if (inputInventory?.Data != null)
            inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        if (outputInventory?.Data != null)
            outputInventory.Data.Event_OnDataChanged -= OnOutputSlotChanged;
    }

    private void EnsureInventoryPanelPrefabAssigned()
    {
        if (InventoryPanel_Prefab != null)
            return;

        if (GameRes.Instance == null)
            return;

        if (!string.IsNullOrWhiteSpace(InventoryPanelPrefabName))
        {
            InventoryPanel_Prefab = GameRes.Instance.GetPrefab(InventoryPanelPrefabName);
            if (InventoryPanel_Prefab != null)
                return;
        }

        string[] fallbackPrefabNames = { "UI_WorkBench", "UI_HandCraftTable", "UI_MakeTable" };
        foreach (string prefabName in fallbackPrefabNames)
        {
            if (string.Equals(prefabName, InventoryPanelPrefabName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            InventoryPanel_Prefab = GameRes.Instance.GetPrefab(prefabName);
            if (InventoryPanel_Prefab != null)
            {
                InventoryPanelPrefabName = prefabName;
                return;
            }
        }
    }

    public void InitData()
    {
        ValidateInventoryConfig();
        InitializeInventoryData(inputInventory, nameof(inputInventory));
        InitializeInventoryData(outputInventory, nameof(outputInventory));
    }

    public void InitUI()
    {
        _currentClickProgress = 0;
        _lastCanCraft = null;

        BindInputSlots();
        BindOutputSlot();

        inputInventory.SyncData();
        outputInventory.SyncData();
        BindCraftPreview();

        workButton = basePanel.GetButton("合成按钮");
        if (workButton == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 未找到合成按钮");
            return;
        }

        workButton.onClick.RemoveListener(OnCraftButtonClick);
        workButton.onClick.AddListener(OnCraftButtonClick);
        _craftingHintText = basePanel.GetText(CraftingHintName);
        LogCraftDebug(
            $"线路绑定完成：输入槽={inputInventory.Data.itemSlots.Count}，" +
            $"输出槽={outputInventory.Data.itemSlots.Count}，按钮={workButton.name}，" +
            $"运行时配方={GameRes.Instance?.recipeById?.Count ?? 0}");

        inputInventory.RefreshUI();
        outputInventory.RefreshUI();
        RefreshCraftPreview();
    }

    private void OnCraftButtonClick()
    {
        LogCraftDebug($"收到合成按钮点击：interactable={workButton != null && workButton.interactable}");
        if (!TryGetCraftPreview(out _))
        {
            LogCraftDebug("按钮回调已进入，但提交前预检失败");
            ResetCraftProgress();
            return;
        }

        _currentClickProgress++;
        int requiredClickCount = RequiredClickCount;
        _currentClickProgress = Mathf.Min(_currentClickProgress, requiredClickCount);
        _outputPreview?.SetProgress(_currentClickProgress / (float)requiredClickCount);
        LogCraftDebug($"点击进度：{_currentClickProgress}/{requiredClickCount}，等级={workbenchLevel}");

        if (_currentClickProgress < requiredClickCount)
            return;

        bool craftResult = Craft(inputInventory, outputInventory);
        ResetCraftProgress();
        RefreshCraftPreview();
        if (craftResult)
            _outputPreview?.PlaySuccess();

        if (!craftResult)
        {
            LogCraftDebug("合成失败，已重置点击进度");
        }
    }

    private void BindCraftPreview()
    {
        if (outputInventory.itemSlot_UI.Count == 0)
        {
            LogCraftDebug("预览线路绑定中止：输出 UI 槽位数量为 0");
            return;
        }

        _outputPreview = CraftingOutputPreview.Attach(basePanel, outputInventory.itemSlot_UI[0]);
        inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        inputInventory.Data.Event_OnDataChanged += OnInputSlotChanged;
        outputInventory.Data.Event_OnDataChanged -= OnOutputSlotChanged;
        outputInventory.Data.Event_OnDataChanged += OnOutputSlotChanged;
        LogCraftDebug($"预览线路已连接：预览组件={(_outputPreview != null ? "有效" : "为空")}");
    }

    private void OnInputSlotChanged(ItemSlot changedSlot)
    {
        LogCraftDebug(
            $"收到输入变化事件：槽={changedSlot?.Index.ToString() ?? "null"}，" +
            $"当前={CraftingPreviewDiagnostics.DescribeInventory(inputInventory, InputSlotCount)}");
        ResetCraftProgress();
        RefreshCraftPreview();
    }

    private void OnOutputSlotChanged(ItemSlot changedSlot)
    {
        LogCraftDebug(
            $"收到输出变化事件：槽={changedSlot?.Index.ToString() ?? "null"}，" +
            $"当前={CraftingPreviewDiagnostics.DescribeInventory(outputInventory, OutputSlotCount)}");
        RefreshCraftPreview();
    }

    private void ResetCraftProgress()
    {
        _currentClickProgress = 0;
        _outputPreview?.SetProgress(0f);
    }

    private void RefreshCraftPreview()
    {
        bool canCraft = TryGetCraftPreview(out ItemData previewItem);
        RefreshCraftingPrompt(canCraft);

        if (_outputPreview == null)
            return;

        if (canCraft)
            _outputPreview.Show(previewItem, _currentClickProgress / (float)RequiredClickCount);
        else
            _outputPreview.Clear();
    }

    /// <summary>同步配方匹配提示与制作按钮可用状态。</summary>
    private void RefreshCraftingPrompt(bool canCraft)
    {
        if (workButton != null)
            workButton.interactable = canCraft;

        if (_craftingHintText != null)
            _craftingHintText.text = canCraft ? ReadyHint : WaitingHint;

        if (_lastCanCraft != canCraft)
        {
            LogCraftDebug($"预览状态切换：canCraft={canCraft}，按钮可交互={workButton != null && workButton.interactable}");
            _lastCanCraft = canCraft;
        }
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

    private void BindOutputSlot()
    {
        outputInventory.itemSlot_UI.Clear();

        var button = basePanel.GetButton("输出_1");
        if (button == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] 未找到输出按钮 输出_1");

        var slotUI = button.GetComponent<ItemSlot_UI>();
        if (slotUI == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] 输出_1 缺少 ItemSlot_UI");

        outputInventory.BindSlotUI(slotUI, 0);
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

        if (inputInventory.Data.itemSlots.Count < InputSlotCount)
            throw new System.InvalidOperationException($"[Mod_HandCraftTable] 输入槽位不足，至少需要 {InputSlotCount} 个");

        if (outputInventory.Data.itemSlots.Count < OutputSlotCount)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] 输出槽位不足，至少需要 1 个");
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

#region 合成逻辑

    public bool Craft(Inventory inputInv, Inventory outputInv)
    {
        LogCraftDebug(
            $"开始制作事务：输入={CraftingPreviewDiagnostics.DescribeInventory(inputInv, InputSlotCount)}，" +
            $"输出={CraftingPreviewDiagnostics.DescribeInventory(outputInv, OutputSlotCount)}");
        CraftingResult result = CraftingService.Craft(inputInv, outputInv, Capabilities, ResolveCraftActor());
        if (!result.Success)
        {
            LogCraftDebug($"制作事务失败：{result.FailureReason} / {result.Message}");
        }
        else
        {
            ItemData output = result.PrimaryOutput;
            LogCraftDebug(
                $"制作事务成功：配方={result.Recipe?.Id ?? "<空>"}，" +
                $"主产物={output?.IDName ?? "<空>"} x{output?.Stack?.Amount ?? 0f}");
        }
        return result.Success;
    }

    private bool TryGetCraftPreview(out ItemData previewItem)
    {
        CraftingResult result = CraftingService.Preview(inputInventory, outputInventory, Capabilities);
        string previousFailure = _lastCraftPreviewMessage;
        CraftingPreviewDiagnostics.ReportFailure(
            nameof(Mod_HandCraftTable),
            inputInventory,
            result,
            ref _lastCraftPreviewMessage,
            Capabilities);
        if (result.Success && !string.IsNullOrEmpty(previousFailure))
        {
            LogCraftDebug(
                $"预览匹配恢复：配方={result.Recipe?.Id ?? "<空>"}，" +
                $"主产物={result.PrimaryOutput?.IDName ?? "<空>"}");
        }
        previewItem = result.PrimaryOutput;
        return result.Success;
    }

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
