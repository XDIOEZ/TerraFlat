using MemoryPack;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using Sirenix.OdinInspector;

public class Chicken : Item, IHunger, ISpeed, ISight,IHealth,IStamina
    , IFocusPoint
{
    public Data_Creature Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Creature; }
    #region 饥饿

    public Nutrition Foods { get => Data.NutritionData; set => Data.NutritionData = value; }
    public float EatingSpeed { get => Data.EatingSpeed; set => throw new System.NotImplementedException(); }
    public UltEvent OnNutrientChanged { get; set; }
    public UltEvent OnDeath { get; set; }
    #endregion
    #region 速度
    public float AdditiveModifier { get; set; }
    public float MultiplicativeModifier { get; set; }
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
    //移动方向/目标
    public Vector3 MoveTargetPosition { get; set; }
    [ShowInInspector]
    public Vector3 FocusPointPosition { get => MoveTargetPosition; set => MoveTargetPosition = value; }
    GameValue_float ISpeed.Speed { get => Data.Speed; set => Data.Speed = value; }
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
    }

    public void TakeABite(IFood food)
    {
        Foods.Food += EatingSpeed;
        food.BeEat(EatingSpeed);
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


