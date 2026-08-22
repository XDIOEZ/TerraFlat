using AYellowpaper.SerializedCollections;
using Sirenix.OdinInspector;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手工制作模块，提供合成物品的功能
/// </summary>
public class Mod_HandMade : Module,IInventory
{
    #region 字段和属性

    [Header("模块数据")]
    public Inventory_ModuleData inventoryModuleData = new Inventory_ModuleData();
    public override ModuleData _Data 
    { 
        get => inventoryModuleData; 
        set => inventoryModuleData = (Inventory_ModuleData)value; 
    }

    [Header("UI组件")]
    [Tooltip("合成界面面板")]
    public BasePanel basePanel;

    [Tooltip("Inventory引用字典-配置字段")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();
    [Tooltip("Inventory引用字典-接口实现")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get => inventoryRefDic; set => inventoryRefDic = value; }

    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory => inventoryRefDic["输入插槽"];
    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory => inventoryRefDic["输出插槽"];

    [Header("交互组件")]
    [Tooltip("合成按钮")]
    public Button workButton;
    [Tooltip("完成一次手工合成需要点击的次数")]
    public int requiredClickCount = 6;

    private int _currentClickProgress;
    private CraftingOutputPreview _outputPreview;
    private string _lastCraftPreviewMessage;
    private Mod_InteractReciver _interactReceiver;
    private static readonly CraftingCapabilities Capabilities = new CraftingCapabilities
    {
        RecipeType = RecipeType.Crafting,
        AllowCompactGrid = false,
        AllowOutputIntoInput = false
    };

    #endregion

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Composite;
        }
    }

    [Button]
    public override void Load()
    {
        //初始化库存
        InitializeInventories();
        //初始化事件监听
        SetupEventListeners();
        //还原面板位置
        RestorePanelPosition();
    }

    public override void Save()
    {
        SavePanelPosition();
        item.itemData.ModuleDataDic[_Data.Name] = _Data;
    }

    private void OnDestroy()
    {
        Unload();
    }

    public override void Unload()
    {
        CleanupEventListeners();
    }

    #endregion

    #region 事件处理

    private void OnCraftButtonClick()
    {
        if (!TryGetCraftPreview(true, out _))
        {
            ResetCraftProgress();
            return;
        }

        int clickCount = Mathf.Max(1, requiredClickCount);
        _currentClickProgress = Mathf.Min(_currentClickProgress + 1, clickCount);
        _outputPreview?.SetProgress(_currentClickProgress / (float)clickCount);

        if (_currentClickProgress < clickCount)
            return;

        bool craftResult = Craft(inputInventory, outputInventory);
        ResetCraftProgress();
        RefreshCraftPreview();
        if (craftResult)
            _outputPreview?.PlaySuccess();
    }

    public override void Act()
    {
        bool craftResult = Craft(inputInventory, outputInventory);
        ResetCraftProgress();
        RefreshCraftPreview();
        if (craftResult)
            _outputPreview?.PlaySuccess();
    }

    /// <summary>
    /// 通过公共事务执行制作。
    /// </summary>
    public bool Craft(Inventory inputInv, Inventory outputInv)
    {
        Player actor = item as Player ?? item?.Owner as Player ?? item?.GetComponentInParent<Player>();
        CraftingResult result = CraftingService.Craft(inputInv, outputInv, Capabilities, actor);
        if (!result.Success)
            Debug.LogWarning($"[Mod_HandMade] 合成失败：{result.Message}");
        return result.Success;
    }

    /// <summary>预检制作结果；被动刷新不报告正常的未匹配状态。</summary>
    private bool TryGetCraftPreview(bool isUserInitiated, out ItemData previewItem)
    {
        CraftingResult result = CraftingService.Preview(inputInventory, outputInventory, Capabilities);
        CraftingPreviewDiagnostics.ReportFailure(
            nameof(Mod_HandMade),
            inputInventory,
            result,
            isUserInitiated,
            ref _lastCraftPreviewMessage);
        previewItem = result.PrimaryOutput;
        return result.Success;
    }

    /// <summary>
    /// 玩家开始交互
    /// </summary>
    public void Interact_Start(Item playerItem)
    {
        if (playerItem.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod))
        {
            inputInventory.DefaultTarget_Inventory = handMod.inventory;
            outputInventory.DefaultTarget_Inventory = handMod.inventory;
        }
        basePanel?.Toggle();
    }

    /// <summary>
    /// 玩家结束交互
    /// </summary>
    public void Interact_Stop(Item playerItem)
    {
        if (inputInventory.DefaultTarget_Inventory == null && 
            outputInventory.DefaultTarget_Inventory == null) 
            return;
            
        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
        basePanel?.Close();
    }

    #endregion

    #region 初始化和设置

    private void InitializeInventories()
    {
        // 同步数据
        if (inventoryModuleData.Data.Count == 0)
        {
            inventoryModuleData.Data[inputInventory.Data.Name] = inputInventory.Data;
            inventoryModuleData.Data[outputInventory.Data.Name] = outputInventory.Data;
        }
        else
    {
            inputInventory.Data = inventoryModuleData.Data[inputInventory.Data.Name];
            outputInventory.Data = inventoryModuleData.Data[outputInventory.Data.Name];
        }

        inputInventory.InitData();
        outputInventory.InitData();

        //TODO 初始化完毕后 从输出插槽上遍历获取
       workButton = outputInventory.basePanel.GetButton("合成按钮");
    }

    private void SetupEventListeners()
    {
        basePanel = GetComponentInChildren<BasePanel>();
        workButton?.onClick.RemoveListener(OnCraftButtonClick);
        workButton?.onClick.AddListener(OnCraftButtonClick);
        BindCraftPreview();
        RefreshCraftPreview();

        // 设置默认目标背包
        if (item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInventory = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
            inputInventory.DefaultTarget_Inventory = handInventory;
            outputInventory.DefaultTarget_Inventory = handInventory;
        }

        // 设置交互事件
        if (_interactReceiver != null)
        {
            _interactReceiver.OnAction_Start -= Interact_Start;
            _interactReceiver.OnAction_Stop -= Interact_Stop;
        }

        _interactReceiver = null;
        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            _interactReceiver = interactMod;
            _interactReceiver.OnAction_Start += Interact_Start;
            _interactReceiver.OnAction_Stop += Interact_Stop;
        }
    }

    private void CleanupEventListeners()
    {
        workButton?.onClick.RemoveListener(OnCraftButtonClick);
        if (inputInventory?.Data != null)
            inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        if (outputInventory?.Data != null)
            outputInventory.Data.Event_OnDataChanged -= OnOutputSlotChanged;

        if (_interactReceiver != null)
        {
            _interactReceiver.OnAction_Start -= Interact_Start;
            _interactReceiver.OnAction_Stop -= Interact_Stop;
            _interactReceiver = null;
        }
    }

    private void BindCraftPreview()
    {
        ItemSlot_UI outputSlot = outputInventory.itemSlot_UI.FirstOrDefault();
        _outputPreview = CraftingOutputPreview.Attach(outputInventory.basePanel ?? basePanel, outputSlot);

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

        if (TryGetCraftPreview(false, out ItemData previewItem))
            _outputPreview.Show(previewItem, _currentClickProgress / (float)Mathf.Max(1, requiredClickCount));
        else
            _outputPreview.Clear();
    }

    #endregion

    #region 面板位置管理

    private void RestorePanelPosition()
    {
        if (basePanel?.Dragger == null) return;
        
        var savedPosition = inventoryModuleData.PanleRectPosition;
        if (savedPosition != null && 
            IsValidVector3(savedPosition) && 
            !IsZeroVector3(savedPosition))
        {
            basePanel.Dragger.rectTransform.anchoredPosition = savedPosition;
        }
    }

    private void SavePanelPosition()
    {
        if (basePanel?.Dragger != null)
        {
            inventoryModuleData.PanleRectPosition = basePanel.Dragger.rectTransform.anchoredPosition;
        }
    }

    private bool IsValidVector3(Vector3 vector)
    {
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) && !float.IsNaN(vector.z) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y) && !float.IsInfinity(vector.z);
    }

    private bool IsZeroVector3(Vector3 vector)
    {
        return vector.x == 0 && vector.y == 0 && vector.z == 0;
    }

    public Inventory GetDefaultTargetInventory()
    {
        return inputInventory;
    }

    #endregion
}
