using MemoryPack;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Chicken : Item, IHunger, ISpeed, ISight,IHealth,IStamina
{
    public Data_Creature Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Creature; }
    #region ����

    public Nutrition Foods { get => Data.NutritionData; set => Data.NutritionData = value; }
    public float EatingSpeed { get => Data.EatingSpeed; set => throw new System.NotImplementedException(); }
    public UltEvent OnNutrientChanged { get; set; }

    #endregion
    #region �ٶ�
    // ����ISpeed�ӿ�ʵ�֣�ֱ��ӳ��AnimalData�е��ٶ�����
    public float Speed
    {
        get => Data.speed;
        set => Data.speed = value;
    }

    public float MaxSpeed
    {
        get => Data.speed_Max;
        set => Data.speed_Max = value;
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
        set => Data.stamina = Mathf.Clamp(value, 0, Data.stamina_Max);
    }

    public float MaxStamina
    {
        get => Data.stamina_Max;
        set => Data.stamina_Max = value;
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
        Data.NutritionData.Food -= Time.fixedDeltaTime * Data.productionSpeed;
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


