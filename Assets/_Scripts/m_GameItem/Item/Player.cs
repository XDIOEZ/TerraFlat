using JetBrains.Annotations;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UIElements;

public class Player
    : Item, IHunger, ISpeed,
      IInventoryData, IHealth, IStamina,
      ISave_Load, IFocusPoint, IRotationSpeed
{
    public UltEvent OnDeath { get; set; }

    #region 字段声明
    [Tooltip("手部选择栏")]
    public SelectSlot selectSlot;
    public SelectSlot SelectSlot { get => selectSlot; set => selectSlot = value; }

    [Tooltip("数据")]
    public Data_Player Data;

    [Tooltip("库存字典事件")]
    private UltEvent _onInventoryData_Dict_Changed = new();

    #endregion

    #region 属性封装

    [Tooltip("物品数据")]
    public override ItemData Item_Data
    {
        get => Data;
        set
        {
            Data = value as Data_Player;
            OnInventoryData_Dict_Changed?.Invoke();
        }
    }

    [Tooltip("营养数据")]
    public Nutrition Foods
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

    public float EatingSpeed { get; set; } = 1;

    public GameValue_float Speed
    {
        get => Data.Speed;
        set => Data.Speed = value;
    }

    [Tooltip("库存数据字典")]
    public Dictionary<string, Inventory_Data> InventoryData_Dict
    {
        get => Data._inventoryData;
        set => Data._inventoryData = value;
    }

    [Tooltip("子库存对象字典")]
    [ShowInInspector]
    public Dictionary<string, Inventory> Children_Inventory_GameObject { get; set; } = new Dictionary<string, Inventory>();
   

    #region 精力相关属性

    [Tooltip("精力值")]
    public float Stamina
    {
        get => Data.stamina;
        set => Data.stamina = value;
    }

    [Tooltip("最大精力值")]
    public float MaxStamina
    {
        get => Data.staminaMax;
        set => Data.staminaMax = value;
    }

    public float StaminaRecoverySpeed
    {
        get => Data.staminaRecoverySpeed;
        set => Data.staminaRecoverySpeed = value;
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

    public Item Belong_Item => this;
    public Vector3 FocusPointPosition { get; set; }
    public Vector3 MoveTargetPosition { get; set; }
    public float RotationSpeed { get; set; } = 100;

    #endregion

    #region 方法

    #region 数据初始化与同步

/*    void GetChildInventory_SetBelongItem_SelectHandSlot()
    {
        inventoryDataInterface = this;

        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            inventory.UI.TargetSendItemSlot = selectSlot;
        }
    }*/

/*    [Button]
    public void FillDataTO_ChildInventory_InstantiateSlots()
    {
        Debug.Log("设置子物品栏数据");

        foreach (var inventory_Child in Children_Inventory_GameObject.Values)
        {
            inventory_Child.Data = Data_InventoryData[inventory_Child.Data.inventoryName];

            foreach (var itemSlot in Data_InventoryData[inventory_Child.Data.inventoryName].itemSlots)
            {
                itemSlot.Belong_Inventory = inventory_Child;
            }

            inventory_Child.UI.Instantiate_ItemSlotUI();
            inventory_Child.UI.RefreshAllInventoryUI();
        }
    }

    [Button("重新实例化对象的所有物品栏")]
    public void Rest_InstantiateSlots()
    {
        inventoryDataInterface = this;
        inventoryDataInterface.FillDict_SetBelongItem(transform);
    }*/

/*    public void GetDataFrom_GameObjectInventory_SaveTOData()
    {
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData[inventory.Data.inventoryName] = inventory.Data;
        }
    }*/

/*    public void InitializeInventory()
    {
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["背包"], 24);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["输入物品槽"], 4);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["输出物品槽"], 2);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["装备栏"], 4);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["快捷栏"], 9);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["手部插槽"], 1);

        foreach (var item in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData.Add(item.Data.inventoryName, item.Data);
        }
    }*/

    #endregion

    #region 生命周期与行为

    public void Start()
    {
        Load();
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    public void TakeABite(IFood food)
    {
        IFood iFood = food;

        if (iFood == null)
        {
            Debug.LogWarning("Weapon 上缺少 IFood 接口！");
            return;
        }

        Nutrition foodData = iFood.NutritionData;

        if (foodData == null)
        {
            Debug.LogWarning("IFood 的 Foods 数据为 null！");
            return;
        }

        if (Foods == null)
        {
            Debug.LogWarning("当前对象上的 Foods 数据未设置！");
            return;
        }

        var eatenResult = iFood.BeEat(EatingSpeed);

        if (eatenResult != null)
        {
            Foods.Food += eatenResult.Food * 0.5f;
            Foods.Water += eatenResult.Water * 0.5f;
        }
    }

    public void Death()
    {
        Application.Quit();
        Application.OpenURL("https://space.bilibili.com/353520649");
    }

    #endregion

    #region 存储与读取

    [Button("保存玩家数据")]
    public void Save()
    {
        base.SyncPosition();
       // GetDataFrom_GameObjectInventory_SaveTOData();
        // this.gameObject.SetActive(false);
    }

    [Button("读取玩家数据")]
    public void Load()
    {
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

      /*  inventoryDataInterface = this;
     //   inventoryDataInterface.FillDict_SetBelongItem(transform);

        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            if (Data_InventoryData.ContainsKey(inventory.Data.inventoryName))
            {
                inventory.Data = Data_InventoryData[inventory.Data.inventoryName];
                inventory.UI.RefreshAllInventoryUI();
            }
        }*/
    }

    #endregion

    #endregion
}
