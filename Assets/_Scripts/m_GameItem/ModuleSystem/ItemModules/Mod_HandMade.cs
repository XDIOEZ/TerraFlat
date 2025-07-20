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

    #region ��������

    [Tooltip("�������������ڴ�źϳ������ԭ������Ʒ")]
    public Inventory inputInventory;

    [Tooltip("������������ڴ�źϳɺ�õ�����Ʒ")]
    public Inventory outputInventory;

    [Tooltip("�ϳɰ�ť")]
    public Button WorkButton;

    #endregion

    [Button]
    public override void Load()
    {
        //ͬ������
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
        // �����䷽��
        string recipeKey = string.Join(",",
            inputInventory_.Data.itemSlots.Select(slot => slot._ItemData?.IDName ?? ""));

        // ����䷽�����Լ�����
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out var recipe) ||
            recipe.recipeType != recipeType)
        {
            Debug.LogError($"�䷽ƥ��ʧ�ܣ��� {recipeKey} �����ڻ����� {recipeType} ��ƥ��");
            return false;
        }

        // ���������
        if (inputInventory_.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"���������ƥ�䣺�䷽Ҫ�� {recipe.inputs.RowItems_List.Count} ����ۣ���ǰ�� {inputInventory_.Data.itemSlots.Count} ��");
            return false;
        }

        // ׼�������Ʒ
        var itemsToAdd = new List<ItemData>();
        foreach (var output in recipe.outputs.results)
        {
            var prefab = GameRes.Instance.AllPrefabs[output.resultItem];
            if (prefab == null)
            {
                Debug.LogError($"Ԥ���岻���ڣ�{output.resultItem}���䷽��{recipe.name}��");
                return false;
            }
            ItemData newItem = prefab.GetComponent<Item>().DeepClone().Item_Data;
            newItem.Stack.Amount = output.resultAmount;
            itemsToAdd.Add(newItem);
        }

        // �����Դ�Ϳռ�
        if (!CheckEnough(inputInventory_, outputInventory_, recipe.inputs, itemsToAdd))
        {
            Debug.LogError("�ϳ�ʧ�ܣ����ϲ��������ռ䲻��");
            return false;
        }

        // ��ʾ�ϳɿ�ʼ��Ϣ
        Debug.Log($"��ʼ�ϳɣ�{recipe.name}");
        Debug.Log($"������ϣ�{recipeKey}");
        Debug.Log($"������{string.Join(", ", itemsToAdd.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // ִ�кϳɣ���������Ʒ
        foreach (var item in itemsToAdd)
        {
            outputInventory_.Data.TryAddItem(item);
            Debug.Log($"��Ӳ��{item.Stack.Amount}x{item.IDName}");
        }

        // �۳��������
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            // ��ʾ��ϸ�ۼ���Ϣ
            Debug.Log($"��� {i}����Ҫ {required.ItemName} x{required.amount}����ǰ�� {slot._ItemData.Stack.Amount}");

            slot._ItemData.Stack.Amount -= required.amount;
            if (slot._ItemData.Stack.Amount <= 0)
            {
                Debug.Log($"��� {i}��{required.ItemName} �Ѻľ����Ƴ���Ʒ");
                inputInventory_.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"��� {i}��ʣ�� {required.ItemName} x{slot._ItemData.Stack.Amount}");
            }
            inputInventory.RefreshUI(i);
        }

        Debug.Log($"�ϳ���ɣ�{recipe.name}");
        return true;
    }

    private bool CheckEnough(Inventory inputInventory_,
                                   Inventory outputInventory_,
                                   Input_List inputList,
                                   List<ItemData> itemsToAdd)
    {
        // ���ÿ����۵���Ʒ�Ƿ�����Ҫ��
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // ����ò�۲���Ҫ��Ʒ������
            if (required.amount == 0) continue;

            // �����Ʒ����������ƥ��
            if (slot._ItemData == null ||
                slot._ItemData.IDName != required.ItemName)
                return false;

            // ��������㹻
            if (slot._ItemData.Stack.Amount < required.amount)
                return false;
        }

        // �������ռ�
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
