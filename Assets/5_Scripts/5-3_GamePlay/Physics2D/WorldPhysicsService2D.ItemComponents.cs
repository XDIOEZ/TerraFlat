using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Pool;

public sealed partial class WorldPhysicsService2D
{
    #region 物品物理组件租借
    private sealed class ItemPhysicsComponentCache
    {
        public ItemPhysicsComponentCache() { }
        internal readonly Dictionary<ItemPhysicsTemplate2D, Collider2D> Templates = new();
        internal readonly Dictionary<ItemPhysicsTemplate2D, bool> TemplateModuleEnabled = new();
        internal readonly Dictionary<ItemPhysicsRegion2D, bool> RegionModuleEnabled = new();
        internal readonly Dictionary<Component, bool> DeclaredModuleEnabled = new();
        internal readonly Dictionary<Component, bool> DisabledColliderInitialEnabled = new();
        internal readonly Dictionary<Component, Collider2D> Declared = new();
        internal readonly Dictionary<Transform, Rigidbody2D> Bodies = new();
        internal readonly Dictionary<Transform, BoxCollider2D> MovableSolids = new();
        internal readonly List<Collider2D> OwnedColliders = new();
        internal readonly List<Rigidbody2D> OwnedBodies = new();
        internal bool Leased;
    }

    private static readonly ConditionalWeakTable<Item, ItemPhysicsComponentCache> itemComponentCaches = new();

    internal static bool HasRentedItemTemplateCollider(ItemPhysicsTemplate2D template)
    {
        Item item = template != null ? template.GetComponentInParent<Item>(true) : null;
        return item != null && itemComponentCaches.TryGetValue(item, out var cache) &&
            cache.Leased && cache.Templates.TryGetValue(template, out Collider2D collider) && collider != null;
    }

    private void RentItemPhysicsComponents(Item item)
    {
        ItemPhysicsComponentCache cache = itemComponentCaches.GetOrCreateValue(item);
        bool restoreDefaults = !cache.Leased;
        cache.Leased = true;
        try
        {
            using var templateScope = ListPool<ItemPhysicsTemplate2D>.Get(out var templates);
            using var animatorScope = HashSetPool<Animator>.Get(out var animatorsToRebind);
            using var resetScope = HashSetPool<Transform>.Get(out var resetTargets);
            item.GetComponentsInChildren(true, templates);
            foreach (ItemPhysicsTemplate2D template in templates)
            {
                if (template == null || template.GetComponentInParent<Item>(true) != item) continue;
                if (!ReferenceEquals(template.owningModule, null) &&
                    (template.owningModule == null || !template.owningModule.enabled))
                {
                    if (cache.Templates.TryGetValue(template, out Collider2D retired) && retired != null)
                        retired.enabled = false;
                    continue;
                }
                if (template.hasBody)
                {
                    bool hadBody = cache.Bodies.TryGetValue(template.transform, out Rigidbody2D body) && body != null;
                    body = RentItemRigidbody(item, template.transform);
                    if (restoreDefaults || !hadBody) ApplyItemBodyTemplate(body, template);
                }
                if (string.IsNullOrEmpty(template.colliderType)) continue;
                bool moduleEnabled = template.owningModule == null || template.owningModule.Enabled;
                bool stateChanged = !cache.TemplateModuleEnabled.TryGetValue(template, out bool previousEnabled) ||
                    previousEnabled != moduleEnabled;
                cache.TemplateModuleEnabled[template] = moduleEnabled;
                Type type = ItemPhysicsDefinitionRuntime.ResolveColliderType(template.colliderType);
                bool hadShape = cache.Templates.TryGetValue(template, out Collider2D shape) && shape != null;
                if (!hadShape || shape.GetType() != type)
                {
                    if (shape != null) shape.enabled = false;
                    shape = CreateOwnedItemCollider(cache, template.gameObject, type);
                    cache.Templates[template] = shape;
                    hadShape = false;
                    if (!item.IsInitialized && template.GetComponent<Mod_Damage>() != null)
                    {
                        Animator animator = template.GetComponentInParent<Animator>(true);
                        if (animator != null) animatorsToRebind.Add(animator);
                    }
                }
                if (restoreDefaults || !hadShape || stateChanged)
                {
                    ApplyItemColliderTemplate(shape, template);
                    resetTargets.Add(template.transform);
                }
                if (!moduleEnabled) shape.enabled = false;
            }

            ItemPhysicsDefinitionRuntime.ItemPhysicsConfiguration configuration =
                ItemPhysicsDefinitionRuntime.GetItemPhysicsConfiguration(item);
            foreach (var entry in configuration.Colliders)
            {
                if (entry.Key == null || entry.Key.GetComponentInParent<Item>(true) != item) continue;
                bool stateChanged = false;
                if (entry.Key is Module module)
                {
                    bool enabled = module != null && module.enabled && module.Enabled;
                    stateChanged = !cache.DeclaredModuleEnabled.TryGetValue(entry.Key, out bool previousEnabled) ||
                        previousEnabled != enabled;
                    cache.DeclaredModuleEnabled[entry.Key] = enabled;
                    if (!enabled)
                    {
                        Type type = ItemPhysicsDefinitionRuntime.ResolveDeclaredColliderType(entry.Key, entry.Value.Type);
                        Collider2D disabled = FindDeclaredItemCollider(cache, entry.Key, type);
                        if (disabled != null)
                        {
                            if (stateChanged) cache.DisabledColliderInitialEnabled[entry.Key] = disabled.enabled;
                            disabled.enabled = false;
                        }
                        continue;
                    }
                }
                Collider2D shape = RentDeclaredItemCollider(cache, entry.Key, entry.Value, out bool created);
                if (created && !item.IsInitialized && entry.Key is Mod_Damage)
                {
                    Animator animator = entry.Key.GetComponentInParent<Animator>(true);
                    if (animator != null) animatorsToRebind.Add(animator);
                }
                if ((restoreDefaults || stateChanged) && cache.Declared.TryGetValue(entry.Key, out Collider2D declared) &&
                    declared == shape && cache.OwnedColliders.Contains(shape) && !cache.Templates.ContainsValue(shape))
                    ResetDeclaredItemCollider(shape);
                else if (stateChanged && cache.DisabledColliderInitialEnabled.Remove(entry.Key, out bool wasEnabled))
                {
                    if (!resetTargets.Contains(entry.Key.transform)) shape.enabled = wasEnabled;
                }
                if (restoreDefaults || created || stateChanged || resetTargets.Contains(entry.Key.transform))
                    ItemPhysicsDefinitionRuntime.ApplyColliderParameters(shape, entry.Value);
            }
            foreach (var entry in configuration.Regions)
            {
                ItemPhysicsRegion2D region = entry.Key;
                if (region == null || region.GetComponentInParent<Item>(true) != item) continue;
                bool moduleEnabled = region.Receiver is not Module receiver || receiver.enabled && receiver.Enabled;
                bool stateChanged = !cache.RegionModuleEnabled.TryGetValue(region, out bool previousEnabled) ||
                    previousEnabled != moduleEnabled;
                cache.RegionModuleEnabled[region] = moduleEnabled;
                Collider2D shape = RentDeclaredItemCollider(cache, region, entry.Value, out bool created);
                if (restoreDefaults || created || stateChanged)
                {
                    if (restoreDefaults && !created) ResetDeclaredItemCollider(shape);
                    shape.enabled = entry.Value.Enabled != false;
                    shape.isTrigger = (region.Purpose & WorldPhysicsPurpose2D.Blocking) == 0;
                    ItemPhysicsDefinitionRuntime.ApplyColliderParameters(shape, entry.Value);
                }
                if (!moduleEnabled) shape.enabled = false;
            }
            using var shapesScope = ListPool<Collider2D>.Get(out var shapes);
            item.GetComponentsInChildren(true, shapes);
            foreach (var entry in configuration.Regions)
            {
                ItemPhysicsRegion2D region = entry.Key;
                if (region == null || !region.gameObject.activeSelf) continue;
                foreach (Collider2D shape in shapes)
                    if (shape != null && shape.GetComponentInParent<Item>(true) == item && region.ReplacesCollider(shape))
                        shape.enabled = false;
            }
            if (restoreDefaults) NotifyItemPhysicsComponentsAvailable(item);
            foreach (Animator animator in animatorsToRebind) RebindInitialItemAnimatorAfterPhysicsLease(animator);
        }
        catch
        {
            ReturnItemPhysicsComponents(item);
            throw;
        }
    }

    private static Collider2D CreateOwnedItemCollider(ItemPhysicsComponentCache cache, GameObject target, Type type)
    {
        var collider = (Collider2D)target.AddComponent(type);
        collider.enabled = false;
        cache.OwnedColliders.Add(collider);
        return collider;
    }

    private static Collider2D RentDeclaredItemCollider(ItemPhysicsComponentCache cache, Component target,
        ItemColliderDefinitionDto definition, out bool created)
    {
        Type type = ItemPhysicsDefinitionRuntime.ResolveDeclaredColliderType(target, definition.Type);
        if (!string.IsNullOrWhiteSpace(definition.Type)) type = ItemPhysicsDefinitionRuntime.ResolveColliderType(definition.Type);
        Collider2D collider = FindDeclaredItemCollider(cache, target, type);
        if (collider == null)
        {
            collider = CreateOwnedItemCollider(cache, target.gameObject, type);
            cache.Declared[target] = collider;
            ResetDeclaredItemCollider(collider);
            created = true;
            return collider;
        }
        cache.Declared[target] = collider;
        created = false;
        return collider;
    }

    private static Collider2D FindDeclaredItemCollider(ItemPhysicsComponentCache cache, Component target, Type type)
    {
        if (cache.Declared.TryGetValue(target, out Collider2D owned) && owned != null && owned.GetType() == type)
            return owned;
        Module desiredModule = target as Module;
        using var templateScope = ListPool<ItemPhysicsTemplate2D>.Get(out var templates);
        target.GetComponents(templates);
        foreach (ItemPhysicsTemplate2D template in templates)
            if ((desiredModule != null ? template.owningModule == desiredModule : ReferenceEquals(template.owningModule, null)) &&
                cache.Templates.TryGetValue(template, out Collider2D shape) &&
                shape != null && shape.GetType() == type) return shape;
        using var colliderScope = ListPool<Collider2D>.Get(out var colliders);
        target.GetComponents(colliders);
        foreach (Collider2D collider in colliders)
            if (collider.GetType() == type && !cache.Templates.ContainsValue(collider) && !cache.Declared.ContainsValue(collider))
                return collider;
        return null;
    }

    private static void ResetDeclaredItemCollider(Collider2D collider)
    {
        collider.enabled = false;
        ResetColliderPhysicsSettings(collider);
        ResetColliderShapeDefaults(collider);
        collider.offset = Vector2.zero;
        collider.isTrigger = false;
        collider.enabled = true;
    }

    internal static void ApplyRegisteredItemColliderDefinition(Item item, Component target, ItemColliderDefinitionDto definition)
    {
        if (!itemOwners.TryGetValue(item, out var owner) || owner.disposed ||
            target is Module module && (!module.enabled || !module.Enabled)) return;
        ItemPhysicsComponentCache cache = itemComponentCaches.GetOrCreateValue(item);
        Collider2D collider = RentDeclaredItemCollider(cache, target, definition, out bool created);
        ItemPhysicsDefinitionRuntime.ApplyColliderParameters(collider, definition);
        owner.RegisterItemColliderBinding(item, collider);
        if (owner.itemColliders.TryGetValue(item, out var registered) && !registered.Contains(collider))
            registered.Add(collider);
        if (created && !item.IsInitialized && target is Mod_Damage)
        {
            Animator animator = target.GetComponentInParent<Animator>(true);
            if (animator != null) RebindInitialItemAnimatorAfterPhysicsLease(animator);
        }
    }

    internal static void RefreshRegisteredItemModulePhysics(Module module)
    {
        Item item = module != null ? module.GetComponentInParent<Item>(true) : null;
        if (item == null || !itemOwners.TryGetValue(item, out var owner) || owner.disposed) return;
        using var scope = ListPool<ItemPhysicsTemplate2D>.Get(out var templates);
        module.GetComponents(templates);
        foreach (ItemPhysicsTemplate2D template in templates)
            if (template.owningModule == module) { owner.RegisterItem(item); return; }
        ItemPhysicsDefinitionRuntime.ItemPhysicsConfiguration configuration = ItemPhysicsDefinitionRuntime.GetItemPhysicsConfiguration(item);
        if (configuration.Colliders.ContainsKey(module)) { owner.RegisterItem(item); return; }
        foreach (var entry in configuration.Regions)
            if (entry.Key != null && entry.Key.Receiver == module) { owner.RegisterItem(item); return; }
    }

    private Rigidbody2D RentItemRigidbody(Item item, Transform target)
    {
        ItemPhysicsComponentCache cache = itemComponentCaches.GetOrCreateValue(item);
        if (cache.Bodies.TryGetValue(target, out Rigidbody2D body) && body != null) return body;
        body = target.GetComponent<Rigidbody2D>();
        if (body == null)
        {
            body = target.gameObject.AddComponent<Rigidbody2D>();
            cache.OwnedBodies.Add(body);
        }
        cache.Bodies[target] = body;
        return body;
    }

    internal static Rigidbody2D RentRegisteredItemRigidbody(Item item)
    {
        if (item == null || !itemOwners.TryGetValue(item, out var owner))
            throw new InvalidOperationException("物品必须登记所属世界后才能领取刚体。");
        Rigidbody2D body = owner.RentItemRigidbody(item, item.transform);
        body.simulated = true;
        return body;
    }

    internal static BoxCollider2D RentRegisteredItemSolidBox(Item item, out bool created)
    {
        if (item == null || !itemOwners.TryGetValue(item, out var owner))
            throw new InvalidOperationException("物品必须登记所属世界后才能领取实体碰撞体。");
        using var scope = ListPool<BoxCollider2D>.Get(out var candidates);
        item.GetComponents(candidates);
        foreach (BoxCollider2D candidate in candidates)
            if (!candidate.isTrigger) { created = false; return candidate; }
        ItemPhysicsComponentCache cache = itemComponentCaches.GetOrCreateValue(item);
        BoxCollider2D collider = (BoxCollider2D)CreateOwnedItemCollider(cache, item.gameObject, typeof(BoxCollider2D));
        collider.isTrigger = false;
        owner.RegisterItemColliderBinding(item, collider);
        if (owner.itemColliders.TryGetValue(item, out var registered)) registered.Add(collider);
        created = true;
        return collider;
    }

    private static void NotifyItemPhysicsComponentsAvailable(Item item)
    {
        using var scope = ListPool<IItemPhysicsComponentsReceiver2D>.Get(out var receivers);
        item.GetComponentsInChildren(true, receivers);
        foreach (IItemPhysicsComponentsReceiver2D receiver in receivers)
            if (receiver is Component component && component.GetComponentInParent<Item>(true) == item)
                receiver.OnItemPhysicsComponentsAvailable();
    }

    private BoxCollider2D RentItemSolidFromTrigger(Item item, BoxCollider2D trigger)
    {
        ItemPhysicsComponentCache cache = itemComponentCaches.GetOrCreateValue(item);
        if (!cache.MovableSolids.TryGetValue(trigger.transform, out BoxCollider2D solid) || solid == null)
        {
            solid = (BoxCollider2D)CreateOwnedItemCollider(cache, trigger.gameObject, typeof(BoxCollider2D));
            cache.MovableSolids[trigger.transform] = solid;
        }
        ResetColliderPhysicsSettings(solid);
        solid.offset = trigger.offset;
        solid.size = trigger.size;
        solid.edgeRadius = trigger.edgeRadius;
        solid.isTrigger = false;
        solid.enabled = true;
        return solid;
    }

    private static void ApplyItemColliderTemplate(Collider2D collider, ItemPhysicsTemplate2D template)
    {
        collider.enabled = false;
        ResetColliderPhysicsSettings(collider);
        if (collider.attachedRigidbody is { bodyType: RigidbodyType2D.Dynamic, useAutoMass: true } &&
            collider.density != template.density) collider.density = template.density;
        collider.sharedMaterial = template.material;
        collider.usedByEffector = template.usedByEffector;
        if (collider.usedByComposite != template.usedByComposite) collider.usedByComposite = template.usedByComposite;
        collider.offset = template.offset;
        collider.isTrigger = template.isTrigger;
        switch (collider)
        {
            case BoxCollider2D box: box.size = template.size; box.edgeRadius = template.edgeRadius; break;
            case CircleCollider2D circle: circle.radius = template.radius; break;
            case CapsuleCollider2D capsule: capsule.size = template.size; capsule.direction = (CapsuleDirection2D)template.capsuleDirection; break;
            case PolygonCollider2D polygon when template.points != null && template.points.Count >= 3:
                polygon.pathCount = 1; polygon.SetPath(0, template.points); break;
        }
        collider.enabled = template.colliderEnabled;
    }

    private static void ApplyItemBodyTemplate(Rigidbody2D body, ItemPhysicsTemplate2D template)
    {
        body.simulated = false;
        body.bodyType = template.bodyType;
        body.useAutoMass = template.bodyUseAutoMass;
        if (!body.useAutoMass) body.mass = template.bodyMass;
        body.drag = template.bodyLinearDrag;
        body.angularDrag = template.bodyAngularDrag;
        body.gravityScale = template.bodyGravityScale;
        body.constraints = template.bodyConstraints;
        body.collisionDetectionMode = template.bodyCollisionDetection;
        body.interpolation = template.bodyInterpolation;
        body.sleepMode = template.bodySleepingMode;
        body.useFullKinematicContacts = template.bodyUseFullKinematicContacts;
        body.simulated = template.bodySimulated;
    }

    private static void RebindInitialItemAnimatorAfterPhysicsLease(Animator animator)
    {
        if (!animator.isActiveAndEnabled || !animator.isInitialized || animator.runtimeAnimatorController == null) return;
        AnimatorControllerParameter[] parameters = animator.parameters;
        var values = new float[parameters.Length];
        var integers = new int[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Float) values[i] = animator.GetFloat(parameter.nameHash);
            else if (parameter.type == AnimatorControllerParameterType.Int) integers[i] = animator.GetInteger(parameter.nameHash);
            else if (parameter.type == AnimatorControllerParameterType.Bool) values[i] = animator.GetBool(parameter.nameHash) ? 1f : 0f;
        }
        var states = new AnimatorStateInfo[animator.layerCount];
        for (int i = 0; i < states.Length; i++) states[i] = animator.GetCurrentAnimatorStateInfo(i);
        // 首次补齐攻击框后重新绑定动画曲线，并恢复已选中的初始状态。
        animator.Rebind();
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Float) animator.SetFloat(parameter.nameHash, values[i]);
            else if (parameter.type == AnimatorControllerParameterType.Int) animator.SetInteger(parameter.nameHash, integers[i]);
            else if (parameter.type == AnimatorControllerParameterType.Bool) animator.SetBool(parameter.nameHash, values[i] != 0f);
        }
        for (int i = 0; i < states.Length; i++)
            if (states[i].fullPathHash != 0) animator.Play(states[i].fullPathHash, i, states[i].normalizedTime);
    }

    private static void ReturnItemPhysicsComponents(Item item)
    {
        if (!itemComponentCaches.TryGetValue(item, out var cache) || !cache.Leased) return;
        cache.Leased = false;
        foreach (Collider2D collider in cache.OwnedColliders)
            if (collider != null) collider.enabled = false;
        foreach (Rigidbody2D body in cache.OwnedBodies)
            if (body != null) body.simulated = false;
    }

    internal static void ReturnUnownedItemPhysicsComponents(Item item)
    {
        if (item != null && !itemOwners.ContainsKey(item)) ReturnItemPhysicsComponents(item);
    }
    #endregion
}
