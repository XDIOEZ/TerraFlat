using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using Unity.Mathematics;
using UnityEngine;

/// <summary>机械数据格的战斗适配器；只在武器 Pulse 内投影候选受击 Collider。</summary>
public sealed class MachineCombatBridge : IGameplayCombatBridge
{
    #region 数据受击
    public static readonly MachineCombatBridge Instance = new();
    private MachineCombatBridge() { }

    /// <summary>机械后端不接管已有世界 Item 的身份。</summary>
    public bool TryGetIdentity(Item item, out CombatIdentity identity)
    {
        identity = default;
        return false;
    }

    /// <summary>数据格负责候选索引，Collider2D 负责实际接触，机械数据负责耐久结算。</summary>
    public void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
    {
        if (!GameNetwork.HasStateAuthority || weapon == null || weapon.RemainingAttackTargets == 0) return;
        float2 center = shape.BoundsCenter, extents = shape.BoundsExtents;
        int minX = Mathf.FloorToInt(center.x - extents.x - 1f);
        int maxX = Mathf.FloorToInt(center.x + extents.x + 1f);
        int minY = Mathf.FloorToInt(center.y - extents.y - 1f);
        int maxY = Mathf.FloorToInt(center.y + extents.y + 1f);
        var nodes = new List<(MachineEntity Node, RuntimeItemDefinition Definition)>();
        var seen = new HashSet<int>();
        var colliderHits = new List<CombatColliderHit2D>();
        using (CombatColliderQuery2D physics = CombatColliderQuery2D.Rent())
        {
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            for (int layer = 0; layer <= 3; layer++)
            {
                MachineEntity node = MachineWorld.GetAtCurrentWorld(new Vector2Int(x, y), layer);
                if (node == null || !seen.Add(node.Id)) continue;
                if (GameRes.ExistingInstance == null ||
                    !GameRes.ExistingInstance.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition) ||
                    definition.Health?.HasHp != true)
                    throw new InvalidOperationException("机械耐久定义无效：" + node.Definition.Id);
                ItemColliderDefinitionDto collider = definition.Health.Collider;
                if (collider?.Enabled == false) continue;
                Vector2 relative = WorldTopologyRuntime.ShortestDelta(
                    (Vector2)center, node.Snapshot.transform.position);
                Vector2 localCenter = (Vector2)(definition.Health.ModuleLocalPosition ?? Vector3.zero) +
                    (collider?.Offset ?? Vector2.zero);
                Vector2 rotatedCenter = (node.RotationQuarterTurns & 3) switch
                {
                    1 => new Vector2(-localCenter.y, localCenter.x),
                    2 => -localCenter,
                    3 => new Vector2(localCenter.y, -localCenter.x),
                    _ => localCenter
                };
                float2 hitCenter = center + new float2(relative.x + rotatedCenter.x, relative.y + rotatedCenter.y);
                PerceptionShape2D hitShape;
                if (string.Equals(collider?.Type, nameof(CircleCollider2D), StringComparison.Ordinal))
                    hitShape = PerceptionShape2D.Circle(hitCenter, Mathf.Abs(collider.Radius ?? 0.5f));
                else
                {
                    Vector2 size = collider?.Size ?? Vector2.one;
                    if ((node.RotationQuarterTurns & 1) != 0) size = new Vector2(size.y, size.x);
                    hitShape = PerceptionShape2D.Aabb(hitCenter,
                        new float2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * .5f);
                }
                physics.Add(nodes.Count, hitShape);
                nodes.Add((node, definition));
            }
            physics.Query(shape, colliderHits);
        }
        colliderHits.Sort((a, b) => a.Fraction != b.Fraction ? a.Fraction.CompareTo(b.Fraction) :
            Vector2.SqrMagnitude(a.Point - (Vector2)center).CompareTo(Vector2.SqrMagnitude(b.Point - (Vector2)center)));
        for (int i = 0; i < colliderHits.Count && weapon.RemainingAttackTargets > 0; i++)
        {
            CombatColliderHit2D contact = colliderHits[i];
            (MachineEntity node, RuntimeItemDefinition definition) = nodes[contact.CandidateId];
            if (!MachineWorld.Contains(node)) continue;
            var identity = new CombatIdentity
            {
                Backend = CombatBackend.External,
                Value = unchecked((uint)node.Id),
                World = context.Attack.Source.World,
                Dimension = context.Attack.Source.Dimension
            };
            if (!weapon.TryReserveExternalTarget(identity)) continue;
            CombatDamageContext hit = context;
            hit.HitPoint = contact.Point;
            ApplyDamage(node, definition, weapon, hit);
        }
    }

    /// <summary>防御、难度和建筑倍率与普通伤害接收器共用数值规则。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ApplyDamage(MachineEntity node, RuntimeItemDefinition definition,
        Mod_Damage weapon, CombatDamageContext context)
    {
        if (!GameNetwork.HasStateAuthority || node == null || !MachineWorld.Contains(node)) return;
        MachineWorld.WakeForInteraction(node);
        if (node.State == null) return;
        ItemDefenseDefinitionDto defense = definition.Health.Defense;
        float4 defenses = defense == null ? float4.zero :
            new float4(defense.Cutting, defense.Piercing, defense.Chopping, defense.Blunt);
        float difficulty = GameplayCombatBridge.Difficulty().Resolve(context.SourceIsPlayer != 0, false);
        float4 values = CombatRules.Resolve(context.Damage,
            context.IsTrueDamage != 0 ? float4.zero : defenses,
            difficulty, context.BuildingMultiplier);
        float damage = math.csum(values);
        if (damage <= 0f) return;

        node.State.Hp = Mathf.Max(0f, node.State.Hp - damage);
        if (node.State.Hp > 0f)
        {
            Mod_Building.SetMechanicalDamageState(node.Snapshot,
                node.State.Hp < MachineWorld.ResolveMaximumHp(node.Snapshot) * .5f);
            MachineWorld.StateChanged(node);
            return;
        }
        if (weapon.TileDamageToolKind == TileDamageToolKind.Hammer)
        {
            if (!Mod_Building.TryDismantleMechanical(node, out string reason))
            {
                node.State.Hp = 1f;
                throw new InvalidOperationException("机械锤击拆除失败：" + reason);
            }
        }
        else Mod_Building.DestroyMechanical(node);
    }
    #endregion
}
