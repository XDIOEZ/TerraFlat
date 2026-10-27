using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class GameplayTestSystemProbe
{
    #region 统一渲染入口规则采样

    private static void RegisterUnifiedRenderingOperations(Dictionary<string, Func<JObject, JObject>> operations)
        => operations.Add("rendering/unified_manager", CaptureUnifiedItemRenderingManager);

    // 独立物品只调用正式表现入口，采样真实绑定与资源状态，不进入世界模拟。
    private static JObject CaptureUnifiedItemRenderingManager(JObject args)
    {
        using var fixture = new UnifiedItemRenderingRuleFixture();
        WorldRenderingManager rendering = fixture.Rendering;
        GameItem item = fixture.Item;
        int beforeCount = rendering.RegisteredItemCount;
        var samples = new JArray();
        void Sample(string step) => samples.Add(fixture.Capture(step, beforeCount));

        bool nullRejected = !rendering.RegisterItemRendering(null);
        bool registered = rendering.RegisterItemRendering(item);
        Sample("registered");
        bool repeated = rendering.RegisterItemRendering(item);
        Sample("registered_twice");
        rendering.GetSortingKey(WorldSortingManager.WorldItemCategory, out int layer, out int order);
        var independent = new GameObject("IndependentSortingRenderer");
        independent.transform.SetParent(fixture.Services.transform, false);
        SpriteRenderer independentRenderer = independent.AddComponent<SpriteRenderer>();
        rendering.ApplyRendererSorting(independentRenderer, WorldSortingManager.WorldItemCategory, 11);
        bool independentSorting = independentRenderer.sortingLayerID == layer && independentRenderer.sortingOrder == order + 11;
        WorldSortingMember member = item.GetComponent<WorldSortingMember>();
        bool registeredSorting = member != null && member.Category == WorldSortingManager.WorldItemCategory &&
            fixture.Sprite.sortingLayerID == layer && fixture.Sprite.sortingOrder == order;

        rendering.SetItemInWater(item, true);
        Sample("in_water");
        rendering.SetItemShadowEnabled(item, false);
        Sample("shadow_disabled");
        item.NotifyRuntimeStructureChanged();
        rendering.RefreshItemRendering(item);
        Sample("structure_refreshed");
        rendering.SetItemShadowEnabled(item, true);
        Sample("shadow_reenabled");

        rendering.SetItemHighlight(item, true);
        Sample("manual_highlight");
        rendering.SetInteractionTargetHighlight(fixture.InteractionA, true);
        rendering.SetInteractionTargetHighlight(fixture.InteractionA, true);
        Sample("interaction_a");
        rendering.SetItemHighlight(item, false);
        Sample("manual_cleared");
        rendering.SetInteractionTargetHighlight(fixture.InteractionB, true);
        Sample("interaction_b");
        rendering.SetInteractionTargetHighlight(fixture.InteractionA, false);
        Sample("interaction_a_cleared");
        rendering.SetInteractionTargetHighlight(fixture.InteractionB, false);
        Sample("all_highlights_cleared");

        rendering.SetItemHighlight(item, true);
        rendering.SetItemShadowEnabled(item, false);
        rendering.UnregisterItemRendering(item);
        rendering.UnregisterItemRendering(item);
        Sample("unregistered_twice");
        rendering.RegisterItemRendering(item);
        Sample("registered_again");

        rendering.SetItemHighlight(item, true);
        rendering.SetItemInWater(item, true);
        rendering.SetItemShadowEnabled(item, false);
        item.PrepareForDespawn(false);
        item.PoolMarker.InPool = true;
        item.NotifyReturnedToPool();
        Sample("returned_to_pool");
        bool pooledRejected = !rendering.RegisterItemRendering(item);
        fixture.Root.SetActive(false);
        item.PrepareForPoolReuse();
        item.PoolMarker.InPool = false;
        fixture.Root.SetActive(true);
        rendering.RegisterItemRendering(item);
        Sample("taken_from_pool");

        rendering.SetItemHighlight(item, true);
        UnityEngine.Object.DestroyImmediate(fixture.Root);
        Sample("destroyed");
        var result = new JObject
        {
            ["registered"] = registered, ["repeatedRegistered"] = repeated,
            ["nullRejected"] = nullRejected, ["pooledRejected"] = pooledRejected,
            ["registeredSorting"] = registeredSorting, ["independentSorting"] = independentSorting,
            ["earlyMemberLayer"] = fixture.EarlyMemberLayer, ["earlyMemberOrder"] = fixture.EarlyMemberOrder,
            ["earlyExpectedLayer"] = fixture.EarlyExpectedLayer, ["earlyExpectedOrder"] = fixture.EarlyExpectedOrder,
            ["earlyMemberPreservedFacade"] = fixture.EarlyMemberPreservedFacade,
            ["samples"] = samples
        };
        fixture.Dispose();
        result["globalSortingRestored"] = fixture.GlobalSortingRestored;
        return result;
    }

    private sealed class UnifiedItemRenderingRuleFixture : IDisposable
    {
        public readonly GameObject Services;
        public readonly GameObject Root;
        public readonly GameItem Item;
        public readonly SpriteRenderer Sprite;
        public readonly Component InteractionA;
        public readonly Component InteractionB;
        public readonly WorldRenderingManager Rendering;
        public readonly int EarlyMemberLayer;
        public readonly int EarlyMemberOrder;
        public readonly int EarlyExpectedLayer;
        public readonly int EarlyExpectedOrder;
        public readonly bool EarlyMemberPreservedFacade;
        private readonly ActorShadowManager contactShadows;
        private readonly WorldShadowProjectionManager sunShadows;
        private readonly Texture2D texture;
        private readonly UnityEngine.Sprite sprite;
        private readonly TransparencySortMode originalSortMode;
        private readonly Vector3 originalSortAxis;
        private bool disposed;

        public bool GlobalSortingRestored => GraphicsSettings.transparencySortMode == originalSortMode &&
            GraphicsSettings.transparencySortAxis == originalSortAxis;

        public UnifiedItemRenderingRuleFixture()
        {
            originalSortMode = GraphicsSettings.transparencySortMode;
            originalSortAxis = GraphicsSettings.transparencySortAxis;
            try
            {
                Services = new GameObject("[LuaUnifiedRenderingServices]") { hideFlags = HideFlags.HideAndDontSave };
                if (WorldSortingManager.GetInstance() == null) Services.AddComponent<WorldSortingManager>();
                WorldRenderingManager beforeFacade = WorldRenderingManager.ExistingInstance;
                var earlyMemberRoot = new GameObject("EarlyStandaloneSortingMember");
                earlyMemberRoot.transform.SetParent(Services.transform, false);
                SpriteRenderer earlyRenderer = earlyMemberRoot.AddComponent<SpriteRenderer>();
                earlyRenderer.sortingOrder = -731;
                earlyMemberRoot.AddComponent<WorldSortingMember>().Bind(WorldSortingManager.GroundPreviewCategory, earlyRenderer);
                EarlyMemberLayer = earlyRenderer.sortingLayerID;
                EarlyMemberOrder = earlyRenderer.sortingOrder;
                WorldSortingManager.GetResourceSortingKey(WorldSortingManager.GroundPreviewCategory,
                    out int earlyLayer, out int earlyOrder);
                EarlyExpectedLayer = earlyLayer;
                EarlyExpectedOrder = earlyOrder;
                EarlyMemberPreservedFacade = ReferenceEquals(WorldRenderingManager.ExistingInstance, beforeFacade);
                contactShadows = ActorShadowManager.GetInstance() ?? Services.AddComponent<ActorShadowManager>();
                sunShadows = UnityEngine.Object.FindFirstObjectByType<WorldShadowProjectionManager>(FindObjectsInactive.Include) ??
                    Services.AddComponent<WorldShadowProjectionManager>();
                Rendering = WorldRenderingManager.EnsureForOwner(Services.transform);
                Rendering.AttachSortingRenderingBackend(WorldSortingManager.GetInstance());
                Rendering.AttachContactShadowRenderingBackend(contactShadows);
                Rendering.AttachSunShadowRenderingBackend(sunShadows);
                Root = new GameObject("[LuaUnifiedRenderingItem]") { hideFlags = HideFlags.HideAndDontSave };
                Root.SetActive(false);
                Item = Root.AddComponent<GameItem>();
                Item.BindData(new Data_GeneralItem
                {
                    IDName = "probe:unified_rendering", Tags = new List<string> { Tag.Plant },
                    Stack = new ItemStack { Amount = 1f, CanBePickedUp = false }
                });
                Sprite = Root.AddComponent<SpriteRenderer>();
                texture = new Texture2D(2, 2);
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * .5f, 2f);
                Sprite.sprite = sprite;
                Item.Sprite = Sprite;
                InteractionA = Root.AddComponent<BoxCollider2D>();
                var child = new GameObject("SecondInteractionSource");
                child.transform.SetParent(Root.transform, false);
                InteractionB = child.AddComponent<BoxCollider2D>();
                Root.SetActive(true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public JObject Capture(string step, int baseline)
        {
            InteractionTargetOutline outline = Item != null ? Item.GetComponent<InteractionTargetOutline>() : null;
            return new JObject
            {
                ["step"] = step,
                ["registeredCount"] = Rendering.RegisteredItemCount - baseline,
                ["registered"] = Rendering.IsItemRenderingRegistered(Item),
                ["shadowEnabled"] = Rendering.IsItemShadowEnabled(Item),
                ["inWater"] = Rendering.IsItemInWater(Item),
                ["contactBackendEnabled"] = contactShadows != null && contactShadows.isActiveAndEnabled,
                ["sunBackendEnabled"] = sunShadows != null && sunShadows.isActiveAndEnabled,
                ["contactBound"] = Rendering.IsItemContactShadowBound(Item),
                ["sunBound"] = Rendering.IsItemSunShadowBound(Item),
                ["contactInWater"] = contactShadows != null && contactShadows.IsItemInWater(Item),
                ["highlighted"] = Rendering.IsItemHighlighted(Item),
                ["manuallyHighlighted"] = outline != null && outline.IsManuallyHighlighted,
                ["interactionRequests"] = outline != null ? outline.InteractionHighlightRequestCount : 0,
                ["outlineControllers"] = Item != null ? Item.GetComponents<InteractionTargetOutline>().Length : 0
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (Rendering != null) Rendering.UnregisterItemRendering(Item); }
            finally
            {
                try { if (Root != null) UnityEngine.Object.DestroyImmediate(Root); }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(Services);
                    UnityEngine.Object.DestroyImmediate(sprite);
                    UnityEngine.Object.DestroyImmediate(texture);
                    GraphicsSettings.transparencySortMode = originalSortMode;
                    GraphicsSettings.transparencySortAxis = originalSortAxis;
                }
            }
        }
    }

    #endregion
}
