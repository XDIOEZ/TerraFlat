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
        Vector2 padding = MachineWorld.GetCombatSearchPadding();
        int minX = Mathf.FloorToInt(center.x - extents.x - padding.x);
        int maxX = Mathf.FloorToInt(center.x + extents.x + padding.x);
        int minY = Mathf.FloorToInt(center.y - extents.y - padding.y);
        int maxY = Mathf.FloorToInt(center.y + extents.y + padding.y);
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
                Vector2 relative = WorldTopologyRuntime.ShortestDelta(
                    (Vector2)center, node.Snapshot.transform.position);
                PerceptionShape2D hitShape = ResolveHitShape(definition.Health, (Vector2)center + relative,
                    node.RotationQuarterTurns, node.Definition.IsConverter);
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
        float difficulty = GameplayCombatBridge.Difficulty().Resolve(context.SourceIsPlayer != 0, false);
        float damage = CalculateDamage(definition.Health, context, difficulty);
        if (!math.isfinite(damage)) throw new InvalidOperationException("机器伤害数值无效：" + node.Definition.Id);
        if (damage <= 0f)
        {
            weapon?.PublishExternalDamage(context, 0f);
            return;
        }

        float previousHp = node.State.Hp;
        node.State.Hp = Mathf.Max(0f, previousHp - damage);
        float actualLoss = Mathf.Max(0f, previousHp - node.State.Hp);
        if (node.State.Hp > 0f)
        {
            Mod_Building.SetMechanicalDamageState(node.Snapshot,
                node.State.Hp < MachineWorld.ResolveMaximumHp(node.Snapshot) * .5f);
            MachineWorld.StateChanged(node);
        }
        else if (weapon != null && weapon.TileDamageToolKind == TileDamageToolKind.Hammer)
        {
            if (!Mod_Building.TryDismantleMechanical(node, out string reason))
            {
                node.State.Hp = 1f;
                Mod_Building.SetMechanicalDamageState(node.Snapshot, true);
                MachineWorld.StateChanged(node);
                throw new InvalidOperationException("机械锤击拆除失败：" + reason);
            }
        }
        else Mod_Building.DestroyMechanical(node);

        // 先提交耐久和摧毁事务，再回传真实损失，避免表现异常留下零血挡路节点。
        weapon?.PublishExternalDamage(context, actualLoss);
    }
    #endregion

    #region 共享受击配置与几何
    /// <summary>所有机器都必须有有效耐久与受击范围，不能靠关闭 Collider 退出战斗系统。</summary>
    public static void ValidateHealth(ItemHealthDefinitionDto health)
    {
        if (health?.HasHp != true || !MachineDefinition.Positive(health.MaxHp))
            throw new InvalidOperationException("必须配置有限且大于零的最大生命。");
        ItemColliderDefinitionDto collider = health.Collider;
        if (collider == null || collider.Enabled == false || collider.IsTrigger == false)
            throw new InvalidOperationException("必须配置启用的独立受击 Trigger。");
        Vector3 modulePosition = health.ModuleLocalPosition ?? Vector3.zero;
        Vector2 offset = collider.Offset ?? Vector2.zero;
        if (!math.all(math.isfinite(new float3(modulePosition.x, modulePosition.y, modulePosition.z))) ||
            !math.all(math.isfinite(new float2(offset.x, offset.y))))
            throw new InvalidOperationException("受击范围偏移必须是有限坐标。");
        if (string.Equals(collider.Type, nameof(CircleCollider2D), StringComparison.Ordinal))
        {
            if (!MachineDefinition.Positive(collider.Radius ?? .5f))
                throw new InvalidOperationException("圆形受击范围半径必须大于零。");
        }
        else if (string.Equals(collider.Type, nameof(BoxCollider2D), StringComparison.Ordinal))
        {
            Vector2 size = collider.Size ?? Vector2.one;
            if (!MachineDefinition.Positive(size.x) || !MachineDefinition.Positive(size.y))
                throw new InvalidOperationException("矩形受击范围长宽必须大于零。");
        }
        else throw new InvalidOperationException("机器受击范围只支持 BoxCollider2D 或 CircleCollider2D。");
        ItemDefenseDefinitionDto defense = health.Defense;
        if (defense != null && !math.all(math.isfinite(new float4(
                defense.Cutting, defense.Piercing, defense.Chopping, defense.Blunt))))
            throw new InvalidOperationException("机器防御必须是有限数值。");
    }

    /// <summary>候选范围与物理投影共用同一形状，九十度旋转与转换器镜像都沿用主体姿态。</summary>
    public static PerceptionShape2D ResolveHitShape(ItemHealthDefinitionDto health, Vector2 anchor,
        int rotationQuarterTurns, bool isConverter = false)
    {
        ItemColliderDefinitionDto collider = health.Collider;
        Vector2 localCenter = (Vector2)(health.ModuleLocalPosition ?? Vector3.zero) +
            (collider.Offset ?? Vector2.zero);
        int rotation = rotationQuarterTurns & 3;
        if (isConverter && rotation == 2)
        {
            localCenter.x = -localCenter.x;
            rotation = 0;
        }
        Vector2 rotatedCenter = rotation switch
        {
            1 => new Vector2(-localCenter.y, localCenter.x),
            2 => -localCenter,
            3 => new Vector2(localCenter.y, -localCenter.x),
            _ => localCenter
        };
        float2 center = new(anchor.x + rotatedCenter.x, anchor.y + rotatedCenter.y);
        if (string.Equals(collider.Type, nameof(CircleCollider2D), StringComparison.Ordinal))
            return PerceptionShape2D.Circle(center, collider.Radius ?? .5f);
        Vector2 size = collider.Size ?? Vector2.one;
        if ((rotation & 1) != 0) size = new Vector2(size.y, size.x);
        return PerceptionShape2D.Aabb(center, new float2(size.x, size.y) * .5f);
    }

    /// <summary>防御后才应用建筑倍率，机器与普通实体共用四类伤害规则。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float CalculateDamage(ItemHealthDefinitionDto health, CombatDamageContext context, float difficulty)
    {
        ItemDefenseDefinitionDto defense = health.Defense;
        float4 defenses = defense == null ? float4.zero :
            new float4(defense.Cutting, defense.Piercing, defense.Chopping, defense.Blunt);
        return math.csum(CombatRules.Resolve(context.Damage,
            context.IsTrueDamage != 0 ? float4.zero : defenses, difficulty, context.BuildingMultiplier));
    }
    #endregion
}
