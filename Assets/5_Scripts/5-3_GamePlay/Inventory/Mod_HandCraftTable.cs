using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Mod_HandCraftTable : Module, IInventory
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

    [Header("交互组件")]
    [Tooltip("合成按钮")]
    public Button workButton;
    [Tooltip("打开/关闭手工合成台的按键")]
    public KeyCode toggleKey = KeyCode.H;

    private const int InputSlotCount = 4;
    private const int OutputSlotCount = 1;
    private const RecipeInputRule UnorderedRule = (RecipeInputRule)0;
    private const RecipeInputRule OrderedRule = (RecipeInputRule)1;

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

        if (basePanel.gameObject.activeInHierarchy)
        {
            inputInventory.DefaultTarget_Inventory = null;
            outputInventory.DefaultTarget_Inventory = null;
        }
        else
        {
            var handInv = GetPlayerHandInventory();
            if (handInv == null)
            {
                Debug.LogError("[Mod_HandCraftTable] 玩家手部容器为空，无法打开手工合成台");
                return;
            }

            inputInventory.DefaultTarget_Inventory = handInv;
            outputInventory.DefaultTarget_Inventory = handInv;
        }

        basePanel.Toggle();
    }

    public bool EnsurePanelCreated()
    {
        if (basePanel != null)
            return false;

        if (InventoryPanel_Prefab == null)
        {
            Debug.LogError("[Mod_HandCraftTable] InventoryPanel_Prefab 未设置");
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

    public void InitData()
    {
        ValidateInventoryConfig();
        InitializeInventoryData(inputInventory, nameof(inputInventory));
        InitializeInventoryData(outputInventory, nameof(outputInventory));
    }

    public void InitUI()
    {
        BindInputSlots();
        BindOutputSlot();

        inputInventory.SyncData();
        outputInventory.SyncData();

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
    }

    private void OnCraftButtonClick()
    {
        Craft(inputInventory, outputInventory);
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

#endregion

#region 合成逻辑

    [Tooltip("输出用于匹配配方的键列表（2x2输入，支持物品名与Tag）")]
    private List<string> GenerateRecipeKey_List(Inventory inputInv)
    {
        List<string> recipeKeys = new List<string>();
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        Input_List orderedInputList = new Input_List();
        orderedInputList.recipeType = RecipeType.Crafting;
        orderedInputList.inputOrder = OrderedRule;

        foreach (ItemSlot slot in inputSlots)
            orderedInputList.AddNameItem(slot.itemData?.IDName ?? "");

        recipeKeys.Add(orderedInputList.ToString());

        orderedInputList.inputOrder = UnorderedRule;
        recipeKeys.Add(orderedInputList.ToString());

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

            recipeKeys.Add(orderedTagInputList.ToString());
            orderedTagInputList.inputOrder = UnorderedRule;
            recipeKeys.Add(orderedTagInputList.ToString());
        }

        return recipeKeys;
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
        List<string> recipeKeys = GenerateRecipeKey_List(inputInv);

        Recipe recipe = null;
        foreach (string recipeKey in recipeKeys)
        {
            if (GameRes.Instance.recipeDict.TryGetValue(recipeKey, out recipe))
                break;
        }

        if (recipe == null)
        {
            Debug.LogError($"[Mod_HandCraftTable] 配方不存在：{string.Join(" 或 ", recipeKeys)}");
            return false;
        }

        if (!ValidateSlotCount(recipe))
            return false;

        var outputItems = PrepareOutputItems(recipe);
        if (outputItems == null || outputItems.Count == 0)
            return false;

        if (!CheckResourcesAndSpace(inputInv, outputInv, recipe, outputItems))
        {
            Debug.LogError("[Mod_HandCraftTable] 合成失败：材料不足或输出空间不足");
            return false;
        }

        ExecuteCrafting(inputInv, outputInv, recipe, outputItems);
        return true;
    }

    private bool ValidateSlotCount(Recipe recipe)
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

    private List<ItemData> PrepareOutputItems(Recipe recipe)
    {
        var itemsToAdd = new List<ItemData>();

        foreach (var output in recipe.outputs.results)
        {
            Item outputItem = output.ItemPrefab.GetComponent<Item>();
            ItemData newItem = outputItem.Get_NewItemData();
            newItem.Stack.Amount = output.amount;
            itemsToAdd.Add(newItem);
        }

        return itemsToAdd;
    }

    private bool CheckResourcesAndSpace(Inventory inputInv, Inventory outputInv, Recipe recipe, List<ItemData> outputItems)
    {
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        if (recipe.inputs.inputOrder == OrderedRule)
        {
            for (int i = 0; i < InputSlotCount; i++)
            {
                var slot = inputSlots[i];
                var required = recipe.inputs.RowItems_List[i];

                if (required.amount == 0)
                    continue;

                if (slot.itemData == null || slot.itemData.Stack.Amount < required.amount)
                    return false;
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
                    return false;
            }
        }

        foreach (var itemData in outputItems)
        {
            if (!outputInv.Data.TryAddItem(itemData, false))
                return false;
        }

        return true;
    }

    private void ExecuteCrafting(Inventory inputInv, Inventory outputInv, Recipe recipe, List<ItemData> outputItems)
    {
        Debug.Log($"[Mod_HandCraftTable] 开始合成：{recipe.name}，输入={GenerateRecipeKey(inputInv)}");

        foreach (var itemData in outputItems)
            outputInv.Data.TryAddItem(itemData);

        if (recipe.inputs.inputOrder == OrderedRule)
        {
            ExecuteOrderedDeduction(inputInv, recipe);
        }
        else
        {
            ExecuteUnorderedDeduction(inputInv, recipe);
        }

        if (recipe.action != null)
        {
            foreach (var action in recipe.action)
            {
                if (action != null)
                    action.Apply(this);
            }
        }

        outputInv.RefreshUI();
        inputInv.RefreshUI();
        Debug.Log($"[Mod_HandCraftTable] 合成完成：{recipe.name}");
    }

    private void ExecuteOrderedDeduction(Inventory inputInv, Recipe recipe)
    {
        List<ItemSlot> inputSlots = GetInputSlots(inputInv);

        for (int i = 0; i < InputSlotCount; i++)
        {
            var slot = inputSlots[i];
            var required = recipe.inputs.RowItems_List[i];

            if (required.amount == 0 || slot.itemData == null)
                continue;

            slot.itemData.Stack.Amount -= required.amount;
            if (slot.itemData.Stack.Amount <= 0)
                inputInv.Data.RemoveItemAll(slot, i);

            inputInv.RefreshUI(i);
        }
    }

    private void ExecuteUnorderedDeduction(Inventory inputInv, Recipe recipe)
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

#endregion
}