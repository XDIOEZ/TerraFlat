
using MemoryPack;
using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;
using NaughtyAttributes;

[System.Serializable]
[MemoryPackable]
public partial class Damage
{
    [Header("�˺���ֵ����")]
    public float PhysicalDamage;
    public float ArmorBreaking;
    public float MagicDamage;

    [Header("�˺���������")]
    public List<string> DamageType;

    [MemoryPackIgnore]
    public float TotalDamage => PhysicalDamage + MagicDamage;


    /// <summary>
    /// ���캯��
    /// </summary>
    public Damage()
    {
        // ��ʼ�� DamageType �б���ֹ�������쳣
        DamageType = new List<string>();
    }

    /// <summary>
    /// ��� Damage �ж�Ӧ���˺������б������ػ������������
    /// </summary>
    /// <param name="damageTypes">��Ҫ�����˺������б�</param>
    /// <returns>���ػ��������������0 ��ʾû�л����κ�����</returns>
    public int Check_DamageType(List<string> damageTypes)
    {
        // ������֤����� damageTypes Ϊ�ջ���Ч��ֱ�ӷ��� 0
        if (damageTypes == null || damageTypes.Count == 0)
        {
            return 0;
        }

        int hitCount = 0; // ��¼�������������

        // ����������˺������б����ÿ�������Ƿ������ DamageType ��
        foreach (string damageType in damageTypes)
        {
            // ����˺�����Ϊ�ջ���Ч������
            if (string.IsNullOrWhiteSpace(damageType))
            {
                continue;
            }

            // ����˺����ʹ����� DamageType �У����ӻ��м���
            if (DamageType.Contains(damageType, StringComparer.OrdinalIgnoreCase))
            {
                hitCount++;
            }
        }

        // ���ػ������������
        return hitCount;
    }

    /// <summary>
    /// ���������˺�
    /// </summary>
    /// <param name="defense">��������</param>
    /// <returns>�����˺�ֵ</returns>
    public float Return_EndDamage(Defense defense = null)
    {
        float damage = 0;

        // ���û�д���������ԣ���Ĭ��Ϊ 0
        float defenseStrength = defense?.defenseStrength ?? 0;
        float defenseToughness = defense?.defenseToughness ?? 0;

        // ��ֹħ������ֵ������Χ [0, 100]��������ħ���˺��������
        float defenseMagic = defense?.defenseMagic ?? 0; // Ĭ��Ϊ 0
        defenseMagic = Mathf.Clamp(defenseMagic, 0, 100); // ������ֵ������ 0-100 ��Χ��
        float magicDamageReduction = defenseMagic / 100f; // ����ħ���˺��������


        GetPhysicalDamage();
        // ����ħ���˺���Ӧ�ü������
        damage += MagicDamage * (1 - magicDamageReduction);

        // �����Ƽ��������˺���Ӱ��




        if (damage < 0)
        {
            damage = 1;
        }

        void GetPhysicalDamage()
        {
            //ʣ�໤��
            float remainingArmor = defenseStrength - ((ArmorBreaking * PhysicalDamage) * (1 - defenseToughness)); // �������������
            if (remainingArmor < 0)
            {
                remainingArmor = 0;
            }

            // ���������˺�
            damage += PhysicalDamage - remainingArmor;
        }

        return damage;


    }


}