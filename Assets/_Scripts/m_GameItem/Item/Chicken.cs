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
    #region 饥饿

    public Hunger_FoodAndWater Foods { get => Data.hunger; set => Data.hunger = value; }
    public float EatingSpeed { get => Data.attackSpeed; set => throw new System.NotImplementedException(); }
    public UltEvent OnNutrientChanged { get; set; }

    #endregion
    #region 速度
    // 完善ISpeed接口实现，直接映射AnimalData中的速度属性
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

    #region 感知

    public float sightRange { get => Data.sightRange; set => Data.sightRange = value; }
    #endregion

  
    #region 生命
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

    #region 精力

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
        //每秒减少1点食物能量*生产速度
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

    #region 生命
    [Tooltip("血量")]
    public Hp hp = new Hp(30);

    [Tooltip("防御力")]
    public Defense defense = new(5, 5);
    #endregion
    #region 攻击
    [Tooltip("攻击力")]
    public Damage damage = new();
    [Tooltip("攻击间隔")]
    public float attackSpeed = 1;
    #endregion

    #region 速度
    [Tooltip("默认速度")]
    public float defaultSpeed = 3;
    [Tooltip("速度")]
    public float speed = 3;
    [Tooltip("奔跑速度")]
    public float runSpeed = 6;
    #endregion

    #region 精力
    [Tooltip("精力值")]
    public float stamina = 100;
    [Tooltip("精力上限")]
    public float staminaMax = 100;
    [Tooltip("精力耐力")]
    public float staminaDefault = 100;
    [Tooltip("精力恢复速度")]
    public float staminaRecoverySpeed = 1;
    [Tooltip("精力上限流逝速度")]
    public float staminaMaxPassesSpeed = 0.1f;
    #endregion

    #region 食物
    [Tooltip("饥饿值")]
    public Hunger_FoodAndWater hunger = new Hunger_FoodAndWater(100, 100);
    #endregion

    #region 生产

    [Tooltip("生产进度")]
    public float progress = 0;
    [Tooltip("生产速度")]
    public float productionSpeed = 1;
    [Tooltip("生产间隔")]
    public float productionInterval = 1;

    #endregion

    #region 感知

    [Tooltip("感知范围")]
    public float sightRange = 10;

    #endregion

    #region 库存

    [ShowNonSerializedField]
    [Tooltip("库存数据")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion

    #region 数据

    [Tooltip("Buff数据")]
    public ItemValues ItemDataValue;
    #endregion

    #region 团队

    public string TeamID = "";

    public Dictionary<string, RelationType> Relations = new Dictionary<string, RelationType>();
    #endregion
}

