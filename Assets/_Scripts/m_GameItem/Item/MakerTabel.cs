using DG.Tweening;
using Force.DeepCloner;
using log4net.Util;
using MemoryPack;
using PlasticGui.WorkspaceWindow.Locks;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using ButtonAttribute = Sirenix.OdinInspector.ButtonAttribute;

public class CraftingTable : Item,IWork, IInteract, IInventoryData,ISave_Load,IHealth,IBuilding
{
    #region 字段

    // 1.inventory 输入容器
    public Inventory inputInventory;
    // 3.inventory 输出容器
    public Inventory outputInventory;
    //合成按钮
    public Button button;
    //关闭按钮
    public Button closeButton;
    //面板
    public Canvas canvas;
    // 4.workerData 工作数据
    public Data_Worker Data;

    public UltEvent _onInventoryData_Dict_Changed;
    public UltEvent OnInventoryData_Dict_Changed { get => _onInventoryData_Dict_Changed; set => _onInventoryData_Dict_Changed = value; }
    public override ItemData Item_Data
    {
        get => Data;
        set => Data = (Data_Worker)value;
    }
    public Dictionary<string, Inventory_Data> InventoryData_Dict
    {
        get
        {
            return Data.Inventory_Data_Dict;
        }
        set
        {
            Data.Inventory_Data_Dict = value;
        }
    }
    public UltEvent OnDeath { get; set; }
    [ShowInInspector]
    public Dictionary<string, Inventory> children_Inventory_GameObject = new Dictionary<string, Inventory>();
    public Dictionary<string, Inventory> Children_Inventory_GameObject
    {
        get
        {
            return children_Inventory_GameObject;
        }
        set
        {
            children_Inventory_GameObject = value;
        }
    }

    public SelectSlot SelectSlot { get; set; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }


    public bool CanInteract { get => !Data.Stack.CanBePickedUp; set => Data.Stack.CanBePickedUp = !value; }
    public Hp Hp { get=> Data.Hp; set => Data.Hp = value; }
    public Defense Defense { get => Data.Defense; set => Data.Defense = value; }
    public UltEvent OnHpChanged { get; set; }
    public UltEvent OnDefenseChanged { get; set; }
    public bool IsInstalled { get =>Data.IsInstalled; set => Data.IsInstalled = value; }
    public bool BePlayerTaken { get => bePlayerTake; set => bePlayerTake = value; }

    public Building_InstallAndUninstall _InstallAndUninstall = new ();
    private bool bePlayerTake = false;
    #endregion

    void Start()
    {
       // IInventoryData inventoryData = this;
     //   inventoryData.FillDict_SetBelongItem(transform);

        button.onClick.AddListener(() => Work_Start());
        closeButton.onClick.AddListener(() => CloseUI());

        CloseUI();

        _InstallAndUninstall.Init(transform);

        if(BelongItem != null)
        {
            BePlayerTaken = true;
        }

    }
    public void Update()
    {
        _InstallAndUninstall.Update();
    }

    #region 安装和拆除

    [Sirenix.OdinInspector.Button]
    public void UnInstall()
    {
        _InstallAndUninstall.UnInstall();
    }
    [Button]
    public void Install()
    {
        _InstallAndUninstall.Install();
    }

    public void OnDestroy()
    {
        // 安全销毁 ghost 实例
        _InstallAndUninstall.CleanupGhost();
    }
    #endregion

    #region 合成方法
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
            inputInventory.SyncUI( i);
        }

        Debug.Log($"合成完成：{recipe.name}");
        return true;
    }
    private bool CheckEnough(Inventory inputInventory_,
                         Inventory outputInventory_,
                         Input_List inputList,
                         List<ItemData> itemsToAdd)
    {
        for (int i = 0; i < inputInventory_.Data.itemSlots.Count; i++)
        {
            var slot = inputInventory_.Data.itemSlots[i];
            var required = inputList.RowItems_List[i];

            // 插槽不需要物品则跳过
            if (required.amount == 0)
            {
                Debug.Log($"槽位{i} 不需要材料，跳过");
                continue;
            }

            // 检查物品是否存在
            if (slot._ItemData == null)
            {
                Debug.LogWarning($"槽位{i} 需要 {required.ItemName} x{required.amount}，但物品为空");
                return false;
            }

            // 检查名称是否匹配
            if (slot._ItemData.IDName != required.ItemName)
            {
                Debug.LogWarning($"槽位{i} 物品不匹配，期望 {required.ItemName}，实际是 {slot._ItemData.IDName}");
                return false;
            }

            // 检查数量是否足够
            if (slot._ItemData.Stack.Amount < required.amount)
            {
                Debug.LogWarning($"槽位{i} 材料数量不足，当前 {slot._ItemData.Stack.Amount}，需要 {required.amount}");
                return false;
            }
            else
            {
                Debug.Log($"槽位{i} 材料检查通过：{slot._ItemData.IDName} x{slot._ItemData.Stack.Amount}/{required.amount}");
            }
        }

        // 检查输出空间
        foreach (var item in itemsToAdd)
        {
            if (!outputInventory_.Data.CanAddTheItem(item))
            {
                Debug.LogWarning($"输出物品 {item.IDName} 无法加入输出背包，可能空间不足");
                return false;
            }
            else
            {
                Debug.Log($"输出检查通过：可以添加 {item.IDName} x{item.Stack.Amount}");
            }
        }

        Debug.Log("所有输入输出检查通过，可以进行合成");
        return true;
    }

    #endregion

    #region 世界交互接口实现
    //切换UI显示状态
    public void SwitchUI()
    {
        canvas.enabled = !canvas.enabled;
    }
    //开启Ui
    public void OpenUI()
    {
        canvas.enabled = true;
    }
    //关闭Ui
    public void CloseUI()
    {
        canvas.enabled = false;
    }
    public void Work_Start()
    {
        Craft(inputInventory, outputInventory, RecipeType.Crafting);
    }

    public void Interact_Start(IInteracter interacter = null)
    {
        if (CanInteract)
        {
            SwitchUI();
            //遍历这个物品的所有的Value，Children_Inventory_GameObject
            foreach (var inventory in Children_Inventory_GameObject.Values)
            {
                inventory.DefaultTarget_Inventory 
                    = interacter.InventoryData.Children_Inventory_GameObject["手部插槽"];
            }
        }
    }

    public override void Act()
    {
        Debug.Log("Act");
        Install();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        CloseUI();
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }

    public void Work_Update()
    {
        throw new System.NotImplementedException();
    }

    public void Work_Stop()
    {
        throw new System.NotImplementedException();
    }

    #region 保存和加载
    public void Save()
    {
        //TODO 保存数据
    }

    public void Load()
    {
        
        //IInventoryData inventoryData = this;
        //inventoryData.FillDict_SetBelongItem(transform);

        inputInventory = Children_Inventory_GameObject["输入物品槽"];
        outputInventory = Children_Inventory_GameObject["输出物品槽"];

        if (InventoryData_Dict.ContainsKey("输入物品槽"))
        {
            inputInventory.Data = InventoryData_Dict["输入物品槽"];
            outputInventory.Data = InventoryData_Dict["输出物品槽"];
        }
    }

    public void Death()
    {
        if (CanInteract)
            UnInstall();
    }
    public float GetDamage(Damage damage)
    {
        if (Hp.Check_Weakness(damage.DamageType))
        {
            float Value = damage.Return_EndDamage();

            Hp.Value -= Value;

            if (Hp.Value <= 0)
            {
                Death();
            }
            return Value;
        }
        else
        {
            float damageValue = damage.Return_EndDamage(Defense);

            Hp.Value -= damageValue;

            if (Hp.Value <= 0)
            {
                Death();
            }
            return damageValue;
        }
    }
    #endregion
    #endregion

}

