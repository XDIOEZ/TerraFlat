
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

public interface IFood
{

    Nutrition NutritionData { get; set; }

    Nutrition BeEat(float BeEatSpeed);

    public IFood SelfFood { get => this; }
}