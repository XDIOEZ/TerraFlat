using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Mod_HandMade : Module
{
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData _Data { get => inventoryModuleData; set => inventoryModuleData = (InventoryModuleData)value; }
    public BasePanel basePanel;
    #region 工作变量

    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory;

    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory;

    [Tooltip("合成按钮")]
    public Button WorkButton;

    #endregion
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
        //同步数据
        if(inventoryModuleData.Data.Count == 0)
        {
            inventoryModuleData.Data[inputInventory.Data.Name] = inputInventory.Data;
            inventoryModuleData.Data[outputInventory.Data.Name] = outputInventory.Data;
        }
        else
        {
            inputInventory.Data = inventoryModuleData.Data[inputInventory.Data.Name];
            outputInventory.Data = inventoryModuleData.Data[outputInventory.Data.Name];
        }

        WorkButton.onClick.AddListener(OnCraftButtonClick);

        if (item.itemMods.ContainsKey_ID(ModText.Hand))
        {
        inputInventory.DefaultTarget_Inventory = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
        outputInventory.DefaultTarget_Inventory = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
        }


        inputInventory.Init();
        outputInventory.Init();

        item.itemMods.GetMod_ByID(ModText.Interact, out Mod_Interaction interactMod);
        if (interactMod != null)
        {
            interactMod.OnAction_Start += Interact_Start;
            interactMod.OnAction_Cancel += Interact_Stop;
        }

        basePanel = GetComponentInChildren<BasePanel>();

    }

    private void OnCraftButtonClick()
    {
        Act();
    }

    public override void Act()
    {
        Craft(inputInventory, outputInventory, RecipeType.Crafting);
    }

    //玩家与此发生交互
    public void Interact_Start(Item item_)
    {
        item_.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod);
        if (handMod == null) return;
        inputInventory.DefaultTarget_Inventory = handMod.inventory;
        outputInventory.DefaultTarget_Inventory = handMod.inventory;
        basePanel.Toggle();
    }
    //玩家结束交互
    public void Interact_Stop(Item item_)
    { 
        if (inputInventory.DefaultTarget_Inventory == null&&outputInventory.DefaultTarget_Inventory == null) return;
        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
        basePanel.Close();
    }


    #region 合成物品逻辑

    public bool Craft(Inventory inputInventory_, Inventory outputInventory_, RecipeType recipeType)
    {
        // 生成配方键
        string recipeKey = string.Join(",",
            inputInventory_.Data.itemSlots.Select(slot => slot.itemData?.IDName ?? ""));

        // 检查配方存在性及类型
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out var recipe) ||
            recipe.recipeType != recipeType)
        {
            Debug.LogError($"配方匹配失败：键 {recipeKey} 不存在或类型 {recipeType} 不匹配");
            return false;
        }

        // 检查插槽数量
        if (inputInventory_.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"插槽数量不匹配：配方要求 {recipe.inputs.RowItems_List.Count} 个插槽，当前有 {inputInventory_.Data.itemSlots.Count} 个");
            return false;
        }

        // 准备输出物品
        var itemsToAdd = new List<ItemData>();
        foreach (var output in recipe.outputs.results)
        {
            var prefab = GameRes.Instance.AllPrefabs[output.ItemName];
            if (prefab == null)
            {
                Debug.LogError($"预制体不存在：{output.ItemName}（配方：{recipe.name}）");
                return false;
            }
            Item item = prefab.GetComponent<Item>();
            var itemdata = item.itemData;
            item.itemData = item.itemData.DeepClone();

            item.IsPrefabInit();

            ItemData newItem = item.itemData;
            item.itemData = itemdata;


            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }

        // 检查资源和空间
        if (!CheckEnough(inputInventory_, outputInventory_, recipe.inputs, itemsToAdd))
        {
            Debug.LogError("合成失败：材料不足或输出空间不足");
            return false;
        }

        // 显示合成开始信息
        Debug.Log($"开始合成：{recipe.name}");
        Debug.Log($"输入材料：{recipeKey}");
        Debug.Log($"输出产物：{string.Join(", ", itemsToAdd.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // 执行合成：添加输出物品
        foreach (var item in itemsToAdd)
        {
            outputInventory_.Data.TryAddItem(item);
            Debug.Log($"添加产物：{item.Stack.Amount}x{item.IDName}");
        }

        // 扣除输入材料
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            // 显示详细扣减信息
            Debug.Log($"插槽 {i}：需要 {required.ItemName} x{required.amount}，当前有 {slot.itemData.Stack.Amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                Debug.Log($"插槽 {i}：{required.ItemName} 已耗尽，移除物品");
                inputInventory_.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"插槽 {i}：剩余 {required.ItemName} x{slot.itemData.Stack.Amount}");
            }
            inputInventory.RefreshUI(i);
        }

        Debug.Log($"合成完成：{recipe.name}");
        outputInventory.RefreshUI();
        inputInventory.RefreshUI();
        return true;
    }

    private bool CheckEnough(Inventory inputInventory_,
                                   Inventory outputInventory_,
                                   Input_List inputList,
                                   List<ItemData> itemsToAdd)
    {
        // 检查每个插槽的物品是否满足要求
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // 如果该插槽不需要物品则跳过
            if (required.amount == 0) continue;

            // 检查物品存在且名称匹配
            if (slot.itemData == null ||
                slot.itemData.IDName != required.ItemName)
                return false;

            // 检查数量足够
            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // 检查输出空间
        foreach (var item in itemsToAdd)
            if (!outputInventory_.Data.TryAddItem(item, false))
                return false;

        return true;
    }
    #endregion

    public override void Save()
    {
        // throw new System.NotImplementedException();

        item.itemMods.GetMod_ByID(ModText.Interact, out Mod_Interaction interactMod);
        if (interactMod != null)
        {
            interactMod.OnAction_Start -= Interact_Start;
            interactMod.OnAction_Cancel -= Interact_Stop;
        }

        item.itemData.ModuleDataDic[_Data.Name] = _Data;
    }
}

[Serializable]
[MemoryPackable]
public partial class InventoryModuleData : ModuleData
{
    [ShowInInspector]
    public Dictionary<string,Inventory_Data> Data = new Dictionary<string, Inventory_Data>();
}
