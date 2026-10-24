using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

public static partial class GameplayTestSystemProbe
{
    #region 物理归属与地表采挖规则注册

    private static void RegisterInteractionOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("player/physics_source", InteractionPhysicsSource);
        operations.Add("player/aim_cursor", InteractionAimCursor);
        operations.Add("player/focus_aim_batch", InteractionFocusAimBatch);
        operations.Add("terrain/harvest_rules", InteractionHarvestRules);
        operations.Add("terrain/ground_cover_work", InteractionGroundCoverWork);
        operations.Add("player/interaction_query_context", InteractionQueryContext);
        operations.Add("player/interaction_registry_prefilter", InteractionRegistryPrefilter);
        operations.Add("player/grid_interaction_bands", InteractionGridBands);
        operations.Add("inventory/discard_hover_query", InventoryDiscardHoverQuery);
        operations.Add("terrain/contact_query_context", TerrainContactQueryContext);
    }

    #endregion

    #region 注册交互目标的距离预筛回归

    // 正式注册表逐项核对活跃、距离、鼠标命中及同帧移动后的实时行为。
    private static JObject InteractionRegistryPrefilter(JObject args)
    {
        var root = new GameObject("[InteractionRegistryPrefilter]") { hideFlags = HideFlags.HideAndDontSave };
        var registered = new List<GameplayTestSpatialInteractionTarget>();
        try
        {
            var actorHost = new GameObject("Actor");
            actorHost.transform.SetParent(root.transform, false);
            actorHost.SetActive(false);
            actorHost.transform.position = new Vector3(15.5f, .5f);
            Item actor = actorHost.AddComponent<GameItem>();

            GameplayTestSpatialInteractionTarget Create(string name, float x, bool allowed, float pointerRadius)
            {
                var host = new GameObject(name);
                host.transform.SetParent(root.transform, false);
                host.transform.position = new Vector3(x, .5f);
                var target = host.AddComponent<GameplayTestSpatialInteractionTarget>();
                target.Allowed = allowed;
                registered.Add(target);
                SpatialInteractionRegistry.Register(target, pointerRadius);
                return target;
            }

            var near = Create("Near", 15.25f, true, .5f);
            var seam = Create("Seam", -15.5f, true, .75f);
            var far = Create("Far", 3f, true, .75f);
            var rejected = Create("Rejected", 15f, false, .75f);
            var inactive = Create("Inactive", 15.3f, true, .75f);
            inactive.gameObject.SetActive(false);

            var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
            var results = new List<IInteractable>();
            SpatialInteractionRegistry.QueryRegisteredTargets(actor, 1f, null, results, wrapped);
            bool correctRange = results.Count == 2 && results.Contains(near) && results.Contains(seam) &&
                !results.Contains(far) && !results.Contains(rejected) && !results.Contains(inactive);
            bool distantSkipped = far.EligibilityChecks == 0 && inactive.EligibilityChecks == 0 &&
                rejected.EligibilityChecks == 1 && near.EligibilityChecks == 1 && seam.EligibilityChecks == 1;

            results.Clear();
            SpatialInteractionRegistry.QueryRegisteredTargets(actor, 1f, new Vector2(-15.5f, .5f), results, wrapped);
            bool pointerMatched = results.Count == 1 && ReferenceEquals(results[0], seam);

            results.Clear();
            SpatialInteractionRegistry.QueryRegisteredTargets(actor, 1f, null, results, default);
            bool infiniteRange = results.Count == 1 && ReferenceEquals(results[0], near);

            far.transform.position = new Vector3(15.75f, .5f);
            results.Clear();
            SpatialInteractionRegistry.QueryRegisteredTargets(actor, 1f, null, results, wrapped);
            bool movedTargetUpdated = results.Count == 3 && results.Contains(far) && far.EligibilityChecks == 1;

            results.Clear();
            SpatialInteractionRegistry.QueryRegisteredTargets(actor, -.5f, null, results, wrapped);
            bool negativeRadiusRejected = results.Count == 0;

            bool radiusEdges = SpatialInteractionRegistry.IsWithinInteractionRadius(
                new Vector2(15.5f, .5f), new Vector2(-15.5f, .5f), 1f, wrapped) &&
                !SpatialInteractionRegistry.IsWithinInteractionRadius(
                    new Vector2(15.5f, .5f), new Vector2(-15.5f, .5f), .99f, wrapped) &&
                !SpatialInteractionRegistry.IsWithinInteractionRadius(
                    new Vector2(15.5f, .5f), new Vector2(-15.5f, .5f), 1f, default) &&
                SpatialInteractionRegistry.IsWithinInteractionRadius(Vector2.zero, Vector2.zero, 0f, default);

            return new JObject
            {
                ["correctRange"] = correctRange, ["distantSkipped"] = distantSkipped,
                ["pointerMatched"] = pointerMatched, ["infiniteRange"] = infiniteRange,
                ["movedTargetUpdated"] = movedTargetUpdated,
                ["negativeRadiusRejected"] = negativeRadiusRejected, ["radiusEdges"] = radiusEdges
            };
        }
        finally
        {
            for (int i = 0; i < registered.Count; i++)
                SpatialInteractionRegistry.Unregister(registered[i]);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion

    #region 角色瞄准单轮拓扑与方向回归

    // 只对照正式瞄准方向入口，覆盖环绕接缝、翻身直瞄和停止半径。
    private static JObject InteractionFocusAimBatch(JObject args)
    {
        Vector2[] starts =
        {
            new(15.5f, .5f), new(-15.5f, .5f), Vector2.zero,
            Vector2.zero, new(-15.5f, -15.5f), new(3.5f, 3.5f)
        };
        Vector2[] targets =
        {
            new(-15.5f, .5f), new(15.5f, .5f), new(16f, 0f),
            new(-16f, 0f), new(15.5f, 15.5f), new(3.5f, 3.5f)
        };
        Vector2[] wrappedExpected =
        {
            new(1f, 0f), new(-1f, 0f), new(-16f, 0f),
            new(-16f, 0f), new(-1f, -1f), Vector2.zero
        };
        float[] stopDistances = { -1f, 0f, .1f, 1f, 2f, 5f, 32f };
        var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
        bool deltasCorrect = true, stopsCorrect = true;
        int sampleCount = 0;
        for (int worldIndex = 0; worldIndex < 2; worldIndex++)
        for (int directIndex = 0; directIndex < 2; directIndex++)
        for (int i = 0; i < starts.Length; i++)
        {
            bool isWrapped = worldIndex != 0;
            bool direct = directIndex != 0;
            WorldTopologyDomain topology = isWrapped ? wrapped : default;
            Vector2 actual = Mod_FocusPoint.ResolveAimDelta(starts[i], targets[i], direct, topology);
            Vector2 expected = isWrapped && !direct ? wrappedExpected[i] : targets[i] - starts[i];
            deltasCorrect &= (actual - expected).sqrMagnitude < .00000001f;
            float oldDistance = isWrapped && !direct
                ? topology.Distance(new float2(starts[i].x, starts[i].y),
                    new float2(targets[i].x, targets[i].y))
                : Vector2.Distance(starts[i], targets[i]);
            for (int j = 0; j < stopDistances.Length; j++)
            {
                float threshold = stopDistances[j];
                stopsCorrect &= Mod_FocusPoint.IsWithinAimStopDistance(actual, threshold) ==
                    (oldDistance <= threshold);
                sampleCount++;
            }
        }

        bool specialThresholds = !Mod_FocusPoint.IsWithinAimStopDistance(Vector2.one, float.NaN) &&
            !Mod_FocusPoint.IsWithinAimStopDistance(Vector2.one, -1f) &&
            Mod_FocusPoint.IsWithinAimStopDistance(Vector2.one, float.PositiveInfinity) &&
            !Mod_FocusPoint.IsWithinAimStopDistance(Vector2.one, float.NegativeInfinity);
        return new JObject
        {
            ["deltasCorrect"] = deltasCorrect, ["stopsCorrect"] = stopsCorrect,
            ["specialThresholds"] = specialThresholds, ["samples"] = sampleCount
        };
    }

    #endregion

    #region 同格交互分区

    private sealed class GridInteractionProbeTarget : IGridInteractionTarget
    {
        public int TargetGuid { get; set; }
        public int Layer;
        public string GridId = "probe:grid";
        public Vector3 WorldPosition => new(.5f, .5f);
        public bool IsValid => true;
        public void OnInteractStart(Item actor) { }
        public void OnInteractCancel(Item actor) { }
        public bool TryGetInteractionGridHit(Vector2 pointer, WorldTopologyDomain topology, out GridInteractionHit hit)
        {
            hit = new GridInteractionHit(GridId, Vector2Int.zero, Layer, pointer.y);
            return pointer.x >= 0f && pointer.x < 1f && pointer.y >= 0f && pointer.y < 1f;
        }
    }

    // 只提交候选并采样正式选择器，层级、份数和边界的期望由 Lua 判断。
    private static JObject InteractionGridBands(JObject args)
    {
        var selector = new GridInteractionSelector();
        var candidates = new List<IInteractable>();
        var targets = new List<GridInteractionProbeTarget>();
        JArray layers = args["layers"] as JArray ?? new JArray(-1, 0);
        for (int i = 0; i < layers.Count; i++)
        {
            var target = new GridInteractionProbeTarget { TargetGuid = i + 1, Layer = (int)layers[i] };
            targets.Add(target); candidates.Add(target);
            if (Flag(args, "duplicate", false)) candidates.Add(target);
        }
        if (Flag(args, "reverse", false)) candidates.Reverse();
        var ordinary = new CompletedInteractionProbe();
        var otherGrid = new GridInteractionProbeTarget { GridId = "probe:other-grid", TargetGuid = 100 };
        candidates.Add(ordinary); candidates.Add(otherGrid);
        selector.FilterPointerTargets(candidates, new Vector2(.5f, (float)Number(args, "height", .5d)), default);
        int count = 0, selected = 0;
        foreach (GridInteractionProbeTarget target in targets)
            if (candidates.Contains(target)) { count++; selected = target.TargetGuid; }
        bool ordinaryPreserved = candidates.Contains(ordinary), otherGridPreserved = candidates.Contains(otherGrid);
        candidates.Clear(); candidates.Add(targets[0]);
        selector.FilterPointerTargets(candidates, new Vector2(.5f, .1f), default);

        var pipe = new MachineDefinition { Fluid = new FluidMachineDefinition { Kind = "pipe" } };
        var wire = new MachineDefinition { Electrical = new ElectricalDefinition { Role = "wire" } };
        var device = new MachineDefinition { PlacementLayer = 0 };
        using var machine = new MachineInteractionTarget(new MachineEntity
        { Cell = Vector2Int.zero, ScopeKey = "probe:machine", Definition = wire });
        return new JObject
        {
            ["count"] = count, ["selected"] = selected,
            ["ordinaryPreserved"] = ordinaryPreserved, ["otherGridPreserved"] = otherGridPreserved,
            ["reusedCount"] = candidates.Count,
            ["reusedSelected"] = candidates.Count > 0 ? ((IWorldInteractionTarget)candidates[0]).TargetGuid : 0,
            ["pipeLayer"] = pipe.VisualLayer, ["wireLayer"] = wire.VisualLayer, ["deviceLayer"] = device.VisualLayer,
            ["cellTopHit"] = machine.TryGetInteractionGridHit(new Vector2(.99f, .99f), default, out _),
            ["cellBottomHit"] = machine.TryGetInteractionGridHit(new Vector2(.01f, .01f), default, out _),
            ["neighbourHit"] = machine.TryGetInteractionGridHit(new Vector2(.5f, 1f), default, out _)
        };
    }

    #endregion

    #region 手机准线距离规则

    // 只调用正式准线计算并返回坐标采样，有限性和距离断言交给 Lua。
    private static JObject InteractionAimCursor(JObject args)
    {
        float minimum = (float)Number(args, "minimum", 0d);
        float maximum = (float)Number(args, "maximum", 2d);
        float strength = (float)Number(args, "strength", 1d);
        switch (Text(args, "invalid", string.Empty))
        {
            case "maximum_infinity": maximum = float.PositiveInfinity; break;
            case "maximum_negative_infinity": maximum = float.NegativeInfinity; break;
            case "maximum_nan": maximum = float.NaN; break;
            case "minimum_infinity": minimum = float.PositiveInfinity; break;
            case "minimum_nan": minimum = float.NaN; break;
            case "strength_nan": strength = float.NaN; break;
        }

        var cursor = new PlayerAimCursorSystem();
        Vector2 direction = Flag(args, "zeroDirection", false) ? Vector2.zero : Vector2.right;
        Vector3 position = cursor.CalculateRadialWorldPosition(Vector3.zero, direction, strength, minimum, maximum);
        bool finite = !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                      !float.IsNaN(position.y) && !float.IsInfinity(position.y);
        var result = new JObject { ["finite"] = finite };
        if (finite)
        {
            result["distance"] = WorldTopologyRuntime.ShortestDelta(Vector2.zero, (Vector2)position).magnitude;
            result["x"] = position.x;
            result["y"] = position.y;
            result["z"] = position.z;
        }
        return result;
    }

    #endregion

    #region 植被交互次数与目标身份

    private static JObject InteractionGroundCoverWork(JObject args)
    {
        using var firstBuffer = new ChunkTerrainBuffer(2, 2);
        using ChunkTerrainData first = firstBuffer.Seal();
        using var secondBuffer = new ChunkTerrainBuffer(2, 2);
        using ChunkTerrainData second = secondBuffer.Seal();
        var work = new GroundCoverHarvestProgress();
        bool unselectedCompleted = work.Advance(4);
        int unselectedUses = work.CompletedUses;
        work.SelectTarget(first, Vector2Int.zero, 0, true);
        bool scissorsCompleted = work.Advance(1);
        int scissorsUses = work.CompletedUses;
        work.Reset();
        var stoneCompleted = new JArray();
        var stoneUses = new JArray();
        for (int i = 0; i < 4; i++)
        {
            // 每次重新解析同一目标，进度仍由正式生产状态累积。
            work.SelectTarget(first, Vector2Int.zero, 0, true);
            stoneCompleted.Add(work.Advance(4));
            stoneUses.Add(work.CompletedUses);
        }
        bool retryCompleted = work.Advance(4);
        int retryUses = work.CompletedUses;
        work.Reset();
        int resetUses = work.CompletedUses;
        bool resetCompleted = work.Advance(4);
        int afterResetUses = work.CompletedUses;
        bool zeroUsesRejected = false, negativeUsesRejected = false;
        try { work.Advance(0); }
        catch (ArgumentOutOfRangeException) { zeroUsesRejected = true; }
        try { work.Advance(-1); }
        catch (ArgumentOutOfRangeException) { negativeUsesRejected = true; }
        int afterInvalidUses = work.CompletedUses;

        var identityWork = new GroundCoverHarvestProgress();
        identityWork.SelectTarget(first, Vector2Int.zero, 0, true);
        identityWork.Advance(4);
        JObject SelectChangedTarget(string kind, ChunkTerrainData terrain, Vector2Int cell, int guid, bool cuttingGrass)
        {
            int before = identityWork.CompletedUses;
            identityWork.SelectTarget(terrain, cell, guid, cuttingGrass);
            int afterSelect = identityWork.CompletedUses;
            bool completed = identityWork.Advance(4);
            return new JObject
            {
                ["kind"] = kind, ["before"] = before, ["afterSelect"] = afterSelect,
                ["completed"] = completed, ["afterWork"] = identityWork.CompletedUses
            };
        }
        Vector2Int otherCell = Vector2Int.right;
        var targetChanges = new JArray
        {
            SelectChangedTarget("cell", first, otherCell, 0, true),
            SelectChangedTarget("grass_to_flower", first, otherCell, 0, false),
            SelectChangedTarget("flower_guid", first, otherCell, 101, false),
            SelectChangedTarget("flower_to_grass", first, otherCell, 101, true),
            SelectChangedTarget("terrain", second, otherCell, 101, true)
        };

        first.SetGrass(0, 0, ChunkTerrainData.GrassEmpty);
        bool emptyGrassConsumed = first.TryConsumeGrass(0, 0);
        byte emptyGrassState = first.GetGrass(0, 0);
        first.SetGrass(0, 0, ChunkTerrainData.GrassPresent);
        bool presentGrassConsumed = first.TryConsumeGrass(0, 0);
        bool repeatGrassConsumed = first.TryConsumeGrass(0, 0);
        byte consumedGrassState = first.GetGrass(0, 0);
        second.Dispose();
        bool disposedCompleted = identityWork.Advance(4);

        return new JObject
        {
            ["unselectedCompleted"] = unselectedCompleted, ["unselectedUses"] = unselectedUses,
            ["scissorsCompleted"] = scissorsCompleted, ["scissorsUses"] = scissorsUses,
            ["stoneCompleted"] = stoneCompleted, ["stoneUses"] = stoneUses,
            ["retryCompleted"] = retryCompleted, ["retryUses"] = retryUses,
            ["targetChanges"] = targetChanges, ["resetUses"] = resetUses,
            ["resetCompleted"] = resetCompleted, ["afterResetUses"] = afterResetUses,
            ["zeroUsesRejected"] = zeroUsesRejected, ["negativeUsesRejected"] = negativeUsesRejected,
            ["afterInvalidUses"] = afterInvalidUses, ["disposedCompleted"] = disposedCompleted,
            ["disposedUses"] = identityWork.CompletedUses, ["emptyGrassConsumed"] = emptyGrassConsumed,
            ["emptyGrassState"] = emptyGrassState, ["expectedEmptyGrass"] = ChunkTerrainData.GrassEmpty,
            ["presentGrassConsumed"] = presentGrassConsumed, ["repeatGrassConsumed"] = repeatGrassConsumed,
            ["consumedGrassState"] = consumedGrassState
        };
    }

    #endregion

    #region 真实 Collider 镜像归源

    // 源层级保持活动以遵守命中解析契约，禁用碰撞和行为，不登记到游戏世界。
    private static JObject InteractionPhysicsSource(JObject args)
    {
        var originalRoot = new GameObject("GameplayTest Independent Collider Source") { hideFlags = HideFlags.HideAndDontSave };
        var mirrorRoot = new GameObject("GameplayTest Independent Collider Image") { hideFlags = HideFlags.HideAndDontSave };
        originalRoot.SetActive(false); mirrorRoot.SetActive(false);
        GameItem owner = originalRoot.AddComponent<GameItem>();
        owner.enabled = false;
        try
        {
            var body = new GameObject("Body");
            body.transform.SetParent(originalRoot.transform, false);
            Collider2D original = body.AddComponent<BoxCollider2D>();
            original.enabled = false;
            var sibling = new GameObject("Sibling Oxygen Capability");
            sibling.transform.SetParent(originalRoot.transform, false);
            Mod_Oxygen capability = sibling.AddComponent<Mod_Oxygen>();
            capability.enabled = false;
            Collider2D image = mirrorRoot.AddComponent<BoxCollider2D>();
            image.enabled = false;
            ColliderSource2D binding = mirrorRoot.AddComponent<ColliderSource2D>();
            binding.Bind(original, new Vector2((float)Number(args, "offsetX", 10d), (float)Number(args, "offsetY", -20d)));
            // 正式物理命中的源父级活动，组件禁用不影响组件归属查找。
            originalRoot.SetActive(true);
            bool sourceHierarchyActive = original.gameObject.activeInHierarchy;
            bool collidersDisabled = !original.enabled && !image.enabled;
            bool ordinaryResolved = ColliderSource2D.Resolve(original) == original;
            bool imageResolved = ColliderSource2D.Resolve(image) == original;
            bool ownerResolved = ReferenceEquals(GameplayPhysics2D.ResolveComponent<GameItem>(image), owner);
            bool siblingResolved = ReferenceEquals(GameplayPhysics2D.ResolveComponent<Mod_Oxygen>(image), capability);
            Vector2 offset = binding.ImageOffset;
            bool absentCapability = GameplayPhysics2D.ResolveComponent<Mod_FishingRod>(image) == null;
            UnityEngine.Object.DestroyImmediate(original);
            mirrorRoot.AddComponent<Mod_Oxygen>();
            bool destroyedRejected = ColliderSource2D.Resolve(image) == null &&
                GameplayPhysics2D.ResolveComponent<Mod_Oxygen>(image) == null &&
                GameplayPhysics2D.ResolveComponent<GameItem>(image) == null;
            binding.Bind(null, Vector2.zero);
            bool clearedRejected = ColliderSource2D.Resolve(image) == null &&
                GameplayPhysics2D.ResolveComponent<Mod_Oxygen>(image) == null;
            return new JObject
            {
                ["ordinaryResolved"] = ordinaryResolved, ["imageResolved"] = imageResolved,
                ["ownerResolved"] = ownerResolved, ["siblingResolved"] = siblingResolved,
                ["sourceHierarchyActive"] = sourceHierarchyActive, ["collidersDisabled"] = collidersDisabled,
                ["offsetX"] = offset.x, ["offsetY"] = offset.y, ["absentCapability"] = absentCapability,
                ["destroyedRejected"] = destroyedRejected, ["clearedRejected"] = clearedRejected,
                ["nullRejected"] = ColliderSource2D.Resolve(null) == null
            };
        }
        finally
        {
            try { owner.PrepareForDespawn(saveData: false); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mirrorRoot);
                UnityEngine.Object.DestroyImmediate(originalRoot);
            }
        }
    }

    #endregion

    #region 正式工具品质与地层状态

    private static JObject InteractionHarvestRules(JObject args)
    {
        var root = new GameObject("GameplayTest Independent Harvest Tool") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Mod_ResourceTool tool = root.AddComponent<Mod_ResourceTool>();
            var parameters = new JObject
            {
                ["harvestTier"] = (int)Number(args, "tier", 1d),
                ["harvestEfficiency"] = (float)Number(args, "efficiency", 1d),
                ["toolKind"] = Text(args, "toolKind", "Pickaxe")
            };
            bool toolValid = true;
            string toolError = null;
            try
            {
                tool.ApplyResourceConfiguration("test:harvest_tool", "test-harvest-tool",
                    parameters.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (InvalidOperationException exception) { toolValid = false; toolError = exception.Message; }
            bool readOnlyParameterRejected = false;
            try
            {
                ModuleJsonConfigurator.Validate(tool, "test:harvest_tool", "test-harvest-tool", Mod_ResourceTool.ModuleId,
                    "{\"HarvestKind\":\"Pickaxe\"}");
            }
            catch (InvalidOperationException) { readOnlyParameterRejected = true; }
            int layerCount = (int)Number(args, "layers", 1d);
            var rule = new GroundTileHarvestRule
            {
                RequiredTool = ResourceToolKind.Pickaxe, ItemId = "test:harvest_product",
                ReplacementTileId = layerCount > 1 ? "test:harvest_tile" : "test:replacement_tile",
                ExhaustedTileId = "test:exhausted_tile", NaturalLayerCount = layerCount,
                BaseUsesPerTile = (int)Number(args, "baseUses", 6d),
                MinimumUsesPerTile = (int)Number(args, "minimumUses", 2d),
                UseInterval = (float)Number(args, "interval", .2d)
            };
            RuntimeTileDefinition definition = null;
            bool ruleValid = true;
            string ruleError = null;
            try
            {
                definition = TileDefinitionFactory.Build(new TileDefinitionDto
                {
                    Id = "test:harvest_tile", RuntimeTileId = 1000042, TileAsset = "test:transient_tile",
                    Data = new TileComponentDefinitionDto { Type = "universal" }, GroundHarvest = rule
                }, _ => tile);
            }
            catch (System.IO.InvalidDataException exception) { ruleValid = false; ruleError = exception.Message; }
            using var buffer = new ChunkTerrainBuffer(2, 2);
            using ChunkTerrainData terrain = buffer.Seal();
            Vector2Int local = Vector2Int.zero;
            int depth = (int)Number(args, "depth", 2d);
            GroundTileLayerSystem.WriteDepth(terrain, local, depth);
            terrain.SetGrass(0, 0, 4);
            GroundLayerCellSaveData snapshot = GroundTileLayerSystem.Capture(terrain, local);
            GroundTileLayerSystem.WriteDepth(terrain, local, 9);
            terrain.SetGrass(0, 0, 0);
            GroundTileLayerSystem.Restore(terrain, snapshot);
            bool invalidDepthRejected = false;
            try { GroundTileLayerSystem.Restore(terrain, new GroundLayerCellSaveData { LocalPosition = local, ExcavatedLayers = 101 }); }
            catch (InvalidOperationException) { invalidDepthRejected = true; }
            var sample = new RuntimeTerrainTileSample(default, terrain, local, local, terrain.GetCell(0, 0), 0);
            terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, 0, 0, (float)Number(args, "progress", .4d));
            float progress = GroundTileHarvestSystem.ReadProgress(sample);
            terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, 0, 0, 5f);
            float highProgress = GroundTileHarvestSystem.ReadProgress(sample);
            terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, 0, 0, -1f);
            float lowProgress = GroundTileHarvestSystem.ReadProgress(sample);
            return new JObject
            {
                ["toolValid"] = toolValid, ["toolError"] = toolError, ["ruleValid"] = ruleValid, ["ruleError"] = ruleError,
                ["requiredUses"] = toolValid && ruleValid ? GroundTileHarvestSystem.ResolveRequiredUses(tool, definition.GroundHarvest) : -1,
                ["tier"] = tool.HarvestTier, ["efficiency"] = tool.HarvestEfficiency,
                ["readOnlyParameterRejected"] = readOnlyParameterRejected,
                ["interval"] = definition?.GroundHarvest.UseInterval, ["layers"] = definition?.GroundHarvest.NaturalLayerCount,
                ["restoredDepth"] = GroundTileLayerSystem.ReadDepth(terrain, local), ["restoredGrass"] = terrain.GetGrass(0, 0),
                ["invalidDepthRejected"] = invalidDepthRejected, ["progress"] = progress,
                ["highProgress"] = highProgress, ["lowProgress"] = lowProgress
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(tile);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion
}

// 编辑态采样用可注册的最小交互组件，统计远距离目标有没有走正式准入逻辑。
internal sealed class GameplayTestSpatialInteractionTarget : MonoBehaviour, IInteractable
{
    internal bool Allowed;
    internal int EligibilityChecks;
    internal int StartCount, EndCount;

    public bool CanInteract(Item actor)
    {
        EligibilityChecks++;
        return Allowed;
    }

    public void OnInteractStart(Item actor) => StartCount++;
    public void OnInteractEnd(Item actor) => EndCount++;
    public void OnInteractCancel(Item actor) { }
}
