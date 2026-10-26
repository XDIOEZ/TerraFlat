using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PrefabStatTableRow
{
    public GameObject Prefab; // 预制体引用
    public string PrefabPath; // 预制体路径
    public string PrefabName; // 预制体名称
    public float MaxHp; // 物品血量上限
    public float Hp; // 物品当前血量
    public float CuttingDefense; // 切割防御
    public float PiercingDefense; // 穿刺防御
    public float ChoppingDefense; // 劈砍防御
    public float BluntDefense; // 钝击防御
    public float CuttingDamage; // 切割伤害
    public float PiercingDamage; // 穿刺伤害
    public float ChoppingDamage; // 劈砍伤害
    public float BluntDamage; // 钝击伤害
    public bool HasHp; // 是否存在血量组件
    public bool HasDefense; // 是否存在防御字段
    public bool HasDamage; // 是否存在伤害组件
}

/// <summary>
/// 旧 Prefab 数值表序列化格式，仅供统一数值工作台迁移历史数据。
/// </summary>
public class PrefabStatTableConfig : ScriptableObject
{
    public List<PrefabStatTableRow> Rows = new();
}
