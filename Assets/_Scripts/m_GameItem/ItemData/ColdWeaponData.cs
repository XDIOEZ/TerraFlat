
using MemoryPack;
using NaughtyAttributes;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ColdWeaponData : ItemData
{
    [Tooltip("伤害组成")]
    public Damage _damage;
    [Tooltip("最小伤害间隔")]
    public float _minDamageInterval;
    [Tooltip("最大攻击距离")]
    public float _maxAttackDistance;
    [Tooltip("出手攻击速度")]
    public float _attackSpeed;
    [Tooltip("武器返回速度")]
    public float _returnSpeed;
    [Tooltip("自旋转速度")]
    public float _spinSpeed;
    [Tooltip("武器精力消耗速度")]
    public float _energyConsumptionSpeed;

    [Button("从Excel同步数据")]
    public override int SyncData()
    {
        int itemRow = base.SyncData();
   
        // 读取物品的其他属性（使用泛型方法）

        // 物理伤害（float）
        _damage.PhysicalDamage = m_ExcelManager.Instance.GetConvertedValue<float>(
            "PhysicalDamage", itemRow, 0.0f);

        // 魔法伤害（float）
        _damage.MagicDamage = m_ExcelManager.Instance.GetConvertedValue<float>(
            "MagicDamage", itemRow, 0.0f);

        // 破甲能力（float）
        _damage.ArmorBreaking = m_ExcelManager.Instance.GetConvertedValue<float>(
            "ArmorBreaking", itemRow, 0.0f);

        // 最小伤害间隔（float）
        _minDamageInterval = m_ExcelManager.Instance.GetConvertedValue<float>(
            "MinDamageInterval", itemRow, 0.0f);

        // 最大攻击距离（float）
        _maxAttackDistance = m_ExcelManager.Instance.GetConvertedValue<float>(
            "MaxAttackDistance", itemRow, 0.0f);

        // 出手攻击速度（float）
        _attackSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "AttackSpeed", itemRow, 0.0f);

        // 武器返回速度（float）
        _returnSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "ReturnSpeed", itemRow, 0.0f);

        // 自旋转速度（float）
        _spinSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "SpinSpeed", itemRow, 0.0f);

        // 武器精力消耗速度（float）
        _energyConsumptionSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "EnergyConsumptionSpeed", itemRow, 0.0f);
        return itemRow;
    }
}
