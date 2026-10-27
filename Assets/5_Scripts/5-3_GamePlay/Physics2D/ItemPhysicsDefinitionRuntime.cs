using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>统一应用现有碰撞配置，并为普通物品装配声明式多区域。</summary>
public static class ItemPhysicsDefinitionRuntime
{
    #region 共享碰撞形状配置
    private static readonly ConditionalWeakTable<Item, List<ItemPhysicsRegion2D>> RegionsByItem = new();

    public static bool HasConfiguredPhysicsRegions(Item item)
        => item != null && RegionsByItem.TryGetValue(item, out var regions) && regions.Count > 0;

    public static void ApplyColliderParameters(Collider2D collider, ItemColliderDefinitionDto definition)
    {
        if (definition == null) return;
        if (definition.Enabled.HasValue) collider.enabled = definition.Enabled.Value;
        if (definition.IsTrigger.HasValue) collider.isTrigger = definition.IsTrigger.Value;
        if (definition.Offset.HasValue) collider.offset = definition.Offset.Value;
        switch (collider)
        {
            case BoxCollider2D box:
                if (definition.Size.HasValue) box.size = definition.Size.Value;
                if (definition.EdgeRadius.HasValue) box.edgeRadius = definition.EdgeRadius.Value;
                break;
            case CircleCollider2D circle:
                if (definition.Radius.HasValue) circle.radius = definition.Radius.Value;
                break;
            case CapsuleCollider2D capsule:
                if (definition.Size.HasValue) capsule.size = definition.Size.Value;
                if (definition.Direction.HasValue) capsule.direction = (CapsuleDirection2D)definition.Direction.Value;
                break;
            case PolygonCollider2D polygon when definition.Points != null:
                polygon.pathCount = 1;
                polygon.SetPath(0, definition.Points);
                break;
        }
        // 模块加载会重新写旧参数，显式区域仍独占已替代的碰撞职责。
        Item owner = collider.GetComponentInParent<Item>(true);
        if (owner == null || collider.GetComponent<ItemPhysicsRegion2D>() != null ||
            !RegionsByItem.TryGetValue(owner, out var regions)) return;
        foreach (ItemPhysicsRegion2D region in regions)
            if (region != null && region.gameObject.activeSelf && region.ReplacesCollider(collider))
            { collider.enabled = false; break; }
    }

    internal static Type ResolveColliderType(string type) => type switch
    {
        nameof(BoxCollider2D) => typeof(BoxCollider2D),
        nameof(CircleCollider2D) => typeof(CircleCollider2D),
        nameof(CapsuleCollider2D) => typeof(CapsuleCollider2D),
        nameof(PolygonCollider2D) => typeof(PolygonCollider2D),
        _ => throw new InvalidDataException($"不支持的物品碰撞形状：{type}")
    };
    #endregion

    #region 定义校验与区域装配
    public static void ValidatePhysicsDefinition(RuntimeItemDefinition definition)
    {
        if (definition.Physics == null) return;
        ItemPhysicsDefinitionDto physics = definition.Physics;
        if (!Enum.IsDefined(typeof(WorldPhysicsProxyMode2D), physics.Mode) ||
            !Enum.IsDefined(typeof(WorldPhysicsMassSource2D), physics.MassSource) ||
            !float.IsFinite(physics.LinearDamping) || physics.LinearDamping < 0f ||
            !float.IsFinite(physics.AngularDamping) || physics.AngularDamping < 0f)
            throw new InvalidDataException($"物品 {definition.Id} 的物理代理模式、质量来源或阻尼配置无效。");
        if (!physics.HasRegions) return;
        if (MachineLogicRegistry.UsesRegisteredDataBackend(definition))
            throw new InvalidDataException($"物品 {definition.Id} 的数据机器后端尚不支持 physics.regions，请沿用现有碰撞定义。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ItemPhysicsRegionDefinitionDto region in definition.Physics.Regions)
        {
            if (region == null || string.IsNullOrWhiteSpace(region.Id) || region.Id.Contains('/') || !ids.Add(region.Id))
                throw new InvalidDataException($"物品 {definition.Id} 的物理区域 id 为空或重复。");
            if (region.Collider == null || region.Purposes == 0 ||
                (region.Purposes & ~(WorldPhysicsPurpose2D.Blocking | WorldPhysicsPurpose2D.Damage |
                  WorldPhysicsPurpose2D.Interaction | WorldPhysicsPurpose2D.Trigger)) != 0)
                throw new InvalidDataException($"物品 {definition.Id} 的物理区域 {region.Id} 缺少形状或用途。");
            if (region.Immutable && (physics.Mode != WorldPhysicsProxyMode2D.WorldFixed ||
                region.Purposes != WorldPhysicsPurpose2D.Blocking || !string.IsNullOrEmpty(region.Module) ||
                !string.IsNullOrEmpty(region.Path) || region.Collider.Type != nameof(BoxCollider2D)))
                throw new InvalidDataException($"物品 {definition.Id} 的不可变区域 {region.Id} 必须是根节点上的固定纯阻挡 Box。");
            ResolveColliderType(region.Collider.Type);
            if (!string.IsNullOrEmpty(region.Module))
            {
                if (!definition.TryGetModuleAssembly(region.Module, out RuntimeItemModuleAssemblyPlan assembly))
                    throw new InvalidDataException($"物品 {definition.Id} 的区域 {region.Id} 引用了不存在的模块槽：{region.Module}");
                if ((region.Purposes & WorldPhysicsPurpose2D.Damage) != 0 &&
                    (assembly.ModuleType == null || !typeof(Mod_DamageReceiver).IsAssignableFrom(assembly.ModuleType)) ||
                    (region.Purposes & WorldPhysicsPurpose2D.Interaction) != 0 &&
                    (assembly.ModuleType == null || !typeof(IInteractable).IsAssignableFrom(assembly.ModuleType)))
                    throw new InvalidDataException($"物品 {definition.Id} 的区域 {region.Id} 接收模块没有声明的受击/交互能力。");
            }
            if ((region.Purposes & ~WorldPhysicsPurpose2D.Blocking) != 0 && string.IsNullOrEmpty(region.Module) && string.IsNullOrEmpty(region.Path))
                throw new InvalidDataException($"物品 {definition.Id} 的区域 {region.Id} 必须明确 module 或接收组件 path。");
            if ((region.Purposes & WorldPhysicsPurpose2D.Blocking) != 0 && region.Collider.IsTrigger == true)
                throw new InvalidDataException($"物品 {definition.Id} 的阻挡区域 {region.Id} 不能使用 Trigger。");
            if ((region.Purposes & WorldPhysicsPurpose2D.Blocking) == 0 && region.Collider.IsTrigger == false)
                throw new InvalidDataException($"物品 {definition.Id} 的非阻挡区域 {region.Id} 必须使用 Trigger。");
            if ((region.Purposes & WorldPhysicsPurpose2D.Blocking) != 0 &&
                (region.Purposes & (WorldPhysicsPurpose2D.Damage | WorldPhysicsPurpose2D.Trigger)) != 0)
                throw new InvalidDataException($"物品 {definition.Id} 的区域 {region.Id} 必须将阻挡与受击/触发拆成独立区域。");
        }
    }

    public static void ConfigureItemPhysicsRegions(Item item, ItemPhysicsDefinitionDto physics)
    {
        if (physics?.HasRegions != true) return;
        List<ItemPhysicsRegion2D> configured = RegionsByItem.GetOrCreateValue(item);
        configured.Clear();
        using var regionScope = ListPool<ItemPhysicsRegion2D>.Get(out var existing);
        item.GetComponentsInChildren(true, existing);
        using var moduleScope = ListPool<Module>.Get(out var modules);
        item.GetComponentsInChildren(true, modules);
        using var colliderScope = ListPool<Collider2D>.Get(out var colliders);
        item.GetComponentsInChildren(true, colliders);
        foreach (ItemPhysicsRegion2D region in existing)
            if (region.GetComponentInParent<Item>(true) == item) region.gameObject.SetActive(false);
        foreach (ItemPhysicsRegionDefinitionDto region in physics.Regions)
        {
            Module receiver = null;
            if (!string.IsNullOrEmpty(region.Module))
            {
                foreach (Module module in modules)
                    if (module.StableName == region.Module && module.GetComponentInParent<Item>(true) == item)
                    { receiver = module; break; }
                if (receiver == null) throw new MissingComponentException($"物品 {item.name} 缺少区域接收模块：{region.Module}");
            }
            Transform anchor = !string.IsNullOrEmpty(region.Path) ? item.transform.Find(region.Path) : receiver != null ? receiver.transform : item.transform;
            if (anchor == null || anchor.GetComponentInParent<Item>(true) != item)
                throw new MissingComponentException($"物品 {item.name} 找不到区域锚点：{region.Path}");
            Component target = receiver;
            if (target == null)
            {
                if ((region.Purposes & WorldPhysicsPurpose2D.Damage) != 0) target = anchor.GetComponent<Mod_DamageReceiver>();
                else if ((region.Purposes & WorldPhysicsPurpose2D.Interaction) != 0) target = anchor.GetComponent<IInteractable>() as Component;
                else if ((region.Purposes & WorldPhysicsPurpose2D.Trigger) != 0) target = anchor.GetComponent<Module>();
            }
            if ((region.Purposes & WorldPhysicsPurpose2D.Damage) != 0 && target is not Mod_DamageReceiver)
                throw new MissingComponentException($"物品 {item.name} 的受击区域 {region.Id} 没有明确生命接收模块。");
            if ((region.Purposes & WorldPhysicsPurpose2D.Interaction) != 0 && target is not IInteractable ||
                (region.Purposes & WorldPhysicsPurpose2D.Trigger) != 0 && target == null)
                throw new MissingComponentException($"物品 {item.name} 的区域 {region.Id} 没有明确交互/触发接收组件。");
            target ??= item;
            ItemPhysicsRegion2D marker = existing.Find(candidate => candidate.GetComponentInParent<Item>(true) == item && candidate.RegionId == region.Id);
            if (marker == null)
            {
                var root = new GameObject("PhysicsRegion_" + region.Id);
                root.transform.SetParent(anchor, false);
                marker = root.AddComponent<ItemPhysicsRegion2D>();
            }
            else marker.transform.SetParent(anchor, false);
            marker.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            marker.transform.localScale = Vector3.one;
            marker.gameObject.layer = (region.Purposes & WorldPhysicsPurpose2D.Damage) != 0
                ? CombatPhysicsChannels.DamageReceiverLayer : (region.Purposes & WorldPhysicsPurpose2D.Blocking) != 0
                    ? item.gameObject.layer : anchor.gameObject.layer;
            marker.BindRegion(region.Id, region.Purposes, target, region.Immutable);
            configured.Add(marker);
            Type type = ResolveColliderType(region.Collider.Type);
            Collider2D shape = marker.GetComponent<Collider2D>();
            if (shape != null && shape.GetType() != type)
                throw new InvalidOperationException($"物品 {item.name} 的区域 {region.Id} 改变了碰撞组件类型，请重新创建外壳。");
            if (shape == null) shape = (Collider2D)marker.gameObject.AddComponent(type);
            shape.enabled = region.Collider.Enabled != false;
            shape.isTrigger = (region.Purposes & WorldPhysicsPurpose2D.Blocking) == 0;
            ApplyColliderParameters(shape, region.Collider);
            foreach (Collider2D previous in colliders)
                if (previous.GetComponentInParent<Item>(true) == item && marker.ReplacesCollider(previous)) previous.enabled = false;
            marker.gameObject.SetActive(true);
        }
    }
    #endregion

}
