using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "新熔炼配方", menuName = "合成/熔炼配方")]
public class CookRecipe : Recipe
{
    public float Temperature  = 0;
}
