using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[System.Serializable]
public class PrefabStatTableRow
{
    public GameObject Prefab; // 预制体引用
    public string PrefabPath; // 预制体路径
    public string PrefabName; // 预制体名称
    public float MaxHp; // 物品血量上限
    public float Hp; // 物品当前血量
    public float Defense; // 物品防御
    public float Damage; // 武器基础伤害
    public bool HasHp; // 是否存在血量组件
    public bool HasDefense; // 是否存在防御字段
    public bool HasDamage; // 是否存在伤害组件
}

/// <summary>
/// Prefab 数值总表配置
/// </summary>
public class PrefabStatTableConfig : ScriptableObject
{
    public List<PrefabStatTableRow> Rows = new(); // 表格行

    private void OnEnable()
    {
        Rows ??= new List<PrefabStatTableRow>();
    }

    private void OnValidate()
    {
        Rows ??= new List<PrefabStatTableRow>();
    }

    public void SaveNow()
    {
        Rows ??= new List<PrefabStatTableRow>();
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
        AssetDatabase.SaveAssets();
    }
}