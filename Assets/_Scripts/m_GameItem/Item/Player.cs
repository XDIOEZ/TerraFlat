using JetBrains.Annotations;
using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UIElements;


public class Player 
    : Item,IHunger,ISpeed,
    IInventoryData,IHealth,IStamina,
    ISave_Load,IFocusPoint, IRotationSpeed
{
    #region �ֶ�����
    [Tooltip("������ݽӿ�")]
    public IInventoryData inventoryDataInterface;

    [Tooltip("�ֲ�ѡ����")]
    public SelectSlot selectSlot;

    public SelectSlot SelectSlot { get => selectSlot; set => selectSlot = value; }



    [Tooltip("����")]
    public Data_Player Data;

    [Tooltip("����ֵ��¼�")]
    private UltEvent _onInventoryData_Dict_Changed = new();

    [Tooltip("�ӿ������ֵ�"), ShowNonSerializedField]
    public Dictionary<string, Inventory> children_Inventory_GameObject = new Dictionary<string, Inventory>();
    #endregion

    #region ���Է�װ

    // ��������
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
    public float EatingSpeed { get => Data.speed; set => Data.speed = value; }


    [Tooltip("�ƶ��ٶ�")]
    public float Speed
    {
        get => Data.speed;
        set => Data.speed = value;
    }

    [Tooltip("Ĭ���ƶ��ٶ�")]
    public float MaxSpeed
    {
        get => 5f;
        set => throw new NotImplementedException("Ĭ���ٶȲ����޸�");
    }

    [Tooltip("�����ٶȣ��������ԣ�")]
    public float RunSpeed
    {
        get => Data.runSpeed;
        set => Data.runSpeed = value;
    }

    [Tooltip("��������ֵ�")]
    public Dictionary<string, Inventory_Data> Data_InventoryData
    {
        get => Data._inventoryData;
        set => Data._inventoryData = value;
    }

    [Tooltip("�ӿ������ֵ�")]
    public Dictionary<string, Inventory> Children_Inventory_GameObject
    {
        get => children_Inventory_GameObject;
        set => children_Inventory_GameObject = value;
    }

    #region �����������
    [Tooltip("����ֵ")]
    public float Stamina
    {
        get => Data.stamina;
        set
        {
            Data.stamina = value;
        }
    }

    [Tooltip("�����ֵ")]
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
    public Item Belong_Item { get => this;}
    public Vector3 FocusPointPosition { get; set; }
    public Vector3 Direction { get; set; }
    public float RotationSpeed { get; set; } = 100;
    #endregion

    #region ����

    #region �������ƶ�����Ϸ������
    // ������Ʒ�嵥�����ݹ�����ϵ
    void GetChildInventory_SetBelongItem_SelectHandSlot()
    {
        inventoryDataInterface = this; // ����ǰ��������Ϊ inventoryData��

        //inventoryDataInterface.FillDict_SetBelongItem(transform.parent); // ���ù���Ϊ�����塣

        //�����ֲ�ѡ����λ
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            inventory.UI.TargetSendItemSlot = selectSlot; // ��Ŀ�귢����Ʒ�������Ϊѡ�в�ۡ�
        }
    }

    [Button]
    // ��������Ʒ������
    public void FillDataTO_ChildInventory_InstantiateSlots()
    {
        Debug.Log("��������Ʒ������"); // ��ӡ��־��ָʾ��ʼ��������Ʒ�����ݡ�
        foreach (var inventory_Child in Children_Inventory_GameObject.Values)
        {
            // ����ǰ��ʾ�� Inventory ��������Ϊ�ֵ��еĶ�Ӧ���ݡ�
            inventory_Child.Data = Data_InventoryData[inventory_Child.Data.inventoryName];

            // ������Ʒ��۲����ù����� Inventory��
            foreach (var itemSlot in Data_InventoryData[inventory_Child.Data.inventoryName].itemSlots)
            {
                itemSlot.Belong_Inventory = inventory_Child; // ������Ʒ��۵Ĺ�����
            }

            inventory_Child.UI.Instantiate_ItemSlotUI(); // ʵ������Ʒ��� UI��

            inventory_Child.UI.RefreshAllInventoryUI(); // ˢ��������Ʒ�� UI��
        }
    }

    [Button("����ʵ���������������Ʒ��")]
    public void Rest_InstantiateSlots()
    {
        inventoryDataInterface = this; // ����ǰ��������Ϊ inventoryData��

        inventoryDataInterface.FillDict_SetBelongItem(transform); // ���ù���Ϊ�����塣

    }
    #endregion

    //���Ӷ�������ݴ���ItemData��
    public void GetDataFrom_GameObjectInventory_SaveTOData()
    {
        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData[inventory.Data.inventoryName] = inventory.Data;
        }
    }


    public void InitializeInventory()
    {
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["����"], 24); // ��ʼ��������۴�СΪ 24��
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["������Ʒ��"], 4); // ��ʼ��������Ʒ�۴�СΪ 4��
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["�����Ʒ��"], 2); // ��ʼ�������Ʒ�۴�СΪ 2��
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["װ����"], 4); // ��ʼ��װ������СΪ 4��
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["�����"], 9); // ��ʼ���������СΪ 9��
        inventoryDataInterface.InitializeInventory(Children_Inventory_GameObject["�ֲ����"], 1); // ��ʼ���ֲ���۴�СΪ 1��

        // ��ÿ����Ʒ�嵥������ӵ��ֵ��С�
        foreach (var item in Children_Inventory_GameObject.Values)
        {
            Data_InventoryData.Add(item.Data.inventoryName, item.Data);
        }
    }
    public void Start()
    {
        Load();
    }
    public override void Act()
    {
        throw new NotImplementedException(); // �׳�δʵ���쳣��
    }
    /// <summary>
    /// ִ�гԲ���������Ϊ�Ե����ٶȣ�Ĭ��ʹ������ٶȣ�
    /// </summary>
    public void Eat(IFood food)
    {
        // ��ȡ�����ϵ�ʳ��ӿ�
        IFood iFood = food;

        if (iFood == null)
        {
            Debug.LogWarning("Weapon ��ȱ�� IFood �ӿڣ�");
            return;
        }

        // ��ȡʳ������
        Nutrition foodData = iFood.NutritionData;

        if (foodData == null)
        {
            Debug.LogWarning("IFood �� Foods ����Ϊ null��");
            return;
        }

        // ��ȡ��ҵļ�������
        if (Foods == null)
        {
            Debug.LogWarning("��ǰ�����ϵ� Foods ����δ���ã�");
            return;
        }

        // ����Գɹ���ʳ�ﷵ����Ӫ��ֵ������ָ���ҵļ���ֵ
        var eatenResult = iFood.BeEat(EatingSpeed);

        if (eatenResult != null)
        {
            Foods.Food += eatenResult.Food * 0.5f;
            Foods.Water += eatenResult.Water * 0.5f;
        }
    }



    // ִ����������
    public void Death()
    {
        // TODO �˳���Ϸ
        Application.Quit(); // �˳���ϷӦ�ó���
        Application.OpenURL("https://space.bilibili.com/353520649"); // ��ָ�� URL��
    }

    #region ��ȡ�뱣��

    [Button("�����������")]
    // ����������ݷ���
    public void Save()
    {
        base.SyncPosition(); // ͬ�����λ�á�
        GetDataFrom_GameObjectInventory_SaveTOData();

        //����������е���Ʒʵ��
       // this.gameObject.SetActive(false);
    }

    [Button("��ȡ�������")]
    // ��ȡ������ݷ���
    public void Load()
    {
        transform.position = Data._transform.Position; // �ָ�λ�á�
        transform.rotation = Data._transform.Rotation; // �ָ���ת��
        transform.localScale = Data._transform.Scale; // �ָ����ű�����

        inventoryDataInterface = this;

        inventoryDataInterface.FillDict_SetBelongItem(transform);

        foreach (var inventory in Children_Inventory_GameObject.Values)
        {
            if (Data_InventoryData.ContainsKey(inventory.Data.inventoryName))
            {
                inventory.Data = Data_InventoryData[inventory.Data.inventoryName];
                inventory.UI.RefreshAllInventoryUI();
            }
        }
    }
    #endregion
    #endregion


}


