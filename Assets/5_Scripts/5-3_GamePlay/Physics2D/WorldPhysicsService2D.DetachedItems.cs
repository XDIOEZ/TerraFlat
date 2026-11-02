using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Pool;

public sealed partial class WorldPhysicsService2D
{
    #region 普通物品独立物理宿主
    // 无动画和模块局部碰撞契约的根形状直接领取世界代理，不在物品外壳创建原生组件。
    private bool TryRegisterDetachedRootItemPhysics(Item item, List<Collider2D> registered,
        bool previousIndependent, bool requestIndependent, WorldPhysicsItemBodyRepresentation2D? previousRepresentation)
    {
        if (!TryGetDetachedRootItemPhysicsDeclaration(item, out ItemPhysicsTemplate2D template,
                out ItemColliderDefinitionDto declared)) return false;
        ItemPhysicsDefinitionDto definition = ResolveEntityPhysicsDefinition(GetConfiguredItemPhysics(item),
            previousIndependent || requestIndependent);
        string type = !string.IsNullOrWhiteSpace(declared?.Type) ? declared.Type : template.colliderType;
        bool trigger = declared?.IsTrigger ?? template?.isTrigger ?? false;
        var region = new WorldPhysicsEntityRegion2D("item-root",
            trigger ? WorldPhysicsPurpose2D.Trigger : WorldPhysicsPurpose2D.Blocking,
            new ItemColliderDefinitionDto { Type = type });
        WorldPhysicsEntityProxy2D proxy = AllocateEntityProxy(item, item.itemData, definition,
            new[] { region }, pose => ApplyDetachedItemPhysicsPose(item, pose),
            stateAuthority: GameNetwork.HasStateAuthority,
            logicalPosition: WorldLocalPresentation.ToLogical(item.transform.position));
        try
        {
            Collider2D shape = proxy.Colliders[0];
            if (template != null) ApplyItemColliderTemplate(shape, template);
            ItemPhysicsDefinitionRuntime.ApplyColliderParameters(shape, declared);
            shape.gameObject.layer = item.gameObject.layer;
            RegisterCollider(shape, item, item, purpose: shape.isTrigger
                ? WorldPhysicsPurpose2D.Trigger : WorldPhysicsPurpose2D.Blocking, entity: item);
            registered.Add(shape);
            proxy.SetEntityScale(item.transform.lossyScale);
            WorldPhysicsEntityPose2D pose = proxy.CapturePose();
            proxy.SetPose(new WorldPhysicsEntityPose2D(item.transform.position, item.transform.eulerAngles.z,
                pose.Velocity, pose.AngularVelocity));
            if (previousIndependent && definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic && previousRepresentation.HasValue)
                RestoreItemBodyRepresentation(item, proxy.Body, previousRepresentation.Value);
            WorldPhysicsItemContactRelay2D relay = proxy.Shell.Root.GetComponent<WorldPhysicsItemContactRelay2D>();
            if (relay == null) relay = proxy.Shell.Root.gameObject.AddComponent<WorldPhysicsItemContactRelay2D>();
            ItemPhysicsProjection2D.Ensure(item);
            relay.Bind(item.GetComponent<ItemPhysicsProjection2D>());
            NotifyItemPhysicsComponentsAvailable(item);
            SynchronizeDetachedItemProxy(item);
            ItemMgr.GetExistingInstance()?.RefreshPhysicsWorldResidentOwnership(item);
            return true;
        }
        catch { ReleaseEntityProxy(proxy); throw; }
    }

    internal static bool TryGetDetachedRootItemPhysicsDeclaration(Item item, out ItemPhysicsTemplate2D template,
        out ItemColliderDefinitionDto declared)
    {
        template = null;
        declared = null;
        if (item == null || item is Player || item is Map) return false;
        if (GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(item.itemData?.IDName, out RuntimeItemDefinition definition) &&
            definition.IsActor) return false;
        ItemPhysicsDefinitionRuntime.ItemPhysicsConfiguration configuration =
            ItemPhysicsDefinitionRuntime.GetItemPhysicsConfiguration(item);
        if (configuration.Regions.Count != 0) return false;
        foreach (var entry in configuration.Colliders)
        {
            // 物品 visual.collider 的正式声明目标是根 Transform，模块声明仍必须独立处理。
            if ((entry.Key != item && entry.Key != item.transform) || declared != null) return false;
            declared = entry.Value;
        }
        using var templatesScope = ListPool<ItemPhysicsTemplate2D>.Get(out var templates);
        item.GetComponentsInChildren(true, templates);
        foreach (ItemPhysicsTemplate2D candidate in templates)
        {
            if (candidate.GetComponentInParent<Item>(true) != item) continue;
            if (candidate.transform != item.transform || candidate.owningModule != null || candidate.hasBody ||
                candidate.usedByComposite || candidate.usedByEffector) return false;
            if (string.IsNullOrEmpty(candidate.colliderType)) continue;
            if (template != null) return false;
            template = candidate;
        }
        if (template == null && string.IsNullOrWhiteSpace(declared?.Type)) return false;
        using var animationsScope = ListPool<Animator>.Get(out var animations);
        item.GetComponentsInChildren(true, animations);
        foreach (Animator animation in animations)
            if (animation.runtimeAnimatorController != null && animation.GetComponentInParent<Item>(true) == item)
                return false;
        using var shapesScope = ListPool<Collider2D>.Get(out var shapes);
        item.GetComponentsInChildren(true, shapes);
        foreach (Collider2D shape in shapes)
            if (shape.GetComponentInParent<Item>(true) == item) return false;
        return true;
    }

    public static Rigidbody2D GetItemRigidbody(Item item)
    {
        if (item == null) return null;
        if (TryGetRegisteredItemProxy(item, out WorldPhysicsEntityProxy2D proxy) && proxy.Shell.OwnsRoot)
            return proxy.Shell.SharedFixedBody ? null : proxy.Body;
        return item.GetComponent<Rigidbody2D>();
    }

    public static T GetItemRootCollider<T>(Item item) where T : Collider2D
    {
        if (item == null) return null;
        if (TryGetRegisteredItemProxy(item, out WorldPhysicsEntityProxy2D proxy) && proxy.Shell.OwnsRoot)
        {
            foreach (Collider2D shape in proxy.Colliders) if (shape is T typed) return typed;
            return null;
        }
        return item.GetComponent<T>();
    }

    internal static void SynchronizeDetachedItemProxy(Item item)
    {
        if (item == null) return;
        if (!TryGetRegisteredItemProxy(item, out WorldPhysicsEntityProxy2D proxy) || !proxy.Shell.OwnsRoot) return;
        bool active = item.gameObject.activeInHierarchy && !item.InHand;
        proxy.SetParticipationInWorldPhysics(active);
        if (!active) return;
        WorldPhysicsEntityPose2D pose = proxy.CapturePose();
        Vector2 position = item.transform.position;
        float rotation = item.transform.eulerAngles.z;
        if (pose.Position != position || Mathf.Abs(Mathf.DeltaAngle(pose.RotationDegrees, rotation)) > .0001f)
            proxy.SetPose(new WorldPhysicsEntityPose2D(position, rotation, pose.Velocity, pose.AngularVelocity));
        if (proxy.Shell.Root.localScale != item.transform.lossyScale)
            proxy.SetEntityScale(item.transform.lossyScale);
    }

    private static void ApplyDetachedItemPhysicsPose(Item item, WorldPhysicsEntityPose2D pose)
    {
        if (item == null || item.DestructionHandled || item.IsInPool || item.InHand) return;
        item.transform.SetPositionAndRotation(new Vector3(pose.Position.x, pose.Position.y, item.transform.position.z),
            Quaternion.Euler(0f, 0f, pose.RotationDegrees));
        item.GetComponent<ItemPhysicsProjection2D>()?.ApplyWorldPhysicsPose(pose);
    }

    private Rigidbody2D EnsureDetachedItemMovableBody(Item item)
    {
        if (!CanItemUseIndependentMotion(item)) return null;
        if (!TryGetItemProxy(item, out WorldPhysicsEntityProxy2D proxy) ||
            proxy.Mode != WorldPhysicsProxyMode2D.IndependentDynamic)
        {
            RegisterItem(item, requestIndependent: true);
            if (!TryGetItemProxy(item, out proxy)) throw new InvalidOperationException("物品未能取得独立运动代理。");
        }
        ConfigureItemBodyForIndependentMotion(item, proxy.Body, refreshRegistration: false);
        if (GetConfiguredItemPhysics(item) == null)
        {
            bool solid = false;
            BoxCollider2D trigger = null;
            foreach (Collider2D shape in proxy.Colliders)
            {
                if (shape.enabled && !shape.isTrigger) solid = true;
                if (shape is BoxCollider2D box && shape.enabled && shape.isTrigger) trigger = box;
            }
            if (!solid && trigger != null)
            {
                BoxCollider2D added = RentDetachedItemSolidBox(item, proxy, out _);
                added.offset = trigger.offset;
                added.size = trigger.size;
                added.edgeRadius = trigger.edgeRadius;
            }
        }
        ItemPhysicsProjection2D.Ensure(item);
        return proxy.Body;
    }

    private BoxCollider2D RentDetachedItemSolidBox(Item item, WorldPhysicsEntityProxy2D proxy, out bool created)
    {
        foreach (Collider2D shape in proxy.Colliders)
            if (shape is BoxCollider2D existing && !shape.isTrigger) { created = false; return existing; }
        var region = new WorldPhysicsEntityRegion2D("item-solid", WorldPhysicsPurpose2D.Blocking,
            new ItemColliderDefinitionDto { Type = nameof(BoxCollider2D) });
        var box = (BoxCollider2D)RentEntityRegionCollider(proxy.Shell, region);
        box.gameObject.layer = item.gameObject.layer;
        RegisterCollider(box, item, item, purpose: WorldPhysicsPurpose2D.Blocking, entity: item);
        itemColliders[item].Add(box);
        created = true;
        return box;
    }
    #endregion
}
