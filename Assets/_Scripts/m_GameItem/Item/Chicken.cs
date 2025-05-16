using MemoryPack;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Chicken : Item, IHunger, ISpeed, ISight,IHealth,IStamina
{
    public AnimalData Data;
    public override ItemData Item_Data { get { return Data; } set { Data = value as AnimalData; } }
    #region ����

    public Hunger_FoodAndWater Foods { get => Data.hunger; set => Data.hunger = value; }
    public float EatingSpeed { get => Data.attackSpeed; set => throw new System.NotImplementedException(); }
    public UltEvent OnNutrientChanged { get; set; }

    #endregion
    #region �ٶ�
    // ����ISpeed�ӿ�ʵ�֣�ֱ��ӳ��AnimalData�е��ٶ�����
    public float Speed
    {
        get => Data.speed;
        set => Data.speed = value;
    }

    public float DefaultSpeed
    {
        get => Data.defaultSpeed;
        set => Data.defaultSpeed = value;
    }

    public float RunSpeed
    {
        get => Data.runSpeed;
        set => Data.runSpeed = value;
    }
    #endregion

    #region ��֪

    public float sightRange { get => Data.sightRange; set => Data.sightRange = value; }
    #endregion

  
    #region ����
    public Hp Hp
    {
        get => Data.hp;
        set
        {
            if (Data.hp != value)
            {
                Data.hp = value;
                OnHpChanged?.Invoke();
            }
        }
    }

    public Defense Defense
    {
        get => Data.defense;
        set
        {
            if (Data.defense != value)
            {
                Data.defense = value;
                OnDefenseChanged?.Invoke();
            }
        }
    }

    public UltEvent OnHpChanged { get; set; } = new UltEvent();

    public UltEvent OnDefenseChanged { get; set; } = new UltEvent();


    #endregion

    #region ����

    public float Stamina
    {
        get => Data.stamina;
        set => Data.stamina = Mathf.Clamp(value, 0, Data.staminaMax);
    }

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
    public UltEvent OnStaminaChanged { get; set; } = new UltEvent();
    #endregion

    public void Start()
    {
        OnNutrientChanged = new UltEvent();
    }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void FixedUpdate()
    {
        Hungry_Update();
     
    }

    public void Eat(IFood food)
    {
        Foods.Food += EatingSpeed;
        food.BeEat(EatingSpeed);
    }

    public void Hungry_Update()
    {
        //ÿ�����1��ʳ������*�����ٶ�
        Data.hunger.Food -= Time.fixedDeltaTime * Data.productionSpeed;
    }

    public void Death()
    {
        Destroy(gameObject);
    }
}

public interface ISight
{
    float sightRange { get; set; }
}

internal interface IAnimator
{
}

[MemoryPackable]
[System.Serializable]
public partial class AnimalData : ItemData
{

    #region ����
    [Tooltip("Ѫ��")]
    public Hp hp = new Hp(30);

    [Tooltip("������")]
    public Defense defense = new(5, 5);
    #endregion
    #region ����
    [Tooltip("������")]
    public Damage damage = new();
    [Tooltip("�������")]
    public float attackSpeed = 1;
    #endregion

    #region �ٶ�
    [Tooltip("Ĭ���ٶ�")]
    public float defaultSpeed = 3;
    [Tooltip("�ٶ�")]
    public float speed = 3;
    [Tooltip("�����ٶ�")]
    public float runSpeed = 6;
    #endregion

    #region ����
    [Tooltip("����ֵ")]
    public float stamina = 100;
    [Tooltip("��������")]
    public float staminaMax = 100;
    [Tooltip("��������")]
    public float staminaDefault = 100;
    [Tooltip("�����ָ��ٶ�")]
    public float staminaRecoverySpeed = 1;
    [Tooltip("�������������ٶ�")]
    public float staminaMaxPassesSpeed = 0.1f;
    #endregion

    #region ʳ��
    [Tooltip("����ֵ")]
    public Hunger_FoodAndWater hunger = new Hunger_FoodAndWater(100, 100);
    #endregion

    #region ����

    [Tooltip("��������")]
    public float progress = 0;
    [Tooltip("�����ٶ�")]
    public float productionSpeed = 1;
    [Tooltip("�������")]
    public float productionInterval = 1;

    #endregion

    #region ��֪

    [Tooltip("��֪��Χ")]
    public float sightRange = 10;

    #endregion

    #region ���

    [ShowNonSerializedField]
    [Tooltip("�������")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion

    #region ����

    [Tooltip("Buff����")]
    public ItemValues ItemDataValue;
    #endregion

    #region �Ŷ�

    public string TeamID = "";

    public Dictionary<string, RelationType> Relations = new Dictionary<string, RelationType>();
    #endregion
}

