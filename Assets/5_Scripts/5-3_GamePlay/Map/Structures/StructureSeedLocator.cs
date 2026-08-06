using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 仅包含由世界种子决定的数据。正式生成器与调试定位器共用此规划结果，
/// 避免遗迹生成算法与 GM 查询算法发生偏差。
/// </summary>
public sealed class StructureSeedCandidate
{
    public StructureDefinitionSO Definition { get; }
    public StructureTemplateSO Template { get; }
    public Vector2Int WorldOrigin { get; }
    public Vector2Int TransformedSize { get; }
    public int QuarterTurns { get; }
    public bool MirrorX { get; }
    public uint InstanceSeed { get; }

    public StructureSeedCandidate(
        StructureDefinitionSO definition,
        StructureTemplateSO template,
        Vector2Int worldOrigin,
        Vector2Int transformedSize,
        int quarterTurns,
        bool mirrorX,
        uint instanceSeed)
    {
        Definition = definition;
        Template = template;
        WorldOrigin = worldOrigin;
        TransformedSize = transformedSize;
        QuarterTurns = quarterTurns;
        MirrorX = mirrorX;
        InstanceSeed = instanceSeed;
    }
}

public static class StructureSeedPlanner
{
    public static bool TryCreateCandidate(
        int worldSeed,
        int generationVersion,
        int regionX,
        int regionY,
        StructureDefinitionSO definition,
        out StructureSeedCandidate candidate)
    {
        candidate = null;
        if (definition == null ||
            !definition.Enabled ||
            string.IsNullOrWhiteSpace(definition.StructureId))
        {
            return false;
        }

        uint seed = BuildCandidateSeed(
            worldSeed,
            generationVersion,
            regionX,
            regionY,
            definition);
        StructureRandom random = new(seed);
        if (random.Next01() > Mathf.Clamp01(definition.SpawnChance))
            return false;

        int regionSize = Mathf.Max(8, definition.RegionSizeInTiles);
        Vector2Int worldOrigin = new(
            regionX * regionSize + random.Range(0, regionSize),
            regionY * regionSize + random.Range(0, regionSize));

        StructureTemplateSO template = PickTemplate(definition, ref random);
        if (template == null || template.Size.x <= 0 || template.Size.y <= 0)
            return false;

        int quarterTurns = definition.AllowRotation ? random.Range(0, 4) : 0;
        bool mirrorX = definition.AllowMirror && random.Next01() < 0.5f;
        candidate = new StructureSeedCandidate(
            definition,
            template,
            worldOrigin,
            template.GetTransformedSize(quarterTurns),
            quarterTurns,
            mirrorX,
            seed);
        return true;
    }

    public static uint BuildCandidateSeed(
        int worldSeed,
        int generationVersion,
        int regionX,
        int regionY,
        StructureDefinitionSO definition)
    {
        uint hash = StructureHashUtility.Begin();
        hash = StructureHashUtility.Add(hash, worldSeed);
        hash = StructureHashUtility.Add(hash, generationVersion);
        hash = StructureHashUtility.Add(hash, regionX);
        hash = StructureHashUtility.Add(hash, regionY);
        hash = StructureHashUtility.Add(hash, definition.StructureId);
        hash = StructureHashUtility.Add(hash, definition.SeedSalt);
        return hash;
    }

    public static bool FitsChunk(
        StructureSeedCandidate candidate,
        Vector2Int chunkOrigin,
        Vector2Int chunkSize)
    {
        Vector2Int localOrigin = candidate.WorldOrigin - chunkOrigin;
        RectInt localBounds = new(localOrigin, candidate.TransformedSize);
        int margin = Mathf.Max(0, candidate.Definition.ChunkEdgeMargin);
        return localBounds.xMin >= margin &&
               localBounds.yMin >= margin &&
               localBounds.xMax <= chunkSize.x - margin &&
               localBounds.yMax <= chunkSize.y - margin;
    }

    public static bool IsFarEnoughFromWorldOrigin(StructureSeedCandidate candidate)
    {
        Vector2 center = candidate.WorldOrigin + (Vector2)candidate.TransformedSize * 0.5f;
        float minDistance = Mathf.Max(0, candidate.Definition.MinDistanceFromWorldOrigin);
        return center.sqrMagnitude >= minDistance * minDistance;
    }

    public static Vector2 ResolveTeleportPoint(StructureSeedCandidate candidate)
    {
        List<StructureMarkerData> markers = candidate.Template.Markers;
        if (markers != null)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                StructureMarkerData marker = markers[i];
                if (marker == null || marker.Type != StructureMarkerType.Entrance)
                    continue;

                Vector2 entrance = StructureTransformUtility.TransformPoint(
                    marker.LocalPosition,
                    candidate.Template.Size,
                    candidate.QuarterTurns,
                    candidate.MirrorX);
                return candidate.WorldOrigin + entrance;
            }
        }

        return candidate.WorldOrigin + (Vector2)candidate.TransformedSize * 0.5f;
    }

    public static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    public static bool ContainsWorldCell(
        Vector2Int chunkOrigin,
        Vector2Int chunkSize,
        Vector2Int worldCell)
    {
        return worldCell.x >= chunkOrigin.x &&
               worldCell.y >= chunkOrigin.y &&
               worldCell.x < chunkOrigin.x + chunkSize.x &&
               worldCell.y < chunkOrigin.y + chunkSize.y;
    }

    private static StructureTemplateSO PickTemplate(
        StructureDefinitionSO definition,
        ref StructureRandom random)
    {
        List<WeightedStructureTemplate> templates =
            (definition.Templates ?? new List<WeightedStructureTemplate>())
            .Where(entry => entry?.Template != null && entry.Weight > 0f)
            .OrderBy(entry => entry.Template.TemplateId, StringComparer.Ordinal)
            .ToList();
        if (templates.Count == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < templates.Count; i++)
            totalWeight += templates[i].Weight;

        float value = random.Next01() * totalWeight;
        for (int i = 0; i < templates.Count; i++)
        {
            value -= templates[i].Weight;
            if (value <= 0f)
                return templates[i].Template;
        }

        return templates[^1].Template;
    }
}

public static class StructureSeedLocator
{
    private const int MaxRegionSearchRadius = 512;

    public static bool TryFindNearest(
        int worldSeed,
        StructureCatalogSO catalog,
        StructureDefinitionSO targetDefinition,
        Vector2 searchOrigin,
        TerrainPreviewSampler terrainPreview,
        out StructureRuntimeLocation nearest,
        out int scannedRegionCount)
    {
        nearest = null;
        scannedRegionCount = 0;
        if (catalog == null ||
            !catalog.Enabled ||
            targetDefinition == null ||
            !targetDefinition.Enabled ||
            string.IsNullOrWhiteSpace(targetDefinition.StructureId))
        {
            return false;
        }

        Vector2 rawChunkSize = ChunkMgr.GetChunkSize();
        Vector2Int chunkSize = new(
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.y)));
        int regionSize = Mathf.Max(8, targetDefinition.RegionSizeInTiles);
        int centerRegionX = StructureSeedPlanner.FloorDiv(
            Mathf.FloorToInt(searchOrigin.x),
            regionSize);
        int centerRegionY = StructureSeedPlanner.FloorDiv(
            Mathf.FloorToInt(searchOrigin.y),
            regionSize);

        float nearestDistance = float.MaxValue;
        if (WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            int minRegionX = StructureSeedPlanner.FloorDiv(bounds.Min.x, regionSize);
            int maxRegionX = StructureSeedPlanner.FloorDiv(bounds.MaxExclusive.x - 1, regionSize);
            int minRegionY = StructureSeedPlanner.FloorDiv(bounds.Min.y, regionSize);
            int maxRegionY = StructureSeedPlanner.FloorDiv(bounds.MaxExclusive.y - 1, regionSize);
            for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
            {
                for (int regionY = minRegionY; regionY <= maxRegionY; regionY++)
                {
                    EvaluateRegion(
                        regionX,
                        regionY,
                        worldSeed,
                        catalog,
                        targetDefinition,
                        searchOrigin,
                        terrainPreview,
                        chunkSize,
                        ref nearest,
                        ref nearestDistance,
                        ref scannedRegionCount);
                }
            }

            return nearest != null;
        }

        for (int ring = 0; ring <= MaxRegionSearchRadius; ring++)
        {
            float futureRingMinimumDistance =
                Mathf.Max(0, ring - 1) * regionSize;
            if (nearest != null &&
                futureRingMinimumDistance * futureRingMinimumDistance > nearestDistance)
            {
                break;
            }

            for (int offsetX = -ring; offsetX <= ring; offsetX++)
            {
                EvaluateRegion(
                    centerRegionX + offsetX,
                    centerRegionY - ring,
                    worldSeed,
                    catalog,
                    targetDefinition,
                    searchOrigin,
                    terrainPreview,
                    chunkSize,
                    ref nearest,
                    ref nearestDistance,
                    ref scannedRegionCount);

                if (ring > 0)
                {
                    EvaluateRegion(
                        centerRegionX + offsetX,
                        centerRegionY + ring,
                        worldSeed,
                        catalog,
                        targetDefinition,
                        searchOrigin,
                        terrainPreview,
                        chunkSize,
                        ref nearest,
                        ref nearestDistance,
                        ref scannedRegionCount);
                }
            }

            for (int offsetY = -ring + 1; offsetY <= ring - 1; offsetY++)
            {
                EvaluateRegion(
                    centerRegionX - ring,
                    centerRegionY + offsetY,
                    worldSeed,
                    catalog,
                    targetDefinition,
                    searchOrigin,
                    terrainPreview,
                    chunkSize,
                    ref nearest,
                    ref nearestDistance,
                    ref scannedRegionCount);

                if (ring > 0)
                {
                    EvaluateRegion(
                        centerRegionX + ring,
                        centerRegionY + offsetY,
                        worldSeed,
                        catalog,
                        targetDefinition,
                        searchOrigin,
                        terrainPreview,
                        chunkSize,
                        ref nearest,
                        ref nearestDistance,
                        ref scannedRegionCount);
                }
            }
        }

        return nearest != null;
    }

    private static void EvaluateRegion(
        int regionX,
        int regionY,
        int worldSeed,
        StructureCatalogSO catalog,
        StructureDefinitionSO targetDefinition,
        Vector2 searchOrigin,
        TerrainPreviewSampler terrainPreview,
        Vector2Int chunkSize,
        ref StructureRuntimeLocation nearest,
        ref float nearestDistance,
        ref int scannedRegionCount)
    {
        scannedRegionCount++;
        if (!StructureSeedPlanner.TryCreateCandidate(
                worldSeed,
                catalog.GenerationVersion,
                regionX,
                regionY,
                targetDefinition,
                out StructureSeedCandidate target))
        {
            return;
        }

        if (WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds) &&
            !bounds.Contains(target.WorldOrigin))
        {
            return;
        }

        Vector2Int chunkOrigin = new(
            StructureSeedPlanner.FloorDiv(target.WorldOrigin.x, chunkSize.x) * chunkSize.x,
            StructureSeedPlanner.FloorDiv(target.WorldOrigin.y, chunkSize.y) * chunkSize.y);
        if (!TryResolveAcceptedCandidate(
                worldSeed,
                catalog,
                target,
                chunkOrigin,
                chunkSize,
                terrainPreview,
                out StructureSeedCandidate accepted))
        {
            return;
        }

        Vector2 entrance = StructureSeedPlanner.ResolveTeleportPoint(accepted);
        float distance = WorldTopologyRuntime.SqrDistance(searchOrigin, entrance);
        if (distance >= nearestDistance)
            return;

        nearestDistance = distance;
        nearest = new StructureRuntimeLocation(
            worldSeed,
            accepted.Definition.StructureId,
            string.IsNullOrWhiteSpace(accepted.Definition.DisplayName)
                ? accepted.Definition.StructureId
                : accepted.Definition.DisplayName,
            accepted.InstanceSeed,
            entrance);
    }

    private static bool TryResolveAcceptedCandidate(
        int worldSeed,
        StructureCatalogSO catalog,
        StructureSeedCandidate target,
        Vector2Int chunkOrigin,
        Vector2Int chunkSize,
        TerrainPreviewSampler terrainPreview,
        out StructureSeedCandidate acceptedTarget)
    {
        acceptedTarget = null;
        List<StructureSeedCandidate> candidates = CollectChunkCandidates(
            worldSeed,
            catalog,
            chunkOrigin,
            chunkSize,
            terrainPreview);
        candidates.Sort((left, right) =>
        {
            int seedCompare = left.InstanceSeed.CompareTo(right.InstanceSeed);
            return seedCompare != 0
                ? seedCompare
                : string.CompareOrdinal(
                    left.Definition.StructureId,
                    right.Definition.StructureId);
        });

        StructureGenerationMask mask = new(chunkSize.x, chunkSize.y);
        for (int i = 0; i < candidates.Count; i++)
        {
            StructureSeedCandidate candidate = candidates[i];
            RectInt localBounds = new(
                candidate.WorldOrigin - chunkOrigin,
                candidate.TransformedSize);
            if (mask.Overlaps(localBounds))
                continue;

            mask.Fill(localBounds);
            if (candidate.InstanceSeed == target.InstanceSeed &&
                candidate.Definition.StructureId == target.Definition.StructureId &&
                candidate.WorldOrigin == target.WorldOrigin)
            {
                acceptedTarget = candidate;
                return true;
            }
        }

        return false;
    }

    private static List<StructureSeedCandidate> CollectChunkCandidates(
        int worldSeed,
        StructureCatalogSO catalog,
        Vector2Int chunkOrigin,
        Vector2Int chunkSize,
        TerrainPreviewSampler terrainPreview)
    {
        List<StructureSeedCandidate> output = new();
        IEnumerable<StructureDefinitionSO> definitions =
            (catalog.Definitions ?? new List<StructureDefinitionSO>())
            .Where(definition =>
                definition != null &&
                definition.Enabled &&
                !string.IsNullOrWhiteSpace(definition.StructureId))
            .OrderBy(definition => definition.StructureId, StringComparer.Ordinal);

        foreach (StructureDefinitionSO definition in definitions)
        {
            int regionSize = Mathf.Max(8, definition.RegionSizeInTiles);
            int minRegionX = StructureSeedPlanner.FloorDiv(chunkOrigin.x, regionSize);
            int maxRegionX = StructureSeedPlanner.FloorDiv(
                chunkOrigin.x + chunkSize.x - 1,
                regionSize);
            int minRegionY = StructureSeedPlanner.FloorDiv(chunkOrigin.y, regionSize);
            int maxRegionY = StructureSeedPlanner.FloorDiv(
                chunkOrigin.y + chunkSize.y - 1,
                regionSize);

            for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
            {
                for (int regionY = minRegionY; regionY <= maxRegionY; regionY++)
                {
                    if (!StructureSeedPlanner.TryCreateCandidate(
                            worldSeed,
                            catalog.GenerationVersion,
                            regionX,
                            regionY,
                            definition,
                            out StructureSeedCandidate candidate) ||
                        !StructureSeedPlanner.ContainsWorldCell(
                            chunkOrigin,
                            chunkSize,
                            candidate.WorldOrigin) ||
                        !StructureSeedPlanner.FitsChunk(
                            candidate,
                            chunkOrigin,
                            chunkSize) ||
                        !StructureSeedPlanner.IsFarEnoughFromWorldOrigin(candidate) ||
                        !IsEnvironmentValid(
                            candidate,
                            worldSeed,
                            terrainPreview))
                    {
                        continue;
                    }

                    output.Add(candidate);
                }
            }
        }

        return output;
    }

    private static bool IsEnvironmentValid(
        StructureSeedCandidate candidate,
        int worldSeed,
        TerrainPreviewSampler terrainPreview)
    {
        if (terrainPreview == null)
            return true;

        Vector2Int center = candidate.WorldOrigin + new Vector2Int(
            candidate.TransformedSize.x / 2,
            candidate.TransformedSize.y / 2);
        if (!terrainPreview.TrySample(center, out TerrainPreviewSample centerPreview) ||
            !candidate.Definition.IsEnvironmentValid(centerPreview.Environment, centerPreview.Biome))
            return false;

        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        for (int x = 0; x < candidate.TransformedSize.x; x++)
        {
            for (int y = 0; y < candidate.TransformedSize.y; y++)
            {
                if (!terrainPreview.TrySample(
                        candidate.WorldOrigin + new Vector2Int(x, y),
                        out TerrainPreviewSample sample))
                {
                    return false;
                }
                minHeight = Mathf.Min(minHeight, sample.Environment.Height);
                maxHeight = Mathf.Max(maxHeight, sample.Environment.Height);
            }
        }

        return maxHeight - minHeight <= candidate.Definition.MaxHeightDelta;
    }
}
