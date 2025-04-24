
using MemoryPack;
using NaughtyAttributes;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ColdWeaponData : ItemData
{
    [Tooltip("�˺����")]
    public Damage _damage;
    [Tooltip("��С�˺����")]
    public float _minDamageInterval;
    [Tooltip("��󹥻�����")]
    public float _maxAttackDistance;
    [Tooltip("���ֹ����ٶ�")]
    public float _attackSpeed;
    [Tooltip("���������ٶ�")]
    public float _returnSpeed;
    [Tooltip("����ת�ٶ�")]
    public float _spinSpeed;
    [Tooltip("�������������ٶ�")]
    public float _energyConsumptionSpeed;

    [Button("��Excelͬ������")]
    public override int SyncData()
    {
        int itemRow = base.SyncData();
   
        // ��ȡ��Ʒ���������ԣ�ʹ�÷��ͷ�����

        // �����˺���float��
        _damage.PhysicalDamage = m_ExcelManager.Instance.GetConvertedValue<float>(
            "PhysicalDamage", itemRow, 0.0f);

        // ħ���˺���float��
        _damage.MagicDamage = m_ExcelManager.Instance.GetConvertedValue<float>(
            "MagicDamage", itemRow, 0.0f);

        // �Ƽ�������float��
        _damage.ArmorBreaking = m_ExcelManager.Instance.GetConvertedValue<float>(
            "ArmorBreaking", itemRow, 0.0f);

        // ��С�˺������float��
        _minDamageInterval = m_ExcelManager.Instance.GetConvertedValue<float>(
            "MinDamageInterval", itemRow, 0.0f);

        // ��󹥻����루float��
        _maxAttackDistance = m_ExcelManager.Instance.GetConvertedValue<float>(
            "MaxAttackDistance", itemRow, 0.0f);

        // ���ֹ����ٶȣ�float��
        _attackSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "AttackSpeed", itemRow, 0.0f);

        // ���������ٶȣ�float��
        _returnSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "ReturnSpeed", itemRow, 0.0f);

        // ����ת�ٶȣ�float��
        _spinSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "SpinSpeed", itemRow, 0.0f);

        // �������������ٶȣ�float��
        _energyConsumptionSpeed = m_ExcelManager.Instance.GetConvertedValue<float>(
            "EnergyConsumptionSpeed", itemRow, 0.0f);
        return itemRow;
    }
}
