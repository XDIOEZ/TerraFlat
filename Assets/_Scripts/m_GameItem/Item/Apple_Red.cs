using ColorThief;
using DG.Tweening;
using MemoryPack;
using NUnit.Framework.Interfaces;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Apple_Red : Item,IFood
{
    public Apple_Red_Data _data;
    public override ItemData Item_Data
    {
        get
        {
            return _data;
        }
        set
        {
            _data = (Apple_Red_Data)value;
        }
    }
    public Hunger_Water Foods { get => _data.Energy_food; set => _data.Energy_food = value; }
    public UltEvent OnNutrientChanged { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EatingValue = 0;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }
    public override void Act()
    {
    }

    public Hunger_Water BeEat(float eatSpeed)
    {
        if (Item_Data == null || Foods == null)  return null;

        // 使用 DOTween 做抖动动画
        SelfFood.ShakeItem(this.transform);

        EatingValue += eatSpeed;
        if (EatingValue >= Foods.MaxFood)
        {
            Item_Data.Stack.Amount--;

            UpdatedUI_Event?.Invoke();

            Foods.Food = Foods.MaxFood;
            Foods.Water = Foods.MaxWater;
             
            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                //停止DoTween动画
                transform.DOKill();
                Destroy(gameObject);
            }
            return Foods;
        }
        return null;
    }
}

