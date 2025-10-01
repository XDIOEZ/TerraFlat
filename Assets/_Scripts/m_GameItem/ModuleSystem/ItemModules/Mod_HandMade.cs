using AYellowpaper.SerializedCollections;
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
public class Mod_HandMade : Module,IInventory
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

    [Tooltip("Inventory�����ֵ�-�����ֶ�")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();
    [Tooltip("Inventory�����ֵ�-�ӿ�ʵ��")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get => inventoryRefDic; set => inventoryRefDic = value; }

    [Tooltip("�������������ڴ�źϳ������ԭ������Ʒ")]
    public Inventory inputInventory => inventoryRefDic["������"];
    [Tooltip("������������ڴ�źϳɺ�õ�����Ʒ")]
    public Inventory outputInventory => inventoryRefDic["������"];

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
        Craft(inputInventory, outputInventory);
    }
/// <summary>
/// ִ�кϳɲ���
/// </summary>
public bool Craft(Inventory inputInv, Inventory outputInv)
{
    // �����䷽���б�
    List<string> recipeKeys = GenerateRecipeKey_List(inputInv);
    
    Recipe recipe = null;
    string matchedKey = null;
    
    // ����ƥ��ÿ���䷽��
    foreach (string recipeKey in recipeKeys)
    {
        if (GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
        {
            matchedKey = recipeKey;
            break;
        }
    }
    
    // ��֤�䷽
    if (recipe == null)
    {
        Debug.LogError($"�䷽ {string.Join(" �� ", recipeKeys)} ������");
        return false;
    }

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

[Tooltip("���һ���ַ����б� ��������Tagģʽ ��itemNameģʽ�� ���� , ���Ӷ���O(n^2)")]
private List<string> GenerateRecipeKey_List(Inventory inputInv)
{
    List<string> recipeKeys = new List<string>();
    
    // 1. ���ɻ�����ƷID���䷽��������ϳɣ�
    Input_List orderedInputList = new Input_List();
    orderedInputList.recipeType = RecipeType.Crafting;
    orderedInputList.inputOrder = RecipeInputRule.����ϳ�;
    foreach (ItemSlot slot in inputInv.Data.itemSlots)
    {
        if (slot.itemData == null)
        {
            orderedInputList.AddNameItem("");
        }
        else
        {
            orderedInputList.AddNameItem(slot.itemData.IDName);
        }
    }
    recipeKeys.Add(orderedInputList.ToString());
    
    // 2. ���ɻ�����ƷID���䷽��������ϳɣ�- ͨ���޸�����ϳɵĹ���
    orderedInputList.inputOrder = RecipeInputRule.�޹���ϳ�;
    recipeKeys.Add(orderedInputList.ToString());
    
    // 3. ���ɻ���Tag���䷽��������ϳɣ�
    for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
    {
        var slot = inputInv.Data.itemSlots[i];
        if (slot.itemData != null && slot.itemData.Tags != null)
        {
            // Ϊÿ������Tag����Ʒ����һ������Tag���䷽���汾������
            Input_List orderedTagInputList = new Input_List();
            orderedTagInputList.recipeType = RecipeType.Crafting;
            orderedTagInputList.inputOrder = RecipeInputRule.����ϳ�;
            for (int j = 0; j < inputInv.Data.itemSlots.Count; j++)
            {
                if (j == i && slot.itemData.Tags.MakeTag != null && slot.itemData.Tags.MakeTag.values.Count > 0)
                {
                    // ʹ�õ�һ��Type��ǩ
                    if (slot.itemData.Tags.MakeTag.values.Count > 0)
                    {
                        orderedTagInputList.AddTagItem(slot.itemData.Tags.MakeTag.values[0]);
                    }
                    else
                    {
                        orderedTagInputList.AddNameItem(slot.itemData?.IDName ?? "");
                    }
                }
                else
                {
                    var otherSlot = inputInv.Data.itemSlots[j];
                    orderedTagInputList.AddNameItem(otherSlot.itemData?.IDName ?? "");
                }
            }
            recipeKeys.Add(orderedTagInputList.ToString());
            
            // 4. ���ɻ���Tag���䷽��������ϳɣ�- ͨ���޸�����ϳɵĹ���
            orderedTagInputList.inputOrder = RecipeInputRule.�޹���ϳ�;
            recipeKeys.Add(orderedTagInputList.ToString());
        }
    }
    
    return recipeKeys;
}

private string GenerateRecipeKey(Inventory inputInv)
{
    Input_List inputList = new Input_List();
    inputList.recipeType = RecipeType.Crafting;
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
            Debug.LogError($"�䷽ {recipeKey} ������");
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
            ItemData newItem = outputitem.Get_NewItemData();
            newItem.Stack.Amount = output.amount;

            itemsToAdd.Add(newItem);
        }
        
        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, 
    Recipe recipe, List<ItemData> outputItems)
{
    // ���recipe.inputs���й���ϳɻ����޹���ϳ�
    if (recipe.inputs.inputOrder == RecipeInputRule.����ϳ�)
    {
        // �й���ϳɰ���ԭ���߼���
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
    }
    else if (recipe.inputs.inputOrder == RecipeInputRule.�޹���ϳ�)
    {
        // ������޹���ϳ� ��ͨ������recipe.inputs.RowItems_List���Ҷ�Ӧ��required ������Ƿ����㹻����Ʒ
        foreach (var required in recipe.inputs.RowItems_List)
        {
            if (required.amount == 0) continue;

            // ���������в���ƥ�����Ʒ
            float foundAmount = 0;
            foreach (var slot in inputInv.Data.itemSlots)
            {
                if (slot.itemData == null) continue;

                bool isMatch = false;
                // ����ƥ��ģʽ����Ƿ�ƥ��
                if (required.matchMode == MatchMode.ExactItem)
                {
                    isMatch = slot.itemData.IDName == required.ItemName;
                }
                else if (required.matchMode == MatchMode.ByTag)
                {
                    isMatch = slot.itemData.Tags != null && 
                             slot.itemData.Tags.MakeTag != null && 
                             slot.itemData.Tags.MakeTag.values.Contains(required.Tag);
                }

                if (isMatch)
                {
                    foundAmount += slot.itemData.Stack.Amount;
                }
            }

            // ����ҵ��������Ƿ��㹻
            if (foundAmount < required.amount)
                return false;
        }
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
if (recipe.inputs.inputOrder == RecipeInputRule.����ϳ�)
{
    // ����ϳ� - ��λ�ö�Ӧ�۳�
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
}
else if (recipe.inputs.inputOrder == RecipeInputRule.�޹���ϳ�)
{
    // ����ϳ� - �����䷽������Ҳ��۳���Ӧ��Ʒ
    foreach (var required in recipe.inputs.RowItems_List)
    {
        if (required.amount == 0) continue;

        float remainingAmountToConsume = required.amount;
        
        // �������в�λ����ƥ�����Ʒ
        for (int i = 0; i < inputInv.Data.itemSlots.Count && remainingAmountToConsume > 0; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            if (slot.itemData == null) continue;

            // �����Ʒ�Ƿ�ƥ������
            bool isMatch = false;
            if (required.matchMode == MatchMode.ExactItem)
            {
                isMatch = slot.itemData.IDName == required.ItemName;
            }
            else if (required.matchMode == MatchMode.ByTag)
            {
                isMatch = slot.itemData.Tags != null && 
                         slot.itemData.Tags.MakeTag != null && 
                         slot.itemData.Tags.MakeTag.values.Contains(required.Tag);
            }

            if (isMatch && slot.itemData.Stack.Amount > 0)
            {
                // ���㱾�ο������ĵ�����
                float consumeAmount = Mathf.Min(remainingAmountToConsume, slot.itemData.Stack.Amount);
                slot.itemData.Stack.Amount -= consumeAmount;
                remainingAmountToConsume -= consumeAmount;
                
                Debug.Log($"��� {i}������ {slot.itemData.IDName} x{consumeAmount}��ʣ�� {slot.itemData.Stack.Amount}");
                
                // �����Ʒ���꣬�Ƴ���Ʒ
                if (slot.itemData.Stack.Amount <= 0)
                {
                    Debug.Log($"��� {i}��{slot.itemData.IDName} �Ѻľ����Ƴ���Ʒ");
                    inputInv.Data.RemoveItemAll(slot, i);
                }
                
                inputInv.RefreshUI(i);
            }
        }
    }
}
        
        // ִ���䷽��������ӿ�ֵ��飩
        if (recipe.action != null)
        {
            foreach(var action in recipe.action)
            {
                if (action != null)
                {
                    action.Apply(this);
                }
            }
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
            var handInventory = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
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

    #endregion
}