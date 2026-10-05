using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Networking;
using Unity.Mathematics;
using UnityEngine;

#region 接触伤害配置

/// <summary>可组合的接触伤害参数，普通物品与资源实体使用同一份配置。</summary>
[Serializable]
public sealed class ContactDamageSettings
{
    public CombatDamage DamageValues = new(0f, 2f, 0f, 0f);
    public float DamageInterval = 1f;
    public Vector2 Offset;
    public Vector2 Size = Vector2.one;
    public bool OnlyMovers = true;

    public bool TryValidate(out string reason)
    {
        float4 damage = GameplayCombatBridge.Values(DamageValues);
        bool valid = DamageValues != null && math.all(math.isfinite(damage)) &&
            math.all(damage >= 0f) && math.csum(damage) > 0f &&
            math.isfinite(DamageInterval) && DamageInterval >= ContactDamageRuntime.QueryInterval &&
            math.all(math.isfinite(new float4(Offset.x, Offset.y, Size.x, Size.y))) &&
            Size.x > 0f && Size.y > 0f;
        reason = valid ? null : "接触伤害需要有限非负伤害、正尺寸及至少 0.1 秒的伤害间隔。";
        return valid;
    }
}

#endregion

/// <summary>无动画的接触伤害能力，不改写武器或接收器的通用结算。</summary>
public sealed class Mod_ContactDamage : Module
{
    #region 模块生命周期

    public Ex_ModData_MemoryPackable ModData = new();
    public ContactDamageSettings Settings = new();
    private ContactDamageRuntime runtime;
    public override string CanonicalModuleId => "Mod_ContactDamage";
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => ContactDamageRuntime.QueryInterval;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("接触伤害模块数据类型错误。");
    }

    public override void Load()
    {
        OnResourcesReloaded();
        runtime = new ContactDamageRuntime(IsWorldSourceActive);
    }

    public override void OnResourcesReloaded()
    {
        if (Settings == null) throw new InvalidOperationException("缺少接触伤害配置。");
        if (!Settings.TryValidate(out string reason)) throw new InvalidOperationException(reason);
    }

    public override void Save() { } // 接触与冷却只属于当前实例，不保存离线伤害。
    public override void Unload() => runtime = null;

    public override void ModUpdate(float deltaTime)
    {
        if (deltaTime <= 0f || !IsWorldSourceActive()) return;
        runtime?.Tick(Settings, item.transform.localToWorldMatrix, GameplayCombatBridge.Identity(item),
            item.gameObject.scene.name, FactionRelationService.GetFactionId(item), item);
    }

    private bool IsWorldSourceActive()
    {
        if (runtime == null || item == null || item.itemData == null || item.itemData.inHand || !gameObject.activeInHierarchy ||
            item.DestructionHandled || !GameNetwork.HasStateAuthority) return false;
        var health = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        return health == null || health.Hp > 0f;
    }

    #endregion
}

/// <summary>仅查询真实身体碰撞体，按目标身份限频并交给正式伤害接口。</summary>
internal sealed class ContactDamageRuntime
{
    #region 查询与伤害结算

    public const float QueryInterval = 0.1f;
    private readonly List<Collider2D> overlaps = new(8);
    private readonly HashSet<CombatIdentity> seen = new();
    private readonly Dictionary<CombatIdentity, double> cooldowns = new();
    private readonly List<CombatIdentity> expired = new();
    private readonly Func<bool> isSourceActive;
    private uint pulse;

    public ContactDamageRuntime(Func<bool> isSourceActive) => this.isSourceActive = isSourceActive;

    public void Tick(ContactDamageSettings settings, Matrix4x4 root, CombatIdentity source,
        string worldKey, string faction, Item sourceItem = null)
    {
        if (!GameNetwork.HasStateAuthority || !source.IsValid || string.IsNullOrEmpty(worldKey) ||
            !isSourceActive()) return;
        double now = Time.timeAsDouble;
        expired.Clear();
        foreach (var pair in cooldowns)
            if (now >= pair.Value) expired.Add(pair.Key);
        foreach (CombatIdentity identity in expired) cooldowns.Remove(identity);

        Vector2 center = root.MultiplyPoint3x4(settings.Offset);
        Vector3 scale = root.lossyScale;
        Vector2 size = Vector2.Scale(settings.Size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        var filter = new ContactFilter2D { useTriggers = false, useDepth = false, useLayerMask = false };
        overlaps.Clear();
        seen.Clear();
        Physics2D.OverlapBox(center, size, root.rotation.eulerAngles.z, filter, overlaps);
        uint currentPulse = ++pulse;
        bool hasSourceItem = !ReferenceEquals(sourceItem, null);
        foreach (Collider2D collider in overlaps)
        {
            if (!isSourceActive() || (hasSourceItem && (sourceItem == null ||
                GameplayCombatBridge.Identity(sourceItem) != source))) break;
            if (collider == null || collider.isTrigger || collider.attachedRigidbody == null ||
                !collider.attachedRigidbody.simulated) continue;
            Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(collider);
            Item target = receiver != null ? receiver.item : null;
            if (target == null || target == sourceItem || target.itemData == null || target.itemData.inHand ||
                target.DestructionHandled || receiver.Hp <= 0f || target.gameObject.scene.name != worldKey ||
                IsAirborne(target) ||
                (settings.OnlyMovers && target.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover) == null)) continue;
            CombatIdentity identity = GameplayCombatBridge.Identity(target);
            if (!identity.IsValid || !seen.Add(identity) || cooldowns.ContainsKey(identity)) continue;

            // 已确认同一世界场景，资源来源补上接收后端的世界键而不伪造 Item。
            CombatIdentity attackSource = source;
            if (source.Backend == CombatBackend.Entity)
            {
                attackSource.World = identity.World;
                attackSource.Dimension = identity.Dimension;
            }
            var context = new CombatDamageContext
            {
                Attack = new CombatAttackKey { Source = attackSource, Sequence = currentPulse, Window = 1, Pulse = 1 },
                Credit = attackSource,
                Faction = faction ?? string.Empty,
                Damage = GameplayCombatBridge.Values(settings.DamageValues),
                Origin = center,
                HitPoint = collider.ClosestPoint(center),
                Clock = new CombatClock { Tick = (ulong)Time.frameCount, Time = now, DeltaTime = Time.deltaTime },
                SourceIsPlayer = (byte)(sourceItem != null && GameDifficultyService.IsPlayer(sourceItem) ? 1 : 0),
                BuildingMultiplier = 1f
            };
            cooldowns[identity] = now + settings.DamageInterval; // 先预约，避免受击回调重入或多碰撞体重复扎伤。
            receiver.Hurt(context);
        }
    }

    private static bool IsAirborne(Item target)
    {
        foreach (Module module in target.itemMods.Mods.Values)
            if (module is ICombatAirborneTarget airborne && airborne.IsAirborne) return true;
        return false;
    }

    #endregion
}
