using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 自然环境声正式规则采样
    private static void RegisterNatureAmbientAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/nature_ambience_sequence", ServiceNatureAmbienceSequence);
        operations.Add("audio/foliage_ambience_volume", ServiceFoliageAmbienceVolume);
        operations.Add("audio/nature_foliage_config", ServiceNatureFoliageConfig);
    }

    private static JObject ServiceNatureAmbienceSequence(JObject args)
    {
        using ChunkTerrainData terrain = NatureAmbienceTerrainFixture();
        var waterContactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LiquidTypeCatalog.DirtyWaterId,
            LiquidTypeCatalog.SeaWaterId
        };
        bool IsWaterContactLiquid(string id) => waterContactIds.Contains(id);
        using var index = new RiverAmbientAudioChunkIndex(terrain, IsWaterContactLiquid);

        float lowInitial = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 0, 0, IsWaterContactLiquid);
        float highInitial = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 1, 0, IsWaterContactLiquid);
        float frozenInitial = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 2, 0, IsWaterContactLiquid);
        float lake = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 3, 0, IsWaterContactLiquid);
        float sea = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 4, 0, IsWaterContactLiquid);
        float dry = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 5, 0, IsWaterContactLiquid);
        float nonWaterContact = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 6, 0, IsWaterContactLiquid);

        JObject initial = CaptureNatureAmbientIndex(index, new Vector2(0.5f, 0.5f), 0f);
        JObject radiusBoundary = CaptureNatureAmbientIndex(index, new Vector2(1.5f, 1.5f), 1f);
        JObject outsideRadius = CaptureNatureAmbientIndex(index, new Vector2(1.5f, 1.5f), 0.99f);
        JObject emptyRadius = CaptureNatureAmbientIndex(index, new Vector2(3.5f, 0.5f), 0.4f);

        terrain.SetLiquid(0, 0, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 40);
        float afterSingleLiquid = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 0, 0, IsWaterContactLiquid);
        long afterSingleLiquidRevision = index.Revision;
        int afterSingleLiquidCandidateCount = index.CandidateCount;
        terrain.SetEnvironmentValue("riverFlow", 0, 0, 0.9f);
        float afterSingleEnvironment = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 0, 0, IsWaterContactLiquid);
        long afterSingleEnvironmentRevision = index.Revision;
        int afterSingleEnvironmentCandidateCount = index.CandidateCount;

        long beforeUnrelatedLayer = index.Revision;
        terrain.SetEnvironmentValue("moisture", 0, 0, 0.75f);
        long afterUnrelatedLayer = index.Revision;

        int waterType = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(1, waterType, 90) });
        float afterLiquidBatch = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 1, 0, IsWaterContactLiquid);
        long afterLiquidBatchRevision = index.Revision;
        int afterLiquidBatchCandidateCount = index.CandidateCount;
        terrain.SetEnvironmentBatch("riverFlow", new[] { new EnvironmentCellUpdate(1, 1.2f) });
        float afterEnvironmentBatch = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 1, 0, IsWaterContactLiquid);
        long afterEnvironmentBatchRevision = index.Revision;
        int afterEnvironmentBatchCandidateCount = index.CandidateCount;

        terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 2, 0, 0.5f);
        float halfThawed = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 2, 0, IsWaterContactLiquid);
        int candidateCountHalfThawed = index.CandidateCount;
        long revisionHalfThawed = index.Revision;
        terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, 2, 0, 1f);
        terrain.SetCell(2, 0, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        terrain.SetLiquid(2, 0, waterType, 80);
        float fullyThawed = RiverAmbientAudioChunkIndex.ResolveRiverCellStrength(terrain, 2, 0, IsWaterContactLiquid);

        long revisionBeforeDispose = index.Revision;
        int candidateCountBeforeDispose = index.CandidateCount;
        index.Dispose();
        terrain.SetLiquid(0, 0, 0, 0);
        long revisionAfterDispose = index.Revision;
        int candidateCountAfterDispose = index.CandidateCount;
        bool queryAfterDispose = index.TryFindNearest(new Vector2(0.5f, 0.5f), 2f, out _, out _);

        return new JObject
        {
            ["waterContactProvider"] = "explicit test fixture IDs; no GameRes lookup",
            ["waterContactIds"] = new JArray(LiquidTypeCatalog.DirtyWaterId, LiquidTypeCatalog.SeaWaterId),
            ["initial"] = initial,
            ["initialLowStrength"] = lowInitial,
            ["initialHighStrength"] = highInitial,
            ["frozenStrength"] = frozenInitial,
            ["lakeStrength"] = lake,
            ["seaStrength"] = sea,
            ["dryStrength"] = dry,
            ["nonWaterContactStrength"] = nonWaterContact,
            ["radiusBoundary"] = radiusBoundary,
            ["outsideRadius"] = outsideRadius,
            ["emptyRadius"] = emptyRadius,
            ["singleLiquidStrength"] = afterSingleLiquid,
            ["singleLiquidRevision"] = afterSingleLiquidRevision,
            ["singleLiquidCandidateCount"] = afterSingleLiquidCandidateCount,
            ["singleEnvironmentStrength"] = afterSingleEnvironment,
            ["singleEnvironmentRevision"] = afterSingleEnvironmentRevision,
            ["singleEnvironmentCandidateCount"] = afterSingleEnvironmentCandidateCount,
            ["unrelatedLayerRevisionBefore"] = beforeUnrelatedLayer,
            ["unrelatedLayerRevisionAfter"] = afterUnrelatedLayer,
            ["liquidBatchStrength"] = afterLiquidBatch,
            ["liquidBatchRevision"] = afterLiquidBatchRevision,
            ["liquidBatchCandidateCount"] = afterLiquidBatchCandidateCount,
            ["environmentBatchStrength"] = afterEnvironmentBatch,
            ["environmentBatchRevision"] = afterEnvironmentBatchRevision,
            ["environmentBatchCandidateCount"] = afterEnvironmentBatchCandidateCount,
            ["halfThawedStrength"] = halfThawed,
            ["halfThawedCandidateCount"] = candidateCountHalfThawed,
            ["halfThawedRevision"] = revisionHalfThawed,
            ["fullyThawedStrength"] = fullyThawed,
            ["revisionBeforeDispose"] = revisionBeforeDispose,
            ["revisionAfterDispose"] = revisionAfterDispose,
            ["candidateCountBeforeDispose"] = candidateCountBeforeDispose,
            ["candidateCountAfterDispose"] = candidateCountAfterDispose,
            ["queryAfterDispose"] = queryAfterDispose
        };
    }

    private static JObject CaptureNatureAmbientIndex(RiverAmbientAudioChunkIndex index,
        Vector2 listenerPosition, float radius)
    {
        bool found = index.TryFindNearest(listenerPosition, radius, out Vector2 localPosition, out float strength);
        return new JObject
        {
            ["candidateCount"] = index.CandidateCount,
            ["revision"] = index.Revision,
            ["found"] = found,
            ["x"] = localPosition.x,
            ["y"] = localPosition.y,
            ["strength"] = strength
        };
    }

    private static ChunkTerrainData NatureAmbienceTerrainFixture()
    {
        const int width = 7;
        const int iceTileId = 999;
        using var buffer = new ChunkTerrainBuffer(width, 1, LiquidTypeCatalog.BuiltIn);
        for (int x = 0; x < width; x++)
            buffer.SetCell(x, 0, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        buffer.SetCell(4, 0, new TerrainCell(1, 0, 0, (int)SurfaceBiomeKind.Ocean, 1, TerrainCellFlags.Walkable));
        var frozenSource = new FrozenSurfaceWaterSource(
            new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable), iceTileId,
            LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.DirtyWaterId), 80);
        buffer.SetCell(2, 0, new TerrainCell(iceTileId, 0, 0, 1, 2, TerrainCellFlags.Walkable));
        FrozenSurfaceWaterLayer.WriteGenerationSource(buffer, 2, frozenSource);
        ChunkTerrainData terrain = buffer.Seal();

        int water = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        terrain.SetLiquid(0, 0, water, 20);
        terrain.SetLiquid(1, 0, water, 80);
        terrain.SetLiquid(3, 0, water, 80);
        terrain.SetLiquid(4, 0, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId), 80);
        terrain.SetLiquid(6, 0, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId), 80);

        terrain.SetEnvironmentValue("riverKind", 0, 0, 1f);
        terrain.SetEnvironmentValue("riverKind", 1, 0, 1f);
        terrain.SetEnvironmentValue("riverKind", 2, 0, 1f);
        terrain.SetEnvironmentValue("riverKind", 3, 0, 2f);
        terrain.SetEnvironmentValue("riverKind", 4, 0, 0f);
        terrain.SetEnvironmentValue("riverKind", 5, 0, 1f);
        terrain.SetEnvironmentValue("riverKind", 6, 0, 1f);
        terrain.SetEnvironmentValue("riverFlow", 0, 0, 0.2f);
        terrain.SetEnvironmentValue("riverFlow", 1, 0, 0.8f);
        terrain.SetEnvironmentValue("riverFlow", 2, 0, 0.8f);
        terrain.SetEnvironmentValue("riverFlow", 3, 0, 0.8f);
        terrain.SetEnvironmentValue("riverFlow", 4, 0, 0.8f);
        terrain.SetEnvironmentValue("riverFlow", 5, 0, 0.8f);
        terrain.SetEnvironmentValue("riverFlow", 6, 0, 0.8f);
        return terrain;
    }

    private static JObject ServiceFoliageAmbienceVolume(JObject args)
    {
        if (args["samples"] is JArray inputs)
        {
            var samples = new JArray();
            foreach (JToken token in inputs)
            {
                if (token is not JObject sample)
                    throw new ArgumentException("叶声采样参数必须是对象。");
                samples.Add(new JObject
                {
                    ["volume"] = NaturalAmbientAudioPresenter.ResolveFoliageVolume(
                        (int)Number(sample, "nearbySourceCount", 0), (float)Number(sample, "windStrength", 0))
                });
            }
            return new JObject { ["samples"] = samples };
        }
        return new JObject
        {
            ["volume"] = NaturalAmbientAudioPresenter.ResolveFoliageVolume(
                (int)Number(args, "nearbySourceCount", 0), (float)Number(args, "windStrength", 0))
        };
    }

    private static JObject ServiceNatureFoliageConfig(JObject args)
    {
        List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        ItemDefinitionDto Find(string id) => definitions.Single(value => value.Id == id);
        bool HasFoliageTag(ItemDefinitionDto definition) =>
            definition.Tags?.Contains("NatureAudio.Foliage", StringComparer.OrdinalIgnoreCase) == true;
        ItemDefinitionDto appleTree = Find("AppleTree");
        ItemDefinitionDto coconutTree = Find("Tree_Coconut");
        ItemDefinitionDto pineTree = Find("Tree_Pine");
        ItemDefinitionDto willowTree = Find("Tree_Willow");
        ItemDefinitionDto deadTree = Find("Tree_Dead");
        return new JObject
        {
            ["appleTreeFoliage"] = HasFoliageTag(appleTree),
            ["coconutTreeFoliage"] = HasFoliageTag(coconutTree),
            ["pineTreeFoliage"] = HasFoliageTag(pineTree),
            ["willowTreeFoliage"] = HasFoliageTag(willowTree),
            ["treesUseResourceEntities"] = new[] { appleTree, coconutTree, pineTree, willowTree }
                .All(value => string.Equals(value.EntityRuntime, "resource", StringComparison.Ordinal)),
            ["deadTreeHasTreeTag"] = deadTree.Tags?.Contains("Tree", StringComparer.OrdinalIgnoreCase) == true,
            ["deadTreeHasFoliage"] = HasFoliageTag(deadTree)
        };
    }
    #endregion
}
