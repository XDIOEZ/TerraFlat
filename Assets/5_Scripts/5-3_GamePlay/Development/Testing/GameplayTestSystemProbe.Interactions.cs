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
        operations.Add("terrain/harvest_rules", InteractionHarvestRules);
    }

    #endregion

    #region 真实 Collider 镜像归源

    // 临时对象保持停用，不模拟物理，不登记到游戏世界。
    private static JObject InteractionPhysicsSource(JObject args)
    {
        var originalRoot = new GameObject("GameplayTest Independent Collider Source") { hideFlags = HideFlags.HideAndDontSave };
        var mirrorRoot = new GameObject("GameplayTest Independent Collider Image") { hideFlags = HideFlags.HideAndDontSave };
        originalRoot.SetActive(false); mirrorRoot.SetActive(false);
        GameItem owner = originalRoot.AddComponent<GameItem>();
        try
        {
            var body = new GameObject("Body");
            body.transform.SetParent(originalRoot.transform, false);
            Collider2D original = body.AddComponent<BoxCollider2D>();
            var sibling = new GameObject("Sibling Oxygen Capability");
            sibling.transform.SetParent(originalRoot.transform, false);
            Mod_Oxygen capability = sibling.AddComponent<Mod_Oxygen>();
            Collider2D image = mirrorRoot.AddComponent<BoxCollider2D>();
            ColliderSource2D binding = mirrorRoot.AddComponent<ColliderSource2D>();
            binding.Bind(original, new Vector2((float)Number(args, "offsetX", 10d), (float)Number(args, "offsetY", -20d)));
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
                ModuleJsonConfigurator.Apply(tool, "test:harvest_tool", "test-harvest-tool", Mod_ResourceTool.ModuleId,
                    parameters.ToString(Newtonsoft.Json.Formatting.None));
                tool.OnResourcesReloaded();
            }
            catch (InvalidOperationException exception) { toolValid = false; toolError = exception.Message; }
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
