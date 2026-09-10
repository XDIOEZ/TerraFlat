using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 受击后状态规则契约：只消费已经完成生命结算的 DamageReceiverDamageInfo，不能反向修改本次伤害。
/// </summary>
public interface IDamageReceivedStatusRule
{
    /// <summary>处理一次已经完成权威生命扣除的受击结果。</summary>
    void Apply(DamageReceiverDamageInfo damageInfo);
}

/// <summary>
/// 受击状态规则注册表。DamageReceiver 只负责发布结算结果；出血、中毒等状态通过独立规则注册，避免把具体 Buff 写进核心伤害模块。
/// </summary>
public static class DamageReceivedStatusEffectRegistry
{
    private static readonly List<IDamageReceivedStatusRule> Rules = new();
    private static bool builtInsRegistered;

    /// <summary>子系统重载时清空静态状态，避免关闭 Domain Reload 后重复注册。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        Rules.Clear();
        builtInsRegistered = false;
    }

    /// <summary>注册自定义受击状态规则；MOD 可通过该入口扩展新的状态机制。</summary>
    public static void Register(IDamageReceivedStatusRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        EnsureBuiltIns();
        if (!Rules.Contains(rule))
            Rules.Add(rule);
    }

    /// <summary>注销此前注册的自定义规则。</summary>
    public static void Unregister(IDamageReceivedStatusRule rule)
    {
        if (rule == null)
            return;

        Rules.Remove(rule);
    }

    /// <summary>按注册顺序广播一次受击结果；单条状态规则异常不会破坏已经提交的生命结算。</summary>
    public static void Publish(DamageReceiverDamageInfo damageInfo)
    {
        if (damageInfo == null)
            return;

        EnsureBuiltIns();
        for (int i = 0; i < Rules.Count; i++)
        {
            try
            {
                Rules[i].Apply(damageInfo);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DamageReceivedStatusEffectRegistry] 受击状态规则执行失败：{exception}");
            }
        }
    }

    /// <summary>懒注册内置规则，兼容编辑器测试未经过 RuntimeInitialize 的场景。</summary>
    private static void EnsureBuiltIns()
    {
        if (builtInsRegistered)
            return;

        Rules.Add(new BleedingDamageStatusRule());
        builtInsRegistered = true;
    }
}

/// <summary>
/// 刃伤出血规则：带 Blood 标签的有血实体受到实际切割、穿刺或劈砍伤害时，按伤害值提高出血概率并决定出血等级。
/// </summary>
public sealed class BleedingDamageStatusRule : IDamageReceivedStatusRule
{
    private const string BloodTag = "Blood";
    private const float ChancePerDamage = 0.04f;
    private const float MaxApplicationChance = 0.9f;
    private const float Bleeding2DamageThreshold = 6f;
    private const float Bleeding3DamageThreshold = 12f;

    /// <summary>处理一次刃伤；0 点、致死、纯钝击以及没有 Blood 标签的目标都不会产生出血。</summary>
    public void Apply(DamageReceiverDamageInfo damageInfo)
    {
        if (damageInfo.DamageValue <= 0f || damageInfo.IsFatal || !IsEligibleReceiver(damageInfo.ReceiverItem))
            return;

        float bleedingDamage = ResolveBleedingRelevantDamage(damageInfo);
        if (bleedingDamage <= 0f)
            return;

        float chance = Mathf.Min(MaxApplicationChance, bleedingDamage * ChancePerDamage);
        if (chance <= 0f || UnityEngine.Random.value >= chance)
            return;

        BuffManager buffManager = damageInfo.ReceiverItem?.itemMods?
            .GetMod_ByID<BuffManager>(ModText.BuffManager);
        if (buffManager == null)
            return;

        int tier = bleedingDamage >= Bleeding3DamageThreshold
            ? 3
            : bleedingDamage >= Bleeding2DamageThreshold
                ? 2
                : 1;
        buffManager.ApplyBleedingTier(tier);
    }

    /// <summary>仅声明 Blood 标签的实体属于可出血目标；物种分类与是否有血保持解耦。</summary>
    private static bool IsEligibleReceiver(Item receiverItem)
    {
        List<string> tags = receiverItem?.itemData?.Tags;
        return tags != null && tags.ContainsTag(BloodTag);
    }

    /// <summary>读取防御与全局倍率之后的真实刃伤分量，并按实际掉血量裁掉过量伤害。</summary>
    private static float ResolveBleedingRelevantDamage(DamageReceiverDamageInfo damageInfo)
    {
        CombatDamage values = damageInfo.ResolvedDamageValues;
        if (values != null)
        {
            float relevant = Mathf.Max(0f, values.Cutting) +
                             Mathf.Max(0f, values.Piercing) +
                             Mathf.Max(0f, values.Chopping);
            float total = Mathf.Max(0f, values.TotalCombatPower);
            if (relevant <= 0f || total <= 0f)
                return 0f;

            float actualRatio = Mathf.Clamp01(damageInfo.DamageValue / total);
            return relevant * actualRatio;
        }

        // 兼容外部系统手工构造 DamageInfo 的情况：按发送端类型占比把实际掉血拆回刃伤分量。
        CombatDamage senderValues = damageInfo.SenderDamageValues;
        if (senderValues == null)
            return 0f;

        float senderRelevant = Mathf.Max(0f, senderValues.Cutting) +
                               Mathf.Max(0f, senderValues.Piercing) +
                               Mathf.Max(0f, senderValues.Chopping);
        float senderTotal = Mathf.Max(0f, senderValues.TotalCombatPower);
        return senderTotal > 0f
            ? damageInfo.DamageValue * Mathf.Clamp01(senderRelevant / senderTotal)
            : 0f;
    }
}
