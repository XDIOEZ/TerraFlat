using JetBrains.Annotations;
using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UIElements;


public class Player : Item,IHunger,ISpeed,IInventoryData,IHealth,IStamina,ISave_Load
{
    #region 字段声明
    [Tooltip("库存数据接口")]
    public IInventoryData inventoryDataInterface;

    [Tooltip("手部选择栏")]
    public SelectSlot selectSlot;

    public SelectSlot SelectSlot { get => selectSlot; set => selectSlot = value; }



    [Tooltip("数据")]
    public PlayerData Data;

    [Tooltip("库存字典事件")]
    private UltEvent _onInventoryData_Dict_Changed = new();

    [Tooltip("子库存对象字典"), ShowNonSerializedField]
    public Dictionary<string, Inventory> children_Inventory_GameObject = new Dictionary<string, Inventory>();
    #endregion

    #region 属性封装

    // 基础属性
    [Tooltip("物品数据")]
    public override ItemData Item_Data
    {
        get => Data;
        set
        {
            Data = value as PlayerData;
            OnInventoryData_Dict_Changed?.Invoke();
        }
    }

    [Tooltip("营养数据")]
    public Hunger_Water Foods
    {
        get => Data.hunger;
        set => Data.hunger = value;
    }

    [Tooltip("防御属性")]
    public Defense Defense
    {
        get => Data.defense;
        set => Data.defense = value;
    }

    [Tooltip("生命值属性")]
    public Hp Hp
    {
        get => Data.hp;
        set => Data.hp = value;
    }
    public float EatingSpeed { get => Data.speed; set => Data.speed = value; }


    [Tooltip("移动速度")]
    public float Speed
    {
        get => Data.speed;
        set => Data.speed = value;
    }

    [Tooltip("默认移动速度")]
    public float DefaultSpeed
    {
        get => 5f;
        set => throw new NotImplementedException("默认速度不可修改");
    }

    [Tooltip("奔跑速度（计算属性）")]
    public float RunSpeed
    {
        get => Data.runSpeed;
        set => Data.runSpeed = value;
    }

    [Tooltip("库存数据字典")]
    public Dictionary<string, Inventory_Data> Data_InventoryData
    {
        get => Data._inventoryData;
        set => Data._inventoryData = value;
    }

    [Tooltip("子库存对象字典")]
    public Dictionary<string, Inventory> Children_Inventory_GameObject
    {
        get => children_Inventory_GameObject;
        set => children_Inventory_GameObject = value;
    }

    #region 精力相关属性
    [Tooltip("精力值")]
    public float Stamina
    {
        get => Data.stamina;
        set
        {
            Data.stamina = value;
        }
    }

    [Tooltip("最大精力值")]
    public float MaxStamina
    {
        get => Data.staminaMax;
        set => Data.staminaMax = value;
    }

    public float StaminaRecoverySpeed
    {
        get
        {
            return Data.staminaRecoverySpeed;
        }

        set
        {
            Data.staminaRecoverySpeed = value;
        }
    }
    #endregion

    #endregion

    #region 事件系统
    [Tooltip("库存数据变化事件")]
    public UltEvent OnInventoryData_Dict_Changed
    {
        get => _onInventoryData_Dict_Changed;
        set => _onInventoryData_Dict_Changed = value;
    }

    [Tooltip("精力值变化事件")]
    public UltEvent OnStaminaChanged { get; set; }

    [Tooltip("生命值变化事件")]
    public UltEvent OnHpChanged { get; set; }

    [Tooltip("防御值变化事件")]
    public UltEvent OnDefenseChanged { get; set; }

    [Tooltip("营养值变化事件")]
    public UltEvent OnNutrientChanged { get; set; }
    public UltEvent onSave { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public UltEvent onLoad { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }


    #endregion

    #region 方法

    #region 将数据移动到游戏物体中
    // 设置物品清单与数据归属关系
    void GetChildInventory_SetBelongItem_SelectHandSlot()
    {
        inventoryDataInterface = this; // 将当前对象设置为 inventoryData。

        //inventoryDataInterface.FillDict_SetBelongItem(transform.parent); // 设置归属为父物体。

        //设置手部选择栏位
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            inventory.UI.TargetSendItemSlot = selectSlot; // 将目标发送物品插槽设置为选中插槽。
        }
    }

    [Button]
    // 设置子物品栏数据
    public void FillDataTO_ChildInventory_InstantiateSlots()
    {
        Debug.Log("设置子物品栏数据"); // 打印日志，指示开始设置子物品栏数据。
        foreach (var inventory_Child in Children_Inventory_GameObject.Values)
        {
            // 将当前显示的 Inventory 数据设置为字典中的对应数据。
            inventory_Child.Data = Data_InventoryData[inventory_Child.Data.inventoryName];

            // 遍历物品插槽并设置归属的 Inventory。
            foreach (var itemSlot in Data_InventoryData[inventory_Child.Data.inventoryName].itemSlots)
            {
                itemSlot.Belong_Inventory = inventory_Child; // 设置物品插槽的归属。
            }

            inventory_Child.UI.Instantiate_ItemSlotUI(); // 实例化物品插槽 UI。

            inventory_Child.UI.RefreshAllInventoryUI(); // 刷新所有物品栏 UI。
        }
    }

    [Button("重新实例化对象的所有物品栏")]
    public void Rest_InstantiateSlots()
    {
        inventoryDataInterface = this; // 将当前对象设置为 inventoryData。

        inventoryDataInterface.FillDict_SetBelongItem(transform.parent); // 设置归属为父物体。

    }
    #endregion

    //将子对象的数据存入ItemData中
    public void GetDataFrom_GameObjectInventory_SaveTOData()
    {
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData[inventory.Data.inventoryName] = inventory.Data;
        }
    }


    public void InitializeInventory()
    {
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["背包"], 24); // 初始化背包插槽大小为 24。
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["输入物品槽"], 4); // 初始化输入物品槽大小为 4。
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["输出物品槽"], 2); // 初始化输出物品槽大小为 2。
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["装备栏"], 4); // 初始化装备栏大小为 4。
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["快捷栏"], 9); // 初始化快捷栏大小为 9。
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["手部插槽"], 1); // 初始化手部插槽大小为 1。

        // 将每个物品清单数据添加到字典中。
        foreach (var item in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData.Add(item.Data.inventoryName, item.Data);
        }
    }
    public void Start()
    {

        Save();
        Load();
    }
    public override void Act()
    {
        throw new NotImplementedException(); // 抛出未实现异常。
    }

    // 执行吃操作，参数为食物被吃掉的量
    public void Eat(float eatSpeed = 1f)
    {
        eatSpeed = Speed; // 吃食物的速度受玩家速度影响。
        var triggerAttack = GetComponentInChildren<ITriggerAttack>();
        if (triggerAttack == null || triggerAttack.Weapon_GameObject == null)
        {
            Debug.Log("未找到 ITriggerAttack 或其 Weapon_GameObject！");
            return;
        }

        GameObject weapon = triggerAttack.Weapon_GameObject;

        IFood iFood = weapon.GetComponent<IFood>();
        Hunger_Water foods_ = iFood.Foods;

        if (iFood == null || foods_ == null)
        {
            Debug.Log("Weapon 上缺少 IFood 或 Hunger_Water 组件！");
            return;
        }

        if (Foods == null)
        {
            Debug.Log("当前对象上的 Foods 数据未设置！");
            return;
        }

        if(iFood.BeEat(eatSpeed)!= null)
        {
            Foods.Food += foods_.Food*0.5f;
            Foods.Water += foods_.Water * 0.5f;
        }
    }


    // 执行死亡操作
    public void Death()
    {
        // TODO 退出游戏
        Application.Quit(); // 退出游戏应用程序。
        Application.OpenURL("https://space.bilibili.com/353520649"); // 打开指定 URL。
    }

    #region 读取与保存

    [Button("保存玩家数据")]
    // 保存玩家数据方法
    public void Save()
    {
        base.SyncPosition(); // 同步玩家位置。
        GetDataFrom_GameObjectInventory_SaveTOData();
    }

    [Button("读取玩家数据")]
    // 读取玩家数据方法
    public void Load()
    {
        transform.position = Data._transform.Position; // 恢复位置。
        transform.rotation = Data._transform.Rotation; // 恢复旋转。
        transform.localScale = Data._transform.Scale; // 恢复缩放比例。

        inventoryDataInterface = this;
        inventoryDataInterface.FillDict_SetBelongItem(transform.parent);
    }
    #endregion
    #endregion


}


