using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Mod_MakeTable : Module, IInventory, IInstanceUI
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion


    #region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();
    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory;
    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory;
    public BasePanel basePanel;
    public GameObject InventoryPanel_Prefab;
    public Mod_InteractReciver mod_InteractReciver;

    private const string InputInventorySaveKey = "maketable.input";
    private const string OutputInventorySaveKey = "maketable.output";

    public override void Load()
    {
        mod_InteractReciver = item.GetComponentInChildren<Mod_InteractReciver>();
        RestoreInventoryState();
        mod_InteractReciver.OnAction_Start += Interact_Start;
        mod_InteractReciver.OnAction_Stop += Interact_Stop;
        InitData();
    }

    private void OnDestroy()
    {
        UnbindCraftPreview();
        inputInventory?.UnbindSlotDataEvents();
        outputInventory?.UnbindSlotDataEvents();

        if (mod_InteractReciver != null)
        {
            mod_InteractReciver.OnAction_Start -= Interact_Start;
            mod_InteractReciver.OnAction_Stop -= Interact_Stop;
        }
    }

    /// <summary>对象销毁时对称解除输入与输出预览监听。</summary>
    private void UnbindCraftPreview()
    {
        if (inputInventory?.Data != null)
            inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        if (outputInventory?.Data != null)
            outputInventory.Data.Event_OnDataChanged -= OnOutputSlotChanged;
    }

    public override void Save()
    {
        SaveInventoryState();
    }
    #endregion

    #region 库存存档

    /// <summary>读取工作台输入/输出槽位；旧 RawData 格式不存在库存时保持兼容。</summary>
    private void RestoreInventoryState()
    {
        Inventory_ModuleData savedData = InventoryModuleDataPersistence.TryRead(ModSaveData);
        InventoryModuleDataPersistence.TryRestore(inputInventory, savedData, InputInventorySaveKey);
        InventoryModuleDataPersistence.TryRestore(outputInventory, savedData, OutputInventorySaveKey);
    }

    /// <summary>保存工作台输入/输出槽位。</summary>
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

    [Header("交互组件")]
    [Tooltip("合成按钮")]
    public Button workButton;
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
    private static readonly CraftingCapabilities Capabilities = new CraftingCapabilities
    {
        RecipeType = RecipeType.Crafting,
        AllowCompactGrid = true,
        AllowOutputIntoInput = false
    };

    private int RequiredClickCount => Mathf.Max(minClickCount, baseClickCount - (Mathf.Max(1, workbenchLevel) - 1) * clickReductionPerLevel);

    public void OnValidate()
    {
        _Data.Name = ModText.WorkBench;
    }


    #region 事件处理

    private void OnCraftButtonClick()
    {
        if (!TryGetCraftPreview(out _))
        {
            ResetCraftProgress();
            return;
        }

        _currentClickProgress++;
        int requiredClickCount = RequiredClickCount;
        _currentClickProgress = Mathf.Min(_currentClickProgress, requiredClickCount);
        _outputPreview?.SetProgress(_currentClickProgress / (float)requiredClickCount);
        Debug.Log($"[Mod_MakeTable] 点击进度：{_currentClickProgress}/{requiredClickCount}，等级={workbenchLevel}");

        if (_currentClickProgress < requiredClickCount)
            return;

        bool craftResult = Craft(inputInventory, outputInventory);
        ResetCraftProgress();
        RefreshCraftPreview();
        if (craftResult)
            _outputPreview?.PlaySuccess();

        if (!craftResult)
        {
            Debug.Log("[Mod_MakeTable] 合成失败，已重置点击进度");
        }

    }


    public void InitData()
    {
        InitializeInventoryData(inputInventory, nameof(inputInventory));
        InitializeInventoryData(outputInventory, nameof(outputInventory));
    }
    public bool EnsurePanelCreated()
    {
        // 如果面板已创建，直接返回 false（表示没有创建新面板）
        if (basePanel != null)
            return false;
        // 如果预制体存在，创建面板
        GameObject panelPrefab = InventoryPanel_Prefab;

        if (panelPrefab == null)
        {
            Debug.LogWarning("[Inventory.EnsurePanelCreated] InventoryPanel_Prefab 未设置，无法创建面板");
            return false;
        }


        basePanel = UIManager.Instance.CreatePanelFromGameObject(panelPrefab).GetComponentInChildren<BasePanel>();

// 使用输入容器数据恢复面板位置（当前脚本没有单独的 Data 字段）
        var panelData = inputInventory.Data;
        RectTransform rt = null;
        if (basePanel.Dragger != null)
            rt = basePanel.Dragger.GetComponent<RectTransform>();
        if (rt == null)
            rt = basePanel.GetComponent<RectTransform>();

        if (rt != null)
        {
            var savedPos = panelData.PanelPosition;
            var savedPos2 = new Vector2(savedPos.x, savedPos.y);
            if (IsValidVector2(savedPos2) && (savedPos2.x != 0 || savedPos2.y != 0))
            {
                rt.anchoredPosition = savedPos2;
            }
            else
            {
                InventoryPanelLayout.ApplyDefaultCraftingPosition(rt);
            }
        }

        // 设置窗口信息
        if (basePanel.TryGetText("窗口信息", out var titleText))
            titleText.text = _Data.Name;

        // 调用UI初始化方法（此时basePanel已存在）
        InitUI();

        return true; // 成功创建了面板
    }

    private static void InitializeInventoryData(Inventory inventory, string inventoryName)
    {
        inventory.UnbindSlotDataEvents();

        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            inventory.Data.itemSlots[i].Index = i;
            inventory.Data.itemSlots[i].SlotMaxVolume = 100;
        }

        inventory.Data.Event_RefreshUI = new();
        inventory.Data.Event_RefreshUI += inventory.RefreshUI;
    }

    private static bool IsValidVector2(Vector2 vector)
    {
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
    }

    /// <summary>
    /// UI初始化（在面板创建后调用）
    /// </summary>
    public void InitUI()
    {
        _currentClickProgress = 0;

        // 绑定槽位 UI
        BindSlotsByPrefix(inputInventory, "输入");
        BindSlotsByPrefix(outputInventory, "输出");

        // 同步 UI 数据
        inputInventory.SyncData();
        outputInventory.SyncData();
        BindCraftPreview();

        // 绑定合成按钮
        workButton = basePanel.GetButton("合成按钮");
        workButton.onClick.RemoveListener(OnCraftButtonClick);
        workButton.onClick.AddListener(OnCraftButtonClick);

        // 初始化UI显示
        basePanel?.Close();
        inputInventory.RefreshUI();
        outputInventory.RefreshUI();
        RefreshCraftPreview();
    }

    private void BindCraftPreview()
    {
        if (outputInventory.itemSlot_UI.Count == 0)
            return;

        _outputPreview = CraftingOutputPreview.Attach(basePanel, outputInventory.itemSlot_UI[0]);
        inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        inputInventory.Data.Event_OnDataChanged += OnInputSlotChanged;
        outputInventory.Data.Event_OnDataChanged -= OnOutputSlotChanged;
        outputInventory.Data.Event_OnDataChanged += OnOutputSlotChanged;
    }

    private void OnInputSlotChanged(ItemSlot _)
    {
        ResetCraftProgress();
        RefreshCraftPreview();
    }

    private void OnOutputSlotChanged(ItemSlot _)
    {
        RefreshCraftPreview();
    }

    private void ResetCraftProgress()
    {
        _currentClickProgress = 0;
        _outputPreview?.SetProgress(0f);
    }

    private void RefreshCraftPreview()
    {
        if (_outputPreview == null)
            return;

        if (TryGetCraftPreview(out ItemData previewItem))
            _outputPreview.Show(previewItem, _currentClickProgress / (float)RequiredClickCount);
        else
            _outputPreview.Clear();
    }

    private void BindSlotsByPrefix(Inventory inventory, string prefix)
    {
        if (inventory == null || inventory.Data == null || inventory.Data.itemSlots == null)
        {
            Debug.LogWarning($"[Mod_MakeTable] 跳过绑定，{prefix} Inventory 无效");
            return;
        }

        inventory.itemSlot_UI.Clear();

        int boundIndex = 0;
        int maxTry = Mathf.Max(inventory.Data.itemSlots.Count, 12);
        for (int i = 1; i <= maxTry; i++)
        {
            if (boundIndex >= inventory.Data.itemSlots.Count)
                break;

            var button = basePanel.GetButton($"{prefix}_{i}");
            if (button == null)
                continue;

            var slotUI = button.GetComponent<ItemSlot_UI>();
            if (slotUI == null)
                continue;

            inventory.BindSlotUI(slotUI, boundIndex);
            boundIndex++;
        }
    }

    /// <summary>
    /// 通过公共事务执行制作。
    /// </summary>
    public bool Craft(Inventory inputInv, Inventory outputInv)
    {
        Player actor = item as Player ?? item?.Owner as Player ?? item?.GetComponentInParent<Player>();
        CraftingResult result = CraftingService.Craft(inputInv, outputInv, Capabilities, actor);
        if (!result.Success)
            Debug.LogWarning($"[Mod_MakeTable] 合成失败：{result.Message}");
        return result.Success;
    }

    private bool TryGetCraftPreview(out ItemData previewItem)
    {
        CraftingResult result = CraftingService.Preview(inputInventory, outputInventory, Capabilities);
        CraftingPreviewDiagnostics.ReportFailure(
            nameof(Mod_MakeTable),
            inputInventory,
            result,
            ref _lastCraftPreviewMessage);
        previewItem = result.PrimaryOutput;
        return result.Success;
    }

    /// <summary>
    /// 玩家开始交互。
    /// </summary>
    public void Interact_Start(Item playerItem)
    {
        EnsurePanelCreated();
        Inventory handInv = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInv == null)
        {
            Debug.LogError("玩家手部容器为空！");
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

        inputInventory.DefaultTarget_Inventory = handInv;
        outputInventory.DefaultTarget_Inventory = handInv;
        basePanel.Open();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    /// <summary>
    /// 玩家结束交互。
    /// </summary>
    public void Interact_Stop(Item playerItem)
    {
        if (basePanel == null)
            return;

        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
        basePanel.Close();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    #endregion

    #region IInstanceUI接口

    public void I_ShowPanel()
    {
        EnsurePanelCreated();
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_MakeTable] basePanel 为空，无法打开面板");

        Inventory handInv = item?.GetComponentInChildren<Mod_Hand>()?.HandInventory ?? Inventory_Hand.PlayerHand;
        if (handInv != null)
        {
            inputInventory.DefaultTarget_Inventory = handInv;
            outputInventory.DefaultTarget_Inventory = handInv;
        }

        basePanel.Open();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    public void I_ClosePanel()
    {
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_MakeTable] basePanel 为空，无法关闭面板");

        basePanel.Close();
        inputInventory.SyncQuickTransferTarget(basePanel);
        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
    }

    public void I_TogglePanel()
    {
        EnsurePanelCreated();
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_MakeTable] basePanel 为空，无法切换面板");

        if (basePanel.IsOpen())
            I_ClosePanel();
        else
            I_ShowPanel();
    }

    #endregion

    #region IInventory接口

    public Inventory GetDefaultTargetInventory()
    {
        return inputInventory;
    }

    #endregion
}
