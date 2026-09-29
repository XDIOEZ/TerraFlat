using System;
using System.Runtime.CompilerServices;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using Unity.Mathematics;
using UnityEngine;

/// <summary>机械数据格的战斗适配器；共享真实武器 Pulse 和攻击配额，不生成每座建筑的 Collider。</summary>
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

    /// <summary>仅查询攻击盒覆盖的数据格，命中后使用共同伤害规则扣除节点耐久。</summary>
    public void QueryWeaponPulse(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
    {
        if (!GameNetwork.HasStateAuthority || weapon == null || weapon.RemainingAttackTargets == 0) return;
        float2 center = shape.BoundsCenter, extents = shape.BoundsExtents;
        int minX = Mathf.FloorToInt(center.x - extents.x - 1f);
        int maxX = Mathf.FloorToInt(center.x + extents.x + 1f);
        int minY = Mathf.FloorToInt(center.y - extents.y - 1f);
        int maxY = Mathf.FloorToInt(center.y + extents.y + 1f);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        for (int layer = 0; layer < 2; layer++)
        {
            if (weapon.RemainingAttackTargets == 0) return;
            MachineEntity node = MachineWorld.GetAtCurrentWorld(new Vector2Int(x, y), layer);
            if (node == null) continue;
            if (GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition) ||
                definition.Health?.HasHp != true)
                throw new InvalidOperationException("机械耐久定义无效：" + node.Definition.Id);

            Vector2 relative = WorldTopologyRuntime.ShortestDelta(
                (Vector2)center, node.Snapshot.transform.position);
            float2 hitCenter = center + new float2(relative.x, relative.y);
            Vector2 size = definition.Health.Collider?.Size ?? Vector2.one;
            PerceptionShape2D hit = PerceptionShape2D.Aabb(hitCenter,
                new float2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * .5f);
            if (!shape.Intersects(hit)) continue;

            var identity = new CombatIdentity
            {
                Backend = CombatBackend.External,
                Value = unchecked((uint)node.Id),
                World = context.Attack.Source.World,
                Dimension = context.Attack.Source.Dimension
            };
            if (!weapon.TryReserveExternalTarget(identity)) continue;
            ApplyDamage(node, definition, weapon, context);
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
