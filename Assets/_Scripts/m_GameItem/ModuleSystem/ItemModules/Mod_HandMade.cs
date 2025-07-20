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
    public override ModuleData Data { get => inventoryModuleData; set => inventoryModuleData = (InventoryModuleData)value; }

    #region 工作变量

    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory;

    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory;

    [Tooltip("合成按钮")]
    public Button WorkButton;

    #endregion

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

        if (item.Mods.ContainsKey(Mod_Text.Hand))
        {
        inputInventory.DefaultTarget_Inventory = item.Mods[Mod_Text.Hand].GetComponent<IInventory>()._Inventory;
        outputInventory.DefaultTarget_Inventory = item.Mods[Mod_Text.Hand].GetComponent<IInventory>()._Inventory;
        }
      
    }

    private void OnCraftButtonClick()
    {
        if (Craft(inputInventory, outputInventory, RecipeType.Crafting))
        {

        }
    }

    public bool Craft(Inventory inputInventory_, Inventory outputInventory_, RecipeType recipeType)
    {
        // 生成配方键
        string recipeKey = string.Join(",",
            inputInventory_.Data.itemSlots.Select(slot => slot._ItemData?.IDName ?? ""));

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
            var prefab = GameRes.Instance.AllPrefabs[output.resultItem];
            if (prefab == null)
            {
                Debug.LogError($"预制体不存在：{output.resultItem}（配方：{recipe.name}）");
                return false;
            }
            ItemData newItem = prefab.GetComponent<Item>().DeepClone().Item_Data;
            newItem.Stack.Amount = output.resultAmount;
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
            Debug.Log($"插槽 {i}：需要 {required.ItemName} x{required.amount}，当前有 {slot._ItemData.Stack.Amount}");

            slot._ItemData.Stack.Amount -= required.amount;
            if (slot._ItemData.Stack.Amount <= 0)
            {
                Debug.Log($"插槽 {i}：{required.ItemName} 已耗尽，移除物品");
                inputInventory_.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"插槽 {i}：剩余 {required.ItemName} x{slot._ItemData.Stack.Amount}");
            }
            inputInventory.RefreshUI(i);
        }

        Debug.Log($"合成完成：{recipe.name}");
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
            if (slot._ItemData == null ||
                slot._ItemData.IDName != required.ItemName)
                return false;

            // 检查数量足够
            if (slot._ItemData.Stack.Amount < required.amount)
                return false;
        }

        // 检查输出空间
        foreach (var item in itemsToAdd)
            if (!outputInventory_.Data.TryAddItem(item, false))
                return false;

        return true;
    }

    public override void Save()
    {
        // throw new System.NotImplementedException();
        item.Item_Data.ModuleDataDic[Data.Name] = Data;
    }
}

[Serializable]
[MemoryPackable]
public partial class InventoryModuleData : ModuleData
{
    [ShowInInspector]
    public Dictionary<string,Inventory_Data> Data = new Dictionary<string, Inventory_Data>();
}
