using System;
using FlatWorld.Combat;
using MemoryPack;

/// <summary>保存致命伤害的事实，文案和语言由死亡界面独立解释。</summary>
[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial class DeathCauseRecord
{
    #region 死因快照
    [MemoryPackOrder(0)] public string CauseId = "unknown";
    [MemoryPackOrder(1)] public string AttackerItemId;
    [MemoryPackOrder(2)] public string AttackerName;
    [MemoryPackOrder(3)] public string WeaponItemId;
    [MemoryPackOrder(4)] public string WeaponName;
    [MemoryPackOrder(5)] public string EffectId;
    [MemoryPackOrder(6)] public string EffectName;
    [MemoryPackOrder(7)] public bool UseCauseForEffect; // 环境持续伤害可明确保留接触原因，普通攻击状态使用 Buff 自己的原因。
    [MemoryPackOrder(8)] public int Variant;

    public DeathCauseRecord Copy() => (DeathCauseRecord)MemberwiseClone();
    #endregion

    #region 来源采集
    public static DeathCauseRecord FromDamage(IDamageSender sender, in CombatDamageContext context)
    {
        var result = new DeathCauseRecord
        {
            CauseId = context.DeathCauseId.Length > 0 ? context.DeathCauseId.ToString()
                : (context.DeliveryCapabilities & CombatDeliveryCapabilities.Projectile) != 0 ? "projectile"
                : !context.Attack.Source.IsValid || context.Attack.Source.Backend == CombatBackend.Environment ? "unknown" : "combat",
            AttackerItemId = context.CreditItemId.ToString(),
            WeaponItemId = context.WeaponItemId.ToString()
        };
        if (sender is IDeathCauseSource reason && !string.IsNullOrWhiteSpace(reason.DeathCauseId))
            result.CauseId = reason.DeathCauseId;
        Item source = sender?.attacker;
        if (source == null) return result;
        if (source.itemMods != null)
            foreach (Module module in source.itemMods.Mods.Values)
                if (module is IDeathCauseSnapshotSource snapshot && snapshot.CaptureDeathCause() is DeathCauseRecord frozen)
                {
                    frozen.CauseId = result.CauseId;
                    return frozen;
                }

        Item credit = source;
        for (int depth = 0; credit.Owner != null && credit.Owner != credit && depth < 8; depth++)
            credit = credit.Owner;
        return CaptureAttack(source, credit, result);
    }

    public static DeathCauseRecord CaptureAttack(Item weapon, Item attacker, DeathCauseRecord result = null)
    {
        result ??= new DeathCauseRecord { CauseId = "combat" };
        if (attacker is Player player)
        {
            result.AttackerItemId = null;
            result.AttackerName = player.ProfileName;
        }
        else
        {
            result.AttackerItemId = attacker?.itemData?.IDName;
            result.AttackerName = attacker?.itemData?.GameName;
        }
        if (weapon != attacker && string.IsNullOrEmpty(result.WeaponItemId))
            result.WeaponItemId = weapon?.itemData?.IDName;
        result.WeaponName = weapon != attacker ? weapon?.itemData?.GameName : null;
        return result;
    }

    public static DeathCauseRecord FromBuff(BuffInstance runtime)
    {
        DeathCauseRecord result = runtime.DamageOrigin?.Copy() ?? new DeathCauseRecord();
        if (!result.UseCauseForEffect || string.IsNullOrWhiteSpace(result.CauseId))
            result.CauseId = string.IsNullOrWhiteSpace(runtime.Definition?.DeathCauseId)
                ? "effect" : runtime.Definition.DeathCauseId;
        result.EffectId = runtime.DefinitionId;
        result.EffectName = runtime.Definition?.DisplayName;
        return result;
    }
    #endregion
}

/// <summary>特殊伤害只声明自己的原因，不让生命模块识别具体玩法。</summary>
public interface IDeathCauseSource
{
    string DeathCauseId { get; }
}

/// <summary>延迟命中在释放时冻结事实，不依赖随后变化的持有关系。</summary>
public interface IDeathCauseSnapshotSource
{
    DeathCauseRecord CaptureDeathCause();
}
