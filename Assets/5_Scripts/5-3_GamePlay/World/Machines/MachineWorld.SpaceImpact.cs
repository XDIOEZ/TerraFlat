using System;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 按真实世界作用域处理结构冲击
    private static PlanetData ResolveScopedPlanet(GameSaveData save, string key)
    {
        if (key == null || key.StartsWith("ship:", StringComparison.Ordinal) || save?.PlanetData_Dict == null) return null;
        if (save.PlanetData_Dict.TryGetValue(key, out PlanetData planet)) return planet;
        return save.PlanetData_Dict.TryGetValue(WorldAddress.FromWorldKey(key).PlanetId, out planet) ? planet : null;
    }

    private static WorldTopologyDomain ResolveScopedTopology(PlanetData planet)
        => WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds) ? bounds.ToDomain() : default;

    // 直接结构损伤与压力武器分开，致命清理由拥有落点世界的调用方完成。
    public static bool ApplySurfaceStructuralDamage(MachineEntity node, float damage, Action<MachineEntity> fatalResolver,
        bool pressureExplosion = false)
    {
        if (!GameNetwork.HasStateAuthority || node == null || !float.IsFinite(damage) || damage <= 0f) return false;
        using var lease = UseNodeScope(node);
        if (!Contains(node)) return false;
        WakeForInteraction(node);
        if (node.State == null || node.State.Hp <= 0f) return false;
        if (pressureExplosion)
        {
            if (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(node.Definition.Id, out RuntimeItemDefinition definition))
                throw new InvalidOperationException("压力伤害缺少机器的真实定义：" + node.Definition.Id);
            damage = CombatRules.ResolvePhysical(damage, definition.Health?.Defense?.Physical ?? 0f);
            if (damage <= 0f) return false;
        }
        node.State.Hp = Mathf.Max(0f, node.State.Hp - damage);
        if (node.View?.item != null)
        {
            Mod_DamageReceiver health = node.View.item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (health != null) health.Hp = node.State.Hp;
        }
        if (node.State.Hp > 0f)
        {
            Mod_Building.SetMechanicalDamageState(node.Snapshot, node.State.Hp < ResolveMaximumHp(node.Snapshot) * .5f);
            StateChanged(node);
        }
        else
        {
            if (fatalResolver == null) throw new ArgumentNullException(nameof(fatalResolver));
            fatalResolver(node);
        }
        return true;
    }

    public static void CaptureSurfaceImpactWorld() => CaptureCurrentWorld();
    #endregion
}
