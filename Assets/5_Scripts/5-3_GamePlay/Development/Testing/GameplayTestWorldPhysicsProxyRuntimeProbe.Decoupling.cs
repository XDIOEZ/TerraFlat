#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestWorldPhysicsProxyRuntimeProbe
{
    #region 自然物正式链路拆分回归
    // 在隔离存档中生成真实 Leaf，重绑整块视图后核对同一份数据、模块与物理租约。
    private static IEnumerator CaptureNaturalItemDecoupling(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(player);
        ChunkView view = null;
        ChunkMgr manager = null;
        Vector2 position = player.transform.position;
        float deadline = Time.realtimeSinceStartup + 30f;
        while (view == null || !view.IsBound)
        {
            using (GameplayWorldRegistry.UseWorld(worldKey))
            {
                manager = ChunkMgr.Instance;
                manager.TryGetRuntimeChunkView(position, out view);
            }
            if (view != null && view.IsBound) break;
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("自然物拆分回归等待完整区块表现超时。");
            yield return null;
        }
        JObject result;
        using (GameplayWorldRegistry.UseWorld(worldKey))
        {
            var chunk = view.Model;
            ChunkNaturalItemRuntime runtime = manager.NaturalItemRuntimes.Ensure(chunk);
            Item leaf = own.SpawnItem("Leaf", WorldLocalPresentation.ToLogical(position));
            leaf.transform.SetParent(runtime.GetOrCreateLogicalItemRoot(), true);
            runtime.RegisterNaturalItem(leaf);
            ItemData identity = leaf.itemData;
            Module food = leaf.GetMod<Mod_Food>();
            Module held = leaf.GetMod<Mod_HeldFood>();
            WorldPhysicsService2D.TryGetRegisteredItemProxy(leaf, out WorldPhysicsEntityProxy2D proxy);
            WorldRenderingManager rendering = WorldRenderingManager.ExistingInstance;
            WorldPresentationProjection projection = null;
            bool hasProjection = rendering != null &&
                rendering.TryGetNaturalItemView(leaf, out projection);
            bool borrowedVisual = rendering != null && leaf.Sprite != null &&
                rendering.OwnsActorVisual(leaf, leaf.Sprite.transform);
            Transform presentation = borrowedVisual ? rendering.GetActorProjectionSource(leaf) :
                hasProjection ? projection.transform : null;
            result = new JObject
            {
                ["realFoodModules"] = food != null && held != null,
                ["logicOutsideChunkView"] = leaf.GetComponentInParent<ChunkView>(true) == null &&
                    ReferenceEquals(leaf.NaturalRuntimeOwner, runtime),
                ["physicsOutsideLogic"] = proxy != null && proxy.Shell.OwnsRoot &&
                    leaf.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                    leaf.GetComponentsInChildren<Rigidbody2D>(true).Length == 0,
                ["pureVisibleView"] = presentation != null &&
                    presentation.GetComponentsInChildren<Module>(true).Length == 0 &&
                    presentation.GetComponentsInChildren<Item>(true).Length == 0 &&
                    presentation.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                    (!borrowedVisual || leaf.GetComponentsInChildren<Renderer>(true).Length == 0)
            };
            int count = runtime.SpawnedItemCount;
            try
            {
                view.Unbind();
                result["viewUnbindKeepsLogic"] = !runtime.IsDisposed && ReferenceEquals(leaf.itemData, identity) &&
                    !leaf.DestructionHandled && runtime.TryGetSpawnedItem(identity.Guid, out Item current) && current == leaf &&
                    leaf.GetMod<Mod_Food>() == food && leaf.GetMod<Mod_HeldFood>() == held;
                result["viewUnbindKeepsPhysics"] = proxy != null && proxy.IsValid &&
                    WorldPhysicsService2D.TryGetRegisteredItemProxy(leaf, out var sameProxy) && ReferenceEquals(sameProxy, proxy);
            }
            finally { view.Bind(manager.WorldRuntime, chunk); }
            result["rebindDoesNotClone"] = runtime.SpawnedItemCount == count &&
                ReferenceEquals(manager.NaturalItemRuntimes.Ensure(chunk), runtime) &&
                runtime.TryGetSpawnedItem(identity.Guid, out Item rebound) && rebound == leaf;
        }
        completed(result);
    }
    #endregion
}
#endif
