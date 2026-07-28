using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using UnityEngine;
using UnityEngine.UI;
using RuntimeRecipeModel = RuntimeRecipe;

public class Mod_HandCraftTable : Module, IInventory, IInstanceUI
{
#region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

#endregion

#region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();
    [Tooltip("2x2输入容器（输入_1~输入_4）")]
    public Inventory inputInventory;
    [Tooltip("输出容器（仅输出_1）")]
    public Inventory outputInventory;
    public BasePanel basePanel;
    public GameObject InventoryPanel_Prefab;
    [Tooltip("手工合成台UI预制体名，Inspector未手动拖拽时会按此名称从GameRes回填")]
    public string InventoryPanelPrefabName = "UI_WorkBench";

    [Header("交互组件")]
    [Tooltip("合成按钮")]
    public Button workButton;
    [Tooltip("打开/关闭手工合成台的按键")]
    public KeyCode toggleKey = KeyCode.H;
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

    private const int InputSlotCount = 4;
    private const int OutputSlotCount = 1;
    private const RecipeInputRule UnorderedRule = (RecipeInputRule)0;
    private const RecipeInputRule OrderedRule = (RecipeInputRule)1;

    [Header("调试")]
    [Tooltip("是否输出手工合成详细调试日志")]
    public bool EnableCraftDebug = true;

#endregion

#region 生命周期

    public void OnValidate()
    {
        _Data.Name = $"{ModText.WorkBench}_手工";
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);
        InitData();
    }

    public override void Save()
    {
        if (inputInventory?.Data != null)
            inputInventory.Data.Event_OnDataChanged -= OnInputSlotChanged;

        ModSaveData.WriteData(RawData);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!Input.GetKeyDown(toggleKey))
            return;

        TogglePanelByKey();
    }

#endregion

#region UI与交互

    private void TogglePanelByKey()
    {
        EnsurePanelCreated();
        if (basePanel == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 面板为空，无法切换显示");
            return;
        }

        if (basePanel.IsOpen())
        {
            basePanel.Close();
            inputInventory.SyncQuickTransferTarget(basePanel);
            inputInventory.DefaultTarget_Inventory = null;
            outputInventory.DefaultTarget_Inventory = null;
            return;
        }

        var handInv = GetPlayerHandInventory();
        if (handInv == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 玩家手部容器为空，无法打开手工合成台");
            return;
        }

        inputInventory.DefaultTarget_Inventory = handInv;
        outputInventory.DefaultTarget_Inventory = handInv;
        basePanel.Open();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    public bool EnsurePanelCreated()
    {
        if (basePanel != null)
            return false;

        EnsureInventoryPanelPrefabAssigned();
        if (InventoryPanel_Prefab == null)
        {
            Debug.LogError($"[Mod_HandCraftTable] InventoryPanel_Prefab 未设置，且无法通过 {InventoryPanelPrefabName} 回填面板预制体");
            return false;
        }

        basePanel = UIManager.Instance.CreatePanelFromGameObject(InventoryPanel_Prefab).GetComponentInChildren<BasePanel>();
        if (basePanel == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 创建面板失败，未找到 BasePanel");
            return false;
        }

        if (basePanel.GetText("窗口信息") != null)
            basePanel.GetText("窗口信息").text = _Data.Name;

        InitUI();
        basePanel.Close();
        return true;
    }

    private void EnsureInventoryPanelPrefabAssigned()
    {
        if (InventoryPanel_Prefab != null)
            return;

        if (GameRes.Instance == null)
            return;

        if (!string.IsNullOrWhiteSpace(InventoryPanelPrefabName))
        {
            InventoryPanel_Prefab = GameRes.Instance.GetPrefab(InventoryPanelPrefabName);
            if (InventoryPanel_Prefab != null)
                return;
        }

        string[] fallbackPrefabNames = { "UI_WorkBench", "UI_HandCraftTable", "UI_MakeTable" };
        foreach (string prefabName in fallbackPrefabNames)
        {
            if (string.Equals(prefabName, InventoryPanelPrefabName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            InventoryPanel_Prefab = GameRes.Instance.GetPrefab(prefabName);
            if (InventoryPanel_Prefab != null)
            {
                InventoryPanelPrefabName = prefabName;
                return;
            }
        }
    }

    public void InitData()
    {
        ValidateInventoryConfig();
        InitializeInventoryData(inputInventory, nameof(inputInventory));
        InitializeInventoryData(outputInventory, nameof(outputInventory));
    }

    public void InitUI()
    {
        _currentClickProgress = 0;

        BindInputSlots();
        BindOutputSlot();

        inputInventory.SyncData();
        outputInventory.SyncData();
        BindCraftPreview();

        workButton = basePanel.GetButton("合成按钮");
        if (workButton == null)
        {
            Debug.LogError("[Mod_HandCraftTable] 未找到合成按钮");
            return;
        }

        workButton.onClick.RemoveListener(OnCraftButtonClick);
        workButton.onClick.AddListener(OnCraftButtonClick);

        inputInventory.RefreshUI();
        outputInventory.RefreshUI();
        RefreshCraftPreview();
    }

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
        LogCraftDebug($"点击进度：{_currentClickProgress}/{requiredClickCount}，等级={workbenchLevel}");

        if (_currentClickProgress < requiredClickCount)
            return;

        bool craftResult = Craft(inputInventory, outputInventory);
        ResetCraftProgress();
        RefreshCraftPreview();
        if (craftResult)
            _outputPreview?.PlaySuccess();

        if (!craftResult)
        {
            LogCraftDebug("合成失败，已重置点击进度");
        }
    }

    private void BindCraftPreview()
    {
        if (outputInventory.itemSlot_UI.Count == 0)
            return;

        _outputPreview = CraftingOutputPreview.Attach(basePanel, outputInventory.itemSlot_UI[0]);
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
            _outputPreview.Show(previewItem, _currentClickProgress / (float)RequiredClickCount);
        else
            _outputPreview.Clear();
    }

    private void BindInputSlots()
    {
        inputInventory.itemSlot_UI.Clear();

        for (int i = 1; i <= InputSlotCount; i++)
        {
            var button = basePanel.GetButton($"输入_{i}");
            if (button == null)
                throw new System.NullReferenceException($"[Mod_HandCraftTable] 未找到输入按钮 输入_{i}");

            var slotUI = button.GetComponent<ItemSlot_UI>();
            if (slotUI == null)
                throw new System.NullReferenceException($"[Mod_HandCraftTable] 输入_{i} 缺少 ItemSlot_UI");

            inputInventory.BindSlotUI(slotUI, i - 1);
        }
    }

    private void BindOutputSlot()
    {
        outputInventory.itemSlot_UI.Clear();

        var button = basePanel.GetButton("输出_1");
        if (button == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] 未找到输出按钮 输出_1");

        var slotUI = button.GetComponent<ItemSlot_UI>();
        if (slotUI == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] 输出_1 缺少 ItemSlot_UI");

        outputInventory.BindSlotUI(slotUI, 0);
    }

    private Inventory GetPlayerHandInventory()
    {
        var handMod = item.GetComponentInChildren<Mod_Hand>();
        if (handMod == null)
            return null;

        return handMod.HandInventory;
    }

    private static void InitializeInventoryData(Inventory inventory, string inventoryName)
    {
        if (inventory == null || inventory.Data == null)
            throw new System.NullReferenceException($"[Mod_HandCraftTable] {inventoryName} 或 Data 为空");

        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            inventory.Data.itemSlots[i].Index = i;
            inventory.Data.itemSlots[i].SlotMaxVolume = 100;
        }

        inventory.Data.Event_RefreshUI = new();
        inventory.Data.Event_RefreshUI += inventory.RefreshUI;
    }

    private void ValidateInventoryConfig()
    {
        if (inputInventory == null || inputInventory.Data == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] inputInventory 未配置");

        if (outputInventory == null || outputInventory.Data == null)
            throw new System.NullReferenceException("[Mod_HandCraftTable] outputInventory 未配置");

        if (inputInventory.Data.itemSlots.Count < InputSlotCount)
            throw new System.InvalidOperationException($"[Mod_HandCraftTable] 输入槽位不足，至少需要 {InputSlotCount} 个");

        if (outputInventory.Data.itemSlots.Count < OutputSlotCount)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] 输出槽位不足，至少需要 1 个");
    }

    public Inventory GetDefaultTargetInventory()
    {
        return inputInventory;
    }

    public void I_ShowPanel()
    {
        EnsurePanelCreated();
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] basePanel 为空，无法打开面板");

        var handInv = GetPlayerHandInventory();
        if (handInv == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] 玩家手部容器为空，无法打开面板");

        inputInventory.DefaultTarget_Inventory = handInv;
        outputInventory.DefaultTarget_Inventory = handInv;
        basePanel.Open();
        inputInventory.SyncQuickTransferTarget(basePanel);
    }

    public void I_ClosePanel()
    {
        if (basePanel == null)
            throw new System.InvalidOperationException("[Mod_HandCraftTable] basePanel 为空，无法关闭面板");

        basePanel.Close();
        inputInventory.SyncQuickTransferTarget(basePanel);
        inputInventory.DefaultTarget_Inventory = null;
        outputInventory.DefaultTarget_Inventory = null;
    }

    public void I_TogglePanel()
    {
        TogglePanelByKey();
    }

#endregion

#region 合成逻辑

    [Tooltip("输出用于匹配配方的键列表（2x2输入，支持物品名与Tag）")]
    private List<string> GenerateRecipeKey_List(Inventory inputInv, HashSet<string> mirroredKeys)
    {
        List<string> recipeKeys = new List<string>();
        HashSet<string> addedKeys = new HashSet<string>();
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        Input_List orderedInputList = new Input_List();
        orderedInputList.recipeType = RecipeType.Crafting;
        orderedInputList.inputOrder = OrderedRule;

        foreach (ItemSlot slot in inputSlots)
            orderedInputList.AddNameItem(slot.itemData?.IDName ?? "");

        string orderedKey = orderedInputList.ToString();
        AddKeyIfNotExists(recipeKeys, addedKeys, orderedKey);

        if (TryBuildMirroredInputList(orderedInputList, out Input_List mirroredOrderedList))
        {
            string mirroredOrderedKey = mirroredOrderedList.ToString();
            AddKeyIfNotExists(recipeKeys, addedKeys, mirroredOrderedKey);
            // 只有镜像键与原始键不同时，才标记为“镜像命中”
            if (mirroredOrderedKey != orderedKey)
                mirroredKeys.Add(mirroredOrderedKey);
        }

        orderedInputList.inputOrder = UnorderedRule;
        AddKeyIfNotExists(recipeKeys, addedKeys, orderedInputList.ToString());

        for (int i = 0; i < inputSlots.Count; i++)
        {
            var slot = inputSlots[i];
            if (slot.itemData == null || slot.itemData.Tags == null || slot.itemData.Tags.Count == 0)
                continue;

            Input_List orderedTagInputList = new Input_List();
            orderedTagInputList.recipeType = RecipeType.Crafting;
            orderedTagInputList.inputOrder = OrderedRule;

            for (int j = 0; j < inputSlots.Count; j++)
            {
                if (i == j)
                    orderedTagInputList.AddTagItem(slot.itemData.Tags[0]);
                else
                    orderedTagInputList.AddNameItem(inputSlots[j].itemData?.IDName ?? "");
            }

            string orderedTagKey = orderedTagInputList.ToString();
            AddKeyIfNotExists(recipeKeys, addedKeys, orderedTagKey);

            if (TryBuildMirroredInputList(orderedTagInputList, out Input_List mirroredOrderedTagList))
            {
                string mirroredOrderedTagKey = mirroredOrderedTagList.ToString();
                AddKeyIfNotExists(recipeKeys, addedKeys, mirroredOrderedTagKey);
                if (mirroredOrderedTagKey != orderedTagKey)
                    mirroredKeys.Add(mirroredOrderedTagKey);
            }

            orderedTagInputList.inputOrder = UnorderedRule;
            AddKeyIfNotExists(recipeKeys, addedKeys, orderedTagInputList.ToString());
        }

        return recipeKeys;
    }

    private void AddKeyIfNotExists(List<string> recipeKeys, HashSet<string> addedKeys, string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (!addedKeys.Add(key))
            return;

        recipeKeys.Add(key);
    }

    private string GenerateRecipeKey(Inventory inputInv)
    {
        Input_List inputList = new Input_List();
        inputList.recipeType = RecipeType.Crafting;

        foreach (ItemSlot slot in GetInputSlots(inputInv))
            inputList.AddNameItem(slot.itemData?.IDName ?? "");

        return inputList.ToString();
    }

    public bool Craft(Inventory inputInv, Inventory outputInv)
    {
        if (!TryResolveRecipe(inputInv, out RuntimeRecipeModel recipe, out bool isMirrorMatched, out List<string> recipeKeys))
        {
            Debug.LogError($"[Mod_HandCraftTable] 配方不存在：{string.Join(" 或 ", recipeKeys)}");
            return false;
        }

        if (!ValidateSlotCount(recipe))
            return false;

        var outputItems = PrepareOutputItems(recipe);
        if (outputItems == null || outputItems.Count == 0)
            return false;

        if (!CheckResourcesAndSpace(inputInv, outputInv, recipe, outputItems, isMirrorMatched))
        {
            Debug.LogError("[Mod_HandCraftTable] 合成失败：材料不足或输出空间不足");
            return false;
        }

        ExecuteCrafting(inputInv, outputInv, recipe, outputItems, isMirrorMatched);
        return true;
    }

    private bool TryGetCraftPreview(out ItemData previewItem)
    {
        previewItem = null;
        if (!TryResolveRecipe(inputInventory, out RuntimeRecipeModel recipe, out bool isMirrorMatched, out _))
            return false;

        if (!ValidateSlotCount(recipe))
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
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        LogCraftDebug($"开始匹配，输入槽快照={BuildSlotSnapshot(inputSlots)}");
        LogCraftDebug($"候选键数量={recipeKeys.Count}");

        recipe = null;
        isMirrorMatched = false;
        foreach (string recipeKey in recipeKeys)
        {
            LogCraftDebug($"尝试键：{recipeKey}，镜像键={mirroredKeys.Contains(recipeKey)}");

            if (!GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
            {
                LogCraftDebug("字典未命中该键");
                continue;
            }

            LogCraftDebug($"命中配方：{recipe.name}，规则={recipe.inputs.inputOrder}，允许镜像={recipe.enableMirrorCrafting}");

            bool isMirrorKey = mirroredKeys.Contains(recipeKey);
            if (isMirrorKey && recipe.inputs.inputOrder == OrderedRule && !recipe.enableMirrorCrafting)
            {
                LogCraftDebug("命中镜像键但配方未启用镜像，跳过");
                recipe = null;
                continue;
            }

            if (recipe.inputs.inputOrder == OrderedRule && !IsOrderedRecipeActuallyMatched(inputInv, recipe, isMirrorKey))
            {
                LogCraftDebug("有序配方严格校验失败，跳过该命中");
                recipe = null;
                continue;
            }

            isMirrorMatched = isMirrorKey;
            LogCraftDebug($"最终匹配成功：{recipe.name}，镜像匹配={isMirrorMatched}");
            return true;
        }

        return false;
    }

    private bool ValidateSlotCount(RuntimeRecipeModel recipe)
    {
        if (recipe.inputs.RowItems_List.Count != InputSlotCount)
        {
            Debug.LogError($"[Mod_HandCraftTable] 仅支持2x2配方，当前配方槽位数={recipe.inputs.RowItems_List.Count}");
            return false;
        }

        if (recipe.outputs.results.Count != 1)
        {
            Debug.LogError($"[Mod_HandCraftTable] 仅支持单输出结果，当前输出数量={recipe.outputs.results.Count}");
            return false;
        }

        return true;
    }

    private bool CanFullyStoreOutput(Inventory inventory, ItemData itemData)
    {
        if (inventory == null || inventory.Data == null || itemData == null || itemData.Stack == null)
            return false;

        return GetInventoryAddCapacity(inventory, itemData) >= itemData.Stack.Amount;
    }

    private float GetInventoryAddCapacity(Inventory inventory, ItemData itemData)
    {
        if (inventory == null || inventory.Data == null || itemData == null || itemData.Stack == null)
            return 0f;

        float unitVolume = itemData.Stack.Volume;

        if (unitVolume > 1)
        {
            return inventory.Data.itemSlots.Any(slot => slot != null && slot.itemData == null) ? itemData.Stack.Amount : 0f;
        }

        float totalCapacity = 0f;
        foreach (var slot in inventory.Data.itemSlots)
        {
            if (slot == null)
                continue;

            if (slot.itemData == null)
            {
                totalCapacity += slot.SlotMaxVolume;
                continue;
            }

            bool sameItem = slot.itemData.IDName == itemData.IDName
                && slot.itemData.ItemSpecialData == itemData.ItemSpecialData;

            if (!sameItem || slot.IsFull)
                continue;

            totalCapacity += Mathf.Max(0f, slot.SlotMaxVolume - slot.itemData.Stack.CurrentVolume);
        }

        return totalCapacity;
    }

    private List<ItemData> PrepareOutputItems(RuntimeRecipeModel recipe)
    {
        var itemsToAdd = new List<ItemData>();

        if (recipe == null || recipe.outputs == null || recipe.outputs.results == null)
            return null;

        foreach (var output in recipe.outputs.results)
        {
            if (output == null || string.IsNullOrEmpty(output.ItemName))
            {
                Debug.LogError($"[Mod_HandCraftTable] 配方输出项名称为空（配方：{recipe.name}）");
                return null;
            }

            if (GameRes.Instance == null ||
                GameRes.Instance.AllPrefabs == null ||
                !GameRes.Instance.AllPrefabs.TryGetValue(output.ItemName, out GameObject outputPrefab) ||
                outputPrefab == null)
            {
                Debug.LogError($"[Mod_HandCraftTable] 找不到有效的产物预制体：{output.ItemName}（配方：{recipe.name}）");
                return null;
            }

            Item outputItem = outputPrefab.GetComponent<Item>();
            if (outputItem == null)
            {
                Debug.LogError($"[Mod_HandCraftTable] 产物预制体缺少 Item 组件：{output.ItemName}（配方：{recipe.name}）");
                return null;
            }

            ItemData newItem = outputItem.Get_NewItemData();
            if (newItem == null || newItem.Stack == null)
            {
                Debug.LogError($"[Mod_HandCraftTable] 无法创建产物数据：{output.ItemName}（配方：{recipe.name}）");
                return null;
            }

            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }

        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, RuntimeRecipeModel recipe, List<ItemData> outputItems, bool isMirrorMatched)
    {
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        if (recipe.inputs.inputOrder == OrderedRule)
        {
            for (int i = 0; i < InputSlotCount; i++)
            {
                var slot = inputSlots[i];
                var required = GetOrderedRequired(recipe, i, isMirrorMatched, InputSlotCount);

                if (required.amount == 0)
                    continue;

                if (slot.itemData == null)
                {
                    LogCraftDebug($"资源校验失败：槽位{i}为空，需求={DescribeIngredient(required)} x{required.amount}");
                    return false;
                }

                if (slot.itemData.Stack.Amount < required.amount)
                {
                    LogCraftDebug($"资源校验失败：槽位{i}数量不足，实际={slot.itemData.Stack.Amount}，需求={required.amount}，物品={slot.itemData.IDName}");
                    return false;
                }

            }
        }
        else
        {
            foreach (var required in recipe.inputs.RowItems_List)
            {
                if (required.amount == 0)
                    continue;

                float foundAmount = 0;
                foreach (var slot in inputSlots)
                {
                    if (slot.itemData == null)
                        continue;

                    bool isMatch = required.matchMode == MatchMode.ExactItem
                        ? slot.itemData.IDName == required.ItemName
                        : slot.itemData.Tags != null && slot.itemData.Tags.Contains(required.Tag);

                    if (isMatch)
                        foundAmount += slot.itemData.Stack.Amount;
                }

                if (foundAmount < required.amount)
                {
                    LogCraftDebug($"无序资源校验失败：需求={DescribeIngredient(required)} x{required.amount}，找到={foundAmount}");
                    return false;
                }
            }
        }

        foreach (var itemData in outputItems)
        {
            if (CanFullyStoreOutput(outputInv, itemData))
                continue;

            // 输出背包无空间，尝试把输入槽作为临时输出口（先检测是否有空输入槽）
            var currentInputSlots = GetInputSlots(inputInv);
            bool hasEmptyInputWithCapacity = currentInputSlots.Any(s => s.itemData == null && s.SlotMaxVolume >= itemData.Stack.Amount);
            if (hasEmptyInputWithCapacity)
            {
                LogCraftDebug($"输出空间不足，但检测到空输入槽且容量足够，可将输出放入输入槽：{itemData?.IDName} x{itemData?.Stack.Amount}");
                continue;
            }

            // 模拟扣除后是否有可用输入槽（被消耗后的槽位或堆叠空位）
            if (recipe.inputs.inputOrder == OrderedRule)
            {
                bool found = false;
                for (int i = 0; i < InputSlotCount; i++)
                {
                    var slot = currentInputSlots[i];
                    var required = GetOrderedRequired(recipe, i, isMirrorMatched, InputSlotCount);
                    float remain = (slot.itemData == null ? 0f : slot.itemData.Stack.Amount) - required.amount;

                    if (remain <= 0)
                    {
                        // 扣除后该槽位将为空，检查是否能容纳输出
                        if (slot.SlotMaxVolume >= itemData.Stack.Amount)
                        {
                            found = true;
                            break;
                        }
                    }
                    else
                    {
                        // 扣除后若为同类物品且有剩余容量，也可用于放置输出
                        if (slot.itemData != null && slot.itemData.IDName == itemData.IDName)
                        {
                            float canAdd = slot.SlotMaxVolume - remain;
                            if (canAdd >= itemData.Stack.Amount)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                }

                if (found)
                    continue;

                LogCraftDebug($"输出空间不足：{itemData?.IDName} x{itemData?.Stack.Amount}");
                return false;
            }
            else
            {
                // 无序配方，模拟扣除过程以判断释放的槽位容量
                float[] remainAmounts = new float[InputSlotCount];
                for (int i = 0; i < InputSlotCount; i++)
                    remainAmounts[i] = currentInputSlots[i].itemData == null ? 0f : currentInputSlots[i].itemData.Stack.Amount;

                foreach (var required in recipe.inputs.RowItems_List)
                {
                    if (required.amount == 0) continue;
                    float need = required.amount;
                    for (int i = 0; i < InputSlotCount && need > 0; i++)
                    {
                        var slot = currentInputSlots[i];
                        if (slot.itemData == null) continue;
                        bool isMatch = required.matchMode == MatchMode.ExactItem
                            ? slot.itemData.IDName == required.ItemName
                            : slot.itemData.Tags != null && slot.itemData.Tags.Contains(required.Tag);
                        if (!isMatch || remainAmounts[i] <= 0) continue;
                        float consume = Mathf.Min(need, remainAmounts[i]);
                        remainAmounts[i] -= consume;
                        need -= consume;
                    }
                    if (need > 0)
                    {
                        LogCraftDebug($"无序配方模拟扣除失败：需求未满足");
                        return false;
                    }
                }

                bool found = false;
                for (int i = 0; i < InputSlotCount; i++)
                {
                    var slot = currentInputSlots[i];
                    float remain = remainAmounts[i];
                    if (remain <= 0)
                    {
                        if (slot.SlotMaxVolume >= itemData.Stack.Amount)
                        {
                            found = true;
                            break;
                        }
                    }
                    else if (slot.itemData != null && slot.itemData.IDName == itemData.IDName)
                    {
                        float canAdd = slot.SlotMaxVolume - remain;
                        if (canAdd >= itemData.Stack.Amount)
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                    continue;

                LogCraftDebug($"输出空间不足：{itemData?.IDName} x{itemData?.Stack.Amount}");
                return false;
            }
        }

        return true;
    }

    private void ExecuteCrafting(Inventory inputInv, Inventory outputInv, RuntimeRecipeModel recipe, List<ItemData> outputItems, bool isMirrorMatched)
    {
        Debug.Log($"[Mod_HandCraftTable] 开始合成：{recipe.name}，输入={GenerateRecipeKey(inputInv)}");

        bool outputNeedsInputSlot = outputItems.Any(itemData => !CanFullyStoreOutput(outputInv, itemData));

        if (outputNeedsInputSlot)
        {
            // 先扣除输入（腾出槽位）
            if (recipe.inputs.inputOrder == OrderedRule)
                ExecuteOrderedDeduction(inputInv, recipe, isMirrorMatched);
            else
                ExecuteUnorderedDeduction(inputInv, recipe);

            // 再放输出：输出口不足时，整件产物直接落到输入槽
            foreach (var itemData in outputItems)
            {
                if (CanFullyStoreOutput(outputInv, itemData))
                    outputInv.Data.TryAddItem(itemData, true);
                else
                    inputInv.Data.TryAddItem(itemData, true);
            }
        }
        else
        {
            // 常规流程：先放输出，再扣除输入
            foreach (var itemData in outputItems)
                outputInv.Data.TryAddItem(itemData, true);

            if (recipe.inputs.inputOrder == OrderedRule)
                ExecuteOrderedDeduction(inputInv, recipe, isMirrorMatched);
            else
                ExecuteUnorderedDeduction(inputInv, recipe);
        }

        RecipeActionRunner.Execute(recipe, inputInv);

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"[Mod_HandCraftTable] 合成完成：{recipe.name}");

        Player actor = ResolveCraftActor();
        for (int i = 0; i < outputItems.Count; i++)
            GameplayProgressEvents.PublishCraftSucceeded(actor, outputItems[i]?.IDName);
    }

    private Player ResolveCraftActor()
    {
        return item as Player ?? item?.Owner as Player ?? item?.GetComponentInParent<Player>();
    }

    private void ExecuteOrderedDeduction(Inventory inputInv, RuntimeRecipeModel recipe, bool isMirrorMatched)
    {
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        for (int i = 0; i < InputSlotCount; i++)
        {
            var slot = inputSlots[i];
            var required = GetOrderedRequired(recipe, i, isMirrorMatched, InputSlotCount);

            if (required.amount == 0 || slot.itemData == null)
                continue;

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
                inputInv.Data.RemoveItemAll(slot, i);

            inputInv.RefreshUI(i);
        }
    }

    private void ExecuteUnorderedDeduction(Inventory inputInv, RuntimeRecipeModel recipe)
    {
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        foreach (var required in recipe.inputs.RowItems_List)
        {
            if (required.amount == 0)
                continue;

            float remain = required.amount;
            for (int i = 0; i < InputSlotCount && remain > 0; i++)
            {
                var slot = inputSlots[i];
                if (slot.itemData == null)
                    continue;

                bool isMatch = required.matchMode == MatchMode.ExactItem
                    ? slot.itemData.IDName == required.ItemName
                    : slot.itemData.Tags != null && slot.itemData.Tags.Contains(required.Tag);

                if (!isMatch || slot.itemData.Stack.Amount <= 0)
                    continue;

                float consume = Mathf.Min(remain, slot.itemData.Stack.Amount);
                slot.itemData.Stack.Amount -= consume;
                remain -= consume;

                if (slot.itemData.Stack.Amount <= 0)
                    inputInv.Data.RemoveItemAll(slot, i);

                inputInv.RefreshUI(i);
            }
        }
    }

    private List<ItemSlot> GetInputSlots(Inventory inputInv)
    {
        return inputInv.Data.itemSlots.Take(InputSlotCount).ToList();
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
        int row = slotIndex / gridSize;
        int col = slotIndex % gridSize;
        int mirroredIndex = row * gridSize + (gridSize - 1 - col);
        return recipe.inputs.RowItems_List[mirroredIndex];
    }

    private bool IsOrderedRecipeActuallyMatched(Inventory inputInv, RuntimeRecipeModel recipe, bool isMirrorMatched)
    {
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);
        LogCraftDebug($"开始有序严格校验：配方={recipe.name}，镜像匹配={isMirrorMatched}");

        for (int i = 0; i < InputSlotCount; i++)
        {
            CraftingIngredient required = GetOrderedRequired(recipe, i, isMirrorMatched, InputSlotCount);
            ItemSlot slot = inputSlots[i];

            if (required.amount == 0)
            {
                LogCraftDebug($"严格校验槽位{i}：需求为空位，跳过");
                continue;
            }

            if (slot.itemData == null)
            {
                LogCraftDebug($"严格校验失败：槽位{i}为空，需求={DescribeIngredient(required)} x{required.amount}");
                return false;
            }

            bool isMatch = required.matchMode == MatchMode.ExactItem
                ? slot.itemData.IDName == required.ItemName
                : slot.itemData.Tags != null && slot.itemData.Tags.Contains(required.Tag);

            if (!isMatch || slot.itemData.Stack.Amount < required.amount)
            {
                LogCraftDebug($"严格校验失败：槽位{i} 实际={slot.itemData.IDName} x{slot.itemData.Stack.Amount}，需求={DescribeIngredient(required)} x{required.amount}，名称匹配={isMatch}");
                return false;
            }

            LogCraftDebug($"严格校验槽位{i}通过：实际={slot.itemData.IDName} x{slot.itemData.Stack.Amount}，需求={DescribeIngredient(required)} x{required.amount}");
        }

        LogCraftDebug("有序严格校验通过");
        return true;
    }

    private void LogCraftDebug(string message)
    {
        if (!EnableCraftDebug)
            return;

        Debug.Log($"[Mod_HandCraftTable][Debug] {message}");
    }

    private string BuildSlotSnapshot(List<ItemSlot> slots)
    {
        List<string> texts = new List<string>();
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.itemData == null)
            {
                texts.Add($"{i}:空");
                continue;
            }

            texts.Add($"{i}:{slot.itemData.IDName}x{slot.itemData.Stack.Amount}");
        }

        return string.Join(" | ", texts);
    }

    private string DescribeIngredient(CraftingIngredient ingredient)
    {
        if (ingredient.matchMode == MatchMode.ByTag)
            return $"Tag:{ingredient.Tag}";

        return ingredient.ItemName;
    }

#endregion
}
