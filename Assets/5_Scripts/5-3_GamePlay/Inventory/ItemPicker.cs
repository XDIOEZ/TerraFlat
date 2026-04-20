using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物品拾取器组件，用于自动收集可拾取物品
/// </summary>
public class ItemPicker : Module
{

    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public string[] Data;
    #endregion

    #region 生命周期

    public override void Awake()
    {
        _Data.ID = ModText.Picker;
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref Data);
        RebuildTargetInventories();

        // 默认优先级：Hotbar -> Bag
        // 无论列表是否已有配置，都补齐这两个核心容器（内部会去重）
        TryAddInventoryById(ModText.Hotbar);
        TryAddInventoryById(ModText.Bag);
    }

    /// <summary>
    /// 初始化时尝试获取自身的Inventory组件
    /// </summary>
    private void Start()
    {
    }
    public override void Save()
    {
        ModSaveData.WriteData(Data);
    }
    public override void Act()
    {
        base.Act();
    }
    #endregion


    /// <summary>
    /// 尝试根据模块 ID 获取对应的 Mod_Inventory，并将其 inventory 加入目标列表
    /// </summary>
    /// <param name="modId">ModText 中定义的背包模块 ID</param>
    private void TryAddInventoryById(string modId)
    {
        var module = item.itemMods.GetMod_ByID(modId);
        if (module == null)
            return;

        if (module is not IInventory inventoryProvider)
            return;

        if (!AddTargetInventories.Contains(inventoryProvider))
            AddTargetInventories.Add(inventoryProvider);
    }


    #region 字段与属性

    [Header("目标物品栏（按优先级排列）")]
    [SerializeField]
    public List<Module> AddTargetInventoryModules = new List<Module>(); // 检查器可拖拽的目标库存模块

    [System.NonSerialized]
    public List<IInventory> AddTargetInventories = new List<IInventory>(); // 运行时使用的目标库存接口列表

    /// <summary>
    /// 基础拾取权限控制变量
    /// </summary>
    private bool canPickUp = true;

    /// <summary>
    /// 综合判断是否可以拾取物品
    /// 1. 检查基础权限
    /// 2. 检查是否有可用的物品栏
    /// </summary>
    public bool CanPickUp
    {
        get
        {
            if (!canPickUp)
                return false;

            // 只要存在有效背包就允许进入拾取流程。
            // 是否能实际放入（包括满包堆叠）由 TryAddItem 决定。
            foreach (var inventory in AddTargetInventories)
            {
                var targetInventory = inventory?.GetDefaultTargetInventory();
                if (targetInventory != null && targetInventory.Data != null)
                {
                    return true;
                }
            }
            return false;
        }
        set => canPickUp = value;
    }

    #endregion


#region 私有方法

    private void RebuildTargetInventories()
    {
        AddTargetInventories.Clear();

        for (int i = 0; i < AddTargetInventoryModules.Count; i++)
        {
            Module module = AddTargetInventoryModules[i];
            if (module is not IInventory inventoryProvider)
            {
                continue;
            }

            if (!AddTargetInventories.Contains(inventoryProvider))
            {
                AddTargetInventories.Add(inventoryProvider);
            }
        }
    }

#endregion



    #region 物品交互

    /// <summary>
    /// 当有物体进入触发器时尝试拾取物品
    /// </summary>
    /// <param name="other">进入触发器的碰撞体</param>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 检查物品栏列表是否为空
        if (AddTargetInventories.Count == 0)
        {
            Debug.LogWarning($"[{nameof(ItemPicker)}] AddTargetInventories is empty on {gameObject.name}");
            return;
        }

        // 检查是否具有拾取权限
        if (!CanPickUp)
        {
            return;
        }

        // 获取物品组件
        var pickAble = other.GetComponent<Item>();

        if (pickAble != null && pickAble.itemData.Stack.CanBePickedUp)
        {
            ItemData itemData = pickAble.itemData;

            // 遍历所有背包，找到第一个可以添加的
            foreach (var inventory in AddTargetInventories)
            {
                var targetInventory = inventory?.GetDefaultTargetInventory();
                if (targetInventory != null && targetInventory.Data != null)
                {
                    if (targetInventory.Data.TryAddItem(itemData))
                    {
                        targetInventory.RefreshUI();
                        // 标记物品为已被拾取
                        itemData.Stack.CanBePickedUp = false;

                        // 保存物品数据
                        pickAble.ModuleSave();

                        // 销毁物品对象
                        Destroy(pickAble.gameObject);

                        return; // 添加成功后立即返回
                    }
                }
            }

            Debug.Log($"[{nameof(ItemPicker)}] All target inventories are full, cannot pick up item: {itemData.IDName}");
        }
    }

    #endregion
}