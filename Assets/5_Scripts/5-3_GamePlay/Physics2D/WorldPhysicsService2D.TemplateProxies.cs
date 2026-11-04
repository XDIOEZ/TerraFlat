using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Pool;

public sealed partial class WorldPhysicsService2D
{
    #region 模板实体的独立物理宿主
    internal sealed class ItemTemplateProxyRegion
    {
        internal Transform Source;
        internal Component Receiver;
        internal Collider2D Shape;
        internal Rigidbody2D Body;
        internal ItemPhysicsTemplate2D Template;
        internal bool SourceActive;
    }

    private static ItemPhysicsTemplate2D FindItemRootBodyTemplate(Item item)
    {
        foreach (ItemPhysicsTemplate2D template in item.GetComponents<ItemPhysicsTemplate2D>())
            if (template.hasBody && template.bodyType != RigidbodyType2D.Static && template.owningModule == null) return template;
        return null;
    }

    private bool TryRegisterTemplateItemPhysicsProxy(Item item, List<Collider2D> registered,
        WorldPhysicsItemBodyRepresentation2D? representation, Vector2 velocity, float angularVelocity)
    {
        ItemPhysicsTemplate2D bodyTemplate = FindItemRootBodyTemplate(item);
        if (bodyTemplate == null) return false;
        ItemPhysicsDefinitionDto configured = GetConfiguredItemPhysics(item);
        ItemPhysicsDefinitionDto definition = configured ?? new ItemPhysicsDefinitionDto
        {
            Mode = WorldPhysicsProxyMode2D.IndependentDynamic,
            FreezeRotation = (bodyTemplate.bodyConstraints & RigidbodyConstraints2D.FreezeRotation) != 0,
            LinearDamping = bodyTemplate.bodyLinearDrag,
            AngularDamping = bodyTemplate.bodyAngularDrag
        };
        WorldPhysicsEntityProxy2D proxy = AllocateEntityProxy(item, item.itemData, definition, null,
            pose => ApplyDetachedItemPhysicsPose(item, pose), GameNetwork.HasStateAuthority,
            WorldLocalPresentation.ToLogical(item.transform.position));
        if (!proxy.Shell.SharedFixedBody)
        {
            ApplyItemBodyTemplate(proxy.Body, bodyTemplate);
            ConfigureProxyBody(proxy.Body, item.itemData, definition, GameNetwork.HasStateAuthority);
            if (representation.HasValue) RestoreItemBodyRepresentation(item, proxy.Body, representation.Value);
            proxy.SetPose(new WorldPhysicsEntityPose2D(item.transform.position, item.transform.eulerAngles.z,
                representation.HasValue ? velocity : item.itemData.PhysicsState?.Velocity ?? Vector2.zero,
                representation.HasValue ? angularVelocity : item.itemData.PhysicsState?.AngularVelocity ?? 0f));
        }
        using var scope = ListPool<ItemPhysicsTemplate2D>.Get(out var templates);
        item.GetComponentsInChildren(true, templates);
        var configuration = ItemPhysicsDefinitionRuntime.GetItemPhysicsConfiguration(item);
        foreach (ItemPhysicsTemplate2D template in templates)
        {
            if (template.GetComponentInParent<Item>(true) != item || string.IsNullOrEmpty(template.colliderType)) continue;
            Component receiver = template.owningModule != null ? template.owningModule :
                template.transform == item.transform ? null : template.GetComponent<Module>();
            configuration.Colliders.TryGetValue(receiver != null ? receiver : template.transform, out var colliderDefinition);
            if (colliderDefinition == null && template.transform == item.transform)
                configuration.Colliders.TryGetValue(item, out colliderDefinition);
            Collider2D shape = RentTemplateItemProxyRegion(proxy, template.transform, receiver,
                string.IsNullOrEmpty(colliderDefinition?.Type) ? template.colliderType : colliderDefinition.Type, registered);
            ApplyItemColliderTemplate(shape, template);
            proxy.TemplateRegions[proxy.TemplateRegions.Count - 1].Template = template;
            if (template.hasBody && template.transform != item.transform)
            {
                Rigidbody2D regionBody = RentItemRigidbody(item, shape.transform);
                ApplyItemBodyTemplate(regionBody, template);
                proxy.TemplateRegions[proxy.TemplateRegions.Count - 1].Body = regionBody;
            }
            ItemPhysicsDefinitionRuntime.ApplyColliderParameters(shape, colliderDefinition);
            RegisterTemplateItemProxyRegion(item, shape, receiver);
            foreach (var entry in configuration.Regions)
            {
                ItemPhysicsRegion2D replacement = entry.Key;
                if ((!shape.isTrigger && (replacement.Purpose & WorldPhysicsPurpose2D.Blocking) != 0) ||
                    receiver != null && replacement.Receiver == receiver ||
                    shape.isTrigger && receiver == null && (replacement.Purpose & WorldPhysicsPurpose2D.Interaction) != 0)
                    shape.enabled = false;
            }
        }
        foreach (var entry in configuration.Colliders)
        {
            if (GetItemCollider<Collider2D>(entry.Key) != null) continue;
            Collider2D shape = RentTemplateItemProxyRegion(proxy, entry.Key.transform, entry.Key,
                ItemPhysicsDefinitionRuntime.ResolveDeclaredColliderType(entry.Key, entry.Value.Type).Name, registered);
            ItemPhysicsDefinitionRuntime.ApplyColliderParameters(shape, entry.Value);
            RegisterTemplateItemProxyRegion(item, shape, entry.Key);
        }
        foreach (var entry in configuration.Regions)
        {
            ItemPhysicsRegion2D region = entry.Key;
            Collider2D shape = RentTemplateItemProxyRegion(proxy, region.transform, region.Receiver,
                entry.Value.Type, registered);
            ItemPhysicsDefinitionRuntime.ApplyColliderParameters(shape, entry.Value);
            shape.isTrigger = (region.Purpose & WorldPhysicsPurpose2D.Blocking) == 0;
            RegisterCollider(shape, item, region.Receiver, region.RegionId, region.Purpose, item);
        }
        ItemPhysicsProjection2D.Ensure(item);
        WorldPhysicsItemContactRelay2D relay = proxy.Shell.Root.GetComponent<WorldPhysicsItemContactRelay2D>();
        if (relay == null) relay = proxy.Shell.Root.gameObject.AddComponent<WorldPhysicsItemContactRelay2D>();
        relay.Bind(item);
        SynchronizeDetachedItemProxy(item);
        NotifyItemPhysicsComponentsAvailable(item);
        ItemMgr.GetExistingInstance()?.RefreshPhysicsWorldResidentOwnership(item);
        return true;
    }

    private Collider2D RentTemplateItemProxyRegion(WorldPhysicsEntityProxy2D proxy,
        Transform source, Component receiver, string type, List<Collider2D> registered)
    {
        var region = new WorldPhysicsEntityRegion2D("template-" + registered.Count,
            WorldPhysicsPurpose2D.Trigger, new ItemColliderDefinitionDto { Type = type });
        Collider2D shape = RentEntityRegionCollider(proxy.Shell, region);
        shape.gameObject.name = source.name + " Physics";
        shape.gameObject.layer = source.gameObject.layer;
        proxy.TemplateRegions.Add(new ItemTemplateProxyRegion
            { Source = source, Receiver = receiver, Shape = shape, SourceActive = true });
        ItemPhysicsRegion2D relay = shape.GetComponent<ItemPhysicsRegion2D>();
        if (relay == null) relay = shape.gameObject.AddComponent<ItemPhysicsRegion2D>();
        relay.BindRegion(null, WorldPhysicsPurpose2D.Trigger, receiver);
        registered.Add(shape);
        return shape;
    }

    private void RegisterTemplateItemProxyRegion(Item item,
        Collider2D shape, Component receiver)
    {
        WorldPhysicsPurpose2D purpose = shape.isTrigger ? WorldPhysicsPurpose2D.Trigger : WorldPhysicsPurpose2D.Blocking;
        if (receiver is IItemPhysicsColliderOwner2D owner)
        {
            owner.ConfigureOwnedPhysicsCollider(shape);
            purpose = owner.PhysicsColliderPurpose;
        }
        RegisterCollider(shape, item, receiver, purpose: purpose, entity: item);
    }

    private static void SynchronizeTemplateItemProxyRegions(WorldPhysicsEntityProxy2D proxy)
    {
        foreach (ItemTemplateProxyRegion region in proxy.TemplateRegions)
        {
            Transform source = region.Source;
            bool active = source != null && IsPhysicsShapeActiveWithinItem(source, proxy.Item.transform) &&
                (region.Receiver is not Module module || module.enabled && module.Enabled);
            if (active != region.SourceActive)
            {
                region.Shape.gameObject.SetActive(active);
                region.SourceActive = active;
            }
            if (!active) continue;
            SynchronizeItemColliderSourceTransform(proxy.Item, source, region.Shape);
            Transform target = region.Shape.transform;
            if (region.Body != null)
            {
                region.Body.position = target.position;
                region.Body.rotation = target.eulerAngles.z;
            }
        }
    }
    #endregion

    #region 按逻辑来源领取物理组件
    internal static void SynchronizeItemColliderSourceTransform(Item item, Transform source, Collider2D shape)
    {
        Transform target = shape.transform;
        if (target == source) return;
        target.SetLocalPositionAndRotation(item.transform.InverseTransformPoint(source.position),
            Quaternion.Inverse(item.transform.rotation) * source.rotation);
        Vector3 scale = source.lossyScale, rootScale = item.transform.lossyScale;
        target.localScale = new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z);
    }

    public static T GetItemCollider<T>(Component source) where T : Collider2D
    {
        if (source == null) return null;
        Item item = source as Item ?? source.GetComponentInParent<Item>(true);
        if (TryGetRegisteredItemProxy(item, out var proxy) && proxy.TemplateRegions.Count > 0)
        {
            for (int index = proxy.TemplateRegions.Count - 1; index >= 0; index--)
            {
                ItemTemplateProxyRegion region = proxy.TemplateRegions[index];
                if (region.Shape is T shape && (source is Module ? region.Receiver == source : region.Source == source.transform))
                    return shape;
            }
            return null;
        }
        return source.GetComponent<T>();
    }

    public static void GetItemColliders(Item item, List<Collider2D> result)
    {
        if (item != null && itemOwners.TryGetValue(item, out var owner) && owner.itemColliders.TryGetValue(item, out var registered))
            result.AddRange(registered);
        else if (item != null) item.GetComponentsInChildren(true, result);
    }

    public static Collider2D[] GetItemColliders(Item item)
    {
        using var scope = ListPool<Collider2D>.Get(out var result);
        GetItemColliders(item, result);
        return result.ToArray();
    }
    #endregion
}
