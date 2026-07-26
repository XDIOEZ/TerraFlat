using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class ChunkGenerator_Structures : ChunkGeneratorBase
{
    [Tooltip("未指定时从 Resources/Config/StructureCatalog_Default 加载")]
    public StructureCatalogSO Catalog;
    public bool LogSummary;

    [Header("Manual Test")]
    [Tooltip("启用时只在包含测试锚点的单个Chunk生成第一个遗迹，仅供人工验证。")]
    public bool TestMode;
    public Vector2Int TestWorldAnchor = Vector2Int.zero;
    public Vector2Int TestLocalOrigin = new(2, 2);

    private sealed class Candidate
    {
        public StructureSeedCandidate SeedCandidate;
        public StructureDefinitionSO Definition;
        public StructureTemplateSO Template;
        public Vector2Int WorldOrigin;
        public Vector2Int LocalOrigin;
        public Vector2Int TransformedSize;
        public int QuarterTurns;
        public bool MirrorX;
        public uint InstanceSeed;
    }

    public override void Generate(MapGenerationContext context)
    {
        if (context?.Map?.Data == null || context.Map.chunk == null)
        {
            Debug.LogError("[ChunkGenerator_Structures] Map、Map.Data或Chunk为空，遗迹生成已跳过", context?.Map);
            return;
        }

        Map = context.Map;
        StructureCatalogSO catalog = Catalog != null ? Catalog : StructureCatalogSO.LoadDefault();
        if (catalog == null || !catalog.Enabled || catalog.Definitions == null || catalog.Definitions.Count == 0)
            return;

        List<Candidate> candidates = CollectCandidates(context, catalog);
        candidates.Sort((left, right) =>
        {
            int seedCompare = left.InstanceSeed.CompareTo(right.InstanceSeed);
            return seedCompare != 0
                ? seedCompare
                : string.CompareOrdinal(left.Definition.StructureId, right.Definition.StructureId);
        });

        int generatedCount = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            RectInt localBounds = new(candidate.LocalOrigin, candidate.TransformedSize);
            if (context.StructureMask.Overlaps(localBounds))
                continue;
            if (!TestMode && !ValidateFootprintEnvironment(candidate, Map.Data))
                continue;

            if (candidate.Definition.ClearProceduralItemsInFootprint)
                ClearProceduralItems(localBounds);

            ApplyTiles(candidate);
            SpawnTemplateItems(candidate, context.WorldSeed);
            SpawnMarkerItems(candidate, context.WorldSeed);
            context.StructureMask.Fill(localBounds);
            StructureRuntimeRegistry.Register(
                context.WorldSeed,
                candidate.Definition.StructureId,
                string.IsNullOrWhiteSpace(candidate.Definition.DisplayName)
                    ? candidate.Definition.StructureId
                    : candidate.Definition.DisplayName,
                candidate.InstanceSeed,
                StructureSeedPlanner.ResolveTeleportPoint(candidate.SeedCandidate));
            generatedCount++;
        }

        if (LogSummary && generatedCount > 0)
            Debug.Log($"[ChunkGenerator_Structures] {Map.chunk.name} 生成遗迹 {generatedCount} 个", Map);
    }

    private List<Candidate> CollectCandidates(MapGenerationContext context, StructureCatalogSO catalog)
    {
        List<Candidate> output = new();
        Data_TileMap mapData = context.Map.Data;
        Vector2Int chunkOrigin = mapData.position;
        int width = mapData.Width;
        int height = mapData.Height;

        IEnumerable<StructureDefinitionSO> definitions = catalog.Definitions
            .Where(definition => definition != null && definition.Enabled)
            .OrderBy(definition => definition.StructureId, StringComparer.Ordinal);

        if (TestMode)
            return CollectTestCandidate(context, definitions);

        foreach (StructureDefinitionSO definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.StructureId))
                continue;

            int regionSize = Mathf.Max(8, definition.RegionSizeInTiles);
            int minRegionX = FloorDiv(chunkOrigin.x, regionSize);
            int maxRegionX = FloorDiv(chunkOrigin.x + width - 1, regionSize);
            int minRegionY = FloorDiv(chunkOrigin.y, regionSize);
            int maxRegionY = FloorDiv(chunkOrigin.y + height - 1, regionSize);

            for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
            {
                for (int regionY = minRegionY; regionY <= maxRegionY; regionY++)
                {
                    if (!StructureSeedPlanner.TryCreateCandidate(
                            context.WorldSeed,
                            catalog.GenerationVersion,
                            regionX,
                            regionY,
                            definition,
                            out StructureSeedCandidate planned))
                    {
                        continue;
                    }

                    Vector2Int candidateOrigin = planned.WorldOrigin;
                    if (!ContainsWorldCell(chunkOrigin, width, height, candidateOrigin))
                        continue;

                    StructureTemplateSO template = planned.Template;
                    int quarterTurns = planned.QuarterTurns;
                    bool mirrorX = planned.MirrorX;
                    Vector2Int transformedSize = planned.TransformedSize;
                    Vector2Int localOrigin = candidateOrigin - chunkOrigin;
                    RectInt localBounds = new(localOrigin, transformedSize);

                    int margin = Mathf.Max(0, definition.ChunkEdgeMargin);
                    if (localBounds.xMin < margin ||
                        localBounds.yMin < margin ||
                        localBounds.xMax > width - margin ||
                        localBounds.yMax > height - margin)
                    {
                        continue;
                    }

                    Vector2 structureCenter = candidateOrigin + (Vector2)transformedSize * 0.5f;
                    float minDistance = Mathf.Max(0, definition.MinDistanceFromWorldOrigin);
                    if (structureCenter.sqrMagnitude < minDistance * minDistance)
                        continue;

                    Vector2Int centerLocal = localOrigin + new Vector2Int(
                        Mathf.Clamp(transformedSize.x / 2, 0, width - 1),
                        Mathf.Clamp(transformedSize.y / 2, 0, height - 1));
                    if (!mapData.IsEnvironmentLocalValid(centerLocal.x, centerLocal.y) ||
                        !definition.IsEnvironmentValid(
                            mapData.EnvironmentLayers,
                            centerLocal.x,
                            centerLocal.y))
                    {
                        continue;
                    }

                    output.Add(new Candidate
                    {
                        SeedCandidate = planned,
                        Definition = definition,
                        Template = template,
                        WorldOrigin = candidateOrigin,
                        LocalOrigin = localOrigin,
                        TransformedSize = transformedSize,
                        QuarterTurns = quarterTurns,
                        MirrorX = mirrorX,
                        InstanceSeed = planned.InstanceSeed
                    });
                }
            }
        }

        return output;
    }

    private List<Candidate> CollectTestCandidate(
        MapGenerationContext context,
        IEnumerable<StructureDefinitionSO> definitions)
    {
        List<Candidate> output = new();
        StructureDefinitionSO definition = definitions.FirstOrDefault();
        if (definition == null)
            return output;

        StructureTemplateSO template = (definition.Templates ?? new List<WeightedStructureTemplate>())
            .Where(entry => entry?.Template != null && entry.Weight > 0f)
            .OrderBy(entry => entry.Template.TemplateId, StringComparer.Ordinal)
            .Select(entry => entry.Template)
            .FirstOrDefault();
        if (template == null)
            return output;

        Data_TileMap mapData = context.Map.Data;
        if (!ContainsWorldCell(
                mapData.position,
                mapData.Width,
                mapData.Height,
                TestWorldAnchor))
        {
            return output;
        }

        Vector2Int size = template.GetTransformedSize(0);
        if (size.x > mapData.Width || size.y > mapData.Height)
        {
            Debug.LogWarning(
                $"[ChunkGenerator_Structures] 测试遗迹尺寸 {size} 超过Chunk尺寸 ({mapData.Width}, {mapData.Height})。",
                context.Map);
            return output;
        }

        Vector2Int anchorLocal = TestWorldAnchor - mapData.position;
        Vector2Int localOrigin = new(
            Mathf.Clamp(anchorLocal.x + TestLocalOrigin.x, 0, mapData.Width - size.x),
            Mathf.Clamp(anchorLocal.y + TestLocalOrigin.y, 0, mapData.Height - size.y));
        Vector2Int worldOrigin = mapData.position + localOrigin;
        uint seed = StructureSeedPlanner.BuildCandidateSeed(
            context.WorldSeed,
            0,
            mapData.position.x,
            mapData.position.y,
            definition);

        StructureSeedCandidate planned = new(
            definition,
            template,
            worldOrigin,
            size,
            0,
            false,
            seed);
        output.Add(new Candidate
        {
            SeedCandidate = planned,
            Definition = definition,
            Template = template,
            WorldOrigin = worldOrigin,
            LocalOrigin = localOrigin,
            TransformedSize = size,
            QuarterTurns = 0,
            MirrorX = false,
            InstanceSeed = seed
        });
        return output;
    }

    private bool ValidateFootprintEnvironment(Candidate candidate, Data_TileMap mapData)
    {
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        RectInt bounds = new(candidate.LocalOrigin, candidate.TransformedSize);

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                if (!mapData.IsEnvironmentLocalValid(x, y))
                    return false;
                float height = mapData.EnvironmentLayers.Hight[x, y];
                minHeight = Mathf.Min(minHeight, height);
                maxHeight = Mathf.Max(maxHeight, height);
            }
        }

        return maxHeight - minHeight <= candidate.Definition.MaxHeightDelta;
    }

    private void ApplyTiles(Candidate candidate)
    {
        List<StructureTileStamp> stamps = candidate.Template.TileStamps;
        if (stamps == null)
            return;

        for (int i = 0; i < stamps.Count; i++)
        {
            StructureTileStamp stamp = stamps[i];
            if (stamp == null)
                continue;

            Vector2Int transformed = StructureTransformUtility.TransformCell(
                stamp.LocalPosition,
                candidate.Template.Size,
                candidate.QuarterTurns,
                candidate.MirrorX);
            Vector2Int worldPosition = candidate.WorldOrigin + transformed;
            List<TileData> tiles = Map.Data.GetTileListAt(worldPosition);
            if (tiles == null)
                continue;

            switch (stamp.WriteMode)
            {
                case StructureTileWriteMode.Clear:
                    tiles.Clear();
                    continue;
                case StructureTileWriteMode.ReplaceAll:
                    tiles.Clear();
                    break;
                case StructureTileWriteMode.ReplaceTop:
                    if (tiles.Count > 0)
                        tiles.RemoveAt(tiles.Count - 1);
                    break;
            }

            TileData template = stamp.TileBlock?.tileDataTemplate;
            if (template == null)
                continue;
            TileData tile = template.Clone();
            tile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
            tiles.Add(tile);
        }
    }

    private void SpawnTemplateItems(Candidate candidate, int worldSeed)
    {
        List<StructureItemStamp> stamps = candidate.Template.ItemStamps;
        if (stamps == null)
            return;

        for (int i = 0; i < stamps.Count; i++)
        {
            StructureItemStamp stamp = stamps[i];
            if (stamp == null || string.IsNullOrWhiteSpace(stamp.ItemPrefabId))
                continue;

            uint itemSeed = BuildItemSeed(worldSeed, candidate, i, stamp.SeedSalt, stamp.ItemPrefabId);
            StructureRandom random = new(itemSeed);
            if (stamp.Optional && random.Next01() > Mathf.Clamp01(stamp.SpawnChance))
                continue;

            Vector2 point = StructureTransformUtility.TransformPoint(
                stamp.LocalPosition,
                candidate.Template.Size,
                candidate.QuarterTurns,
                candidate.MirrorX);
            Vector3 scale = stamp.Scale == Vector3.zero ? Vector3.one : stamp.Scale;
            float rotation = stamp.RotationZ;
            if (stamp.OrientationMode == StructureOrientationMode.FollowStructure)
            {
                rotation = StructureTransformUtility.TransformRotation(
                    stamp.RotationZ,
                    candidate.QuarterTurns,
                    candidate.MirrorX);
                scale = StructureTransformUtility.TransformScale(scale, candidate.MirrorX);
            }

            SpawnDeterministicItem(
                stamp.ItemPrefabId,
                itemSeed,
                candidate.WorldOrigin + point,
                rotation,
                scale);
        }
    }

    private void SpawnMarkerItems(Candidate candidate, int worldSeed)
    {
        List<StructureMarkerData> markers = candidate.Template.Markers;
        if (markers == null)
            return;

        for (int i = 0; i < markers.Count; i++)
        {
            StructureMarkerData marker = markers[i];
            if (marker == null ||
                string.IsNullOrWhiteSpace(marker.ContentId) ||
                marker.Type is StructureMarkerType.Entrance or StructureMarkerType.ClearArea)
            {
                continue;
            }

            uint seed = BuildItemSeed(
                worldSeed,
                candidate,
                100000 + i,
                marker.SeedSalt,
                marker.ContentId);
            StructureRandom random = new(seed);
            if (random.Next01() > Mathf.Clamp01(marker.Chance))
                continue;

            Vector2 point = StructureTransformUtility.TransformPoint(
                marker.LocalPosition,
                candidate.Template.Size,
                candidate.QuarterTurns,
                candidate.MirrorX);
            float rotation = marker.RotationZ;
            Vector3 scale = Vector3.one;
            if (marker.OrientationMode == StructureOrientationMode.FollowStructure)
            {
                rotation = StructureTransformUtility.TransformRotation(
                    marker.RotationZ,
                    candidate.QuarterTurns,
                    candidate.MirrorX);
                scale = StructureTransformUtility.TransformScale(scale, candidate.MirrorX);
            }

            SpawnDeterministicItem(
                marker.ContentId,
                seed,
                candidate.WorldOrigin + point,
                rotation,
                scale);
        }
    }

    private void SpawnDeterministicItem(
        string itemId,
        uint itemSeed,
        Vector2 worldPosition,
        float rotationZ,
        Vector3 scale)
    {
        int guid = ResolveGuidCollision(unchecked((int)itemSeed));
        try
        {
            Item item = Map.chunk.InstantiateItemInChunkDeterministic(
                itemId,
                guid,
                new Vector3(worldPosition.x, worldPosition.y, 0f),
                Quaternion.Euler(0f, 0f, rotationZ),
                scale);
            item?.Load();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ChunkGenerator_Structures] 生成遗迹物品失败: {itemId}\n{exception}", Map);
        }
    }

    private int ResolveGuidCollision(int initialGuid)
    {
        int guid = initialGuid == 0 ? 1 : initialGuid;
        int salt = 0;
        while (Map.chunk.RunTimeItems.ContainsKey(guid))
        {
            uint hash = StructureHashUtility.Begin();
            hash = StructureHashUtility.Add(hash, guid);
            hash = StructureHashUtility.Add(hash, ++salt);
            guid = unchecked((int)hash);
            if (guid == 0)
                guid = 1;
        }
        return guid;
    }

    private void ClearProceduralItems(RectInt localBounds)
    {
        List<Item> items = Map.chunk.RunTimeItems.Values
            .Where(item => item != null && !(item is global::Map))
            .ToList();
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            Vector2Int worldCell = new(
                Mathf.FloorToInt(item.transform.position.x),
                Mathf.FloorToInt(item.transform.position.y));
            Vector2Int localCell = worldCell - Map.Data.position;
            if (!localBounds.Contains(localCell))
                continue;

            Map.chunk.RemoveItem(item);
            ItemMgr.Instance?.DespawnItem(item, saveData: false, detachFromChunk: false);
        }
    }

    private static uint BuildItemSeed(
        int worldSeed,
        Candidate candidate,
        int index,
        int salt,
        string itemId)
    {
        uint hash = StructureHashUtility.Begin();
        hash = StructureHashUtility.Add(hash, worldSeed);
        hash = StructureHashUtility.Add(hash, candidate.InstanceSeed);
        hash = StructureHashUtility.Add(hash, candidate.Definition.StructureId);
        hash = StructureHashUtility.Add(hash, candidate.Template.TemplateId);
        hash = StructureHashUtility.Add(hash, index);
        hash = StructureHashUtility.Add(hash, salt);
        hash = StructureHashUtility.Add(hash, itemId);
        return hash;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static bool ContainsWorldCell(
        Vector2Int chunkOrigin,
        int width,
        int height,
        Vector2Int worldCell)
    {
        return worldCell.x >= chunkOrigin.x &&
               worldCell.y >= chunkOrigin.y &&
               worldCell.x < chunkOrigin.x + width &&
               worldCell.y < chunkOrigin.y + height;
    }
}
