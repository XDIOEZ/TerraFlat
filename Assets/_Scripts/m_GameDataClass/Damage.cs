
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
    [Header("伤害数值设置")]
    public float PhysicalDamage;
    public float ArmorBreaking;
    public float MagicDamage;

    [Header("伤害类型设置")]
    public List<string> DamageType;

    [MemoryPackIgnore]
    public float TotalDamage => PhysicalDamage + MagicDamage;


    /// <summary>
    /// 构造函数
    /// </summary>
    public Damage()
    {
        // 初始化 DamageType 列表，防止空引用异常
        DamageType = new List<string>();
    }

    /// <summary>
    /// 检测 Damage 中对应的伤害类型列表，并返回击中弱点的数量
    /// </summary>
    /// <param name="damageTypes">需要检测的伤害类型列表</param>
    /// <returns>返回击中弱点的数量，0 表示没有击中任何弱点</returns>
    public int Check_DamageType(List<string> damageTypes)
    {
        // 输入验证：如果 damageTypes 为空或无效，直接返回 0
        if (damageTypes == null || damageTypes.Count == 0)
        {
            return 0;
        }

        int hitCount = 0; // 记录击中弱点的数量

        // 遍历输入的伤害类型列表，检查每个类型是否存在于 DamageType 中
        foreach (string damageType in damageTypes)
        {
            // 如果伤害类型为空或无效，跳过
            if (string.IsNullOrWhiteSpace(damageType))
            {
                continue;
            }

            // 如果伤害类型存在于 DamageType 中，增加击中计数
            if (DamageType.Contains(damageType, StringComparer.OrdinalIgnoreCase))
            {
                hitCount++;
            }
        }

        // 返回击中弱点的数量
        return hitCount;
    }

    /// <summary>
    /// 计算最终伤害
    /// </summary>
    /// <param name="defense">防御属性</param>
    /// <returns>最终伤害值</returns>
    public float Return_EndDamage(Defense defense = null)
    {
        float damage = 0;

        // 如果没有传入防御属性，则默认为 0
        float defenseStrength = defense?.defenseStrength ?? 0;
        float defenseToughness = defense?.defenseToughness ?? 0;

        // 防止魔法防御值超出范围 [0, 100]，并计算魔法伤害减免比例
        float defenseMagic = defense?.defenseMagic ?? 0; // 默认为 0
        defenseMagic = Mathf.Clamp(defenseMagic, 0, 100); // 将防御值限制在 0-100 范围内
        float magicDamageReduction = defenseMagic / 100f; // 计算魔法伤害减免比例


        GetPhysicalDamage();
        // 计算魔法伤害，应用减免比例
        damage += MagicDamage * (1 - magicDamageReduction);

        // 计算破甲能力对伤害的影响




        if (damage < 0)
        {
            damage = 1;
        }

        void GetPhysicalDamage()
        {
            //剩余护甲
            float remainingArmor = defenseStrength - ((ArmorBreaking * PhysicalDamage) * (1 - defenseToughness)); // 计算防御力减免
            if (remainingArmor < 0)
            {
                remainingArmor = 0;
            }

            // 计算物理伤害
            damage += PhysicalDamage - remainingArmor;
        }

        return damage;


    }


}