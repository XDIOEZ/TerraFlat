using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
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
    /// 计算最小包围网格并生成对应的配方键
    /// </summary>
    /// <param name="inputInv">输入物品栏</param>
    /// <returns>基于最小包围网格的配方键列表</returns>
    private List<string> CalculateMinimalBoundingGrid(Inventory inputInv)
    {
        List<string> result = new List<string>();

        // 假设是正方形网格，计算网格大小
        int gridSize = Mathf.RoundToInt(Mathf.Sqrt(inputInv.Data.itemSlots.Count));

        // 找出包含物品的最小矩形边界
        int minRow = gridSize; // 初始化为最大可能值
        int maxRow = -1;       // 初始化为最小可能值
        int minCol = gridSize;
        int maxCol = -1;

        // 遍历所有槽位，找出包含物品的最小矩形边界
        for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
        {
            if (inputInv.Data.itemSlots[i].itemData != null)
            {
                int row = i / gridSize;
                int col = i % gridSize;

                minRow = Mathf.Min(minRow, row);
                maxRow = Mathf.Max(maxRow, row);
                minCol = Mathf.Min(minCol, col);
                maxCol = Mathf.Max(maxCol, col);
            }
        }

        // 如果找到了物品（边界被更新过）
        if (maxRow >= 0 && maxCol >= 0)
        {
            // 生成基于最小包围网格的Input_List
            Input_List minimalGridList = new Input_List();
            minimalGridList.recipeType = RecipeType.Crafting;

            // 填充最小包围网格内的物品
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    int slotIndex = row * gridSize + col;
                    if (slotIndex < inputInv.Data.itemSlots.Count && inputInv.Data.itemSlots[slotIndex].itemData != null)
                    {
                        minimalGridList.AddNameItem(inputInv.Data.itemSlots[slotIndex].itemData.IDName);
                    }
                    else
                    {
                        minimalGridList.AddNameItem("");
                    }
                }
            }

            // 生成有序合成的配方键
            minimalGridList.inputOrder = RecipeInputRule.规则合成;
            result.Add(minimalGridList.ToString());

            // 生成无序合成的配方键
            minimalGridList.inputOrder = RecipeInputRule.无规则合成;
            result.Add(minimalGridList.ToString());

            // 为最小包围网格内的每个有Tag的物品生成Tag版本的配方键
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    int slotIndex = row * gridSize + col;
                    var slot = inputInv.Data.itemSlots[slotIndex];

                    if (slot.itemData != null && slot.itemData.Tags != null &&
                        slot.itemData.Tags != null && slot.itemData.Tags.Count > 0)
                    {
                        // 创建基于Tag的Input_List
                        Input_List tagGridList = new Input_List();
                        tagGridList.recipeType = RecipeType.Crafting;

                        for (int r = minRow; r <= maxRow; r++)
                        {
                            for (int c = minCol; c <= maxCol; c++)
                            {
                                int currentSlotIndex = r * gridSize + c;
                                var currentSlot = inputInv.Data.itemSlots[currentSlotIndex];

                                if (r == row && c == col)
                                {
                                    // 使用第一个标签
                                    tagGridList.AddTagItem(slot.itemData.Tags[0]);
                                }
                                else
                                {
                                    tagGridList.AddNameItem(currentSlot.itemData?.IDName ?? "");
                                }
                            }
                        }

                        // 添加有序合成的Tag版本
                        tagGridList.inputOrder = RecipeInputRule.规则合成;
                        result.Add(tagGridList.ToString());

                        // 添加无序合成的Tag版本
                        tagGridList.inputOrder = RecipeInputRule.无规则合成;
                        result.Add(tagGridList.ToString());
                    }
                }
            }
        }

        return result;
    }
    /// <summary>
    /// 执行合成操作
    /// </summary>
    public bool Craft(Inventory inputInv, Inventory outputInv)
    {
        if (!TryResolveRecipe(inputInv, out Recipe recipe, out bool isMirrorMatched, out List<string> recipeKeys))
        {
            Debug.LogError($"配方 {string.Join(" 或 ", recipeKeys)} 不存在");
            return false;
        }

        // // 验证输入槽位数量
        // if (!ValidateSlotCount(inputInv, recipe))
        //     return false;

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
        if (outputInventory == null)
            return false;

        if (!TryResolveRecipe(inputInventory, out Recipe recipe, out bool isMirrorMatched, out _))
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
        out Recipe recipe,
        out bool isMirrorMatched,
        out List<string> recipeKeys)
    {
        HashSet<string> mirroredKeys = new HashSet<string>();
        recipeKeys = GenerateRecipeKey_List(inputInv, mirroredKeys);
        recipeKeys.AddRange(CalculateMinimalBoundingGrid(inputInv));

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
    /// 玩家结束交互
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

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, Recipe recipe, List<ItemData> outputItems, bool isMirrorMatched)
    {
        // 检查recipe.inputs是有规则合成还是无规则合成
        if (recipe.inputs.inputOrder == RecipeInputRule.规则合成)
        {
            // 计算输入槽位的网格大小
            int inputGridSize = Mathf.RoundToInt(Mathf.Sqrt(inputInv.Data.itemSlots.Count));
            int recipeGridSize = Mathf.RoundToInt(Mathf.Sqrt(recipe.inputs.RowItems_List.Count));

            // 如果配方网格大小小于输入网格大小，尝试使用最小包围网格匹配
            if (recipeGridSize < inputGridSize)
            {
                // 找出包含物品的最小矩形边界
                int minRow = inputGridSize;
                int maxRow = -1;
                int minCol = inputGridSize;
                int maxCol = -1;

                for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
                {
                    if (inputInv.Data.itemSlots[i].itemData != null)
                    {
                        int row = i / inputGridSize;
                        int col = i % inputGridSize;

                        minRow = Mathf.Min(minRow, row);
                        maxRow = Mathf.Max(maxRow, row);
                        minCol = Mathf.Min(minCol, col);
                        maxCol = Mathf.Max(maxCol, col);
                    }
                }

                // 如果找到物品并且边界大小与配方匹配
                int boundedHeight = maxRow - minRow + 1;
                int boundedWidth = maxCol - minCol + 1;

                if (maxRow >= 0 && maxCol >= 0 && boundedHeight == recipeGridSize && boundedWidth == recipeGridSize)
                {
                    // 在最小包围网格内检查资源
                    for (int r = 0; r < recipeGridSize; r++)
                    {
                        for (int c = 0; c < recipeGridSize; c++)
                        {
                            int recipeIndex = r * recipeGridSize + c;
                            int inputIndex = (minRow + r) * inputGridSize + (minCol + c);

                            var required = GetOrderedRequiredByRecipeIndex(recipe, recipeIndex, isMirrorMatched, recipeGridSize);
                            if (required.amount == 0) continue;

                            // 检查输入槽位是否有效且包含物品
                            if (inputIndex >= inputInv.Data.itemSlots.Count || inputInv.Data.itemSlots[inputIndex].itemData == null)
                                return false;

                            var slot = inputInv.Data.itemSlots[inputIndex];
                            if (slot.itemData.Stack.Amount < required.amount)
                                return false;
                        }
                    }
                    // 最小包围网格匹配成功，继续检查输出空间
                }
                else
                {
                    // 如果边界与配方不匹配，尝试传统的位置匹配作为后备方案
                    // 但只在输入和配方网格大小相同时执行
                    if (inputGridSize == recipeGridSize)
                    {
                        for (int i = 0; i < Mathf.Min(inputInv.Data.itemSlots.Count, recipe.inputs.RowItems_List.Count); i++)
                        {
                            var slot = inputInv.Data.itemSlots[i];
                            var required = GetOrderedRequired(recipe, i, isMirrorMatched, inputInv.Data.itemSlots.Count);

                            if (required.amount == 0) continue;

                            if (slot.itemData == null || slot.itemData.Stack.Amount < required.amount)
                                return false;
                        }
                    }
                    else
                    {
                        // 如果网格大小不匹配且找不到合适的最小包围网格，返回失败
                        return false;
                    }
                }
            }
            else
            {
                // 原始逻辑：当配方网格大小大于等于输入网格大小时
                for (int i = 0; i < Mathf.Min(inputInv.Data.itemSlots.Count, recipe.inputs.RowItems_List.Count); i++)
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
        }
        else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
        {
            // 无规则合成逻辑保持不变
            foreach (var required in recipe.inputs.RowItems_List)
            {
                if (required.amount == 0) continue;

                float foundAmount = 0;
                foreach (var slot in inputInv.Data.itemSlots)
                {
                    if (slot.itemData == null) continue;

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

                    if (isMatch)
                    {
                        foundAmount += slot.itemData.Stack.Amount;
                    }
                }

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

    private void ExecuteCrafting(Inventory inputInv, Inventory outputInv, Recipe recipe, List<ItemData> outputItems, bool isMirrorMatched)
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
            // 计算输入槽位和配方的网格大小
            int inputGridSize = Mathf.RoundToInt(Mathf.Sqrt(inputInv.Data.itemSlots.Count));
            int recipeGridSize = Mathf.RoundToInt(Mathf.Sqrt(recipe.inputs.RowItems_List.Count));

            // 如果配方网格大小小于输入网格大小，尝试使用最小包围网格匹配扣除
            if (recipeGridSize < inputGridSize)
            {
                // 找出包含物品的最小矩形边界
                int minRow = inputGridSize;
                int maxRow = -1;
                int minCol = inputGridSize;
                int maxCol = -1;

                for (int i = 0; i < inputInv.Data.itemSlots.Count; i++)
                {
                    if (inputInv.Data.itemSlots[i].itemData != null)
                    {
                        int row = i / inputGridSize;
                        int col = i % inputGridSize;

                        minRow = Mathf.Min(minRow, row);
                        maxRow = Mathf.Max(maxRow, row);
                        minCol = Mathf.Min(minCol, col);
                        maxCol = Mathf.Max(maxCol, col);
                    }
                }

                // 如果找到物品并且边界大小与配方匹配
                int boundedHeight = maxRow - minRow + 1;
                int boundedWidth = maxCol - minCol + 1;

                if (maxRow >= 0 && maxCol >= 0 && boundedHeight == recipeGridSize && boundedWidth == recipeGridSize)
                {
                    // 在最小包围网格内扣除材料
                    for (int r = 0; r < recipeGridSize; r++)
                    {
                        for (int c = 0; c < recipeGridSize; c++)
                        {
                            int recipeIndex = r * recipeGridSize + c;
                            int inputIndex = (minRow + r) * inputGridSize + (minCol + c);

                            var required = GetOrderedRequiredByRecipeIndex(recipe, recipeIndex, isMirrorMatched, recipeGridSize);
                            if (required.amount == 0) continue;

                            // 检查输入槽位是否有效且包含物品
                            if (inputIndex < inputInv.Data.itemSlots.Count && inputInv.Data.itemSlots[inputIndex].itemData != null)
                            {
                                var slot = inputInv.Data.itemSlots[inputIndex];
                                Debug.Log($"插槽 {inputIndex}：需要 {required.ItemName} x{required.amount}，当前有 {slot.itemData.Stack.Amount}");

                                slot.itemData.Stack.Amount -= required.amount;
                                if (slot.itemData.Stack.Amount <= 0)
                                {
                                    Debug.Log($"插槽 {inputIndex}：{required.ItemName} 已耗尽，移除物品");
                                    inputInv.Data.RemoveItemAll(slot, inputIndex);
                                }
                                else
                                {
                                    Debug.Log($"插槽 {inputIndex}：剩余 {required.ItemName} x{slot.itemData.Stack.Amount}");
                                }
                                inputInv.RefreshUI(inputIndex);
                            }
                        }
                    }
                }
                else
                {
                    // 如果边界与配方不匹配，使用传统的位置扣除作为后备方案
                    ExecuteTraditionalDeduction(inputInv, recipe, isMirrorMatched);
                }
            }
            else
            {
                // 原始逻辑：当配方网格大小大于等于输入网格大小时
                ExecuteTraditionalDeduction(inputInv, recipe, isMirrorMatched);
            }
        }
        else if (recipe.inputs.inputOrder == RecipeInputRule.无规则合成)
        {
            // 无序合成逻辑保持不变
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
        if (recipe.action != null)
        {
            foreach (var action in recipe.action)
            {
                if (action != null)
                {
                    action.Apply(mod_Inventory);
                }
            }
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"合成完成：{recipe.name}");
    }

    // 提取传统的位置扣除逻辑为单独方法，方便复用
    private void ExecuteTraditionalDeduction(Inventory inputInv, Recipe recipe, bool isMirrorMatched)
    {
        for (int i = 0; i < Mathf.Min(inputInv.Data.itemSlots.Count, recipe.inputs.RowItems_List.Count); i++)
        {
            var slot = inputInv.Data.itemSlots[i];
            var required = GetOrderedRequired(recipe, i, isMirrorMatched, inputInv.Data.itemSlots.Count);

            if (required.amount == 0 || slot.itemData == null) continue;

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

    private static CraftingIngredient GetOrderedRequired(Recipe recipe, int slotIndex, bool isMirrorMatched, int slotCount)
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

    private static CraftingIngredient GetOrderedRequiredByRecipeIndex(Recipe recipe, int recipeIndex, bool isMirrorMatched, int recipeGridSize)
    {
        if (!isMirrorMatched)
            return recipe.inputs.RowItems_List[recipeIndex];

        int row = recipeIndex / recipeGridSize;
        int col = recipeIndex % recipeGridSize;
        int mirroredIndex = row * recipeGridSize + (recipeGridSize - 1 - col);
        return recipe.inputs.RowItems_List[mirroredIndex];
    }


    #endregion
}
