using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using Unity.Mathematics;
using UnityEngine;

/// <summary>少量后端 Bridge 的契约；只有实际武器 Pulse 查询外部后端，不为每只 AI 注册事件。</summary>
public interface IGameplayCombatBridge
{
    /// <summary>为当前后端所属世界的旧对象补充世界身份。</summary>
    bool TryGetIdentity(Item item, out CombatIdentity identity);
    /// <summary>消费已经同步好的真实武器窗口，共用武器的目标配额。</summary>
    void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context);
}

/// <summary>可组合发送端能力把静态值附加到统一上下文，不订阅或查询每个 ECS 目标。</summary>
public interface ICombatDamageContextModifier
{
    /// <summary>追加本能力的纯值参数，禁止在构造上下文时直接结算目标。</summary>
    void ModifyDamageContext(ref CombatDamageContext context);
}

/// <summary>旧对象与纯值战斗的单向适配；不引用 AIECS，任意数据后端均可接入。</summary>
public static class GameplayCombatBridge
{
    private static readonly List<IGameplayCombatBridge> Bridges = new List<IGameplayCombatBridge>();

    /// <summary>注册后端；调用方在世界退出时注销。</summary>
    public static void Register(IGameplayCombatBridge bridge) { if (bridge != null && !Bridges.Contains(bridge)) Bridges.Add(bridge); }

    /// <summary>释放后端引用，避免禁用或换世界后的旧输入继续进入。</summary>
    public static void Unregister(IGameplayCombatBridge bridge) => Bridges.Remove(bridge);

    /// <summary>关闭域重载时也清除上一次 Play 的后端引用。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Bridges.Clear();

    /// <summary>优先读取后端世界身份，旧后端独立运行时仍保存 UID 和对象代际。</summary>
    public static CombatIdentity Identity(Item item)
    {
        if (item?.itemData == null) return default;
        for (int i = 0; i < Bridges.Count; i++) if (Bridges[i].TryGetIdentity(item, out var identity)) return identity;
        return new CombatIdentity { Backend = CombatBackend.External, Value = unchecked((uint)item.itemData.Guid), Generation = item.RuntimeGeneration };
    }

    /// <summary>统一难度参数供旧接收器与纯数据结算使用。</summary>
    public static CombatDifficulty Difficulty()
    {
        var values = GameDifficultyService.Current.CreatureCombat;
        return new CombatDifficulty { PlayerAttack = values.PlayerAttackMultiplier,
            CreatureAttack = values.AttackMultiplier, CreatureHealth = values.MaxHealthMultiplier };
    }

    /// <summary>冷/旧路径把托管四类数值转换为 Native 可用值。</summary>
    public static float4 Values(CombatDamage damage) => damage == null ? float4.zero : new float4(damage.Cutting, damage.Piercing, damage.Chopping, damage.Blunt);

    /// <summary>旧接收器仍使用现有序列化防御对象，只在结算入口转换。</summary>
    public static float4 Values(CombatDefense defense) => defense == null ? float4.zero : new float4(defense.Cutting, defense.Piercing, defense.Chopping, defense.Blunt);

    /// <summary>把结算分量转换成旧反馈事件所需类型，托管分配不进入 ECS 热路径。</summary>
    public static CombatDamage LegacyValues(float4 values) => new CombatDamage(values.x, values.y, values.z, values.w);

    /// <summary>真实 Item 发送端适配为纯上下文；武器身份与持有者击杀归因分开保存。</summary>
    public static CombatDamageContext Context(IDamageSender sender, CombatClock clock)
    {
        Item item = sender.attacker; var slow = sender as IHitSlowdownSource;
        var context = new CombatDamageContext { Attack = new CombatAttackKey { Source = Identity(item) }, Credit = Identity(item?.Owner ?? item),
            Faction = FactionRelationService.GetFactionId(item), Damage = Values(sender.DamageValues), Clock = clock,
            Origin = sender is Mod_Damage weapon ? (float2)weapon.DamageOrigin : item != null ? (float2)(Vector2)item.transform.position : float2.zero,
            SourceIsPlayer = (byte)(GameDifficultyService.IsPlayer(item) ? 1 : 0),
            SlowEnabled = (byte)(slow != null && slow.HitSlowdownEnabled ? 1 : 0),
            SlowMultiplier = slow?.HitSlowMultiplier ?? 1f, SlowDuration = slow?.HitSlowDuration ?? 0f,
            BuildingMultiplier = sender is IBuildingDamageSource building ? building.BuildingDamageMultiplier : 1f };
        if (sender is ICombatDamageContextModifier modifier) modifier.ModifyDamageContext(ref context);
        return context;
    }

    /// <summary>一次实际攻击 Pulse 分发给少量注册后端；不因 Collider 每帧启用而反复扫描。</summary>
    public static void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
    {
        for (int i = 0; i < Bridges.Count; i++) Bridges[i].QueryWeaponPulse(weapon, shape, context);
    }
}
