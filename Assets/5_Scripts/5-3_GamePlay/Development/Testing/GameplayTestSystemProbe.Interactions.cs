using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public static partial class GameplayTestSystemProbe
{
    #region 物理归属与地表采挖规则注册

    private static void RegisterInteractionOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("player/physics_source", InteractionPhysicsSource);
        operations.Add("player/aim_cursor", InteractionAimCursor);
        operations.Add("terrain/harvest_rules", InteractionHarvestRules);
        operations.Add("terrain/ground_cover_work", InteractionGroundCoverWork);
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
