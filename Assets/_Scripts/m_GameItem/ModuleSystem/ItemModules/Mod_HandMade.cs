using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// �ֹ�����ģ�飬�ṩ�ϳ���Ʒ�Ĺ���
/// </summary>
public class Mod_HandMade : Module
{
    #region �ֶκ�����

    [Header("ģ������")]
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData _Data 
    { 
        get => inventoryModuleData; 
        set => inventoryModuleData = (InventoryModuleData)value; 
    }

    [Header("UI���")]
    [Tooltip("�ϳɽ������")]
    public BasePanel basePanel;
    
    [Header("�ϳ�����")]
    [Tooltip("�������������ڴ�źϳ������ԭ������Ʒ")]
    public Inventory inputInventory;
    [Tooltip("������������ڴ�źϳɺ�õ�����Ʒ")]
    public Inventory outputInventory;

    [Header("�������")]
    [Tooltip("�ϳɰ�ť")]
    public Button workButton;

    #endregion

    #region ��������

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

    #region �¼�����

    private void OnCraftButtonClick()
    {
        Act();
    }

    public override void Act()
    {
        Craft(inputInventory, outputInventory, RecipeType.Crafting);
    }

    /// <summary>
    /// ��ҿ�ʼ����
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
    /// ��ҽ�������
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

    #region �ϳ��߼�

    /// <summary>
    /// ִ�кϳɲ���
    /// </summary>
    public bool Craft(Inventory inputInv, Inventory outputInv, RecipeType recipeType)
    {
        try
        {
            // �����䷽��
            string recipeKey = GenerateRecipeKey(inputInv);
            
            // ��֤�䷽
            if (!ValidateRecipe(recipeKey, recipeType, out var recipe))
                return false;

            // ��֤�����λ����
            if (!ValidateSlotCount(inputInv, recipe))
                return false;

            // ׼�������Ʒ
            var outputItems = PrepareOutputItems(recipe);
            if (outputItems == null)
                return false;

            // �����Դ�Ϳռ�
            if (!CheckResourcesAndSpace(inputInv, outputInv, recipe, outputItems))
            {
                Debug.LogError("�ϳ�ʧ�ܣ����ϲ��������ռ䲻��");
                return false;
            }

            // ִ�кϳ�
            ExecuteCrafting(inputInv, outputInv, recipe, outputItems);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"�ϳɹ����з�������: {ex.Message}");
            return false;
        }
    }

    private string GenerateRecipeKey(Inventory inputInv)
    {
        return string.Join(",",
            inputInv.Data.itemSlots.Select(slot => slot.itemData?.IDName ?? ""));
    }

    private bool ValidateRecipe(string recipeKey, RecipeType recipeType, out Recipe recipe)
    {
        recipe = null;
        
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe) ||
            recipe.recipeType != recipeType)
        {
            Debug.Log($"�䷽ƥ��ʧ�ܣ��� {recipeKey} �����ڻ����� {recipeType} ��ƥ��");
            return false;
        }
        
        return true;
    }

    private bool ValidateSlotCount(Inventory inputInv, Recipe recipe)
    {
        if (inputInv.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"���������ƥ�䣺�䷽Ҫ�� {recipe.inputs.RowItems_List.Count} ����ۣ���ǰ�� {inputInv.Data.itemSlots.Count} ��");
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
            ItemData newItem = outputitem.IsPrefabInit();
            newItem.Stack.Amount = output.amount;

            itemsToAdd.Add(newItem);
        }
        
        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, 
        Recipe recipe, List<ItemData> outputItems)
    {
        // ����������
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            if (slot.itemData == null || slot.itemData.IDName != required.ItemName)
                return false;

            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }

        // �������ռ�
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
        Debug.Log($"��ʼ�ϳɣ�{recipe.name}");
        Debug.Log($"������ϣ�{GenerateRecipeKey(inputInv)}");
        Debug.Log($"������{string.Join(", ", outputItems.Select(item => $"{item.Stack.Amount}x{item.IDName}"))}");

        // ��������Ʒ
        foreach (var item in outputItems)
        {
            outputInv.Data.TryAddItem(item);
            Debug.Log($"��Ӳ��{item.Stack.Amount}x{item.IDName}");
        }

        // �۳��������
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0) continue;

            Debug.Log($"��� {i}����Ҫ {required.ItemName} x{required.amount}����ǰ�� {slot.itemData.Stack.Amount}");

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
            {
                Debug.Log($"��� {i}��{required.ItemName} �Ѻľ����Ƴ���Ʒ");
                inputInv.Data.RemoveItemAll(slot, i);
            }
            else
            {
                Debug.Log($"��� {i}��ʣ�� {required.ItemName} x{slot.itemData.Stack.Amount}");
            }
            inputInv.RefreshUI(i);
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"�ϳ���ɣ�{recipe.name}");
    }

    #endregion

    #region ��ʼ��������

    private void InitializeInventories()
    {
        // ͬ������
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

        // ����Ĭ��Ŀ�걳��
        if (item.itemMods.ContainsKey_ID(ModText.Hand))
        {
            var handInventory = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
            inputInventory.DefaultTarget_Inventory = handInventory;
            outputInventory.DefaultTarget_Inventory = handInventory;
        }

        // ���ý����¼�
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

    #region ���λ�ù���

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