using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 食物数值总表配置
/// </summary>
public class FoodStatTableConfig : ScriptableObject
{
    public List<FoodStatTableRow> Rows = new(); // 表格行

    private void OnEnable()
    {
        Rows ??= new List<FoodStatTableRow>();
    }

    private void OnValidate()
    {
        Rows ??= new List<FoodStatTableRow>();
    }

    public void SaveNow()
    {
        Rows ??= new List<FoodStatTableRow>();
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
        AssetDatabase.SaveAssets();
    }
}
