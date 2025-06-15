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

    #region �ֶ�����
    [Tooltip("�ֲ�ѡ����")]
    public SelectSlot selectSlot;
    public SelectSlot SelectSlot { get => selectSlot; set => selectSlot = value; }

    [Tooltip("����")]
    public Data_Player Data;

    [Tooltip("����ֵ��¼�")]
    private UltEvent _onInventoryData_Dict_Changed = new();

    #endregion

    #region ���Է�װ

    [Tooltip("��Ʒ����")]
    public override ItemData Item_Data
    {
        get => Data;
        set
        {
            Data = value as Data_Player;
            OnInventoryData_Dict_Changed?.Invoke();
        }
    }

    [Tooltip("Ӫ������")]
    public Nutrition Foods
    {
        get => Data.hunger;
        set => Data.hunger = value;
    }

    [Tooltip("��������")]
    public Defense Defense
    {
        get => Data.defense;
        set => Data.defense = value;
    }

    [Tooltip("����ֵ����")]
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

    [Tooltip("��������ֵ�")]
    public Dictionary<string, Inventory_Data> InventoryData_Dict
    {
        get => Data._inventoryData;
        set => Data._inventoryData = value;
    }

    [Tooltip("�ӿ������ֵ�")]
    [ShowInInspector]
    public Dictionary<string, Inventory> Children_Inventory_GameObject { get; set; } = new Dictionary<string, Inventory>();
   

    #region �����������

    [Tooltip("����ֵ")]
    public float Stamina
    {
        get => Data.stamina;
        set => Data.stamina = value;
    }

    [Tooltip("�����ֵ")]
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

    #region �¼�ϵͳ

    [Tooltip("������ݱ仯�¼�")]
    public UltEvent OnInventoryData_Dict_Changed
    {
        get => _onInventoryData_Dict_Changed;
        set => _onInventoryData_Dict_Changed = value;
    }

    [Tooltip("����ֵ�仯�¼�")]
    public UltEvent OnStaminaChanged { get; set; }

    [Tooltip("����ֵ�仯�¼�")]
    public UltEvent OnHpChanged { get; set; }

    [Tooltip("����ֵ�仯�¼�")]
    public UltEvent OnDefenseChanged { get; set; }

    [Tooltip("Ӫ��ֵ�仯�¼�")]
    public UltEvent OnNutrientChanged { get; set; }

    public UltEvent onSave { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public UltEvent onLoad { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public Item Belong_Item => this;
    public Vector3 FocusPointPosition { get; set; }
    public Vector3 MoveTargetPosition { get; set; }
    public float RotationSpeed { get; set; } = 100;

    #endregion

    #region ����

    #region ���ݳ�ʼ����ͬ��

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
        Debug.Log("��������Ʒ������");

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

    [Button("����ʵ���������������Ʒ��")]
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
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["����"], 24);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["������Ʒ��"], 4);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["�����Ʒ��"], 2);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["װ����"], 4);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["�����"], 9);
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["�ֲ����"], 1);

        foreach (var item in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData.Add(item.Data.inventoryName, item.Data);
        }
    }*/

    #endregion

    #region ������������Ϊ

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
            Debug.LogWarning("Weapon ��ȱ�� IFood �ӿڣ�");
            return;
        }

        Nutrition foodData = iFood.NutritionData;

        if (foodData == null)
        {
            Debug.LogWarning("IFood �� Foods ����Ϊ null��");
            return;
        }

        if (Foods == null)
        {
            Debug.LogWarning("��ǰ�����ϵ� Foods ����δ���ã�");
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

    #region �洢���ȡ

    [Button("�����������")]
    public void Save()
    {
        base.SyncPosition();
       // GetDataFrom_GameObjectInventory_SaveTOData();
        // this.gameObject.SetActive(false);
    }

    [Button("��ȡ�������")]
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
