using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>少量后端 Bridge 的契约；只有实际武器 Pulse 查询外部后端，不为每只 AI 注册事件。</summary>
public interface IGameplayCombatBridge
{
    /// <summary>为当前后端所属世界的旧对象补充世界身份。</summary>
    bool TryGetIdentity(Item item, out CombatIdentity identity);
    /// <summary>消费已经同步好的真实武器窗口，共用武器的目标配额。</summary>
    void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context);
}

/// <summary>中立环境范围伤害由真实数据后端采候选，外部玩家仍由自身接收器结算。</summary>
public interface IGameplayEnvironmentalDamageBridge
{
    void ApplyEnvironmentalDamage(string worldKey, in CombatDamageContext context, float radius);
}

/// <summary>可组合发送端能力把静态值附加到统一上下文，不订阅或查询每个 ECS 目标。</summary>
public interface ICombatDamageContextModifier
{
    /// <summary>追加本能力的纯值参数，禁止在构造上下文时直接结算目标。</summary>
    void ModifyDamageContext(ref CombatDamageContext context);
}

/// <summary>物理投影只转交 Unity 确认的目标与接触点，生命和命中资格仍由数据后端裁定。</summary>
internal interface IGameplayPhysicsDamageTarget
{
    /// <summary>消费真实碰撞命中，沿用武器窗口预约和后端伤害入口。</summary>
    void ReceivePhysicsDamage(Mod_Damage weapon, in CombatDamageContext context);
}

/// <summary>旧对象与纯值战斗的单向适配；不引用 AIECS，任意数据后端均可接入。</summary>
public static class GameplayCombatBridge
{
    private static readonly Unity.Profiling.ProfilerMarker ContextMarker = new("FlatWorld.Combat.Context");
    private static readonly Unity.Profiling.ProfilerMarker QueryMarker = new("FlatWorld.Combat.QueryWeaponPulse");
    private static readonly List<IGameplayCombatBridge> Bridges = new List<IGameplayCombatBridge>();

    #region 物理碰撞目标映射

    /// <summary>武器命中只认明确受击区域，不沿父子或兄弟节点猜测生命归属。</summary>
    public static Mod_DamageReceiver ResolvePhysicsDamageReceiver(Collider2D collider)
    {
        Collider2D source = ResolveSameWorldPhysicsSource(collider);
        if (source == null) return null;
        if (WorldPhysicsService2D.TryResolveCollider(collider, out WorldPhysicsColliderBinding2D binding))
            return (binding.Purpose & WorldPhysicsPurpose2D.Damage) != 0
                ? binding.Receiver as Mod_DamageReceiver ?? binding.Entity as Mod_DamageReceiver : null;
        ItemPhysicsRegion2D region = source.GetComponent<ItemPhysicsRegion2D>();
        if (region != null)
            return (region.Purpose & WorldPhysicsPurpose2D.Damage) != 0 ? region.Receiver as Mod_DamageReceiver : null;
        return CombatPhysicsChannels.IsDamageReceiverCollider(source) ? source.GetComponent<Mod_DamageReceiver>() : null;
    }

    /// <summary>身体接触沿已登记物品读取主生命能力，专供荆棘和投射物实体碰撞。</summary>
    public static Mod_DamageReceiver ResolvePhysicsBodyDamageReceiver(Collider2D collider)
    {
        Collider2D source = ResolveSameWorldPhysicsSource(collider);
        if (source == null || source.isTrigger) return null;
        Item owner;
        if (WorldPhysicsService2D.TryResolveCollider(collider, out WorldPhysicsColliderBinding2D binding))
        {
            if ((binding.Purpose & WorldPhysicsPurpose2D.Blocking) == 0) return null;
            owner = binding.Item ?? binding.Entity as Item;
        }
        else owner = source.GetComponentInParent<Item>();
        // 身体只转交主生命模块，不把同一物品的任意子接收器当作受击部位。
        return owner != null ? owner.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp) : null;
    }

    private static Collider2D ResolveSameWorldPhysicsSource(Collider2D collider)
    {
        Collider2D source = ColliderSource2D.Resolve(collider);
        return source != null && (source == collider ||
            source.gameObject.scene.GetPhysicsScene2D() == collider.gameObject.scene.GetPhysicsScene2D()) ? source : null;
    }

    /// <summary>按 Unity 返回的具体 Collider 读取目标，不按接触点猜测附近实体。</summary>
    internal static bool TryGetPhysicsDamageTarget(Collider2D collider, out IGameplayPhysicsDamageTarget target)
    {
        target = null;
        if (!WorldPhysicsService2D.TryResolveCollider(collider, out WorldPhysicsColliderBinding2D binding) ||
            (binding.Purpose & WorldPhysicsPurpose2D.Damage) == 0) return false;
        // 物理层只提供区域归属，伤害能力由玩法适配层解析。
        target = binding.Entity as IGameplayPhysicsDamageTarget ?? binding.Receiver as IGameplayPhysicsDamageTarget;
        return target != null;
    }

    /// <summary>实体碰撞允许沿阻挡区域找到原生生命后端，但交互区域不能伪造命中。</summary>
    internal static bool TryGetPhysicsContactDamageTarget(Collider2D collider, out IGameplayPhysicsDamageTarget target)
    {
        target = null;
        if (!WorldPhysicsService2D.TryResolveCollider(collider, out WorldPhysicsColliderBinding2D binding) ||
            (binding.Purpose & (WorldPhysicsPurpose2D.Damage | WorldPhysicsPurpose2D.Blocking)) == 0) return false;
        target = binding.Entity as IGameplayPhysicsDamageTarget ?? binding.Receiver as IGameplayPhysicsDamageTarget;
        return target != null;
    }

    #endregion

    /// <summary>注册后端；调用方在世界退出时注销。</summary>
    public static void Register(IGameplayCombatBridge bridge) { if (bridge != null && !Bridges.Contains(bridge)) Bridges.Add(bridge); }

    /// <summary>释放后端引用，避免禁用或换世界后的旧输入继续进入。</summary>
    public static void Unregister(IGameplayCombatBridge bridge) => Bridges.Remove(bridge);

    /// <summary>关闭域重载时也清除上一次 Play 的后端引用。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Bridges.Clear();
    }

    /// <summary>优先读取后端世界身份，旧后端独立运行时仍保存 UID 和对象代际。</summary>
    public static CombatIdentity Identity(Item item)
    {
        if (item?.itemData == null) return default;
        for (int i = 0; i < Bridges.Count; i++) if (Bridges[i].TryGetIdentity(item, out var identity)) return identity;
        return new CombatIdentity { Backend = CombatBackend.External, Value = unchecked((uint)item.itemData.Guid), Generation = item.RuntimeGeneration };
    }

    /// <summary>冻结当前战斗参数供接收器与纯数据结算共同使用。</summary>
    public static CombatTuning Tuning()
    {
        var values = GameRuleConfiguration.Current.CreatureCombat;
        return new CombatTuning { PlayerAttack = values.PlayerAttackMultiplier,
            CreatureAttack = values.AttackMultiplier, CreatureHealth = values.MaxHealthMultiplier };
    }

    /// <summary>托管伤害配置转换成物理、火焰、辐射、真实四通道。</summary>
    public static float4 Values(CombatDamage damage) => damage == null ? float4.zero : new float4(damage.Physical, damage.Fire, damage.Radiation, damage.True);

    /// <summary>防御只存在于三种可防护伤害通道。</summary>
    public static float4 Values(CombatDefense defense) => new float4(defense?.Physical ?? 0f, defense?.Fire ?? 0f, defense?.Radiation ?? 0f, 0f);

    /// <summary>从纯数值结算恢复对应的命中反馈类型。</summary>
    public static CombatDamage LegacyValues(float4 values, byte impactKind = 0) =>
        new(values.x, values.y, values.z, values.w, (CombatDamageKind)impactKind);

    /// <summary>真实 Item 发送端适配为纯上下文；武器身份与持有者击杀归因分开保存。</summary>
    public static CombatDamageContext Context(IDamageSender sender, CombatClock clock)
    {
        using var marker = ContextMarker.Auto();
        Item item = sender.attacker; var slow = sender as IHitSlowdownSource;
        Item credit = item;
        for (int depth = 0; credit?.Owner != null && credit.Owner != credit && depth < 8; depth++)
            credit = credit.Owner;
        var context = new CombatDamageContext { Attack = new CombatAttackKey { Source = Identity(item) }, Credit = Identity(credit),
            SourceItemId = NarrativeItemId(item?.itemData?.IDName),
            CreditItemId = NarrativeItemId(credit?.itemData?.IDName),
            WeaponItemId = NarrativeItemId(item != credit ? item?.itemData?.IDName : null),
            DeathCauseId = sender is IDeathCauseSource reason ? NarrativeCauseId(reason.DeathCauseId) : default,
            Faction = FactionRelationService.GetFactionId(item), Damage = Values(sender.DamageValues),
            ImpactKind = (byte)(sender.DamageValues?.DominantKind ?? CombatDamageKind.None), Clock = clock,
            Origin = sender is Mod_Damage weapon ? (float2)weapon.DamageOrigin : item != null ? (float2)(Vector2)item.transform.position : float2.zero,
            SourceIsPlayer = (byte)(ActorIdentity.IsPlayer(item) ? 1 : 0),
            SlowEnabled = (byte)(slow != null && slow.HitSlowdownEnabled ? 1 : 0),
            SlowMultiplier = slow?.HitSlowMultiplier ?? 1f, SlowDuration = slow?.HitSlowDuration ?? 0f,
            BuildingMultiplier = sender is IBuildingDamageSource building ? building.BuildingDamageMultiplier : 1f };
        if (sender is ICombatDamageContextModifier modifier) modifier.ModifyDamageContext(ref context);
        if (sender is IResourceHarvestTool resourceTool)
        {
            context.ResourceToolKind = (int)resourceTool.HarvestKind;
            context.ResourceToolTier = resourceTool.HarvestTier;
            context.ResourceToolEfficiency = resourceTool.HarvestEfficiency;
        }
        // 角色临时固定攻击修饰应用于完整发送端数值，手持武器与投射物共用拥有者来源。
        context.Damage = BuffAttributeEffects.ModifyAttackDamage(credit, context.Damage);
        return context;
    }

    /// <summary>叙事字段超容量时留空，不能阻断正式伤害或把截断 ID 当成另一物品。</summary>
    public static Unity.Collections.FixedString128Bytes NarrativeItemId(string value)
    {
        var result = new Unity.Collections.FixedString128Bytes();
        return result.CopyFrom(value ?? string.Empty) == Unity.Collections.CopyError.None ? result : default;
    }

    public static Unity.Collections.FixedString64Bytes NarrativeCauseId(string value)
    {
        var result = new Unity.Collections.FixedString64Bytes();
        return result.CopyFrom(value ?? string.Empty) == Unity.Collections.CopyError.None ? result : default;
    }

    /// <summary>一次实际攻击 Pulse 分发给少量注册后端；不因 Collider 每帧启用而反复扫描。</summary>
    public static void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
    {
        using var marker = QueryMarker.Auto();
        for (int i = 0; i < Bridges.Count; i++) Bridges[i].QueryWeaponPulse(weapon, shape, context);
    }

    #region 中立环境输入
    /// <summary>一次环境事件只分发已注册后端，不创建实体代理或修改表现层生命。</summary>
    public static void ApplyEnvironmentalDamage(string worldKey, in CombatDamageContext context, float radius)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority ||
            !string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, worldKey, System.StringComparison.Ordinal) ||
            context.Attack.Source.Backend != CombatBackend.Environment || !context.Attack.Source.IsValid ||
            !float.IsFinite(radius) || radius <= 0f) return;
        for (int i = 0; i < Bridges.Count; i++)
            if (Bridges[i] is IGameplayEnvironmentalDamageBridge environment)
                environment.ApplyEnvironmentalDamage(worldKey, context, radius);
    }
    #endregion
}
