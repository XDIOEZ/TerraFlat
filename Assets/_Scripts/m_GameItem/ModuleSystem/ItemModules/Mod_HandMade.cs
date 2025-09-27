using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手工制作模块，提供合成物品的功能
/// </summary>
public class Mod_HandMade : Module
{
    #region 字段和属性

    [Header("模块数据")]
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData _Data 
    { 
        get => inventoryModuleData; 
        set => inventoryModuleData = (InventoryModuleData)value; 
    }

    [Header("UI组件")]
    [Tooltip("合成界面面板")]
    public BasePanel basePanel;
    
    [Header("合成容器")]
    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory;
    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory;

    [Header("交互组件")]
    [Tooltip("合成按钮")]
    public Button workButton;

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
        InitializeInventories();
        SetupEventListeners();
        RestorePanelPosition();
    }

    public override void Save()
    {
        SavePanelPosition();
        CleanupEventListeners();
        item.itemData.ModuleDataDic[_Data.Name] = _Data;
    }

    #endregion

    #region 事件处理

    private void OnCraftButtonClick()
    {
        Act();
    }

    public override void Act()
    {
        Craft(inputInventory, outputInventory);
    }
    /// <summary>
/// 执行合成操作
/// </summary>
public bool Craft(Inventory inputInv, Inventory outputInv)
{
    try
    {
        // 生成配方键列表
        List<string> recipeKeys = GenerateRecipeKey_List(inputInv);
        
        Recipe recipe = null;
        string matchedKey = null;
        
        // 尝试匹配每个配方键
        foreach (string recipeKey in recipeKeys)
        {
            if (GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
            {
                matchedKey = recipeKey;
                break;
            }
        }
        
        // 验证配方
        if (recipe == null)
        {
            Debug.LogError($"配方 {string.Join(" 或 ", recipeKeys)} 不存在");
            return false;
        }

        // 验证输入槽位数量
        if (!ValidateSlotCount(inputInv, recipe))
            return false;

        // 准备输出物品
        var outputItems = PrepareOutputItems(recipe);
        if (outputItems == null)
            return false;

        // 检查资源和空间
        if (!CheckResourcesAndSpace(inputInv, outputInv, recipe, outputItems))
        {
            Debug.LogError("合成失败：材料不足或输出空间不足");
            return false;
        }

        // 执行合成
        ExecuteCrafting(inputInv, outputInv, recipe, outputItems);
        return true;
    }
    catch (Exception ex)
    {
        Debug.LogError($"合成过程中发生错误: {ex.Message}");
        return false;
    }
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

    #region 合成逻辑

[Tooltip("输出一个字符串列表 包含所有Tag模式 和itemName模式的 集合 , 复杂度是O(n^2)")]
private List<string> GenerateRecipeKey_List(Inventory inputInv)
{
    List<string> recipeKeys = new List<string>();
    
    // 生成基于物品ID的配方键
    Input_List inputList = new Input_List();
    foreach (ItemSlot slot in inputInv.Data.itemSlots)
    {
        if (slot.itemData == null)
        {
            inputList.AddNameItem("");
        }
        else
        {
            inputList.AddNameItem(slot.itemData.IDName);
        }
    }
    recipeKeys.Add(inputList.ToString());
    
    // 生成基于Tag的配方键（如果有物品包含Tag）
    for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
    {
        var slot = inputInv.Data.itemSlots[i];
        if (slot.itemData != null && slot.itemData.Tags != null)
        {
            // 为每个包含Tag的物品生成一个基于Tag的配方键版本
            Input_List tagInputList = new Input_List();
            for (int j = 0; j < inputInv.Data.itemSlots.Count; j++)
            {
                if (j == i && slot.itemData.Tags.MakeTag != null && slot.itemData.Tags.MakeTag.values.Count > 0)
                {
                    // 使用第一个Type标签
                    if (slot.itemData.Tags.MakeTag.values.Count > 0)
                    {
                        tagInputList.AddTagItem(slot.itemData.Tags.MakeTag.values[0]);
                    }
                    else
                    {
                        tagInputList.AddNameItem(slot.itemData?.IDName ?? "");
                    }
                }
                else
                {
                    var otherSlot = inputInv.Data.itemSlots[j];
                    tagInputList.AddNameItem(otherSlot.itemData?.IDName ?? "");
                }
            }
            recipeKeys.Add(tagInputList.ToString());
        }
    }
    
    return recipeKeys;
}

private string GenerateRecipeKey(Inventory inputInv)
{
    Input_List inputList = new Input_List();
    foreach (ItemSlot slot in inputInv.Data.itemSlots)
    {
        if (slot.itemData == null)
        {
            inputList.AddNameItem("");
        }
        else
        {
            inputList.AddNameItem(slot.itemData.IDName);
        }
    }
    return inputList.ToString();
}

    private bool ValidateRecipe(string recipeKey, out Recipe recipe)
    {
        recipe = null;
        
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
        {
            Debug.LogError($"配方 {recipeKey} 不存在");
            return false;
        }
        return true;
    }

    private bool ValidateSlotCount(Inventory inputInv, Recipe recipe)
    {
        if (inputInv.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"插槽数量不匹配：配方要求 {recipe.inputs.RowItems_List.Count} 个插槽，当前有 {inputInv.Data.itemSlots.Count} 个");
            return false;
        }
        return true;
    }

    private List<ItemData> PrepareOutputItems(Recipe recipe)
    {
        var itemsToAdd = new List<ItemData>();
        
        foreach (var output in recipe.outputs.results)
        {
            Item outputitem = output.ItemPrefab.GetComponent<Item>();
            ItemData newItem = outputitem.Get_NewItemData();
            newItem.Stack.Amount = output.amount;

            itemsToAdd.Add(newItem);
        }
        
        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        // 检查输入材料
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            if (slot.itemData == null)
                return false;

            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // 检查输出空间
        foreach (var item in outputItems)
        {
            if (!outputInv.Data.TryAddItem(item, false))
                return false;
        }

        return true;
    }

    private void ExecuteCrafting(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        Debug.Log($"开始合成：{recipe.name}");
        Debug.Log($"输入材料：{GenerateRecipeKey(inputInv)}");
        Debug.Log($"输出产物：{string.Join(", ", outputItems.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // 添加输出物品
        foreach (var item in outputItems)
        {
            outputInv.Data.TryAddItem(item);
            Debug.Log($"添加产物：{item.Stack.Amount}x{item.IDName}");
        }

        // 扣除输入材料
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            Debug.Log($"插槽 {i}：需要 {required.ItemName} x{required.amount}，当前有 {slot.itemData.Stack.Amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                Debug.Log($"插槽 {i}：{required.ItemName} 已耗尽，移除物品");
                inputInv.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"插槽 {i}：剩余 {required.ItemName} x{slot.itemData.Stack.Amount}");
            }
            inputInv.RefreshUI(i);
        }
        foreach(var action in recipe.action)
        {
            action.Apply(inputInv.Data.itemSlots[action.slotIndex]);
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"合成完成：{recipe.name}");
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

        inputInventory.Init();
        outputInventory.Init();
    }

    private void SetupEventListeners()
    {
        workButton?.onClick.AddListener(OnCraftButtonClick);

        // 设置默认目标背包
        if (item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInventory = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
            inputInventory.DefaultTarget_Inventory = handInventory;
            outputInventory.DefaultTarget_Inventory = handInventory;
        }

        // 设置交互事件
        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_Interaction interactMod))
        {
            interactMod.OnAction_Start += Interact_Start;
            interactMod.OnAction_Stop += Interact_Stop;
        }

        basePanel = GetComponentInChildren<BasePanel>();
    }

    private void CleanupEventListeners()
    {
        workButton?.onClick.RemoveListener(OnCraftButtonClick);

        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_Interaction interactMod))
        {
            interactMod.OnAction_Start -= Interact_Start;
            interactMod.OnAction_Stop -= Interact_Stop;
        }
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
            basePanel.Dragger.transform.position = savedPosition;
        }
    }

    private void SavePanelPosition()
    {
        if (basePanel?.Dragger != null)
        {
            inventoryModuleData.PanleRectPosition = basePanel.Dragger.transform.position;
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

    #endregion
}