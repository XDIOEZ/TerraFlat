using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FoodEater : MonoBehaviour
{
    public IHunger hunger;
    public float EatingSpeed { get=> hunger.EatingSpeed; set => hunger.EatingSpeed = value; }
    // Start is called before the first frame update
    void Start()
    {
        hunger = GetComponentInParent<IHunger>();
    }

    public void Eat(IFood food)
    {
        hunger.Nutrition.Food += EatingSpeed;
        food.BeEat(EatingSpeed);
    }
}
