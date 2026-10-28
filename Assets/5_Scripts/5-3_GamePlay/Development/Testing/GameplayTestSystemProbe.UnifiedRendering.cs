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

    private static void RegisterUnifiedRenderingOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/unified_manager", CaptureUnifiedItemRenderingManager);
        operations.Add("rendering/item_sorting_configuration", CaptureItemConfiguredWorldSorting);
        operations.Add("rendering/water_surface_palette", CaptureSharedWaterSurfaceMaterialPalette);
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
        try
        {
            resources.ItemDefinitions[current.Id] = current;
            dropped = DroppedItemVisual.Resolve(item.itemData, fixture.Sprite.sprite);
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
            if (definition.Id is "ShipFloor_Iron" or "ShipFloor_Refractory" or "ShipFloor_Iron_Summoner" or "Stick_Wood" or "RailTrack")
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
            ["playerLayerValue"] = SortingLayer.GetLayerValueFromID(playerLayer), ["playerOrder"] = playerOrder,
            ["rejectsUnknownLayer"] = Rejects(new ItemVisualDefinitionDto { SortingLayerName = "probe:missing-layer" }),
            ["rejectsEmptyLayer"] = Rejects(new ItemVisualDefinitionDto { SortingLayerName = "" }),
            ["rejectsOrderUnderflow"] = Rejects(new ItemVisualDefinitionDto { SortingOrder = short.MinValue - 1 }),
            ["rejectsOrderOverflow"] = Rejects(new ItemVisualDefinitionDto { SortingOrder = short.MaxValue + 1 }) };
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
        var existing = new HashSet<int>();
        foreach (GameObject previous in scene.GetRootGameObjects()) existing.Add(previous.GetInstanceID());
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 10f; camera.aspect = 1f;
        var actorIds = new[] { "Wolf", "Chicken" };
        WorldSortingManager.ResolveItemVisualSortingKey(new ItemVisualDefinitionDto { SortingLayerName = "Default", SortingOrder = 37 },
            playerLayer, playerOrder, out int customLayer, out int customOrder);
        var keys = new[] { new AiecsActorSortingKey(playerLayer, playerOrder), new AiecsActorSortingKey(customLayer, customOrder) };
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.GroundShadowCategory, out int shadowLayer, out int shadowOrder);
        AiecsWorldRenderer display = null;
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
                    Facing = new float2(1f, 0f), Hp = 1f, MaxHp = 1f
                };
            List<MeshRenderer> Renderers()
            {
                var result = new List<MeshRenderer>();
                foreach (GameObject node in scene.GetRootGameObjects())
                    if (!existing.Contains(node.GetInstanceID()) && node.name == "AIECS 连续绘制批次")
                    {
                        MeshRenderer value = node.GetComponent<MeshRenderer>();
                        if (value != null) result.Add(value);
                    }
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
                ["activeAfterCull"] = activeAfterCull, ["hidden"] = hidden };
        }
        finally
        {
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
            return new JObject
            {
                ["sameRoot"] = body != null && body == attachment,
                ["separateKeys"] = separateKeys,
                ["outerKey"] = group != null && group.sortingLayerID == playerLayer && group.sortingOrder == playerOrder,
                ["innerOrders"] = innerZero && innerOne,
                ["initialRows"] = initialRows, ["remainingRows"] = depth.RowCount, ["rekeyed"] = rekeyed,
                ["snapshots"] = snapshotBody && snapshotAttachment && snapshotOther,
                ["snapshotOrdering"] = outerBeforeY && yBeforeInner && sameGroupInnerOrder
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
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
