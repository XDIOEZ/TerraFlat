using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 旧食物数值表序列化格式，仅供统一数值工作台迁移历史数据。
/// </summary>
public class FoodStatTableConfig : ScriptableObject
{
    public List<FoodStatTableRow> Rows = new();
}
