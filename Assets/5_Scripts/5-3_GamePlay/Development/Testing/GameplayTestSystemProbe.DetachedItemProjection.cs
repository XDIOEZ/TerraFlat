using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 逻辑物品与独立物理绘制回归
    // 使用真实物理服务和视觉投影，关闭视图后继续检查同一个逻辑物品与碰撞租约。
    private static JObject DetachedItemProjection(JObject args)
    {
        using var space = WorldPhysicsScene2D.CreateIsolated("Detached item probe " + Guid.NewGuid().ToString("N"));
        using var physics = new WorldPhysicsService2D("probe:detached:" + Guid.NewGuid().ToString("N"),
            WorldPhysicsScene2D.Borrow(space.Scene));
        var root = new GameObject("Leaf logic probe");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, space.Scene);
        GameItem item = root.AddComponent<GameItem>();
        Texture2D texture = null;
        Sprite sprite = null;
        WorldPresentationProjection projection = null;
        try
        {
            item.BindData(EntityProbeData("probe:detached-root", 1));
            item.itemData.Guid = 749313;
            ItemData data = item.itemData;
            var template = root.AddComponent<ItemPhysicsTemplate2D>();
            template.colliderType = nameof(BoxCollider2D);
            template.isTrigger = true;
            template.size = new Vector2(.6f, .8f);
            template.offset = new Vector2(.1f, -.1f);
            ItemPhysicsDefinitionRuntime.ConfigureDeclaredCollider(item.transform, new ItemColliderDefinitionDto
            {
                Type = nameof(BoxCollider2D), IsTrigger = true, Size = template.size, Offset = template.offset
            });
            root.transform.position = new Vector3(40f, 40f, 0f);
            var render = new GameObject("Render");
            render.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = render.AddComponent<SpriteRenderer>();
            texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 2f);
            renderer.sprite = sprite;
            item.Sprite = renderer;
            new GameObject("Module_Food").transform.SetParent(root.transform, false);
            new GameObject("Mod_HeldFood").transform.SetParent(root.transform, false);
            root.SetActive(true);
            physics.RegisterItem(item);
            if (!physics.TryGetItemProxy(item, out WorldPhysicsEntityProxy2D proxy))
                throw new InvalidOperationException("普通物品没有领取物理代理。");
            BoxCollider2D shape = WorldPhysicsService2D.GetItemRootCollider<BoxCollider2D>(item);
            var result = new JObject
            {
                ["noNativePhysicsOnItem"] = item.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                    item.GetComponentsInChildren<Rigidbody2D>(true).Length == 0,
                ["separatePhysicsRoot"] = !proxy.Shell.Root.IsChildOf(item.transform) &&
                    proxy.Shell.Root.parent.parent.name == "Physics",
                ["declaredGeometry"] = shape != null && shape.size == template.size && shape.offset == template.offset && shape.isTrigger,
                ["hitResolvesLogicalItem"] = GameplayPhysics2D.ResolveComponent<Item>(shape) == item &&
                    WorldPhysicsService2D.TryResolveCollider(shape, out var binding) && binding.Item == item
            };
            projection = WorldPresentationProjection.Create(item.transform,
                RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(space.Scene, RuntimeSceneObjectCategory.Rendering),
                visualHierarchyOnly: true);
            projection.Synchronize(Matrix4x4.identity, true);
            result["viewHasNoLogicOrPhysics"] = projection.GetComponentsInChildren<Item>(true).Length == 0 &&
                projection.GetComponentsInChildren<Module>(true).Length == 0 &&
                projection.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                projection.GetComponentsInChildren<Rigidbody2D>(true).Length == 0;
            result["noLogicPlaceholderNodes"] = projection.transform.Find("Module_Food") == null &&
                projection.transform.Find("Mod_HeldFood") == null;
            SpriteRenderer[] displayed = projection.GetComponentsInChildren<SpriteRenderer>(true);
            result["realVisualCopied"] = displayed.Length == 1 && displayed[0].sprite == sprite && renderer.forceRenderingOff;
            projection.Release();
            projection = null;
            result["viewReleaseKeepsLogicAndPhysics"] = ReferenceEquals(item.itemData, data) && !item.DestructionHandled &&
                proxy.IsValid && shape.enabled && shape.gameObject.activeInHierarchy;

            root.transform.SetPositionAndRotation(new Vector3(43f, 42f), Quaternion.Euler(0, 0, 37));
            root.transform.localScale = Vector3.one * .5f;
            WorldPhysicsService2D.SynchronizeDetachedItemProxy(item);
            result["poseAndScaleFollowOwner"] = Vector2.Distance(proxy.CapturePose().Position, item.transform.position) < .0001f &&
                Mathf.Abs(Mathf.DeltaAngle(proxy.CapturePose().RotationDegrees, 37f)) < .001f &&
                proxy.Shell.Root.localScale == Vector3.one * .5f;
            Physics2D.SyncTransforms();
            var hits = new Collider2D[8];
            int count = space.PhysicsScene.OverlapPoint(shape.bounds.center, new ContactFilter2D { useTriggers = true }, hits);
            result["nativeQueryFindsProxy"] = count == 1 && hits[0] == shape;
            item.SetInHand(true);
            result["heldItemDisablesPhysics"] = !proxy.IsParticipatingInWorldPhysics && !shape.gameObject.activeInHierarchy;
            item.SetInHand(false);
            result["dropReusesSameLease"] = physics.TryGetItemProxy(item, out var same) && ReferenceEquals(same, proxy) &&
                proxy.IsParticipatingInWorldPhysics && shape.gameObject.activeInHierarchy;
            WorldPhysicsService2D.ReleaseItem(item);
            result["releasedProxyCannotResolve"] = !proxy.IsValid && !WorldPhysicsService2D.TryResolveCollider(shape, out _);
            physics.RegisterItem(item);
            result["reuseKeepsItemFreeOfNativePhysics"] = physics.TryGetItemProxy(item, out var rebound) && rebound.IsValid &&
                item.GetComponentsInChildren<Collider2D>(true).Length == 0 && ReferenceEquals(item.itemData, data);
            physics.RegisterItem(item, requestIndependent: true);
            physics.TryGetItemProxy(item, out WorldPhysicsEntityProxy2D moving);
            Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(item);
            result["independentBodyDetached"] = body != null && body.transform != item.transform &&
                moving.Mode == WorldPhysicsProxyMode2D.IndependentDynamic;
            body.velocity = Vector2.right * 2f;
            Vector2 before = item.transform.position;
            physics.Simulate(.1f);
            result["nativeMovementWritesBack"] = item.transform.position.x > before.x + .1f &&
                Vector2.Distance(item.transform.position, body.position) < .001f &&
                item.itemData.PhysicsState.Velocity.x > 1f;
            WorldPhysicsService2D.ConfigureItemBodyForExternallyPositionedMotion(item, body);
            body.simulated = false;
            physics.RegisterItem(item);
            body = WorldPhysicsService2D.GetItemRigidbody(item);
            result["reregisterPreservesRepresentation"] = body != null && body.bodyType == RigidbodyType2D.Kinematic && !body.simulated;
            return result;
        }
        finally
        {
            projection?.Release();
            WorldPhysicsService2D.ReleaseItem(item);
            UnityEngine.Object.DestroyImmediate(root);
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    #endregion
}
