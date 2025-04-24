using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(fileName = "�������䷽", menuName = "�ϳ�/�����䷽")]
public class CookRecipe : ScriptableObject
{
    [Header("�������")]
    public Input_List inputs = new Input_List();

    [Header("�������")]
    public Output_List outputs = new Output_List();

    public AssetReference assetRef;
}
