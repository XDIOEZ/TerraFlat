using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

public class Inventory_WorkBench : Inventory
{
    [Header("策划必配")]
    [InfoBox("策划：需要手动拖拽挂接 Mod_Inventory（工作台的容器模块）。\n否则输出容器/合成会无法正常工作。", InfoMessageType.Warning, VisibleIf = "@mod_Inventory == null")]
    [Required("策划：请手动挂接 Mod_Inventory（工作台容器模块）")]
    public Mod_Inventory mod_Inventory;

    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory => this;
    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory => mod_Inventory.InventoryInstances[1];


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
    private static readonly CraftingCapabilities Capabilities = new CraftingCapabilities
    {
        RecipeType = RecipeType.Crafting,
        AllowCompactGrid = true,
        AllowOutputIntoInput = false
    };

    private int RequiredClickCount => Mathf.Max(minClickCount, baseClickCount - (Mathf.Max(1, workbenchLevel) - 1) * clickReductionPerLevel);

    public override void OnValidate()
    {
        Data.Name = ModText.WorkBench;
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
        Debug.Log($"工作台点击进度：{_currentClickProgress}/{requiredClickCount}，等级={workbenchLevel}");

        if (_currentClickProgress < requiredClickCount)
            return;

        // 检查inputInventory和outputInventory是否有效
        if (inputInventory == null)
        {
            Debug.LogError("inputInventory 为 null！");
            ResetCraftProgress();
            return;
        }

        if (outputInventory == null)
        {
            Debug.LogError("outputInventory 为 null！");
            ResetCraftProgress();
            return;
        }

        Debug.Log("开始执行合成操作...");
        bool craftResult = Craft(inputInventory, outputInventory);
        Debug.Log($"合成操作完成，结果: {craftResult}");
        ResetCraftProgress();
        RefreshCraftPreview();
        if (craftResult)
            _outputPreview?.PlaySuccess();
    }


    public override void InitData()
    {
        base.InitData();

        // 添加空值检查
        if (mod_Inventory == null)
        {
            Debug.LogError("无法获取Mod_Inventory组件！");
            return;
        }
    }

    /// <summary>
    /// UI初始化（在面板创建后调用）
    /// </summary>
    public override void InitUI()
    {
        base.InitUI();
        _currentClickProgress = 0;

        // 尝试获取按钮
        workButton = basePanel.GetButton("合成按钮");

        // 添加按钮空值检查
        if (workButton == null)
        {
            Debug.LogError("无法在basePanel中找到名为'合成按钮'的按钮！");
            return;
        }

        // 移除现有的监听器，避免重复绑定
        workButton.onClick.RemoveListener(OnCraftButtonClick);
        // 监听合成按钮点击事件
        workButton.onClick.AddListener(OnCraftButtonClick);
        BindCraftPreview();
        RefreshCraftPreview();
        Debug.Log("合成按钮事件绑定成功！");
    }

    private void BindCraftPreview()
    {
        ItemSlot_UI outputSlot = null;
        if (outputInventory != null && outputInventory.itemSlot_UI.Count > 0)
            outputSlot = outputInventory.itemSlot_UI[0];

        if (outputSlot == null)
            outputSlot = basePanel.GetButton("输出_1")?.GetComponent<ItemSlot_UI>();

        _outputPreview = CraftingOutputPreview.Attach(basePanel, outputSlot);
        Data.Event_OnDataChanged -= OnInputSlotChanged;
        Data.Event_OnDataChanged += OnInputSlotChanged;
    }

    private void OnInputSlotChanged(ItemSlot _)
    {
        ResetCraftProgress();
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

    /// <summary>
    /// 通过公共事务执行制作。
    /// </summary>
    public bool Craft(Inventory inputInv, Inventory outputInv)
    {
        Player actor = item as Player ?? item?.Owner as Player ?? item?.GetComponentInParent<Player>();
        CraftingResult result = CraftingService.Craft(
            inputInv,
            outputInv,
            Capabilities,
            actor);
        if (!result.Success)
            Debug.LogWarning($"[Inventory_WorkBench] 合成失败：{result.Message}");
        return result.Success;
    }

    private bool TryGetCraftPreview(out ItemData previewItem)
    {
        CraftingResult result = CraftingService.Preview(inputInventory, outputInventory, Capabilities);
        previewItem = result.PrimaryOutput;
        return result.Success;
    }

    /// <summary>
    /// 玩家开始交互。
    /// </summary>
    public override void Interact_Start(Item playerItem)
    {
        base.Interact_Start(playerItem);
        if (playerItem.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod))
        {
            inputInventory.DefaultTarget_Inventory = handMod.inventory;
            outputInventory.DefaultTarget_Inventory = handMod.inventory;
        }
        Debug.Log($"玩家 {playerItem.name} 开始交互工作台");
    }

    /// <summary>
    /// 玩家结束交互。
    /// </summary>
    public void Interact_Stop(Item playerItem)
    {
        if (inputInventory.DefaultTarget_Inventory == null &&
            outputInventory.DefaultTarget_Inventory == null)
            return;

        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
        // 关闭工作台UI
        foreach (var inventory in mod_Inventory.inventoryBasePanelCache.Values)
        {
            inventory.Close();
        }
    }

    #endregion
}
