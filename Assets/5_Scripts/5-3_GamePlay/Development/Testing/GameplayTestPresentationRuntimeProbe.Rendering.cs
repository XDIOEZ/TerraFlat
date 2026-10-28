using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 正式角色渲染绑定和特效池租约
    private sealed class RenderingFixture : IDisposable
    {
        public readonly ItemMgr Items;
        public readonly ActorRenderEffectController Controller;
        public readonly VisualEffectManager Effects;
        public readonly WorldRenderingManager Rendering;
        public Item WorldItem;
        public Item Item;
        public GameObject Attachment;
        public GameObject Effect;
        private bool disposed;

        public RenderingFixture(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            Items = ItemMgr.Instance ?? throw new InvalidOperationException("正式物品管理器未就绪。");
            Controller = player.GetComponentInChildren<ActorRenderEffectController>(true) ??
                throw new InvalidOperationException("已入世玩家没有正式角色渲染控制器。");
            Effects = VisualEffectManager.Instance;
            Rendering = WorldRenderingManager.ExistingInstance ??
                throw new InvalidOperationException("真实世界没有统一渲染管理器。");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                try
                {
                    if (Item != null)
                    {
                        try { Rendering.StopOwnerEffect(Item.transform, "WetStatusEffect"); }
                        finally { Controller.UnregisterExternalRenderers(Item.transform); }
                    }
                }
                finally { if (Effect != null) Rendering.ReturnEffectToPool("WetStatusEffect", Effect); }
            }
            finally
            {
                try
                {
                    if (WorldItem != null && !WorldItem.DestructionHandled) Items.DespawnItem(WorldItem, false);
                }
                finally
                {
                    try { if (Item != null && !Item.DestructionHandled) Items.DespawnItem(Item, false); }
                    finally { if (Attachment != null) UnityEngine.Object.Destroy(Attachment); }
                }
            }
        }
    }

    private static int ActiveWetEffects(VisualEffectManager effects) =>
        effects.activeEffects.TryGetValue("WetStatusEffect", out var values) ? values.Count(value => value != null) : 0;

    private static IEnumerator CaptureRendering(Player player, JObject args, Action<JObject> complete)
    {
        var fixture = new RenderingFixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        var floors = new System.Collections.Generic.List<Item>();
        // 船板保留到案例结束截图后，再由隔离宿主统一回收。
        IEnumerator ReleaseFloorsAfterScreenshot()
        {
            foreach (Item floor in floors)
                if (floor != null && !floor.DestructionHandled) fixture.Items.DespawnItem(floor, false);
            yield break;
        }
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(ReleaseFloorsAfterScreenshot);
        JObject result = null;
        int beforeEffects = ActiveWetEffects(fixture.Effects);
        try
        {
            var floorSorting = new JArray();
            foreach (string floorId in new[] { "ShipFloor_Iron", "ShipFloor_Refractory" })
            {
                Item floor = fixture.Items.InstantiateItem(floorId, player.transform.position +
                    (floorId == "ShipFloor_Iron" ? Vector3.down : Vector3.up) * .5f);
                floors.Add(floor);
                floor.Load();
            }
            yield return null;
            yield return null;
            foreach (Item floor in floors)
            {
                WorldSortingManager.ReadExternalRendererKey(floor.Sprite, out int layer, out int order);
                floorSorting.Add(new JObject { ["id"] = floor.itemData.IDName, ["loaded"] = floor.IsInitialized,
                    ["layer"] = SortingLayer.IDToName(layer), ["order"] = order,
                    ["layerValue"] = SortingLayer.GetLayerValueFromID(layer),
                    ["projectedIntoDynamicBatch"] = BuildingDepthMeshBridge.IsProjected(floor.Sprite) });
            }
            WorldSortingManager.ReadExternalRendererKey(player.Sprite, out int playerLayer, out _);
            fixture.WorldItem = fixture.Items.InstantiateItem("Axe_Stone", player.transform.position + Vector3.right * 6f);
            fixture.WorldItem.Load();
            SpriteRenderer worldSprite = fixture.WorldItem.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(value => value.enabled && value.sprite != null);
            if (worldSprite == null) throw new InvalidOperationException("正式世界石斧没有真实 SpriteRenderer。");
            WorldSortingMember member = fixture.WorldItem.GetComponent<WorldSortingMember>() ??
                throw new InvalidOperationException("正式物品激活后没有接入世界排序。");
            WorldSortingManager sorting = UnityEngine.Object.FindObjectOfType<WorldSortingManager>() ??
                throw new InvalidOperationException("真实世界没有排序管理器。");
            fixture.Rendering.GetSortingKey(member.Category, out int expectedLayer, out int expectedOrder);
            SortingGroup group = worldSprite.GetComponentsInParent<SortingGroup>(true).LastOrDefault(value => value.enabled &&
                (value.transform == fixture.WorldItem.transform || value.transform.IsChildOf(fixture.WorldItem.transform)));
            bool sortingMatches = (group != null ? group.sortingLayerID : worldSprite.sortingLayerID) == expectedLayer &&
                (group != null ? group.sortingOrder : worldSprite.sortingOrder) == expectedOrder;
            string category = member.Category;
            int worldGuid = fixture.WorldItem.itemData.Guid;
            bool realWorldItemLoaded = fixture.WorldItem.IsInitialized;
            bool worldVisible = member.IsWorldVisible && !fixture.WorldItem.InHand;
            // 世界物同帧采真实排序，跨帧绑定另用正式手持入口，避免散落物按规则转为轻量掉落。
            fixture.Items.DespawnItem(fixture.WorldItem, false);
            bool worldItemReleased = fixture.Items.GetItemByGuid(worldGuid) == null;
            fixture.WorldItem = null;
            fixture.Attachment = new GameObject("Lua渲染夹具_自有手持挂点");
            fixture.Attachment.transform.position = player.transform.position + Vector3.right * 2f;
            ItemData heldData = GameRes.ExistingInstance.CreateItemData("Axe_Stone");
            heldData.Stack.CanBePickedUp = false;
            fixture.Item = fixture.Items.InstantiateHeldItem(heldData, player, fixture.Attachment.transform);
            fixture.Item.Load();
            SpriteRenderer external = fixture.Item.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(value => value.enabled && value.sprite != null);
            if (external == null) throw new InvalidOperationException("正式石斧没有真实 SpriteRenderer。");
            int itemInstanceId = fixture.Item.GetInstanceID();
            int rendererInstanceId = external.GetInstanceID();
            uint itemGeneration = fixture.Item.RuntimeGeneration;
            SpriteRenderer body = player.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(value => value.enabled && value.sprite != null && value.sharedMaterial != null &&
                    value.sharedMaterial.HasProperty("_BodyMinV") && fixture.Controller.Renderers.Contains(value));
            if (body == null) throw new InvalidOperationException("真实玩家主体没有支持 MPB 合成的正式共享材质。");
            Material[] originalMaterials = external.sharedMaterials;
            var originalBlock = new MaterialPropertyBlock();
            int markerId = Shader.PropertyToID("_LuaPresentationProbe");
            int bodyMinId = Shader.PropertyToID("_BodyMinV");
            external.GetPropertyBlock(originalBlock);
            originalBlock.SetFloat(markerId, 41f);
            originalBlock.SetFloat(bodyMinId, -987f);
            external.SetPropertyBlock(originalBlock);

            fixture.Controller.RegisterExternalRenderers(fixture.Item.transform);
            fixture.Controller.RegisterExternalRenderers(fixture.Item.transform);
            yield return null;
            yield return null;
            if (fixture.Item == null || !fixture.Item.IsInitialized || fixture.Item.RuntimeGeneration != itemGeneration || external == null)
                throw new InvalidOperationException("正式自有手持石斧或 SpriteRenderer 在 MPB 等待期间被回收或换代。");
            var submitted = new MaterialPropertyBlock();
            external.GetPropertyBlock(submitted);
            bool registered = fixture.Controller.Renderers.Contains(external);
            int registrations = fixture.Controller.Renderers.Count(value => value == external);
            bool sharedPlayerMaterial = external.sharedMaterial == body.sharedMaterial;
            float submittedMin = submitted.GetFloat(bodyMinId);
            float marker = submitted.GetFloat(markerId);
            fixture.Controller.UnregisterExternalRenderers(fixture.Item.transform);
            var restored = new MaterialPropertyBlock();
            external.GetPropertyBlock(restored);
            bool materialRestored = external.sharedMaterials.SequenceEqual(originalMaterials);
            bool bindingReleased = !fixture.Controller.Renderers.Contains(external);

            fixture.Effect = fixture.Rendering.PlayEffect(fixture.Item.transform, "WetStatusEffect",
                fixture.Item.transform, Vector3.up * .4f, -1f, EffectStackMode.NonStackable);
            if (fixture.Effect == null) throw new InvalidOperationException("正式资源目录无法创建 WetStatusEffect。");
            Vector3 initialScale = fixture.Effect.transform.localScale;
            GameObject repeated = fixture.Rendering.PlayEffect(fixture.Item.transform, "WetStatusEffect",
                fixture.Item.transform, Vector3.up * .4f, -1f, EffectStackMode.NonStackable);
            ParticleSystem[] particles = fixture.Effect.GetComponentsInChildren<ParticleSystem>(true);
            yield return null;
            yield return null;
            bool ownerBound = fixture.Rendering.HasEffect(fixture.Item.transform, "WetStatusEffect");
            int duringEffects = ActiveWetEffects(fixture.Effects);
            int ownRegistrations = fixture.Effects.activeEffects.TryGetValue("WetStatusEffect", out var activeWet)
                ? activeWet.Count(value => value == fixture.Effect) : 0;
            bool active = fixture.Effect.activeInHierarchy;
            bool particleMaterials = particles.Length > 0 && particles.All(value =>
                value.GetComponent<ParticleSystemRenderer>()?.sharedMaterial != null);
            fixture.Effect.transform.localScale = initialScale * 3f;
            fixture.Rendering.StopOwnerEffect(fixture.Item.transform, "WetStatusEffect");
            bool ownerReleased = !fixture.Rendering.HasEffect(fixture.Item.transform, "WetStatusEffect");
            bool pooledInactive = !fixture.Effect.activeSelf;
            bool particlesCleared = particles.All(value => value.particleCount == 0 && !value.isPlaying);
            bool poseRestored = fixture.Effect.transform.localScale == initialScale;
            fixture.Rendering.ReturnEffectToPool("WetStatusEffect", fixture.Effect);
            int afterDoubleReturn = ActiveWetEffects(fixture.Effects);
            bool ownRegistrationReleased = !fixture.Effects.activeEffects.TryGetValue("WetStatusEffect", out var remainingWet) ||
                !remainingWet.Contains(fixture.Effect);
            result = new JObject
            {
                ["realItemLoaded"] = fixture.Item.IsInitialized, ["itemId"] = fixture.Item.itemData.IDName,
                ["realPlayerController"] = fixture.Controller.isActiveAndEnabled,
                ["bodyVisible"] = body.gameObject.activeInHierarchy && body.enabled && body.color.a > 0f,
                ["worldSortingMatches"] = sortingMatches, ["category"] = category,
                ["floors"] = floorSorting, ["playerLayerValue"] = SortingLayer.GetLayerValueFromID(playerLayer),
                ["realWorldItemLoaded"] = realWorldItemLoaded, ["worldVisible"] = worldVisible,
                ["worldGuid"] = worldGuid, ["worldItemReleased"] = worldItemReleased,
                ["heldGuid"] = fixture.Item.itemData.Guid, ["heldInHand"] = fixture.Item.InHand,
                ["ownedByPlayer"] = ReferenceEquals(fixture.Item.Owner, player),
                ["selfOwnedAttachment"] = fixture.Item.transform.parent == fixture.Attachment.transform,
                ["itemInstanceId"] = itemInstanceId, ["currentItemInstanceId"] = fixture.Item.GetInstanceID(),
                ["rendererInstanceId"] = rendererInstanceId, ["currentRendererInstanceId"] = external.GetInstanceID(),
                ["itemGeneration"] = itemGeneration, ["currentItemGeneration"] = fixture.Item.RuntimeGeneration,
                ["registered"] = registered, ["registrationCount"] = registrations,
                ["sharedPlayerMaterial"] = sharedPlayerMaterial,
                ["submittedBodyMin"] = submittedMin, ["expectedBodyMin"] = external.sprite.bounds.min.y,
                ["customPropertyPreserved"] = marker == 41f,
                ["materialRestored"] = materialRestored, ["bindingReleased"] = bindingReleased,
                ["originalBodyMinRestored"] = restored.GetFloat(bodyMinId), ["originalMarkerRestored"] = restored.GetFloat(markerId),
                ["effectActive"] = active, ["sameNonStackableEffect"] = repeated == fixture.Effect,
                ["ownerBound"] = ownerBound, ["beforeEffects"] = beforeEffects, ["duringEffects"] = duringEffects,
                ["ownRegistrations"] = ownRegistrations,
                ["particleMaterialsReady"] = particleMaterials, ["ownerReleased"] = ownerReleased,
                ["pooledInactive"] = pooledInactive, ["particlesCleared"] = particlesCleared,
                ["poseRestored"] = poseRestored, ["afterDoubleReturn"] = afterDoubleReturn,
                ["ownRegistrationReleased"] = ownRegistrationReleased
            };

            GameObject firstStack = fixture.Rendering.PlayEffect(fixture.Item.transform, "WetStatusEffect",
                fixture.Item.transform, Vector3.zero, -1f, EffectStackMode.Stackable);
            GameObject secondStack = fixture.Rendering.PlayEffect(fixture.Item.transform, "WetStatusEffect",
                fixture.Item.transform, Vector3.up * .2f, -1f, EffectStackMode.Stackable);
            result["stackedEffectsActive"] = firstStack != null && secondStack != null &&
                firstStack != secondStack && firstStack.activeInHierarchy && secondStack.activeInHierarchy;
            fixture.Rendering.UnregisterItemRendering(fixture.Item);
            result["stackedOwnerReleased"] = !fixture.Rendering.HasEffect(fixture.Item.transform, "WetStatusEffect");
            result["allStackedEffectsPooled"] = firstStack != null && secondStack != null &&
                !firstStack.activeSelf && !secondStack.activeSelf;
            result["stackedParticlesCleared"] = new[] { firstStack, secondStack }.All(effect => effect != null &&
                effect.GetComponentsInChildren<ParticleSystem>(true).All(value => value.particleCount == 0 && !value.isPlaying));
            fixture.Rendering.RegisterItemRendering(fixture.Item);
            fixture.Effect = fixture.Rendering.PlayItemVisualEffect(fixture.Item, "WetStatusEffect");
            result["newLeaseOwnerBound"] = fixture.Effect != null && fixture.Effect.activeInHierarchy &&
                fixture.Rendering.GetOwnerEffect(fixture.Item.transform, "WetStatusEffect") == fixture.Effect;
            fixture.Rendering.StopItemVisualEffect(fixture.Item, "WetStatusEffect");
            result["newLeaseReturned"] = fixture.Effect != null && !fixture.Effect.activeSelf;
            fixture.Rendering.UnregisterItemRendering(fixture.Item);
            result["unregisteredBeforeDirectFeatureCalls"] = !fixture.Rendering.IsItemRenderingRegistered(fixture.Item);
            fixture.Rendering.SetItemHighlight(fixture.Item, true);
            fixture.Effect = fixture.Rendering.PlayItemVisualEffect(fixture.Item, "WetStatusEffect");
            result["directFeatureCallsActive"] = fixture.Rendering.IsItemHighlighted(fixture.Item) &&
                fixture.Effect != null && fixture.Effect.activeInHierarchy;
            fixture.Items.DespawnItem(fixture.Item, false);
            result["directFeatureLifecycleCleaned"] = !fixture.Rendering.IsItemRenderingRegistered(fixture.Item) &&
                !fixture.Rendering.IsItemHighlighted(fixture.Item) && fixture.Effect != null && !fixture.Effect.activeSelf;
        }
        finally { fixture.Dispose(); }
        result["itemReleased"] = fixture.Item == null || fixture.Item.IsInPool || fixture.Item.DestructionHandled;
        result["ownEffectReleased"] = fixture.Effect == null || !fixture.Effect.activeSelf;
        yield return null;
        result["attachmentReleased"] = fixture.Attachment == null;
        complete(result);
    }
    #endregion
}
