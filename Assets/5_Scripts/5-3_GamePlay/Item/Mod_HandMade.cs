using AYellowpaper.SerializedCollections;
using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using UnityEngine;
using UnityEngine.UI;
using RuntimeRecipeModel = RuntimeRecipe;

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
        CleanupEventListeners();
        item.itemData.ModuleDataDic[_Data.Name] = _Data;
    }

    #endregion

    #region 事件处理

    private void OnCraftButtonClick()
    {
        if (!TryGetCraftPreview(out _))
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
/// 执行合成操作
/// </summary>
public bool Craft(Inventory inputInv, Inventory outputInv)
{
    if (!TryResolveRecipe(inputInv, out RuntimeRecipeModel recipe, out bool isMirrorMatched, out List<string> recipeKeys))
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
    if (!CheckResourcesAndSpace(inputInv, outputInv, recipe, outputItems, isMirrorMatched))
    {
        Debug.LogError("合成失败：材料不足或输出空间不足");
        return false;
    }

    // 执行合成
    ExecuteCrafting(inputInv, outputInv, recipe, outputItems, isMirrorMatched);
    return true;
}

private bool TryGetCraftPreview(out ItemData previewItem)
{
    previewItem = null;
    if (!TryResolveRecipe(inputInventory, out RuntimeRecipeModel recipe, out bool isMirrorMatched, out _))
        return false;

    if (!ValidateSlotCount(inputInventory, recipe))
        return false;

    List<ItemData> outputItems = PrepareOutputItems(recipe);
    if (outputItems == null || outputItems.Count == 0)
        return false;

    if (!CheckResourcesAndSpace(inputInventory, outputInventory, recipe, outputItems, isMirrorMatched))
        return false;

    previewItem = outputItems[0];
    return true;
}

private bool TryResolveRecipe(
    Inventory inputInv,
    out RuntimeRecipeModel recipe,
    out bool isMirrorMatched,
    out List<string> recipeKeys)
{
    HashSet<string> mirroredKeys = new HashSet<string>();
    recipeKeys = GenerateRecipeKey_List(inputInv, mirroredKeys);

    recipe = null;
    isMirrorMatched = false;
    foreach (string recipeKey in recipeKeys)
    {
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
            continue;

        bool isMirrorKey = mirroredKeys.Contains(recipeKey);
        if (isMirrorKey && recipe.inputs.inputOrder == RecipeInputRule.规则合成 && !recipe.enableMirrorCrafting)
        {
            recipe = null;
            continue;
        }

        isMirrorMatched = isMirrorKey;
        return true;
    }

    return false;
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
private List<string> GenerateRecipeKey_List(Inventory inputInv, HashSet<string> mirroredKeys)
{
    List<string> recipeKeys = new List<string>();
    
    // 1. 生成基于物品ID的配方键（有序合成）
    Input_List orderedInputList = new Input_List();
    orderedInputList.recipeType = RecipeType.Crafting;
    orderedInputList.inputOrder = RecipeInputRule.规则合成;
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
    string orderedKey = orderedInputList.ToString();
    recipeKeys.Add(orderedKey);

    if (TryBuildMirroredInputList(orderedInputList, out Input_List mirroredOrderedInputList))
    {
        string mirroredOrderedKey = mirroredOrderedInputList.ToString();
        recipeKeys.Add(mirroredOrderedKey);
        mirroredKeys.Add(mirroredOrderedKey);
    }
    
    // 2. 生成基于物品ID的配方键（无序合成）- 通过修改有序合成的规则
    orderedInputList.inputOrder = RecipeInputRule.无规则合成;
    recipeKeys.Add(orderedInputList.ToString());
    
    // 3. 生成基于Tag的配方键（有序合成）
    for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
    {
        var slot = inputInv.Data.itemSlots[i];
        if (slot.itemData != null && slot.itemData.Tags != null)
        {
            // 为每个包含Tag的物品生成一个基于Tag的配方键版本（有序）
            Input_List orderedTagInputList = new Input_List();
            orderedTagInputList.recipeType = RecipeType.Crafting;
            orderedTagInputList.inputOrder = RecipeInputRule.规则合成;
            for (int j = 0; j < inputInv.Data.itemSlots.Count; j++)
            {
                if (j == i && slot.itemData.Tags != null && slot.itemData.Tags.Count > 0)
                {
                    // 使用第一个Type标签
                    if (slot.itemData.Tags.Count > 0)
                    {
                        orderedTagInputList.AddTagItem(slot.itemData.Tags[0]);
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
            string orderedTagKey = orderedTagInputList.ToString();
            recipeKeys.Add(orderedTagKey);

            if (TryBuildMirroredInputList(orderedTagInputList, out Input_List mirroredOrderedTagInputList))
            {
                string mirroredOrderedTagKey = mirroredOrderedTagInputList.ToString();
                recipeKeys.Add(mirroredOrderedTagKey);
                mirroredKeys.Add(mirroredOrderedTagKey);
            }
            
            // 4. 生成基于Tag的配方键（无序合成）- 通过修改有序合成的规则
            orderedTagInputList.inputOrder = RecipeInputRule.无规则合成;
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

    private bool ValidateRecipe(string recipeKey, out RuntimeRecipeModel recipe)
    {
        recipe = null;
        
        if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
        {
            Debug.LogError($"配方 {recipeKey} 不存在");
            return false;
        }
        return true;
    }

    private bool ValidateSlotCount(Inventory inputInv, RuntimeRecipeModel recipe)
    {
        if (inputInv.Data.itemSlots.Count != recipe.inputs.RowItems_List.Count)
        {
            Debug.LogError($"插槽数量不匹配：配方要求 {recipe.inputs.RowItems_List.Count} 个插槽，当前有 {inputInv.Data.itemSlots.Count} 个");
            return false;
        }
        return true;
    }

    private List<ItemData> PrepareOutputItems(RuntimeRecipeModel recipe)
    {
        var itemsToAdd = new List<ItemData>();
        
        foreach (var output in recipe.outputs.results)
        {
            if (!GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out GameObject outputPrefab) || outputPrefab == null)
            {
                Debug.LogError($"配方 {recipe.Id} 找不到输出物品：{output.ItemName}");
                return null;
            }

            Item outputitem = outputPrefab.GetComponent<Item>();
            if (outputitem == null)
            {
                Debug.LogError($"配方 {recipe.Id} 的输出 Prefab 缺少 Item：{output.ItemName}");
                return null;
            }
            ItemData newItem = outputitem.Get_NewItemData();
            newItem.Stack.Amount = output.amount;

            itemsToAdd.Add(newItem);
        }
        
        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, 
    RuntimeRecipeModel recipe, List<ItemData> outputItems, bool isMirrorMatched)
{
    // 检查recipe.inputs是有规则合成还是无规则合成
    if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
    {
        // 有规则合成按照原有逻辑走
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = GetOrderedRequired(recipe, i, isMirrorMatched, inputInv.Data.itemSlots.Count);

            if (required.amount == 0) continue;

            if (slot.itemData == null)
                return false;

            if (slot.itemData.Stack.Amount < required.amount)
                return false;
        }
    }
    else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
    {
        // 如果是无规则合成 则通过遍历recipe.inputs.RowItems_List查找对应的required 并检查是否有足够的物品
        foreach (var required in recipe.inputs.RowItems_List)
        {
            if (required.amount == 0) continue;

            // 在输入库存中查找匹配的物品
            float foundAmount = 0;
            foreach (var slot in inputInv.Data.itemSlots)
            {
                if (slot.itemData == null) continue;

                bool isMatch = false;
                // 根据匹配模式检查是否匹配
                if (required.matchMode == MatchMode.ExactItem)
                {
                    isMatch = slot.itemData.IDName == required.ItemName;
                }
                else if (required.matchMode == MatchMode.ByTag)
                {
                    isMatch = slot.itemData.Tags != null && 
                             slot.itemData.Tags != null && 
                             slot.itemData.Tags.Contains(required.Tag);
                }

                if (isMatch)
                {
                    foundAmount += slot.itemData.Stack.Amount;
                }
            }

            // 检查找到的数量是否足够
            if (foundAmount < required.amount)
                return false;
        }
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
    RuntimeRecipeModel recipe, List<ItemData> outputItems, bool isMirrorMatched)
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
if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
{
    // 有序合成 - 按位置对应扣除
    for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
    {
        var slot = inputInv.Data.itemSlots[i];
        var required = GetOrderedRequired(recipe, i, isMirrorMatched, inputInv.Data.itemSlots.Count);

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
}
else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
{
    // 无序合成 - 根据配方需求查找并扣除对应物品
    foreach (var required in recipe.inputs.RowItems_List)
    {
        if (required.amount == 0) continue;

        float remainingAmountToConsume = required.amount;
        
        // 遍历所有槽位查找匹配的物品
        for (int i = 0; i < inputInv.Data.itemSlots.Count && remainingAmountToConsume > 0; i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            if (slot.itemData == null) continue;

            // 检查物品是否匹配需求
            bool isMatch = false;
            if (required.matchMode == MatchMode.ExactItem)
            {
                isMatch = slot.itemData.IDName == required.ItemName;
            }
            else if (required.matchMode == MatchMode.ByTag)
            {
                isMatch = slot.itemData.Tags != null && 
                         slot.itemData.Tags != null && 
                         slot.itemData.Tags.Contains(required.Tag);
            }

            if (isMatch && slot.itemData.Stack.Amount > 0)
            {
                // 计算本次可以消耗的数量
                float consumeAmount = Mathf.Min(remainingAmountToConsume, slot.itemData.Stack.Amount);
                slot.itemData.Stack.Amount -= consumeAmount;
                remainingAmountToConsume -= consumeAmount;
                
                Debug.Log($"插槽 {i}：消耗 {slot.itemData.IDName} x{consumeAmount}，剩余 {slot.itemData.Stack.Amount}");
                
                // 如果物品用完，移除物品
                if (slot.itemData.Stack.Amount <= 0)
                {
                    Debug.Log($"插槽 {i}：{slot.itemData.IDName} 已耗尽，移除物品");
                    inputInv.Data.RemoveItemAll(slot, i);
                }
                
                inputInv.RefreshUI(i);
            }
        }
    }
}
        
        // 执行配方动作（添加空值检查）
        RecipeActionRunner.Execute(recipe, inputInv);

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"合成完成：{recipe.name}");

        Player actor = item as Player ?? item?.Owner as Player ?? item?.GetComponentInParent<Player>();
        for (int i = 0; i < outputItems.Count; i++)
            GameplayProgressEvents.PublishCraftSucceeded(actor, outputItems[i]?.IDName);
    }

    private static bool TryBuildMirroredInputList(Input_List source, out Input_List mirrored)
    {
        mirrored = null;
        int count = source.RowItems_List.Count;
        int gridSize = Mathf.RoundToInt(Mathf.Sqrt(count));
        if (gridSize * gridSize != count)
            return false;

        mirrored = new Input_List();
        mirrored.recipeType = source.recipeType;
        mirrored.inputOrder = source.inputOrder;

        for (int row = 0; row < gridSize; row++)
        {
            for (int col = 0; col < gridSize; col++)
            {
                int sourceIndex = row * gridSize + (gridSize - 1 - col);
                CraftingIngredient ingredient = source.RowItems_List[sourceIndex];
                if (ingredient.matchMode == MatchMode.ByTag)
                    mirrored.AddTagItem(ingredient.Tag);
                else
                    mirrored.AddNameItem(ingredient.ItemName);
            }
        }

        return true;
    }

    private static CraftingIngredient GetOrderedRequired(RuntimeRecipeModel recipe, int slotIndex, bool isMirrorMatched, int slotCount)
    {
        if (!isMirrorMatched)
            return recipe.inputs.RowItems_List[slotIndex];

        int gridSize = Mathf.RoundToInt(Mathf.Sqrt(slotCount));
        if (gridSize * gridSize != slotCount)
            return recipe.inputs.RowItems_List[slotIndex];

        int row = slotIndex / gridSize;
        int col = slotIndex % gridSize;
        int mirroredIndex = row * gridSize + (gridSize - 1 - col);
        return recipe.inputs.RowItems_List[mirroredIndex];
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
        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            interactMod.OnAction_Start += Interact_Start;
            interactMod.OnAction_Stop += Interact_Stop;
        }
    }

    private void CleanupEventListeners()
    {
        workButton?.onClick.RemoveListener(OnCraftButtonClick);
        if (inputInventory?.Data != null)
            inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;

        if (item.itemMods.GetMod_ByID(ModText.Interact, out Mod_InteractReciver interactMod))
        {
            interactMod.OnAction_Start -= Interact_Start;
            interactMod.OnAction_Stop -= Interact_Stop;
        }
    }

    private void BindCraftPreview()
    {
        ItemSlot_UI outputSlot = outputInventory.itemSlot_UI.FirstOrDefault();
        _outputPreview = CraftingOutputPreview.Attach(outputInventory.basePanel ?? basePanel, outputSlot);

        inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;
        inputInventory.Data.Event_OnDataChanged += OnInputSlotChanged;
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
        throw new NotImplementedException();
    }

    #endregion
}
