using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 部位是独立耐久、护甲和倍率容器。整体生命只扣一次最终伤害；耐久耗尽不致死且仍可命中。
/// 装备部位护甲以来源登记，不写回持久化基础防御；惩罚由耐久状态派生，按部位来源撤销。
/// </summary>
public partial class DamageReceiver
{
    #region 部位结算
    private readonly Dictionary<object, BodyArmor> bodyArmorSources = new();
    private BuffManager bodyPenaltyManager;
    private readonly HashSet<string> bodyPenaltySources = new();
    private bool bodyPenaltiesDirty = true;

    private sealed class BodyArmor
    {
        public BodyPartType[] Parts;
        public CombatDefense Defense;
    }

    private bool CanMutateBodyState() => FlatWorld.Networking.GameNetwork.HasStateAuthority;

    private static void ValidateBodyValue(float value, string parameter)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new ArgumentOutOfRangeException(parameter, "数值必须是有限非负数。");
    }

    /// <summary>MOD/装备以来源登记覆盖部位；同一来源替换而非累加，卸装只撤销自身。</summary>
    public void SetBodyPartArmor(object source, BodyPartType[] parts, CombatDefense defense)
    {
        if (source == null || parts == null || defense == null) throw new ArgumentNullException();
        ValidateBodyValue(defense.Cutting, nameof(defense.Cutting));
        ValidateBodyValue(defense.Piercing, nameof(defense.Piercing));
        ValidateBodyValue(defense.Chopping, nameof(defense.Chopping));
        ValidateBodyValue(defense.Blunt, nameof(defense.Blunt));
        bodyArmorSources[source] = new BodyArmor { Parts = (BodyPartType[])parts.Clone(),
            Defense = new CombatDefense(defense.Cutting, defense.Piercing, defense.Chopping, defense.Blunt) };
    }

    public void RemoveBodyPartArmor(object source) => bodyArmorSources.Remove(source);

    /// <summary>全身基础防御与覆盖此部位的装备防御只合并一次，再按类型抵扣。</summary>
    public CombatDefense GetBodyPartDefense(BodyPartHealth part)
    {
        var result = new CombatDefense(Defense.Cutting, Defense.Piercing, Defense.Chopping, Defense.Blunt);
        result.Add(part.DefenseValues);
        foreach (BodyArmor armor in bodyArmorSources.Values)
            if (Array.IndexOf(armor.Parts, part.Part) >= 0) result.Add(armor.Defense);
        return result;
    }

    /// <summary>共享纯计算入口；先护甲，再部位倍率、难度和来源倍率，不受剩余耐久截断。</summary>
    public static float4 CalculateBodyPartDamage(float4 raw, CombatDefense armor, float partMultiplier,
        float share = 1f, float difficulty = 1f, float receivedMultiplier = 1f)
    {
        return math.max(float4.zero, raw - GameplayCombatBridge.Values(armor)) *
            (share * partMultiplier * difficulty * receivedMultiplier);
    }

    private float4 ResolveBodyPartAttack(float4 raw, float difficulty, float receivedMultiplier,
        BodyPartType? targetPart, out List<BodyPartDamageInfo> hits)
    {
        hits = new List<BodyPartDamageInfo>(2);
        BodyPartHealth first;
        if (targetPart.HasValue)
        {
            if (!TryGetBodyPart(targetPart.Value, out first))
                throw new ArgumentException("目标不存在指定部位。", nameof(targetPart));
        }
        else first = SelectRandomBodyPart(null);
        if (first == null) throw new InvalidOperationException("身体配置没有正命中权重的部位。");
        BodyPartHealth second = !targetPart.HasValue && UnityEngine.Random.value < Data.TwoPartHitChance
            ? SelectRandomBodyPart(first) : null;
        float share = second == null ? 1f : 0.5f;
        float4 result = PrepareBodyPartHit(first, raw, share, difficulty, receivedMultiplier, hits);
        if (second != null) result += PrepareBodyPartHit(second, raw, share, difficulty, receivedMultiplier, hits);
        return result;
    }

    private float4 PrepareBodyPartHit(BodyPartHealth part, float4 raw, float share, float difficulty,
        float receivedMultiplier, List<BodyPartDamageInfo> hits)
    {
        float4 typed = CalculateBodyPartDamage(raw, GetBodyPartDefense(part), part.DamageMultiplier,
            share, difficulty, receivedMultiplier);
        float damage = math.csum(typed);
        hits.Add(new BodyPartDamageInfo { Part = part.Part, DamageValue = damage, DamageShare = share,
            HpBefore = part.Hp, HpAfter = Mathf.Max(0f, part.Hp - damage), MaxHp = part.MaxHp,
            IsDepleted = part.Hp <= damage });
        return typed;
    }
    #endregion

    #region 独立改造与恢复
    /// <summary>提升或降低指定部位耐久上限，不改变整体生命或自动治疗。</summary>
    public void SetBodyPartMaxDurability(BodyPartType part, float value)
    {
        ValidateBodyValue(value, nameof(value));
        if (!CanMutateBodyState()) return;
        if (!TryGetBodyPart(part, out BodyPartHealth state)) throw new ArgumentException("未知部位", nameof(part));
        float previous = state.Hp;
        state.MaxHp = value;
        state.Hp = Mathf.Min(state.Hp, value);
        try { DispatchBodyPartHealthChanged(state, previous); }
        finally { NotifyBodyStateChanged(); }
    }

    public float RestoreBodyPartDurability(BodyPartType part, float amount) => HealBodyPart(part, amount);

    /// <summary>显式重生恢复全部耐久；普通回血不调用此入口。</summary>
    public void ResetBodyPartDurability()
    {
        if (!CanMutateBodyState() || !UsesBodyPartHealth) return;
        var previous = CaptureBodyPartSnapshots();
        foreach (BodyPartHealth part in Data.BodyParts) part.Hp = part.MaxHp;
        try { DispatchNetworkBodyPartChanges(previous); }
        finally { ReconcileBodyPartPenalties(); NotifyBodyStateChanged(); }
    }

    private void NotifyBodyStateChanged()
    {
        try { DataUpdate?.Invoke(); OnAction?.Invoke(Hp); }
        finally { ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item); }
    }
    #endregion

    #region 派生惩罚生命周期
    /// <summary>全模块装配之后按耐久派生独立来源 Buff；远程副本仅重建表现/修饰，不施加伤害。</summary>
    private void ReconcileBodyPartPenalties()
    {
        if (!bodyPenaltiesDirty) return;
        BuffManager manager = item?.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        if (manager == null) return;
        if (bodyPenaltyManager != manager) { ClearBodyPartPenalties(); bodyPenaltyManager = manager; }
        var expected = new HashSet<string>();
        if (UsesBodyPartHealth && Hp > 0f)
            foreach (BodyPartHealth part in Data.BodyParts)
                if (part.Hp <= 0f)
                    foreach (string buffId in part.DepletionBuffIds)
                    {
                        // 有限时长创伤由实际受伤触发，不由零耐久状态反复续上。
                        BuffDefinition definition = GameRes.Instance.GetBuffDefinition(buffId);
                        if (definition == null || !definition.IsPermanent) continue;
                        string source = "body:" + part.Part + ":" + buffId;
                        expected.Add(source);
                        manager.EnsureSourceBuff(buffId, source);
                    }
        foreach (string source in bodyPenaltySources)
            if (!expected.Contains(source)) manager.RemoveSourceBuff(source);
        bodyPenaltySources.Clear();
        bodyPenaltySources.UnionWith(expected);
        bodyPenaltiesDirty = false;
    }

    /// <summary>只有新的有效部位伤害才施加有限创伤；耐久已为零仍可再次触发，治疗/读档不重放。</summary>
    private void ApplyTimedBodyPartTrauma(BodyPartDamageInfo hit)
    {
        if (!CanMutateBodyState() || Hp <= 0f || hit.DamageValue <= 0f || hit.HpAfter > 0f ||
            !TryGetBodyPart(hit.Part, out BodyPartHealth part)) return;
        BuffManager manager = item.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
        if (manager == null) return;
        foreach (string buffId in part.DepletionBuffIds)
        {
            BuffDefinition definition = GameRes.Instance.GetBuffDefinition(buffId);
            if (definition != null && !definition.IsPermanent) manager.AddBuff(buffId);
        }
    }

    private void ClearBodyPartPenalties()
    {
        if (bodyPenaltyManager != null)
            foreach (string source in bodyPenaltySources) bodyPenaltyManager.RemoveSourceBuff(source);
        bodyPenaltySources.Clear();
        bodyPenaltyManager = null;
        bodyPenaltiesDirty = true;
    }
    #endregion
}
