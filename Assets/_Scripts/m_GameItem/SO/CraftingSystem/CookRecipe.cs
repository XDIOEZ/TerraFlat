using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "新熔炼配方", menuName = "合成/熔炼配方")]
public class CookRecipe : Recipe
{
    [Header("熔炼温度")]
    public float Temperature  = 0;
}
