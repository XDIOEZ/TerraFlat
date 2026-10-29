using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class GameplayTestSystemProbe
{
    #region 统一渲染入口规则采样

    private sealed class SortingDomainRankProbe
    {
        internal GameObject Source;
        internal WorldPresentationProjection Projection;
        internal SortingGroup SourceGroup;
        internal SortingGroup DisplayGroup => Projection.GetComponent<SortingGroup>();
        internal SpriteRenderer[] SourceParts;
        internal SpriteRenderer[] DisplayParts => new[]
        {
            Projection.transform.Find("Part_0").GetComponent<SpriteRenderer>(),
            Projection.transform.Find("Part_1").GetComponent<SpriteRenderer>()
        };
    }

    private static void RegisterUnifiedRenderingOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/unified_manager", CaptureUnifiedItemRenderingManager);
        operations.Add("rendering/item_sorting_configuration", CaptureItemConfiguredWorldSorting);
        operations.Add("rendering/presentation_sorting_domain", CapturePresentationSortingDomain);
        operations.Add("rendering/cold_projection_initialization", CaptureColdProjectionResourceInitialization);
        operations.Add("rendering/water_surface_palette", CaptureSharedWaterSurfaceMaterialPalette);
    }

    private static SortingDomainRankProbe CreateSortingDomainRankProbe(Transform sourceParent,
        Transform projectionParent, WorldPresentationSortingDomain domain, Matrix4x4 projection,
        string name, Vector3 position, int layerId, int rawOrder)
    {
        var source = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        source.transform.SetParent(sourceParent, false);
        source.transform.position = position;
        var group = source.AddComponent<SortingGroup>();
        group.sortAtRoot = true;
        group.sortingLayerID = layerId;
        group.sortingOrder = rawOrder;
        var parts = new SpriteRenderer[2];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = new GameObject("Part_" + i) { hideFlags = HideFlags.HideAndDontSave };
            part.transform.SetParent(source.transform, false);
            part.transform.localPosition = i == 0 ? Vector3.down * .1f : Vector3.up * .1f;
            parts[i] = part.AddComponent<SpriteRenderer>();
            parts[i].sortingLayerID = layerId;
            parts[i].sortingOrder = rawOrder + (i == 0 ? -3 : 3);
        }
        var result = new SortingDomainRankProbe
        {
            Source = source,
            SourceGroup = group,
            SourceParts = parts,
            Projection = WorldPresentationProjection.Create(source.transform, projectionParent)
        };
        result.Projection.SetSortingDomain(domain);
        result.Projection.Synchronize(projection, true);
        return result;
    }

    private static void ApplySortingDomainRankProbeKey(WorldPresentationSortingDomain domain,
        SortingDomainRankProbe probe)
    {
        Material material = probe.SourceParts[0].sharedMaterial;
        domain.ApplyLocalSortingGroupKey(probe.DisplayGroup, probe.SourceGroup.sortingLayerID,
            probe.SourceGroup.sortingOrder, material != null ? material.renderQueue : 3000,
            probe.DisplayGroup.transform.position.y);
    }

    // 使用正式定义装配、原位更新和排序入口采样，不在夹具内重算渲染优先级。
    private static JObject CaptureItemConfiguredWorldSorting(JObject args)
    {
        using var fixture = new UnifiedItemRenderingRuleFixture();
        GameItem item = fixture.Item;
        WorldRenderingManager rendering = fixture.Rendering;
        GameRes resources = GameRes.ExistingInstance ?? fixture.Services.AddComponent<GameRes>();
        var samples = new JArray();
        RuntimeItemDefinition Definition(ItemVisualDefinitionDto visual) => new(
            item.itemData.IDName, "", null, item.itemData, visual, null, null, null,
            fixture.Sprite.sprite, null, null, null, null);
        var current = Definition(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 0 });
        void Sample(string step)
        {
            WorldSortingManager.ReadExternalRendererKey(fixture.Sprite, out int layer, out int order);
            bool depthBatch = BuildingDepthMeshBridge.TryResolveConfiguredDepthSorting(fixture.Sprite, out bool groundBatch);
            samples.Add(new JObject { ["step"] = step, ["layer"] = SortingLayer.IDToName(layer),
                ["layerValue"] = SortingLayer.GetLayerValueFromID(layer), ["order"] = order,
                ["y"] = item.transform.position.y, ["depthBatch"] = depthBatch, ["groundBatch"] = groundBatch });
        }
        void Refresh(ItemVisualDefinitionDto visual, string step)
        {
            RuntimeItemDefinition next = Definition(visual);
            ItemDefinitionRuntime.RefreshLiveConfiguration(item, current, next);
            current = next;
            rendering.RefreshItemRendering(item);
            Sample(step);
        }

        fixture.Root.SetActive(false);
        ItemDefinitionRuntime.ConfigureInstance(resources, current, item, item.itemData);
        fixture.Root.AddComponent<WorldSortingMember>().Bind(WorldSortingManager.BuildingCategory, fixture.Sprite, item);
        fixture.Root.SetActive(true);
        rendering.RegisterItemRendering(item);
        bool definitionBound = ReferenceEquals(item.VisualDefinition, current.Visual);
        item.transform.position = Vector3.up * 100f;
        Sample("floor_north");
        item.transform.position = Vector3.down * 100f;
        rendering.RefreshItemRendering(item);
        Sample("floor_south");

        var group = fixture.Root.AddComponent<SortingGroup>();
        rendering.RefreshItemRendering(item);
        fixture.Sprite.sortingOrder = 7;
        rendering.RefreshItemRendering(item);
        Sample("grouped_floor");
        bool relativeOrderPreserved = fixture.Sprite.sortingOrder == 7;
        Refresh(new ItemVisualDefinitionDto { SortingLayerName = "Player", SortingOrder = 147 }, "custom_order");
        using (var aquatic = new AquaticActorPresentation(item, fixture.Sprite))
        {
            aquatic.SetUnderwater(true);
            Sample("aquatic_submerged");
            rendering.RefreshItemRendering(item);
            Sample("aquatic_refreshed");
            aquatic.SetCarried(true, .5f);
            aquatic.Tick(true, 0f);
            Sample("aquatic_carried");
            aquatic.SetCarried(false, 0f);
            aquatic.Tick(true, 0f);
            Sample("aquatic_returned");
            aquatic.SetUnderwater(false);
            Sample("aquatic_resurfaced");
        }
        // 水生表现清理后恢复原测试夹具的建筑类别。
        item.GetComponent<WorldSortingMember>().Bind(WorldSortingManager.BuildingCategory, fixture.Sprite, item);
        item.SetInHand(true);
        bool heldRestored = group.sortingOrder != 147;
        item.SetInHand(false);
        Sample("returned_from_hand");
        fixture.Root.SetActive(false);
        fixture.Root.SetActive(true);
        Sample("reenabled");
        Refresh(new ItemVisualDefinitionDto { SortingOrder = 99 }, "order_only");
        Refresh(new ItemVisualDefinitionDto { SortingLayerName = "Default" }, "layer_only");
        Refresh(null, "defaults_restored");
        bool removedConfigurationCleared = item.VisualDefinition == null;
        Refresh(new ItemVisualDefinitionDto { SortingLayerName = "Player", SortingOrder = short.MinValue }, "minimum_order");
        Refresh(new ItemVisualDefinitionDto { SortingLayerName = "Player", SortingOrder = short.MaxValue }, "maximum_order");
        var despawnAquatic = new AquaticActorPresentation(item, fixture.Sprite);
        despawnAquatic.SetUnderwater(true);
        item.PrepareForDespawn(false);
        WorldSortingManager.ReadExternalRendererKey(fixture.Sprite, out int despawnLayer, out int despawnOrder);
        item.GetComponent<WorldSortingMember>().SetEnvironmentSortingOverride(AquaticActorPresentation.SortingCategory);
        WorldSortingManager.ReadExternalRendererKey(fixture.Sprite, out int afterLateLayer, out int afterLateOrder);
        despawnAquatic.Dispose();
        WorldSortingManager.ReadExternalRendererKey(fixture.Sprite, out int afterDisposeLayer, out int afterDisposeOrder);
        fixture.Root.SetActive(false);
        fixture.Root.SetActive(true);
        WorldSortingManager.ReadExternalRendererKey(fixture.Sprite, out int afterEnableLayer, out int afterEnableOrder);
        bool aquaticDespawnReleased = despawnLayer == afterDisposeLayer && despawnOrder == afterDisposeOrder &&
            despawnLayer == afterLateLayer && despawnOrder == afterLateOrder && despawnLayer == afterEnableLayer &&
            despawnOrder == afterEnableOrder && !rendering.IsItemRenderingRegistered(item);
        fixture.Root.SetActive(false);
        item.PrepareForPoolReuse();
        bool pooledConfigurationCleared = item.VisualDefinition == null;
        current = Definition(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 0 });
        ItemDefinitionRuntime.ConfigureInstance(resources, current, item, item.itemData);
        fixture.Root.SetActive(true);
        rendering.RegisterItemRendering(item);
        Sample("reused_as_floor");

        bool hadDefinition = resources.ItemDefinitions.TryGetValue(current.Id, out RuntimeItemDefinition previous);
        DroppedItemVisual dropped;
        bool droppedNullDataRejected = false, droppedNullSpriteRejected = false, droppedNullSpriteDataRejected = false;
        bool droppedMissingResourcesChecked = GameRes.ExistingInstance == null, droppedMissingResourcesRejected = false;
        try
        {
            resources.ItemDefinitions[current.Id] = current;
            dropped = DroppedItemVisual.Resolve(item.itemData, fixture.Sprite.sprite, resources);
            try { DroppedItemVisual.Resolve(null, fixture.Sprite.sprite, resources); }
            catch (ArgumentNullException) { droppedNullDataRejected = true; }
            try { DroppedItemVisual.Resolve(item.itemData, null, resources); }
            catch (ArgumentNullException) { droppedNullSpriteRejected = true; }
            try { DroppedItemVisual.ResolveSprite(null, resources); }
            catch (ArgumentNullException) { droppedNullSpriteDataRejected = true; }
            if (droppedMissingResourcesChecked)
            {
                try { DroppedItemVisual.Resolve(item.itemData, fixture.Sprite.sprite); }
                catch (InvalidOperationException error)
                { droppedMissingResourcesRejected = error.Message.Contains(item.itemData.IDName); }
            }
        }
        finally
        {
            if (hadDefinition) resources.ItemDefinitions[current.Id] = previous;
            else resources.ItemDefinitions.Remove(current.Id);
        }
        bool Rejects(ItemVisualDefinitionDto visual)
        {
            try { Definition(visual); }
            catch (System.IO.InvalidDataException) { return true; }
            return false;
        }
        bool GroundCoverAccepts(ItemVisualDefinitionDto visual)
        {
            try { ItemDefinitionCatalogLoader.ValidateGroundCoverWorldSortingConfiguration("probe:ground_cover", visual); }
            catch (System.IO.InvalidDataException) { return false; }
            return true;
        }
        bool NaturalSortingMatches(ItemVisualDefinitionDto visual, string layer, int order)
        {
            var definition = new RuntimeItemDefinition(item.itemData.IDName, "", null, item.itemData, visual,
                null, null, null, fixture.Sprite.sprite, null, null, null, null,
                material: Resources.Load<Material>("Config/WorldModel/BRG/ChunkMesh-Water-Lit"), entityRuntime: "resource");
            if (!NaturalEntityEcsProfileCompiler.TryCompile(definition, out NaturalEntityEcsProfile profile, out string reason))
                throw new InvalidOperationException("自然物排序采样无法编译：" + reason);
            return SortingLayer.IDToName(profile.SortingLayerId) == layer && profile.SortingOrder == order;
        }
        var catalog = new JArray();
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (definition.Id is "ShipFloor_Iron" or "ShipFloor_Refractory" or "ShipFloor_Iron_Summoner" or "Stick_Wood" or "RailTrack" or "ShipFiber")
                catalog.Add(new JObject { ["id"] = definition.Id, ["layer"] = definition.Visual?.SortingLayerName,
                    ["order"] = definition.Visual?.SortingOrder, ["material"] = definition.Visual?.MaterialAddress });
        var actors = new JArray();
        foreach (ItemDefinitionDto definition in ActorDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (!definition.Abstract)
                actors.Add(new JObject { ["id"] = definition.Id, ["layer"] = definition.Visual?.SortingLayerName,
                    ["order"] = definition.Visual?.SortingOrder });
        rendering.GetSortingKey("player", out int playerLayer, out int playerOrder);
        return new JObject { ["samples"] = samples, ["catalog"] = catalog, ["actors"] = actors, ["definitionBound"] = definitionBound,
            ["depthRows"] = CaptureDepthBatchConfiguredSorting(fixture.Sprite.sprite, playerLayer, playerOrder),
            ["groundFacilityOverlay"] = CaptureProjectedGroundFacilityOverlayOrdering(fixture.Sprite.sprite, fixture.Services.transform),
            ["nestedSorting"] = CaptureNestedSortingGroupPriority(playerLayer, playerOrder),
            ["aiecs"] = CaptureAiecsConfiguredSorting(playerLayer, playerOrder),
            ["groundCoverDefault"] = GroundCoverAccepts(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 0 }),
            ["groundCoverFallback"] = GroundCoverAccepts(null),
            ["groundCoverWrongLayerRejected"] = !GroundCoverAccepts(new ItemVisualDefinitionDto { SortingLayerName = "Player", SortingOrder = 100 }),
            ["groundCoverWrongOrderRejected"] = !GroundCoverAccepts(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 1 }),
            ["naturalCustom"] = NaturalSortingMatches(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 37 }, "Default", 37),
            ["naturalFallback"] = NaturalSortingMatches(null, "Player", 100),
            ["relativeOrderPreserved"] = relativeOrderPreserved, ["heldRestored"] = heldRestored,
            ["aquaticDespawnReleased"] = aquaticDespawnReleased,
            ["removedConfigurationCleared"] = removedConfigurationCleared, ["pooledConfigurationCleared"] = pooledConfigurationCleared,
            ["droppedLayer"] = SortingLayer.IDToName(dropped.Layer), ["droppedOrder"] = dropped.Order,
            ["droppedNullDataRejected"] = droppedNullDataRejected, ["droppedNullSpriteRejected"] = droppedNullSpriteRejected,
            ["droppedNullSpriteDataRejected"] = droppedNullSpriteDataRejected,
            ["droppedMissingResourcesChecked"] = droppedMissingResourcesChecked,
            ["droppedMissingResourcesRejected"] = droppedMissingResourcesRejected,
            ["playerLayerValue"] = SortingLayer.GetLayerValueFromID(playerLayer), ["playerOrder"] = playerOrder,
            ["rejectsUnknownLayer"] = Rejects(new ItemVisualDefinitionDto { SortingLayerName = "probe:missing-layer" }),
            ["rejectsEmptyLayer"] = Rejects(new ItemVisualDefinitionDto { SortingLayerName = "" }),
            ["rejectsOrderUnderflow"] = Rejects(new ItemVisualDefinitionDto { SortingOrder = short.MinValue - 1 }),
            ["rejectsOrderOverflow"] = Rejects(new ItemVisualDefinitionDto { SortingOrder = short.MaxValue + 1 }) };
    }

    // 停用父节点下先投影再激活，检查真实材质参数不依赖 Awake 的执行顺序。
    private static JObject CaptureColdProjectionResourceInitialization(JObject args)
    {
        var source = new GameObject("ColdProjectionSource") { hideFlags = HideFlags.HideAndDontSave };
        var parent = new GameObject("ColdProjectionParent") { hideFlags = HideFlags.HideAndDontSave };
        parent.SetActive(false);
        WorldPresentationProjection projection = null;
        try
        {
            SpriteRenderer renderer = source.AddComponent<SpriteRenderer>();
            int marker = Shader.PropertyToID("_ColdProjectionProbe");
            var block = new MaterialPropertyBlock();
            block.SetFloat(marker, 37f);
            renderer.SetPropertyBlock(block);
            projection = WorldPresentationProjection.Create(source.transform, parent.transform);
            projection.Synchronize(Matrix4x4.identity, true);
            SpriteRenderer target = projection.GetComponent<SpriteRenderer>();
            target.GetPropertyBlock(block);
            float before = block.GetFloat(marker);
            bool initiallyInactive = !projection.gameObject.activeInHierarchy;
            parent.SetActive(true);
            projection.Synchronize(Matrix4x4.Translate(Vector3.right), true);
            target.GetPropertyBlock(block);
            float after = block.GetFloat(marker);
            float projected = block.GetFloat(Shader.PropertyToID("_WorldPresentationProjectionEnabled"));
            projection.Release();
            return new JObject { ["initiallyInactive"] = initiallyInactive, ["before"] = before,
                ["after"] = after, ["projected"] = projected, ["released"] = projection.IsReleased,
                ["sourceRestored"] = !renderer.forceRenderingOff };
        }
        finally
        {
            if (projection != null) projection.Release();
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(parent);
        }
    }

    // 通过正式投影和排序域采样原生 Renderer 的域归属及局部键保留。
    private static JObject CapturePresentationSortingDomain(JObject args)
    {
        using var fixture = new UnifiedItemRenderingRuleFixture();
        var sourceA = new GameObject("DomainA_Source") { hideFlags = HideFlags.HideAndDontSave };
        var sourceB = new GameObject("DomainB_Source") { hideFlags = HideFlags.HideAndDontSave };
        var sharedSource = new GameObject("SharedDomain_Source") { hideFlags = HideFlags.HideAndDontSave };
        var background = new GameObject("DomainBackground") { hideFlags = HideFlags.HideAndDontSave };
        sourceA.transform.SetParent(fixture.Services.transform, false);
        sourceB.transform.SetParent(fixture.Services.transform, false);
        sharedSource.transform.SetParent(fixture.Services.transform, false);
        background.transform.SetParent(fixture.Services.transform, false);
        sourceA.transform.position = new Vector3(3f, 4f, 0f);
        sourceB.transform.position = new Vector3(-2f, 1f, 0f);
        sharedSource.transform.position = new Vector3(6f, -3f, 0f);

        SortingGroup sourceParentGroup = sourceA.AddComponent<SortingGroup>();
        sourceParentGroup.sortAtRoot = true;
        sourceParentGroup.sortingLayerID = SortingLayer.NameToID("Player");
        sourceParentGroup.sortingOrder = 31;
        SpriteRenderer directSprite = sourceA.AddComponent<SpriteRenderer>();
        directSprite.sortingLayerID = SortingLayer.NameToID("Default");
        directSprite.sortingOrder = 44;
        var nestedRoot = new GameObject("NestedRoot");
        nestedRoot.transform.SetParent(sourceA.transform, false);
        nestedRoot.transform.localPosition = Vector3.up * 2f;
        SortingGroup sourceNestedGroup = nestedRoot.AddComponent<SortingGroup>();
        sourceNestedGroup.sortAtRoot = true;
        sourceNestedGroup.sortingLayerID = SortingLayer.NameToID("MechanicalShaft");
        sourceNestedGroup.sortingOrder = 44;
        var groupedNode = new GameObject("GroupedSprite");
        groupedNode.transform.SetParent(nestedRoot.transform, false);
        SpriteRenderer groupedSprite = groupedNode.AddComponent<SpriteRenderer>();
        groupedSprite.sortingLayerID = SortingLayer.NameToID("Paticle");
        groupedSprite.sortingOrder = 44;
        groupedSprite.forceRenderingOff = true;
        var meshNode = new GameObject("MeshSource");
        meshNode.transform.SetParent(sourceA.transform, false);
        meshNode.transform.localPosition = Vector3.down * 2f;
        meshNode.AddComponent<MeshFilter>();
        MeshRenderer sourceMesh = meshNode.AddComponent<MeshRenderer>();
        sourceMesh.sortingLayerID = SortingLayer.NameToID("Player");
        sourceMesh.sortingOrder = 73;
        var particleNode = new GameObject("ParticleSource");
        particleNode.transform.SetParent(sourceA.transform, false);
        particleNode.AddComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer sourceParticles = particleNode.GetComponent<ParticleSystemRenderer>();
        sourceParticles.sortingLayerID = SortingLayer.NameToID("HeldFood");
        sourceParticles.sortingOrder = -11;
        SpriteRenderer secondSprite = sourceB.AddComponent<SpriteRenderer>();
        secondSprite.sortingLayerID = SortingLayer.NameToID("Water");
        secondSprite.sortingOrder = -9;
        SpriteRenderer sharedSprite = sharedSource.AddComponent<SpriteRenderer>();
        sharedSprite.sortingLayerID = SortingLayer.NameToID("Default");
        sharedSprite.sortingOrder = 5;

        SortingGroup backgroundGroup = background.AddComponent<SortingGroup>();
        backgroundGroup.sortAtRoot = true;
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.WorldBackgroundCategory,
            out int backgroundLayer, out int backgroundOrder);
        backgroundGroup.sortingLayerID = backgroundLayer;
        backgroundGroup.sortingOrder = backgroundOrder;
        SpriteRenderer backgroundSprite = background.AddComponent<SpriteRenderer>();

        string worldA = "probe:sorting-domain:a", worldB = "probe:sorting-domain:b";
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.ProjectedWorldCategory,
            out int expectedDomainLayer, out int expectedDomainOrder);
        Matrix4x4 matrixA = Matrix4x4.Translate(new Vector3(10f, 20f, 0f));
        Matrix4x4 matrixB = Matrix4x4.TRS(new Vector3(-8f, 5f, 0f), Quaternion.Euler(0f, 0f, 31f), Vector3.one);
        WorldPresentationSortingDomain domainA = null, domainB = null;
        WorldPresentationProjection projectionA = null, projectionB = null, sharedProjectionA = null, sharedProjectionB = null;
        bool keyMatchesCategory = false, backgroundBehind = false, distinctDomains = false;
        bool allRenderersInside = false, nestedRootKeyApplied = false, localKeysPreserved = false;
        bool rendererTypesProjected = false, yAndPositionProjectedOnce = false, hiddenSourcesSuppressed = false;
        bool scheduledPoseImmediate = false, scheduledRendererDeferred = false, scheduledLatestRenderData = false;
        bool sourceRestored = false, registryReleased = false, domainsDisposed = false;
        bool domainDisposeRestoresSources = false, domainDisposeHidesCopies = false;
        bool sharedSourceHeldUntilLastProjection = false, sharedSourceRestoredAfterLastProjection = false;
        bool sameRawYRankedAndSwapped = false, domainsRankIndependently = false;
        bool fixedFloorBelowActor = false, sourceGroupRemainsAtomic = false, hideKeepsRank = false;
        bool moveRestoresRawRank = false, destinationAndReentryRerank = false, removeRestoresRawRank = false;
        bool releaseRestoresRawRank = false, domainDisposeRestoresRawRank = false;
        var rankProbeSources = new List<GameObject>();
        var rankProbeProjections = new List<WorldPresentationProjection>();
        var detachedRankTargets = new List<GameObject>();
        int copiedRendererCount = 0;
        try
        {
            domainA = WorldPresentationSortingDomain.Create(fixture.Services.transform, worldA,
                WorldSortingManager.ProjectedWorldCategory);
            domainB = WorldPresentationSortingDomain.Create(fixture.Services.transform, worldB,
                WorldSortingManager.ProjectedWorldCategory);
            domainA.Synchronize(matrixA, true);
            domainB.Synchronize(matrixB, true);
            WorldPresentationTransformRegistry.Set(worldA, matrixA, true, domainA);
            WorldPresentationTransformRegistry.Set(worldB, matrixB, true, domainB);
            projectionA = WorldPresentationProjection.Create(sourceA.transform, fixture.Services.transform);
            projectionB = WorldPresentationProjection.Create(sourceB.transform, fixture.Services.transform);
            projectionA.SetSortingDomain(domainA);
            projectionB.SetSortingDomain(domainB);
            projectionA.Synchronize(matrixA, true);
            // 外部乘员已经在画面坐标时使用单位矩阵，域本身仍可承载船姿变换。
            projectionB.Synchronize(Matrix4x4.identity, true);
            sharedProjectionA = WorldPresentationProjection.Create(sharedSource.transform, fixture.Services.transform);
            sharedProjectionB = WorldPresentationProjection.Create(sharedSource.transform, fixture.Services.transform);
            sharedProjectionA.SetSortingDomain(domainA);
            sharedProjectionB.SetSortingDomain(domainB);
            sharedProjectionA.Synchronize(matrixA, true);
            sharedProjectionB.Synchronize(Matrix4x4.identity, true);

            SortingGroup domainGroupA = domainA.Root.GetComponent<SortingGroup>();
            SortingGroup domainGroupB = domainB.Root.GetComponent<SortingGroup>();
            SpriteRenderer projectedSprite = projectionA.GetComponent<SpriteRenderer>();
            Transform escapedNestedRoot = domainA.Root.Find("NestedRoot");
            SortingGroup projectedNestedGroup = escapedNestedRoot.GetComponent<SortingGroup>();
            SpriteRenderer projectedGroupedSprite = escapedNestedRoot.Find("GroupedSprite").GetComponent<SpriteRenderer>();
            MeshRenderer projectedMesh = projectionA.transform.Find("MeshSource").GetComponent<MeshRenderer>();
            MeshRenderer projectedParticles = projectionA.transform.Find("ParticleSource/ProjectedParticles").GetComponent<MeshRenderer>();
            SpriteRenderer projectedSecondSprite = projectionB.GetComponent<SpriteRenderer>();
            Renderer[] projectedRenderersA = projectionA.GetComponentsInChildren<Renderer>(true);
            copiedRendererCount = projectedRenderersA.Length + (projectedGroupedSprite != null ? 1 : 0) +
                projectionB.GetComponentsInChildren<Renderer>(true).Length;

            keyMatchesCategory = domainGroupA.sortingLayerID == expectedDomainLayer &&
                domainGroupA.sortingOrder == expectedDomainOrder && domainGroupB.sortingLayerID == expectedDomainLayer &&
                domainGroupB.sortingOrder == expectedDomainOrder;
            WorldVisualSortingKey projectedKey = WorldVisualSortingKey.FromRenderer(projectedSprite);
            WorldVisualSortingKey backgroundKey = WorldVisualSortingKey.FromRenderer(backgroundSprite);
            backgroundBehind = projectedKey.CompareTo(backgroundKey) > 0;
            distinctDomains = domainA.Root != domainB.Root && domainA.WorldKey != domainB.WorldKey &&
                projectionA.transform.IsChildOf(domainA.Root) && !projectionA.transform.IsChildOf(domainB.Root) &&
                projectionB.transform.IsChildOf(domainB.Root) && !projectionB.transform.IsChildOf(domainA.Root) &&
                WorldVisualSortingKey.FromRenderer(projectedSprite).Group == domainGroupA.GetInstanceID() &&
                WorldVisualSortingKey.FromRenderer(projectedSecondSprite).Group == domainGroupB.GetInstanceID();
            allRenderersInside = projectedRenderersA.Length == 3 && projectedGroupedSprite.transform.IsChildOf(domainA.Root);
            foreach (Renderer renderer in projectedRenderersA)
            {
                WorldVisualSortingKey key = WorldVisualSortingKey.FromRenderer(renderer);
                allRenderersInside &= renderer.transform.IsChildOf(domainA.Root) &&
                    key.Layer == SortingLayer.GetLayerValueFromID(expectedDomainLayer) &&
                    key.Order == expectedDomainOrder && key.Group == domainGroupA.GetInstanceID();
            }
            WorldVisualSortingKey escapedKey = WorldVisualSortingKey.FromRenderer(projectedGroupedSprite);
            allRenderersInside &= escapedKey.Layer == SortingLayer.GetLayerValueFromID(expectedDomainLayer) &&
                escapedKey.Order == expectedDomainOrder && escapedKey.Group == domainGroupA.GetInstanceID();
            nestedRootKeyApplied = sourceNestedGroup.sortAtRoot && !projectedNestedGroup.sortAtRoot &&
                sourceParentGroup.sortAtRoot && !projectionA.GetComponent<SortingGroup>().sortAtRoot &&
                projectedNestedGroup.transform.parent == domainA.Root &&
                projectedNestedGroup.sortingLayerID == sourceNestedGroup.sortingLayerID &&
                projectedNestedGroup.sortingOrder == sourceNestedGroup.sortingOrder &&
                WorldVisualSortingKey.FromRenderer(projectedGroupedSprite).Group == domainGroupA.GetInstanceID();
            localKeysPreserved = projectionA.GetComponent<SortingGroup>().sortingLayerID == sourceParentGroup.sortingLayerID &&
                projectionA.GetComponent<SortingGroup>().sortingOrder == sourceParentGroup.sortingOrder &&
                projectedSprite.sortingLayerID == directSprite.sortingLayerID &&
                projectedSprite.sortingOrder == directSprite.sortingOrder &&
                projectedNestedGroup.sortingLayerID == sourceNestedGroup.sortingLayerID &&
                projectedNestedGroup.sortingOrder == sourceNestedGroup.sortingOrder &&
                projectedGroupedSprite.sortingLayerID == groupedSprite.sortingLayerID &&
                projectedGroupedSprite.sortingOrder == groupedSprite.sortingOrder &&
                projectedMesh.sortingLayerID == sourceMesh.sortingLayerID && projectedMesh.sortingOrder == sourceMesh.sortingOrder &&
                projectedParticles.sortingLayerID == sourceParticles.sortingLayerID &&
                projectedParticles.sortingOrder == sourceParticles.sortingOrder &&
                projectedSecondSprite.sortingLayerID == secondSprite.sortingLayerID &&
                projectedSecondSprite.sortingOrder == secondSprite.sortingOrder;
            rendererTypesProjected = projectedSprite != null && projectedGroupedSprite != null &&
                projectedMesh != null && projectedParticles != null;
            yAndPositionProjectedOnce = Vector3.Distance(projectionA.transform.position,
                    matrixA.MultiplyPoint3x4(sourceA.transform.position)) < .001f &&
                Vector3.Distance(projectedNestedGroup.transform.position,
                    matrixA.MultiplyPoint3x4(nestedRoot.transform.position)) < .001f &&
                Vector3.Distance(projectedSprite.transform.position, matrixA.MultiplyPoint3x4(directSprite.transform.position)) < .001f &&
                Vector3.Distance(projectedMesh.transform.position, matrixA.MultiplyPoint3x4(sourceMesh.transform.position)) < .001f &&
                Vector3.Distance(projectionB.transform.position, sourceB.transform.position) < .001f &&
                Math.Abs(projectedMesh.transform.position.y - projectedSprite.transform.position.y -
                    (sourceMesh.transform.position.y - directSprite.transform.position.y)) < .001f;
            Color originalColor = directSprite.color;
            directSprite.color = Color.red;
            Matrix4x4 scheduledMatrix = Matrix4x4.Translate(Vector3.right * 7f) * matrixA;
            projectionA.ScheduleSynchronizationBeforeRendering(scheduledMatrix, true);
            scheduledPoseImmediate = Vector3.Distance(projectionA.transform.position,
                scheduledMatrix.MultiplyPoint3x4(sourceA.transform.position)) < .001f;
            scheduledRendererDeferred = projectedSprite.color == originalColor;
            directSprite.color = Color.green;
            projectionA.ScheduleSynchronizationBeforeRendering(scheduledMatrix, true);
            scheduledRendererDeferred &= projectedSprite.color == originalColor;
            domainA.FlushLocalSortingKeys();
            scheduledLatestRenderData = projectedSprite.color == Color.green;
            directSprite.color = originalColor;
            projectionA.Synchronize(matrixA, true);

            hiddenSourcesSuppressed = WorldPresentationTransformRegistry.TryGetSortingDomain(worldA, out var registeredA) &&
                ReferenceEquals(registeredA, domainA) && WorldPresentationTransformRegistry.TryGetSortingDomain(worldB, out var registeredB) &&
                ReferenceEquals(registeredB, domainB);
            projectionA.Synchronize(matrixA, false);
            domainA.Synchronize(matrixA, false);
            directSprite.forceRenderingOff = false;
            projectionA.ScheduleSynchronizationBeforeRendering(matrixA, false);
            hiddenSourcesSuppressed &= !projectionA.gameObject.activeInHierarchy && !domainA.Root.gameObject.activeInHierarchy &&
                directSprite.forceRenderingOff && groupedSprite.forceRenderingOff && sourceMesh.forceRenderingOff &&
                sourceParticles.forceRenderingOff;
            projectionA.Synchronize(matrixA, true);
            domainA.Synchronize(matrixA, true);

            int playerLayerId = SortingLayer.NameToID("Player");
            int defaultLayerId = SortingLayer.NameToID("Default");
            SortingDomainRankProbe rankALow = CreateSortingDomainRankProbe(fixture.Services.transform,
                fixture.Services.transform, domainA, matrixA, "RankA_LowY", new Vector3(0f, -2f), playerLayerId, 100);
            SortingDomainRankProbe rankAHigh = CreateSortingDomainRankProbe(fixture.Services.transform,
                fixture.Services.transform, domainA, matrixA, "RankA_HighY", new Vector3(0f, 2f), playerLayerId, 100);
            SortingDomainRankProbe rankBLow = CreateSortingDomainRankProbe(fixture.Services.transform,
                fixture.Services.transform, domainB, matrixB, "RankB_LowY", new Vector3(0f, -2f), playerLayerId, 100);
            SortingDomainRankProbe rankBHigh = CreateSortingDomainRankProbe(fixture.Services.transform,
                fixture.Services.transform, domainB, matrixB, "RankB_HighY", new Vector3(0f, 2f), playerLayerId, 100);
            SortingDomainRankProbe floorProbe = CreateSortingDomainRankProbe(fixture.Services.transform,
                fixture.Services.transform, domainA, matrixA, "Rank_FixedFloor", new Vector3(1f, 0f), defaultLayerId, 0);
            SortingDomainRankProbe actorProbe = CreateSortingDomainRankProbe(fixture.Services.transform,
                fixture.Services.transform, domainA, matrixA, "Rank_FixedActor", new Vector3(1f, 0f), playerLayerId, 100);
            foreach (SortingDomainRankProbe probe in new[] { rankALow, rankAHigh, rankBLow, rankBHigh, floorProbe, actorProbe })
            {
                rankProbeSources.Add(probe.Source);
                rankProbeProjections.Add(probe.Projection);
            }
            ApplySortingDomainRankProbeKey(domainA, rankALow);
            ApplySortingDomainRankProbeKey(domainA, rankAHigh);
            ApplySortingDomainRankProbeKey(domainA, floorProbe);
            ApplySortingDomainRankProbeKey(domainA, actorProbe);
            domainA.FlushLocalSortingKeys();
            ApplySortingDomainRankProbeKey(domainB, rankBLow);
            ApplySortingDomainRankProbeKey(domainB, rankBHigh);
            domainB.FlushLocalSortingKeys();

            int rankAHighBeforeSwap = rankAHigh.DisplayGroup.sortingOrder;
            int rankALowBeforeSwap = rankALow.DisplayGroup.sortingOrder;
            int rankBHighBeforeSwap = rankBHigh.DisplayGroup.sortingOrder;
            int rankBLowBeforeSwap = rankBLow.DisplayGroup.sortingOrder;
            rankAHigh.Source.transform.position = new Vector3(0f, -2f, 0f);
            rankALow.Source.transform.position = new Vector3(0f, 2f, 0f);
            rankAHigh.Projection.Synchronize(matrixA, true);
            rankALow.Projection.Synchronize(matrixA, true);
            ApplySortingDomainRankProbeKey(domainA, rankALow);
            ApplySortingDomainRankProbeKey(domainA, rankAHigh);
            domainA.FlushLocalSortingKeys();
            bool domainASwappedWithY = rankALow.DisplayGroup.sortingOrder < rankAHigh.DisplayGroup.sortingOrder;
            int rankAHighAfterSwap = rankAHigh.DisplayGroup.sortingOrder;
            int rankALowAfterSwap = rankALow.DisplayGroup.sortingOrder;
            rankBHigh.Source.transform.position = new Vector3(0f, -2f, 0f);
            rankBLow.Source.transform.position = new Vector3(0f, 2f, 0f);
            rankBHigh.Projection.Synchronize(matrixB, true);
            rankBLow.Projection.Synchronize(matrixB, true);
            ApplySortingDomainRankProbeKey(domainB, rankBLow);
            ApplySortingDomainRankProbeKey(domainB, rankBHigh);
            domainB.FlushLocalSortingKeys();
            domainA.FlushLocalSortingKeys();
            bool domainBSwappedWithY = rankBLow.DisplayGroup.sortingOrder < rankBHigh.DisplayGroup.sortingOrder;
            sameRawYRankedAndSwapped = rankAHigh.SourceGroup.sortingLayerID == rankALow.SourceGroup.sortingLayerID &&
                rankAHigh.SourceGroup.sortingOrder == rankALow.SourceGroup.sortingOrder &&
                rankALow.DisplayGroup.sortingOrder < rankAHigh.DisplayGroup.sortingOrder &&
                rankBHigh.SourceGroup.sortingOrder == rankBLow.SourceGroup.sortingOrder &&
                rankBLow.DisplayGroup.sortingOrder < rankBHigh.DisplayGroup.sortingOrder &&
                rankAHighBeforeSwap < rankALowBeforeSwap && rankBHighBeforeSwap < rankBLowBeforeSwap &&
                rankAHighBeforeSwap != rankALowBeforeSwap && rankBHighBeforeSwap != rankBLowBeforeSwap &&
                domainASwappedWithY && domainBSwappedWithY;
            domainsRankIndependently = rankAHigh.DisplayGroup.sortingOrder == rankAHighAfterSwap &&
                rankALow.DisplayGroup.sortingOrder == rankALowAfterSwap &&
                rankBHighBeforeSwap != rankBLowBeforeSwap;
            int floorLayerValue = SortingLayer.GetLayerValueFromID(floorProbe.DisplayGroup.sortingLayerID);
            int actorLayerValue = SortingLayer.GetLayerValueFromID(actorProbe.DisplayGroup.sortingLayerID);
            fixedFloorBelowActor = floorLayerValue < actorLayerValue &&
                floorProbe.DisplayGroup.sortingLayerID == defaultLayerId && actorProbe.DisplayGroup.sortingLayerID == playerLayerId;
            SpriteRenderer[] highSourceParts = rankAHigh.SourceParts;
            SpriteRenderer[] highDisplayParts = rankAHigh.DisplayParts;
            sourceGroupRemainsAtomic = rankAHigh.DisplayGroup.sortingOrder != rankAHigh.SourceGroup.sortingOrder &&
                highDisplayParts[0].sortingOrder == highSourceParts[0].sortingOrder &&
                highDisplayParts[1].sortingOrder == highSourceParts[1].sortingOrder &&
                highDisplayParts[0].sortingOrder < highDisplayParts[1].sortingOrder &&
                !rankAHigh.DisplayGroup.sortAtRoot && rankAHigh.DisplayGroup.transform.parent == domainA.Root;

            int hiddenRank = rankALow.DisplayGroup.sortingOrder;
            rankALow.Projection.SetVisible(false);
            domainA.FlushLocalSortingKeys();
            hideKeepsRank = !rankALow.Projection.gameObject.activeInHierarchy &&
                rankALow.DisplayGroup.sortingOrder == hiddenRank;
            rankALow.Projection.SetVisible(true);
            rankALow.Projection.Synchronize(matrixA, true);
            ApplySortingDomainRankProbeKey(domainA, rankALow);
            domainA.FlushLocalSortingKeys();

            rankAHigh.Projection.SetSortingDomain(domainB);
            moveRestoresRawRank = rankAHigh.DisplayGroup.sortingOrder == rankAHigh.SourceGroup.sortingOrder;
            rankAHigh.Projection.Synchronize(matrixB, true);
            ApplySortingDomainRankProbeKey(domainB, rankAHigh);
            domainB.FlushLocalSortingKeys();
            bool movedToDestinationRank = rankAHigh.Projection.transform.parent == domainB.Root &&
                rankAHigh.DisplayGroup.sortingOrder != rankAHigh.SourceGroup.sortingOrder;
            rankAHigh.Projection.SetSortingDomain(domainA);
            bool returnedRawRank = rankAHigh.DisplayGroup.sortingOrder == rankAHigh.SourceGroup.sortingOrder;
            rankAHigh.Projection.Synchronize(matrixA, true);
            ApplySortingDomainRankProbeKey(domainA, rankAHigh);
            domainA.FlushLocalSortingKeys();
            destinationAndReentryRerank = movedToDestinationRank && returnedRawRank &&
                rankAHigh.Projection.transform.parent == domainA.Root &&
                rankAHigh.DisplayGroup.sortingOrder != rankAHigh.SourceGroup.sortingOrder;
            domainA.RemoveLocalSortingKey(rankAHigh.DisplayGroup);
            removeRestoresRawRank = rankAHigh.DisplayGroup.sortingOrder == rankAHigh.SourceGroup.sortingOrder;
            rankAHigh.Projection.Synchronize(matrixA, true);
            ApplySortingDomainRankProbeKey(domainA, rankAHigh);
            domainA.FlushLocalSortingKeys();

            var releaseSource = new GameObject("Rank_ReleaseSource") { hideFlags = HideFlags.HideAndDontSave };
            releaseSource.transform.SetParent(fixture.Services.transform, false);
            rankProbeSources.Add(releaseSource);
            var releaseSourcePart = new GameObject("ReleasePart") { hideFlags = HideFlags.HideAndDontSave };
            releaseSourcePart.transform.SetParent(releaseSource.transform, false);
            SpriteRenderer releaseSourceRenderer = releaseSourcePart.AddComponent<SpriteRenderer>();
            releaseSourceRenderer.sortingLayerID = playerLayerId;
            releaseSourceRenderer.sortingOrder = 123;
            WorldPresentationProjection releaseProjection = WorldPresentationProjection.Create(
                releaseSource.transform, fixture.Services.transform);
            rankProbeProjections.Add(releaseProjection);
            releaseProjection.SetSortingDomain(domainA);
            releaseProjection.Synchronize(matrixA, true);
            SpriteRenderer releaseTargetRenderer = releaseProjection.transform.Find("ReleasePart")
                .GetComponent<SpriteRenderer>();
            detachedRankTargets.Add(releaseTargetRenderer.gameObject);
            domainA.FlushLocalSortingKeys();
            bool releaseTargetRanked = releaseTargetRenderer.sortingOrder != releaseSourceRenderer.sortingOrder;
            releaseTargetRenderer.transform.SetParent(fixture.Services.transform, true);
            releaseProjection.Release();
            domainA.FlushLocalSortingKeys();
            releaseRestoresRawRank = releaseTargetRanked && releaseTargetRenderer.sortingLayerID == playerLayerId &&
                releaseTargetRenderer.sortingOrder == 123 && releaseSourceRenderer.sortingLayerID == playerLayerId &&
                releaseSourceRenderer.sortingOrder == 123 && !releaseSourceRenderer.forceRenderingOff;
            var disposeRankTarget = new GameObject("Rank_DisposeTarget") { hideFlags = HideFlags.HideAndDontSave };
            disposeRankTarget.transform.SetParent(domainA.Root, false);
            SortingGroup disposeRankGroup = disposeRankTarget.AddComponent<SortingGroup>();
            disposeRankGroup.sortingLayerID = playerLayerId;
            disposeRankGroup.sortingOrder = 321;
            domainA.ApplyLocalSortingGroupKey(disposeRankGroup, playerLayerId, 321, 3000, disposeRankTarget.transform.position.y);
            domainA.FlushLocalSortingKeys();
            bool disposeTargetRanked = disposeRankGroup.sortingOrder != 321;
            disposeRankTarget.transform.SetParent(fixture.Services.transform, true);
            domainA.Dispose();
            domainDisposeRestoresRawRank = disposeTargetRanked && disposeRankGroup.sortingLayerID == playerLayerId &&
                disposeRankGroup.sortingOrder == 321;
            UnityEngine.Object.DestroyImmediate(disposeRankTarget);
        }
        finally
        {
            WorldPresentationTransformRegistry.Remove(worldA);
            WorldPresentationTransformRegistry.Remove(worldB);
            registryReleased = !WorldPresentationTransformRegistry.TryGetSortingDomain(worldA, out _) &&
                !WorldPresentationTransformRegistry.TryGetSortingDomain(worldB, out _);
            domainA?.Dispose();
            sharedSourceHeldUntilLastProjection = sharedSprite.forceRenderingOff;
            domainDisposeRestoresSources = !directSprite.forceRenderingOff && groupedSprite.forceRenderingOff &&
                !sourceMesh.forceRenderingOff && !sourceParticles.forceRenderingOff;
            domainB?.Dispose();
            sharedSourceRestoredAfterLastProjection = !sharedSprite.forceRenderingOff;
            sourceRestored = !directSprite.forceRenderingOff && groupedSprite.forceRenderingOff &&
                !sourceMesh.forceRenderingOff && !sourceParticles.forceRenderingOff && !secondSprite.forceRenderingOff &&
                !sharedSprite.forceRenderingOff;
            domainsDisposed = (domainA == null || domainA.IsDisposed) && (domainB == null || domainB.IsDisposed);
            domainDisposeHidesCopies = (projectionA == null || !projectionA.gameObject.activeInHierarchy) &&
                (projectionB == null || !projectionB.gameObject.activeInHierarchy) &&
                (sharedProjectionA == null || !sharedProjectionA.gameObject.activeInHierarchy) &&
                (sharedProjectionB == null || !sharedProjectionB.gameObject.activeInHierarchy);
            if (projectionA != null) projectionA.Release();
            if (projectionB != null) projectionB.Release();
            if (sharedProjectionA != null) sharedProjectionA.Release();
            if (sharedProjectionB != null) sharedProjectionB.Release();
            foreach (WorldPresentationProjection projection in rankProbeProjections)
                if (projection != null) projection.Release();
            UnityEngine.Object.DestroyImmediate(sourceA);
            UnityEngine.Object.DestroyImmediate(sourceB);
            UnityEngine.Object.DestroyImmediate(sharedSource);
            UnityEngine.Object.DestroyImmediate(background);
            foreach (GameObject source in rankProbeSources)
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            foreach (GameObject target in detachedRankTargets)
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
        }
        return new JObject
        {
            ["category"] = WorldSortingManager.ProjectedWorldCategory,
            ["domainOrder"] = expectedDomainOrder,
            ["keyMatchesCategory"] = keyMatchesCategory,
            ["backgroundBehind"] = backgroundBehind,
            ["distinctDomains"] = distinctDomains,
            ["allRenderersInside"] = allRenderersInside,
            ["nestedRootKeyApplied"] = nestedRootKeyApplied,
            ["localKeysPreserved"] = localKeysPreserved,
            ["rendererTypesProjected"] = rendererTypesProjected,
            ["yAndPositionProjectedOnce"] = yAndPositionProjectedOnce,
            ["scheduledPoseImmediate"] = scheduledPoseImmediate,
            ["scheduledRendererDeferred"] = scheduledRendererDeferred,
            ["scheduledLatestRenderData"] = scheduledLatestRenderData,
            ["copiedRendererCount"] = copiedRendererCount,
            ["hiddenSourcesSuppressed"] = hiddenSourcesSuppressed,
            ["domainDisposeRestoresSources"] = domainDisposeRestoresSources,
            ["domainDisposeHidesCopies"] = domainDisposeHidesCopies,
            ["sharedSourceHeldUntilLastProjection"] = sharedSourceHeldUntilLastProjection,
            ["sharedSourceRestoredAfterLastProjection"] = sharedSourceRestoredAfterLastProjection,
            ["sourceRestored"] = sourceRestored,
            ["registryReleased"] = registryReleased,
            ["domainsDisposed"] = domainsDisposed,
            ["sameRawYRankedAndSwapped"] = sameRawYRankedAndSwapped,
            ["domainsRankIndependently"] = domainsRankIndependently,
            ["fixedFloorBelowActor"] = fixedFloorBelowActor,
            ["sourceGroupRemainsAtomic"] = sourceGroupRemainsAtomic,
            ["hideKeepsRank"] = hideKeepsRank,
            ["moveRestoresRawRank"] = moveRestoresRawRank,
            ["destinationAndReentryRerank"] = destinationAndReentryRerank,
            ["removeRestoresRawRank"] = removeRestoresRawRank,
            ["releaseRestoresRawRank"] = releaseRestoresRawRank,
            ["domainDisposeRestoresRawRank"] = domainDisposeRestoresRawRank
        };
    }

    /// <summary>真实嵌套分组先比较子组，再比较子组内精灵，并遵守独立根。</summary>
    private static JObject CaptureNestedSortingGroupPriority(int playerLayer, int playerOrder)
    {
        var root = new GameObject("[LuaNestedSorting]") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            SortingGroup outer = root.AddComponent<SortingGroup>();
            outer.sortingLayerID = playerLayer; outer.sortingOrder = playerOrder;
            var first = new GameObject("FirstGroup"); first.transform.SetParent(root.transform, false);
            var second = new GameObject("SecondGroup"); second.transform.SetParent(root.transform, false);
            SortingGroup firstGroup = first.AddComponent<SortingGroup>();
            SortingGroup secondGroup = second.AddComponent<SortingGroup>();
            firstGroup.sortingOrder = -4; secondGroup.sortingOrder = 4;
            SpriteRenderer firstSprite = first.AddComponent<SpriteRenderer>(); firstSprite.sortingOrder = 100;
            SpriteRenderer secondSprite = second.AddComponent<SpriteRenderer>(); secondSprite.sortingOrder = -100;
            var same = new GameObject("SameGroupSprite"); same.transform.SetParent(first.transform, false);
            SpriteRenderer sameSprite = same.AddComponent<SpriteRenderer>(); sameSprite.sortingOrder = 101;
            WorldVisualSortingKey firstKey = WorldVisualSortingKey.FromRenderer(firstSprite);
            WorldVisualSortingKey secondKey = WorldVisualSortingKey.FromRenderer(secondSprite);
            WorldVisualSortingKey sameKey = WorldVisualSortingKey.FromRenderer(sameSprite);
            bool nestedGroups = firstKey.CompareTo(secondKey) < 0;
            bool sameGroup = firstKey.CompareTo(sameKey) < 0;
            secondGroup.sortingOrder = firstGroup.sortingOrder;
            secondKey = WorldVisualSortingKey.FromRenderer(secondSprite);
            int firstComparedToSecond = firstKey.CompareTo(secondKey);
            int sameComparedToSecond = sameKey.CompareTo(secondKey);
            bool groupsStayTogether = firstComparedToSecond != 0 &&
                Math.Sign(firstComparedToSecond) == Math.Sign(sameComparedToSecond);
            secondGroup.sortAtRoot = true;
            WorldSortingManager.ReadExternalRendererKey(secondSprite, out int layer, out int order);
            bool rootRead = layer == secondGroup.sortingLayerID && order == secondGroup.sortingOrder &&
                WorldVisualSortingKey.FromRenderer(secondSprite).Group == secondGroup.GetInstanceID();
            firstGroup.sortAtRoot = true;
            outer.sortingOrder = 333;
            first.AddComponent<WorldSortingMember>().Bind(WorldSortingManager.BuildingCategory, firstSprite);
            bool rootApply = firstGroup.sortingLayerID == playerLayer && firstGroup.sortingOrder == playerOrder &&
                outer.sortingOrder == 333;
            return new JObject { ["nestedGroups"] = nestedGroups, ["sameGroup"] = sameGroup,
                ["groupsStayTogether"] = groupsStayTogether,
                ["rootRead"] = rootRead, ["rootApply"] = rootApply };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// <summary>提交少量正式生物帧，采样原生 Renderer 排序键、脚点和池复用。</summary>
    private static JObject CaptureAiecsConfiguredSorting(int playerLayer, int playerOrder)
    {
#if UNITY_EDITOR
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(
            "Assets/6_Art/Generated/Actors/AIECS/生物动画目录.asset") ??
            throw new InvalidOperationException("AIECS 排序采样缺少正式动画目录。");
        var cameraObject = new GameObject("[LuaAiecsSortingCamera]") { hideFlags = HideFlags.HideAndDontSave };
        var scene = cameraObject.scene;
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 10f; camera.aspect = 1f;
        var actorIds = new[] { "Wolf", "Chicken" };
        WorldSortingManager.ResolveItemVisualSortingKey(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 37 },
            playerLayer, playerOrder, out int customLayer, out int customOrder);
        var keys = new[] { new AiecsActorSortingKey(playerLayer, playerOrder), new AiecsActorSortingKey(customLayer, customOrder) };
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.GroundShadowCategory, out int shadowLayer, out int shadowOrder);
        AiecsWorldRenderer display = null;
        WorldPresentationSortingDomain domain = null;
        const string aiecsWorldKey = "probe:aiecs-sorting-domain";
        bool domainParented = false, domainCategoryApplied = false, domainFootpointsProjected = false;
        bool domainInnerKeysPreserved = false, domainProjectionHidden = false, domainBatchKeyApplied = false;
        bool domainDifferentSourceYMerged = false, domainSameSourceYSeparated = false, domainWaterLineUsesSourceCoordinates = false;
        bool domainRawOrdersSubmitted = false, domainRankOrdersDiffer = false, domainYRankOrderPreserved = false;
        bool domainHiddenRanksRestored = false, domainProjectionReentered = false, domainReentryRanked = false;
        int projectedDomainBatchCount = 0;
        var records = new NativeArray<AiecsDisplayRecord>(4, Allocator.Temp);
        try
        {
            display = new AiecsWorldRenderer(catalog, actorIds, scene, new AiecsWorldSortingKeys(keys, shadowLayer, shadowOrder));
            display.ShadowsEnabled = false;
            for (int i = 0; i < records.Length; i++)
                records[i] = new AiecsDisplayRecord
                {
                    Key = new CombatIdentity { Backend = CombatBackend.Entity, Value = (ulong)(i + 1), Generation = 1 },
                    Definition = i == 3 ? 1 : 0,
                    Position = new float2(i == 0 ? -1f : i == 1 ? 1f : 0f, i == 2 ? 3f : 1f),
                    Facing = new float2(1f, 0f), Hp = 1f, MaxHp = 1f, LiquidDepth = .35f, WaterBlend = .65f
                };
            List<MeshRenderer> Renderers()
            {
                var result = new List<MeshRenderer>();
                if (display?.PresentationRoot == null) return result;
                foreach (MeshRenderer value in display.PresentationRoot.GetComponentsInChildren<MeshRenderer>(true))
                    if (value != null && value.gameObject.name == "AIECS 连续绘制批次") result.Add(value);
                return result;
            }
            display.DrawRecords(records, camera, default);
            List<MeshRenderer> first = Renderers();
            bool sharedYBatch = false, otherYBatch = false, customBatch = false;
            foreach (MeshRenderer value in first)
            {
                int vertices = value.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                sharedYBatch |= value.enabled && value.sortingLayerID == playerLayer && value.sortingOrder == playerOrder &&
                    Mathf.Abs(value.bounds.center.y - 1f) < .0001f && vertices == 8;
                otherYBatch |= value.enabled && value.sortingLayerID == playerLayer && value.sortingOrder == playerOrder &&
                    Mathf.Abs(value.bounds.center.y - 3f) < .0001f && vertices == 4;
                customBatch |= value.enabled && value.sortingLayerID == customLayer && value.sortingOrder == customOrder &&
                    Mathf.Abs(value.bounds.center.y - 1f) < .0001f && vertices == 4;
            }
            int initialBatches = display.BatchCount;
            int initialBytes = display.UploadedBytes;

            domain = WorldPresentationSortingDomain.Create(cameraObject.transform, aiecsWorldKey,
                WorldSortingManager.ProjectedWorldCategory);
            Matrix4x4 domainMatrix = Matrix4x4.TRS(new Vector3(4f, 5f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
            domain.Synchronize(domainMatrix, true);
            WorldPresentationTransformRegistry.Set(aiecsWorldKey, domainMatrix, true, domain);
            WorldSortingManager.GetResourceSortingKey(WorldSortingManager.ProjectedWorldCategory,
                out int domainLayer, out int domainOrder);
            var projectedKeys = new[] { keys[0], keys[0] };
            display.SetActorSortingKeys(projectedKeys);
            display.SetWorldPresentation(domain.Root, domainMatrix, true);
            display.SetSortingKeyCallbacks(
                (renderer, layer, order, queue, footY) => domain.ApplyLocalRendererSortingKey(renderer, layer, order, queue, footY),
                domain.RemoveLocalSortingKey);
            display.DrawRecords(records, camera, default);
            List<MeshRenderer> domainRenderers = Renderers();
            projectedDomainBatchCount = display.BatchCount;
            UnityEngine.Rendering.SortingGroup domainGroup = domain.Root.GetComponent<UnityEngine.Rendering.SortingGroup>();
            domainParented = display.PresentationRoot.parent == domain.Root && display.PresentationVisible &&
                display.WorldToDisplay.Equals(domainMatrix) && display.PresentationRoot.position == Vector3.zero &&
                display.PresentationRoot.rotation == Quaternion.identity;
            domainCategoryApplied = domainGroup.sortingLayerID == domainLayer && domainGroup.sortingOrder == domainOrder;
            domainRawOrdersSubmitted = domainRenderers.Count == projectedDomainBatchCount &&
                domainRenderers.TrueForAll(value => value.sortingLayerID == playerLayer && value.sortingOrder == playerOrder);
            domain.FlushLocalSortingKeys();
            bool allFootpointsProjected = domainRenderers.Count == 3;
            bool allInnerKeysPreserved = domainRenderers.Count == projectedDomainBatchCount;
            bool allDomainKeysApplied = domainRenderers.Count == projectedDomainBatchCount;
            int projectedLowYBatchCount = 0, projectedHighYBatchCount = 0, mergedMiddleYBatchCount = 0;
            int projectedLowYOrder = 0, projectedMiddleYOrder = 0, projectedHighYOrder = 0;
            bool foundProjectedLowY = false, foundProjectedMiddleY = false, foundProjectedHighY = false;
            bool allWaterLinesUseSourceCoordinates = domainRenderers.Count == projectedDomainBatchCount;
            var waterProperties = new MaterialPropertyBlock();
            int waterUsesSourceId = Shader.PropertyToID("_AiecsWaterUseSource");
            int waterSourceMatrixId = Shader.PropertyToID("_AiecsWaterWorldToSource");
            Matrix4x4 expectedSourceMatrix = domainMatrix.inverse;
            var expectedWaterSourceYs = new List<float>(records.Length);
            var actualWaterSourceYs = new List<float>();
            for (int i = 0; i < records.Length; i++)
            {
                AiecsDisplayRecord record = records[i];
                AiecsActorVisual definition = Array.Find(catalog.Actors, value => value.Id == actorIds[record.Definition]);
                float height = Mathf.Max(.0001f, definition.BodyYRange.y - definition.BodyYRange.x);
                expectedWaterSourceYs.Add(record.Position.y + definition.BodyYRange.x + height * record.LiquidDepth);
            }
            foreach (MeshRenderer value in domainRenderers)
            {
                Vector3 center = value.bounds.center;
                bool expectedFootpoint = false;
                for (int i = 0; i < records.Length; i++)
                {
                    float2 local = records[i].Position;
                    float expectedY = domainMatrix.MultiplyPoint3x4(new Vector3(local.x, local.y, 0f)).y;
                    expectedFootpoint |= Mathf.Abs(center.y - expectedY) < .0001f;
                }
                int vertices = value.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                allFootpointsProjected &= expectedFootpoint;
                allInnerKeysPreserved &= value.sortingLayerID == playerLayer;
                if (Mathf.Abs(center.y - domainMatrix.MultiplyPoint3x4(new Vector3(-1f, 1f, 0f)).y) < .0001f)
                {
                    projectedLowYBatchCount += vertices == 4 ? 1 : 100;
                    projectedLowYOrder = value.sortingOrder;
                    foundProjectedLowY = true;
                }
                if (Mathf.Abs(center.y - domainMatrix.MultiplyPoint3x4(new Vector3(1f, 1f, 0f)).y) < .0001f)
                {
                    projectedHighYBatchCount += vertices == 4 ? 1 : 100;
                    projectedHighYOrder = value.sortingOrder;
                    foundProjectedHighY = true;
                }
                if (Mathf.Abs(center.y - domainMatrix.MultiplyPoint3x4(new Vector3(0f, 3f, 0f)).y) < .0001f)
                {
                    mergedMiddleYBatchCount += vertices == 8 ? 1 : 100;
                    projectedMiddleYOrder = value.sortingOrder;
                    foundProjectedMiddleY = true;
                }
                WorldVisualSortingKey key = WorldVisualSortingKey.FromRenderer(value);
                allDomainKeysApplied &= value.transform.IsChildOf(domain.Root) &&
                    key.Layer == SortingLayer.GetLayerValueFromID(domainLayer) && key.Order == domainOrder &&
                    key.Group == domainGroup.GetInstanceID();
                value.GetPropertyBlock(waterProperties);
                allWaterLinesUseSourceCoordinates &= waterProperties.GetFloat(waterUsesSourceId) > .5f &&
                    waterProperties.GetMatrix(waterSourceMatrixId).Equals(expectedSourceMatrix);
                var waterValues = new List<Vector4>();
                value.GetComponent<MeshFilter>().sharedMesh.GetUVs(1, waterValues);
                foreach (Vector4 waterValue in waterValues) actualWaterSourceYs.Add(waterValue.y);
            }
            foreach (float expectedWaterY in expectedWaterSourceYs)
            {
                int matchingSourceVertices = 0;
                foreach (float actualWaterY in actualWaterSourceYs)
                {
                    bool matchesSourceY = Mathf.Abs(actualWaterY - expectedWaterY) < .0001f;
                    bool matchesAnySourceY = false;
                    foreach (float candidate in expectedWaterSourceYs)
                        matchesAnySourceY |= Mathf.Abs(actualWaterY - candidate) < .0001f;
                    allWaterLinesUseSourceCoordinates &= matchesAnySourceY;
                    if (matchesSourceY) matchingSourceVertices++;
                }
                allWaterLinesUseSourceCoordinates &= matchingSourceVertices >= 4;
            }
            domainFootpointsProjected = allFootpointsProjected;
            domainInnerKeysPreserved = allInnerKeysPreserved;
            domainBatchKeyApplied = allDomainKeysApplied;
            domainDifferentSourceYMerged = mergedMiddleYBatchCount == 1;
            domainSameSourceYSeparated = projectedLowYBatchCount == 1 && projectedHighYBatchCount == 1;
            domainRankOrdersDiffer = foundProjectedLowY && foundProjectedMiddleY && foundProjectedHighY &&
                projectedLowYOrder != projectedMiddleYOrder && projectedMiddleYOrder != projectedHighYOrder &&
                projectedLowYOrder != projectedHighYOrder;
            domainYRankOrderPreserved = foundProjectedLowY && foundProjectedMiddleY && foundProjectedHighY &&
                projectedHighYOrder < projectedMiddleYOrder && projectedMiddleYOrder < projectedLowYOrder;
            domainWaterLineUsesSourceCoordinates = allWaterLinesUseSourceCoordinates;
            display.SetWorldPresentation(domain.Root, domainMatrix, false);
            display.DrawRecords(records, camera, default);
            domainRenderers = Renderers();
            domainProjectionHidden = !display.PresentationVisible && !display.PresentationRoot.gameObject.activeSelf;
            domainHiddenRanksRestored = true;
            foreach (MeshRenderer value in domainRenderers)
            {
                domainProjectionHidden &= !value.enabled;
                domainHiddenRanksRestored &= value.sortingOrder == playerOrder;
            }
            display.SetWorldPresentation(domain.Root, domainMatrix, true);
            display.DrawRecords(records, camera, default);
            domain.FlushLocalSortingKeys();
            domainRenderers = Renderers();
            domainProjectionReentered = domainRenderers.Count == 3 && display.PresentationVisible &&
                display.PresentationRoot.parent == domain.Root;
            domainReentryRanked = domainProjectionReentered &&
                domainRenderers.Count == projectedDomainBatchCount &&
                domainRenderers.TrueForAll(value => value.sortingOrder != playerOrder);
            display.SetSortingKeyCallbacks(null, null);
            display.SetWorldPresentation(null, Matrix4x4.identity, true);
            display.SetActorSortingKeys(keys);
            display.DrawRecords(records, camera, default);

            var firstIds = new HashSet<int>();
            foreach (MeshRenderer value in first) firstIds.Add(value.GetInstanceID());
            keys[1] = new AiecsActorSortingKey(playerLayer, 147);
            display.SetActorSortingKeys(keys);
            display.DrawRecords(records, camera, default);
            List<MeshRenderer> refreshed = Renderers();
            bool updated = false, reused = refreshed.Count == first.Count;
            foreach (MeshRenderer value in refreshed)
            {
                reused &= firstIds.Contains(value.GetInstanceID());
                updated |= value.enabled && value.sortingLayerID == playerLayer && value.sortingOrder == 147 &&
                    Mathf.Abs(value.bounds.center.y - 1f) < .0001f;
            }
            AiecsDisplayRecord offscreen = records[2]; offscreen.Position = new float2(0f, 100f); records[2] = offscreen;
            display.DrawRecords(records, camera, default);
            int culledBatches = display.BatchCount;
            int activeAfterCull = 0;
            foreach (MeshRenderer value in refreshed) if (value.enabled) activeAfterCull++;
            display.DrawRecords(records, null, default);
            bool hidden = display.BatchCount == 0;
            foreach (MeshRenderer value in refreshed) hidden &= !value.enabled;
            return new JObject { ["batches"] = initialBatches, ["uploadedBytes"] = initialBytes, ["sharedYBatch"] = sharedYBatch,
                ["otherYBatch"] = otherYBatch, ["customBatch"] = customBatch,
                ["updated"] = updated, ["reused"] = reused, ["culledBatches"] = culledBatches,
                ["activeAfterCull"] = activeAfterCull, ["hidden"] = hidden,
                ["domainParented"] = domainParented, ["domainCategoryApplied"] = domainCategoryApplied,
                ["domainFootpointsProjected"] = domainFootpointsProjected, ["domainInnerKeysPreserved"] = domainInnerKeysPreserved,
                ["domainBatchKeyApplied"] = domainBatchKeyApplied, ["domainProjectionHidden"] = domainProjectionHidden,
                ["domainRawOrdersSubmitted"] = domainRawOrdersSubmitted, ["domainRankOrdersDiffer"] = domainRankOrdersDiffer,
                ["domainYRankOrderPreserved"] = domainYRankOrderPreserved,
                ["domainHiddenRanksRestored"] = domainHiddenRanksRestored,
                ["domainProjectionReentered"] = domainProjectionReentered, ["domainReentryRanked"] = domainReentryRanked,
                ["domainDifferentSourceYMerged"] = domainDifferentSourceYMerged,
                ["domainSameSourceYSeparated"] = domainSameSourceYSeparated,
                ["domainWaterLineUsesSourceCoordinates"] = domainWaterLineUsesSourceCoordinates,
                ["projectedDomainBatchCount"] = projectedDomainBatchCount };
        }
        finally
        {
            display?.SetSortingKeyCallbacks(null, null);
            if (display != null) display.SetWorldPresentation(null, Matrix4x4.identity, false);
            WorldPresentationTransformRegistry.Remove(aiecsWorldKey);
            domain?.Dispose();
            display?.Dispose();
            records.Dispose();
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
#else
        throw new InvalidOperationException("AIECS 配置规则采样需要 Unity Editor 中的动画目录。");
#endif
    }

    /// <summary>以正式行网格提交验证外层配置分组与组内部件顺序。</summary>
    private static JObject CaptureDepthBatchConfiguredSorting(Sprite sprite, int playerLayer, int playerOrder)
    {
#if UNITY_EDITOR
        // 共享网格缓存持有 Sprite，规则使用正式资源而非测试结束即销毁的临时 Sprite。
        sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/6_Art/Food/Tea.png") ??
            throw new InvalidOperationException("排序采样缺少正式 Sprite。");
#endif
        var root = new GameObject("[LuaDepthSorting]") { hideFlags = HideFlags.HideAndDontSave };
        Material material = Resources.Load<Material>("Config/WorldModel/BRG/ChunkMesh-Water-Lit") ??
            throw new InvalidOperationException("排序采样缺少正式行网格材质。");
        try
        {
            using var depth = new ChunkDepthMeshRenderer(root.transform);
            void Set(int entity, int part, int innerOrder, int layer, int order) => depth.Set(
                ChunkDepthMeshRenderer.BuildingDomain, entity, part, sprite, material,
                Matrix4x4.identity, Vector3.zero, innerOrder, Color.white, sortingLayerId: layer, sortingOrder: order);
            Set(1, 0, 0, playerLayer, playerOrder);
            Set(1, 1, 1, playerLayer, playerOrder);
            Set(2, 0, 0, playerLayer, playerOrder + 1);
            Set(3, 0, 0, SortingLayer.NameToID("Default"), 0);
            depth.Flush();
            Transform body = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 1, 0);
            Transform attachment = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 1, 1);
            Transform otherOrder = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 2, 0);
            Transform ground = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 3, 0);
            bool separateKeys = otherOrder != null && ground != null && otherOrder != body && ground != body && otherOrder != ground;
            SortingGroup group = body != null ? body.GetComponent<SortingGroup>() : null;
            bool innerZero = false, innerOne = false;
            if (body != null)
                foreach (MeshRenderer part in body.GetComponentsInChildren<MeshRenderer>(true))
                {
                    innerZero |= part.sortingOrder == 0;
                    innerOne |= part.sortingOrder == 1;
                }
            int initialRows = depth.RowCount;
            var snapshots = new List<WorldVisualSnapshot>();
            depth.CollectVisualSnapshots(Matrix4x4.identity, snapshots);
            int playerLayerValue = SortingLayer.GetLayerValueFromID(playerLayer);
            int expectedGroup = body.GetInstanceID();
            bool snapshotBody = false, snapshotAttachment = false, snapshotOther = false;
            foreach (WorldVisualSnapshot snapshot in snapshots)
            {
                WorldVisualSortingKey key = snapshot.SortingKey;
                snapshotBody |= key.Layer == playerLayerValue && key.Order == playerOrder && key.Group == expectedGroup && key.InternalOrder == 0;
                snapshotAttachment |= key.Layer == playerLayerValue && key.Order == playerOrder && key.Group == expectedGroup && key.InternalOrder == 1;
                snapshotOther |= key.Layer == playerLayerValue && key.Order == playerOrder + 1 && key.Group == otherOrder.GetInstanceID();
            }
            bool outerBeforeY = new WorldVisualSortingKey(playerLayerValue, 100, -100f, 1, 10)
                .CompareTo(new WorldVisualSortingKey(playerLayerValue, 101, 100f, 2, -10)) < 0;
            bool yBeforeInner = new WorldVisualSortingKey(playerLayerValue, 100, 5f, 1, 10)
                .CompareTo(new WorldVisualSortingKey(playerLayerValue, 100, -5f, 1, -10)) < 0;
            bool sameGroupInnerOrder = new WorldVisualSortingKey(playerLayerValue, 100, 0f, 1, 0)
                .CompareTo(new WorldVisualSortingKey(playerLayerValue, 100, 0f, 1, 1)) < 0;
            Set(2, 0, 0, playerLayer, playerOrder);
            bool rekeyed = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 2, 0) == body;
            var result = new JObject
            {
                ["sameRoot"] = body != null && body == attachment,
                ["separateKeys"] = separateKeys,
                ["outerKey"] = group != null && group.sortingLayerID == playerLayer && group.sortingOrder == playerOrder,
                ["innerOrders"] = innerZero && innerOne,
                ["initialRows"] = initialRows, ["remainingRows"] = depth.RowCount, ["rekeyed"] = rekeyed,
                ["snapshots"] = snapshotBody && snapshotAttachment && snapshotOther,
                ["snapshotOrdering"] = outerBeforeY && yBeforeInner && sameGroupInnerOrder
            };
            JObject entitySorting = CaptureRotatedEntityDepthSorting(sprite, material, root.transform, playerLayer, playerOrder);
            foreach (JProperty property in entitySorting.Properties()) result[property.Name] = property.Value.DeepClone();
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // 使用正式地板、线路材质与机器行网格，验证任意脚点下线路始终夹在地板和墙体之间。
    private static JObject CaptureProjectedGroundFacilityOverlayOrdering(Sprite sprite, Transform parent)
    {
#if UNITY_EDITOR
        sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/6_Art/Food/Tea.png") ??
            throw new InvalidOperationException("贴地线路排序采样缺少正式 Sprite。");
        Material floorMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/9_Shaders/Material/Ground-Facility-Lit.mat");
        Material fiberMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/9_Shaders/Material/Ground-Connection-Lit.mat");
        Material machineMaterial = Resources.Load<Material>("Config/WorldModel/BRG/ChunkMesh-Water-Lit");
        if (floorMaterial == null || fiberMaterial == null || machineMaterial == null)
            throw new InvalidOperationException("贴地线路排序采样缺少正式材质。");
        using var domain = WorldPresentationSortingDomain.Create(parent, "probe:ground-facility-overlay",
            WorldSortingManager.ProjectedWorldCategory);
        var source = new GameObject("[LuaGroundFacilitySource]") { hideFlags = HideFlags.HideAndDontSave };
        source.transform.SetParent(parent, false);
        source.transform.localPosition = Vector3.down * 32f;
        Matrix4x4 projection = Matrix4x4.TRS(new Vector3(13.123456f, 14.58542f, 0f),
            Quaternion.Euler(0f, 0f, 31f), Vector3.one);
        domain.Synchronize(projection, true);
        int layer = SortingLayer.NameToID(WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
        int supportQueue = ChunkBatchRendererGroupService.GetLayerRenderQueue(ChunkBatchRendererGroupService.VisualLayer.Support);
        int facilityQueue = ChunkBatchRendererGroupService.GetLayerRenderQueue(ChunkBatchRendererGroupService.VisualLayer.GroundFacility);
        int wallQueue = ChunkBatchRendererGroupService.GetLayerRenderQueue(ChunkBatchRendererGroupService.VisualLayer.Blocking);
        var probes = new List<SortingDomainRankProbe>();
        WorldPresentationProjection machineProjection = null;
        Material wallMaterial = null;
        try
        {
            using var depth = new ChunkDepthMeshRenderer(source.transform, layer, 0, facilityQueue);
            SortingDomainRankProbe AddProbe(string name, Vector3 position, Material material)
            {
                SortingDomainRankProbe probe = CreateSortingDomainRankProbe(parent, parent, domain,
                    projection, name, position, layer, 0);
                probes.Add(probe);
                foreach (SpriteRenderer part in probe.SourceParts)
                {
                    part.sprite = sprite;
                    part.sharedMaterial = material;
                }
                probe.Projection.Synchronize(projection, true);
                return probe;
            }
            SortingDomainRankProbe floor = AddProbe("OverlayFloor", Vector3.down * 100f, floorMaterial);
            SortingDomainRankProbe fiber = AddProbe("OverlayFiber", Vector3.up * 20f, fiberMaterial);
            wallMaterial = new Material(floorMaterial) { renderQueue = wallQueue };
            SortingDomainRankProbe wall = AddProbe("OverlayWall", Vector3.up * 200f, wallMaterial);
            depth.SetEntitySortingEnabled(true);
            foreach (int entity in new[] { 2, 1 })
            {
                Vector3 anchor = Vector3.up * 20f;
                depth.Set(ChunkDepthMeshRenderer.MachineDomain, entity, 0, sprite, machineMaterial,
                    Matrix4x4.Translate(anchor), anchor, entity == 1 ? -64 : -32, Color.white);
            }
            depth.Flush();
            machineProjection = WorldPresentationProjection.Create(source.transform, parent);
            machineProjection.SetSortingDomain(domain);
            machineProjection.Synchronize(projection, true);
            domain.FlushLocalSortingKeys();
            bool machinesAboveFloor = true, machinesBelowWall = true, machineQueuesMatch = true;
            int pipeOrder = 0, wireOrder = 0;
            foreach (int entity in new[] { 1, 2 })
            {
                Transform raw = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.MachineDomain, entity, 0);
                bool found = machineProjection.TryGetProjectedTransform(raw, out Transform projected);
                SortingGroup group = found ? projected.GetComponent<SortingGroup>() : null;
                MeshRenderer renderer = found ? projected.GetComponentInChildren<MeshRenderer>() : null;
                machinesAboveFloor &= group != null && group.sortingOrder > floor.DisplayGroup.sortingOrder;
                machinesBelowWall &= group != null && group.sortingOrder < wall.DisplayGroup.sortingOrder;
                machineQueuesMatch &= renderer != null && renderer.sharedMaterial.renderQueue == facilityQueue;
                if (group != null) { if (entity == 1) pipeOrder = group.sortingOrder; else wireOrder = group.sortingOrder; }
            }
            return new JObject
            {
                ["supportQueue"] = supportQueue, ["facilityQueue"] = facilityQueue, ["wallQueue"] = wallQueue,
                ["floorMaterialMatchesSupport"] = floorMaterial.renderQueue == supportQueue,
                ["fiberMaterialMatchesFacility"] = fiberMaterial.renderQueue == facilityQueue,
                ["machineQueuesMatch"] = machineQueuesMatch,
                ["machinesAboveFloor"] = machinesAboveFloor, ["machinesBelowWall"] = machinesBelowWall,
                ["connectionsKeepLayerOrder"] = pipeOrder < wireOrder && wireOrder < fiber.DisplayGroup.sortingOrder,
                ["fiberAboveFloor"] = fiber.DisplayGroup.sortingOrder > floor.DisplayGroup.sortingOrder,
                ["fiberBelowWall"] = fiber.DisplayGroup.sortingOrder < wall.DisplayGroup.sortingOrder
            };
        }
        finally
        {
            machineProjection?.Release();
            foreach (SortingDomainRankProbe probe in probes)
            {
                probe.Projection.Release();
                UnityEngine.Object.DestroyImmediate(probe.Source);
            }
            UnityEngine.Object.DestroyImmediate(source);
            if (wallMaterial != null) UnityEngine.Object.DestroyImmediate(wallMaterial);
        }
#else
        throw new InvalidOperationException("贴地线路排序采样需要 Unity Editor。");
#endif
    }

    private static JObject CaptureRotatedEntityDepthSorting(Sprite sprite, Material material, Transform originalParent,
        int sortingLayerId, int rawOrder)
    {
        var source = new GameObject("[LuaEntityDepthSource]") { hideFlags = HideFlags.HideAndDontSave };
        source.transform.SetParent(originalParent, false);
        ChunkDepthMeshRenderer depth = null;
        WorldPresentationSortingDomain domain = null;
        WorldPresentationProjection projection = null;
        bool sameSourceRow = false, entitiesSplit = false, projectedYDiffers = false, rankFollowsProjectedY = false;
        bool projectedGroupsInDomain = false, splitPartOrderPreserved = false;
        bool rawKeyRestoredOnExit = false, sourceRenderersRestored = false;
        bool disabledModeRejoinedRow = false, mergedPartOrderPreserved = false;
        int rowCountBeforeSplit = 0, rowCountAfterSplit = 0, rowCountAfterExit = 0;
        int entityAOrder = 0, entityBOrder = 0;
        float entityAY = 0f, entityBY = 0f;
        try
        {
            depth = new ChunkDepthMeshRenderer(source.transform, sortingLayerId, rawOrder, material.renderQueue);
            void SetEntityPart(int entity, int part, int partOrder, Vector3 anchor)
            {
                Matrix4x4 world = Matrix4x4.TRS(anchor, Quaternion.identity, Vector3.one);
                depth.Set(ChunkDepthMeshRenderer.BuildingDomain, entity, part, sprite, material,
                    world, anchor, partOrder, Color.white, sortingLayerId: sortingLayerId, sortingOrder: rawOrder);
            }

            Vector3 entityAAnchor = new(.5f, .5f, 0f);
            Vector3 entityBAnchor = new(1.5f, .5f, 0f);
            SetEntityPart(101, 0, 0, entityAAnchor);
            SetEntityPart(101, 1, 1, entityAAnchor);
            SetEntityPart(202, 0, 0, entityBAnchor);
            depth.Flush();
            Transform sourceEntityA = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 101, 0);
            Transform sourceEntityB = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 202, 0);
            rowCountBeforeSplit = depth.RowCount;
            sameSourceRow = sourceEntityA != null && sourceEntityA == sourceEntityB && rowCountBeforeSplit == 1 &&
                Mathf.Abs(entityAAnchor.x - entityBAnchor.x) > .5f &&
                Mathf.Abs(entityAAnchor.y - entityBAnchor.y) < .001f;

            depth.SetEntitySortingEnabled(true);
            depth.Flush();
            sourceEntityA = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 101, 0);
            sourceEntityB = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 202, 0);
            rowCountAfterSplit = depth.RowCount;
            SortingGroup sourceGroupA = sourceEntityA != null ? sourceEntityA.GetComponent<SortingGroup>() : null;
            SortingGroup sourceGroupB = sourceEntityB != null ? sourceEntityB.GetComponent<SortingGroup>() : null;
            entitiesSplit = sourceEntityA != null && sourceEntityB != null && sourceEntityA != sourceEntityB &&
                rowCountAfterSplit == 2 && sourceGroupA != null && sourceGroupB != null &&
                sourceGroupA.sortingLayerID == sortingLayerId && sourceGroupA.sortingOrder == rawOrder &&
                sourceGroupB.sortingLayerID == sortingLayerId && sourceGroupB.sortingOrder == rawOrder &&
                sourceEntityA.position.x != sourceEntityB.position.x &&
                Mathf.Abs(sourceEntityA.position.y - sourceEntityB.position.y) < .001f;

            domain = WorldPresentationSortingDomain.Create(originalParent, "probe:rotated-entity-depth",
                WorldSortingManager.ProjectedWorldCategory);
            domain.Synchronize(Matrix4x4.identity, true);
            projection = WorldPresentationProjection.Create(source.transform, originalParent);
            projection.SetSortingDomain(domain);
            Matrix4x4 rotateWorld = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f));
            projection.Synchronize(rotateWorld, true);
            domain.FlushLocalSortingKeys();
            bool hasProjectedA = projection.TryGetProjectedTransform(sourceEntityA, out Transform projectedEntityA);
            bool hasProjectedB = projection.TryGetProjectedTransform(sourceEntityB, out Transform projectedEntityB);
            SortingGroup projectedGroupA = hasProjectedA ? projectedEntityA.GetComponent<SortingGroup>() : null;
            SortingGroup projectedGroupB = hasProjectedB ? projectedEntityB.GetComponent<SortingGroup>() : null;
            entityAY = projectedEntityA != null ? projectedEntityA.position.y : 0f;
            entityBY = projectedEntityB != null ? projectedEntityB.position.y : 0f;
            entityAOrder = projectedGroupA != null ? projectedGroupA.sortingOrder : 0;
            entityBOrder = projectedGroupB != null ? projectedGroupB.sortingOrder : 0;
            float expectedEntityAY = sourceEntityA != null ? rotateWorld.MultiplyPoint3x4(sourceEntityA.position).y : float.NaN;
            float expectedEntityBY = sourceEntityB != null ? rotateWorld.MultiplyPoint3x4(sourceEntityB.position).y : float.NaN;
            projectedGroupsInDomain = projection.SortingDomain == domain && projectedEntityA != null &&
                projectedEntityB != null && projectedEntityA.IsChildOf(domain.Root) && projectedEntityB.IsChildOf(domain.Root);
            projectedYDiffers = projectedGroupsInDomain && entityBY > entityAY + .001f &&
                Mathf.Abs(entityAY - expectedEntityAY) < .002f && Mathf.Abs(entityBY - expectedEntityBY) < .002f;
            rankFollowsProjectedY = projectedYDiffers && entityBOrder < entityAOrder && entityAOrder != entityBOrder &&
                projectedGroupA.sortingLayerID == sortingLayerId && projectedGroupB.sortingLayerID == sortingLayerId;
            bool hasPartOrderZero = false, hasPartOrderOne = false;
            if (projectedEntityA != null)
                foreach (MeshRenderer part in projectedEntityA.GetComponentsInChildren<MeshRenderer>(true))
                {
                    hasPartOrderZero |= part.sortingOrder == 0;
                    hasPartOrderOne |= part.sortingOrder == 1;
                }
            splitPartOrderPreserved = hasPartOrderZero && hasPartOrderOne;

            projection.SetSortingDomain(null);
            domain.FlushLocalSortingKeys();
            rawKeyRestoredOnExit = projection.SortingDomain == null && projectedGroupA != null && projectedGroupB != null &&
                projectedGroupA.sortingLayerID == sortingLayerId && projectedGroupA.sortingOrder == rawOrder &&
                projectedGroupB.sortingLayerID == sortingLayerId && projectedGroupB.sortingOrder == rawOrder &&
                sourceGroupA.sortingLayerID == sortingLayerId && sourceGroupA.sortingOrder == rawOrder &&
                sourceGroupB.sortingLayerID == sortingLayerId && sourceGroupB.sortingOrder == rawOrder;
            projection.Release();
            projection = null;
            sourceRenderersRestored = true;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
                sourceRenderersRestored &= !renderer.forceRenderingOff;

            depth.SetEntitySortingEnabled(false);
            depth.Flush();
            Transform mergedEntityA = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 101, 0);
            Transform mergedEntityB = depth.GetPartSortingRoot(ChunkDepthMeshRenderer.BuildingDomain, 202, 0);
            rowCountAfterExit = depth.RowCount;
            disabledModeRejoinedRow = rowCountAfterExit == 1 && mergedEntityA != null && mergedEntityA == mergedEntityB &&
                mergedEntityA.GetComponent<SortingGroup>().sortingLayerID == sortingLayerId &&
                mergedEntityA.GetComponent<SortingGroup>().sortingOrder == rawOrder;
            bool mergedOrderZero = false, mergedOrderOne = false;
            if (mergedEntityA != null)
                foreach (MeshRenderer part in mergedEntityA.GetComponentsInChildren<MeshRenderer>(true))
                {
                    mergedOrderZero |= part.sortingOrder == 0;
                    mergedOrderOne |= part.sortingOrder == 1;
                }
            mergedPartOrderPreserved = mergedOrderZero && mergedOrderOne;
        }
        finally
        {
            projection?.Release();
            depth?.Dispose();
            domain?.Dispose();
            UnityEngine.Object.DestroyImmediate(source);
        }
        return new JObject
        {
            ["sameSourceRow"] = sameSourceRow,
            ["entityRowsSplit"] = entitiesSplit,
            ["rotatedEntityFootpointsDiffer"] = projectedYDiffers,
            ["projectedEntityGroupsInDomain"] = projectedGroupsInDomain,
            ["rotatedEntityRanksFollowFootpoints"] = rankFollowsProjectedY,
            ["splitEntityPartOrdersPreserved"] = splitPartOrderPreserved,
            ["domainExitRestoresRawEntityKeys"] = rawKeyRestoredOnExit,
            ["domainExitRestoresSourceRenderers"] = sourceRenderersRestored,
            ["entityModeExitMergesRow"] = disabledModeRejoinedRow,
            ["mergedEntityPartOrdersPreserved"] = mergedPartOrderPreserved,
            ["rowsBeforeEntitySplit"] = rowCountBeforeSplit,
            ["rowsAfterEntitySplit"] = rowCountAfterSplit,
            ["rowsAfterEntityModeExit"] = rowCountAfterExit,
            ["entityAFootY"] = entityAY,
            ["entityBFootY"] = entityBY,
            ["entityARankOrder"] = entityAOrder,
            ["entityBRankOrder"] = entityBOrder
        };
    }

    // 读取三条正式水面材质，颜色关系由 Lua 判断，不复算 Shader。
    private static JObject CaptureSharedWaterSurfaceMaterialPalette(JObject args)
    {
        var materials = new List<Material>
        {
            Resources.Load<Material>("Config/WorldModel/BRG/ChunkMesh-Water-Lit"),
            Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Water-Lit")
        };
#if UNITY_EDITOR
        materials.Insert(0, UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/9_Shaders/Material/Tilemap-Water-Lit.mat"));
#endif
        var samples = new JArray();
        foreach (Material material in materials)
        {
            var colors = new JObject();
            foreach (string property in new[] { "_DeepColor", "_ShallowColor", "_FoamColor" })
            {
                Color color = material.GetColor(property);
                colors[property] = new JArray(color.r, color.g, color.b);
            }
            var strengths = new JObject();
            foreach (string property in new[] { "_ReflectionStrength", "_SpecularStrength", "_RippleStrength",
                "_WhitecapStrength", "_ShoreFoamStrength" }) strengths[property] = material.GetFloat(property);
            samples.Add(new JObject { ["name"] = material.name, ["colors"] = colors, ["strengths"] = strengths,
                ["shaderSupported"] = material.shader.isSupported, ["passCount"] = material.passCount,
                ["litPass"] = material.FindPass("Universal2D"), ["forwardPass"] = material.FindPass("UniversalForward") });
        }
        return new JObject { ["materials"] = samples };
    }

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
                Item = Root.AddComponent<GameplayTestRenderingLifecycleItem>();
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

// 编辑态夹具也接收 Unity 的真实销毁回调，不用普通 MonoBehaviour 缺失回调制造伪失败。
[ExecuteAlways, AddComponentMenu("")]
internal sealed class GameplayTestRenderingLifecycleItem : GameItem
{
    #region 编辑态物品生命周期
    #endregion
}
