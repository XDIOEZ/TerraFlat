using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "新熔炼配方", menuName = "合成/熔炼配方")]
public class CookRecipe : ScriptableObject
{
    [Header("输入材料")]
    public Input_List inputs = new Input_List();

    [Header("输出产物")]
    public Output_List outputs = new Output_List();

    public AssetReference assetRef;
}
