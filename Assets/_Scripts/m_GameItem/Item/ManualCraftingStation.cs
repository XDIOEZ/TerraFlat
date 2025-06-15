using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NaughtyAttributes;
using System.Linq;
using Newtonsoft.Json;
using static Recipe;
using Force.DeepCloner;

public class ManualCraftingStation : MonoBehaviour, IInteract
{
    #region 字段定义
    [Tooltip("输入容器，用于存放合成所需的原材料物品")]
    public Inventory inputInventory;

    [Tooltip("输出容器，用于存放合成后得到的物品")]
    public Inventory outputInventory;

    [Tooltip("交互面板，挂接的UI对象，主要用于接受玩家输入，例如点击合成按钮等操作")]
    public GameObject interactionPanel;

    [Tooltip("当前已合成时间，可用于后续实现合成时间限制等功能")]
    public float currentCraftingTime;

    [Tooltip("合成清单字典的引用，存储所有可用的合成配方，键为合成所需材料的字符串表示，值为输出列表")]
    public Dictionary<string, Recipe> recipes = new Dictionary<string, Recipe>();

    [Tooltip("合成次数，记录玩家进行合成操作的次数")]
    public int craftingTimes = 0;
    #endregion

    #region Unity 生命周期
    private void Start()
    {
        if (interactionPanel != null)
        {
            Button craftButton = interactionPanel.GetComponentInChildren<Button>();
            if (craftButton != null)
                craftButton.onClick.AddListener(OnCraftButtonClick);
        }
        LoadRecipes();
    }
    #endregion

    #region 配方加载
    [Button]
    public void LoadRecipes()
    {
        recipes = GameRes.Instance.recipeDict;
    }
    #endregion

    #region 交互接口实现
    public void Interact_Start(IInteracter interacter = null)
    {
        interactionPanel?.SetActive(true);
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        interactionPanel?.SetActive(false);
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }
    #endregion

    #region UI事件回调
    private void OnCraftButtonClick()
    {
        if (Craft(inputInventory, outputInventory, RecipeType.Crafting))
            craftingTimes++;
    }
    #endregion

    #region 合成逻辑核心
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
            outputInventory_.Data.AddItem(item);
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
            inputInventory.SyncUIData(i);
        }

        Debug.Log($"合成完成：{recipe.name}");
        return true;
    }

    private  bool CheckEnough(Inventory inputInventory_,
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
            if (!outputInventory_.Data.CanAddTheItem(item))
                return false;

        return true;
    }
    #endregion
}