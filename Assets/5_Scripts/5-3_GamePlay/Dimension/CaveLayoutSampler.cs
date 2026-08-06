using Unity.Mathematics;
using UnityEngine;

internal struct CaveLayoutConfig
{
    public float2 DefaultSpawnPosition;
    public float CaveSafeRadius;
    public float EntranceChunkChance;
    public float EntranceSafeRadius;
    public int2 ChunkSize;
    public WorldTopologyDomain Topology;
}

public static class CaveLayoutSampler
{
    private const int RegionSize = 20;
    private const float MinimumRoomRadius = 3.8f;
    private const float MaximumRoomRadius = 6.8f;
    private const float MinimumTunnelRadius = 1.35f;
    private const float MaximumTunnelRadius = 2.15f;

    internal static CaveLayoutConfig CreateConfig(
        DimensionDefinition definition,
        Vector2Int requestedChunkSize = default,
        PlanetData planetData = null)
    {
        Vector2 rawChunkSize = requestedChunkSize == default
            ? ChunkMgr.GetChunkSize()
            : requestedChunkSize;
        return new CaveLayoutConfig
        {
            DefaultSpawnPosition = definition != null
                ? new float2(definition.DefaultSpawnPosition.x, definition.DefaultSpawnPosition.y)
                : new float2(0.5f, 0.5f),
            CaveSafeRadius = math.max(0f, definition?.CaveSafeRadius ?? 4f),
            EntranceChunkChance = math.saturate(definition?.CaveEntranceChunkChance ?? 0f),
            EntranceSafeRadius = math.max(1f, definition?.CaveEntranceSafeRadius ?? 3f),
            ChunkSize = new int2(
                math.max(1, Mathf.RoundToInt(rawChunkSize.x)),
                math.max(1, Mathf.RoundToInt(rawChunkSize.y))),
            Topology = WorldTopologyBounds.TryCreate(planetData, out WorldTopologyBounds bounds)
                ? bounds.ToDomain()
                : default
        };
    }

    public static bool IsOpenAtWorld(
        Vector2Int worldCell,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int portalChunkSize = default)
    {
        return IsOpenAtWorld(
            new int2(worldCell.x, worldCell.y),
            CreateConfig(definition, portalChunkSize),
            worldSeed);
    }

    internal static bool IsOpenAtWorld(int2 worldCell, CaveLayoutConfig config, int worldSeed)
    {
        worldCell = config.Topology.Normalize(worldCell);
        float2 point = new float2(worldCell.x + 0.5f, worldCell.y + 0.5f);
        if (IsInsidePortalNetwork(point, config, worldSeed))
            return true;

        float2 entrance = config.Topology.Normalize(config.DefaultSpawnPosition);
        float entranceRadius = math.max(4f, config.CaveSafeRadius + 1f);
        if (DistanceSq(point, entrance, config) <= entranceRadius * entranceRadius)
            return true;

        int2 entranceRegion = GetRegionCoordinates(entrance, config);
        RoomData entranceRoom = CreateRoom(entranceRegion.x, entranceRegion.y, worldSeed, config);
        if (IsInsideTunnel(point, entrance, entranceRoom.Center, Hash(worldSeed, entranceRegion.x, entranceRegion.y, 7919), config))
            return true;

        int2 region = GetRegionCoordinates(point, config);
        for (int x = region.x - 1; x <= region.x + 1; x++)
        {
            for (int y = region.y - 1; y <= region.y + 1; y++)
            {
                RoomData room = CreateRoom(x, y, worldSeed, config);
                if (IsInsideRoom(point, room, worldSeed, config))
                    return true;

                RoomData right = CreateRoom(x + 1, y, worldSeed, config);
                if (IsInsideTunnel(point, room.Center, right.Center, HashRoom(config, worldSeed, x, y, 101), config))
                    return true;

                RoomData up = CreateRoom(x, y + 1, worldSeed, config);
                if (IsInsideTunnel(point, room.Center, up.Center, HashRoom(config, worldSeed, x, y, 211), config))
                    return true;

                if ((HashRoom(config, worldSeed, x, y, 307) & 3u) == 0u)
                {
                    RoomData diagonal = CreateRoom(x + 1, y + 1, worldSeed, config);
                    if (IsInsideTunnel(point, room.Center, diagonal.Center, HashRoom(config, worldSeed, x, y, 401), config))
                        return true;
                }
            }
        }

        return false;
    }

    public static bool IsWallEdge(
        Vector2Int worldCell,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int portalChunkSize = default)
    {
        CaveLayoutConfig config = CreateConfig(definition, portalChunkSize);
        return IsWallEdge(new int2(worldCell.x, worldCell.y), config, worldSeed);
    }

    internal static bool IsWallEdge(int2 worldCell, CaveLayoutConfig config, int worldSeed)
    {
        if (!IsOpenAtWorld(worldCell, config, worldSeed))
            return false;

        return !IsOpenAtWorld(worldCell + new int2(-1, 0), config, worldSeed) ||
               !IsOpenAtWorld(worldCell + new int2(1, 0), config, worldSeed) ||
               !IsOpenAtWorld(worldCell + new int2(0, 1), config, worldSeed) ||
               !IsOpenAtWorld(worldCell + new int2(0, -1), config, worldSeed);
    }

    public static float GetDepositStrength(Vector2Int worldCell, int worldSeed)
    {
        return GetDepositStrength(new int2(worldCell.x, worldCell.y), worldSeed);
    }

    public static float GetDepositStrength(Vector2Int worldCell, int worldSeed, PlanetData planetData)
    {
        CaveLayoutConfig config = CreateConfig(null, default, planetData);
        return GetDepositStrength(new int2(worldCell.x, worldCell.y), worldSeed, config);
    }

    internal static float GetDepositStrength(int2 worldCell, int worldSeed)
    {
        return GetDepositStrength(worldCell, worldSeed, default);
    }

    internal static float GetDepositStrength(int2 worldCell, int worldSeed, CaveLayoutConfig config)
    {
        worldCell = config.Topology.Normalize(worldCell);
        float2 seedOffset = TerrainNoiseKernel.GetSeedOffset(worldSeed, NoiseType.Height);
        float broad = SamplePeriodicNoise(
            new float2(worldCell.x, worldCell.y),
            new float2(seedOffset.x + 1703f, seedOffset.y - 2909f),
            0.052f,
            config.Topology);
        float detail = SamplePeriodicNoise(
            new float2(worldCell.x, worldCell.y),
            new float2(-seedOffset.x + 421f, -seedOffset.y + 947f),
            0.14f,
            config.Topology);
        return broad * 0.72f + detail * 0.28f;
    }

    private static bool IsInsidePortalNetwork(float2 point, CaveLayoutConfig config, int worldSeed)
    {
        float chance = math.saturate(config.EntranceChunkChance);
        if (chance <= 0f)
            return false;

        int2 chunkSize = math.max(new int2(1), config.ChunkSize);
        int2 currentChunk = GetChunkOrigin(point, chunkSize, config);
        float safeRadius = math.max(1f, config.EntranceSafeRadius);
        float connectionReach = RegionSize + MaximumRoomRadius + safeRadius;
        int searchX = math.max(1, (int)math.ceil(connectionReach / chunkSize.x));
        int searchY = math.max(1, (int)math.ceil(connectionReach / chunkSize.y));

        for (int chunkX = -searchX; chunkX <= searchX; chunkX++)
        {
            for (int chunkY = -searchY; chunkY <= searchY; chunkY++)
            {
                int2 chunkOrigin = currentChunk + new int2(chunkX * chunkSize.x, chunkY * chunkSize.y);
                chunkOrigin = NormalizeChunkOrigin(chunkOrigin, config);
                if (!DimensionPortalLayout.ShouldGenerateEntrance(chunkOrigin, worldSeed, chance))
                    continue;

                for (int candidateIndex = 0; candidateIndex < DimensionPortalLayout.CandidateCount; candidateIndex++)
                {
                    int2 entranceCell = DimensionPortalLayout.GetCandidateCell(
                        chunkOrigin,
                        chunkSize,
                        worldSeed,
                        candidateIndex);
                    float2 entranceCenter = new float2(entranceCell.x + 0.5f, entranceCell.y + 0.5f);
                    entranceCenter = config.Topology.Normalize(entranceCenter);
                    if (DistanceSq(point, entranceCenter, config) <= safeRadius * safeRadius)
                        return true;

                    int2 region = GetRegionCoordinates(entranceCenter, config);
                    RoomData room = CreateRoom(region.x, region.y, worldSeed, config);
                    if (IsInsideTunnel(
                            point,
                            entranceCenter,
                            room.Center,
                            Hash(worldSeed, entranceCell.x, entranceCell.y, 7919 + candidateIndex * 397),
                            config))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static RoomData CreateRoom(
        int regionX,
        int regionY,
        int worldSeed,
        CaveLayoutConfig config)
    {
        int2 regionCount = GetRegionCount(config);
        int2 canonicalRegion = CanonicalRegion(new int2(regionX, regionY), regionCount, config.Topology.IsWrapped);
        uint state = Hash(worldSeed, canonicalRegion.x, canonicalRegion.y, 17);
        float2 regionExtent = GetRegionExtent(config, regionCount);
        float2 jitter = regionExtent * 0.32f;
        return new RoomData
        {
            Center = GetRegionMin(config, canonicalRegion, regionExtent) + regionExtent * 0.5f +
                     new float2(
                         math.lerp(-jitter.x, jitter.x, NextUnitFloat(ref state)),
                         math.lerp(-jitter.y, jitter.y, NextUnitFloat(ref state))),
            RadiusX = math.lerp(MinimumRoomRadius, MaximumRoomRadius, NextUnitFloat(ref state)),
            RadiusY = math.lerp(MinimumRoomRadius * 0.82f, MaximumRoomRadius * 0.94f, NextUnitFloat(ref state)),
            AngleRadians = math.lerp(-math.PI, math.PI, NextUnitFloat(ref state))
        };
    }

    private static bool IsInsideRoom(
        float2 point,
        RoomData room,
        int worldSeed,
        CaveLayoutConfig config)
    {
        float2 delta = ShortestDelta(room.Center, point, config);
        math.sincos(room.AngleRadians, out float sin, out float cos);
        float localX = delta.x * cos + delta.y * sin;
        float localY = -delta.x * sin + delta.y * cos;
        float normalizedDistance = math.sqrt(
            localX * localX / (room.RadiusX * room.RadiusX) +
            localY * localY / (room.RadiusY * room.RadiusY));
        float edgeNoise = SampleEdgeNoise(point, worldSeed, 0.16f, 503f, config);
        return normalizedDistance <= 1f + (edgeNoise - 0.5f) * 0.24f;
    }

    private static bool IsInsideTunnel(
        float2 point,
        float2 start,
        float2 end,
        uint state,
        CaveLayoutConfig config)
    {
        end = start + ShortestDelta(start, end, config);
        point = start + ShortestDelta(start, point, config);
        float2 direction = end - start;
        float length = math.length(direction);
        if (length <= 0.001f)
            return false;

        float2 perpendicular = new float2(-direction.y, direction.x) / length;
        float2 bendA = math.lerp(start, end, 0.34f) + perpendicular * math.lerp(-5.5f, 5.5f, NextUnitFloat(ref state));
        float2 bendB = math.lerp(start, end, 0.67f) + perpendicular * math.lerp(-5.5f, 5.5f, NextUnitFloat(ref state));
        float radius = math.lerp(MinimumTunnelRadius, MaximumTunnelRadius, NextUnitFloat(ref state));
        float edgeNoise = SampleEdgeNoise(point, (int)state, 0.21f, 883f, config);
        float effectiveRadius = radius + (edgeNoise - 0.5f) * 0.55f;

        float distance = math.min(
            DistanceToSegment(point, start, bendA),
            math.min(DistanceToSegment(point, bendA, bendB), DistanceToSegment(point, bendB, end)));
        return distance <= effectiveRadius;
    }

    private static float DistanceToSegment(float2 point, float2 start, float2 end)
    {
        float2 segment = end - start;
        float lengthSquared = math.lengthsq(segment);
        if (lengthSquared <= 0.0001f)
            return math.distance(point, start);

        float t = math.saturate(math.dot(point - start, segment) / lengthSquared);
        return math.distance(point, start + segment * t);
    }

    private static float SampleEdgeNoise(
        float2 point,
        int worldSeed,
        float scale,
        float salt,
        CaveLayoutConfig config)
    {
        float2 seedOffset = TerrainNoiseKernel.GetSeedOffset(worldSeed, NoiseType.Height);
        return SamplePeriodicNoise(
            point,
            new float2(seedOffset.x + salt, seedOffset.y - salt),
            scale,
            config.Topology);
    }

    private static int2 GetRegionCoordinates(float2 point, CaveLayoutConfig config)
    {
        if (!config.Topology.IsWrapped)
            return (int2)math.floor(point / RegionSize);

        int2 count = GetRegionCount(config);
        float2 extent = GetRegionExtent(config, count);
        float2 normalized = config.Topology.Normalize(point);
        return CanonicalRegion(
            (int2)math.floor((normalized - config.Topology.Min) / extent),
            count,
            true);
    }

    private static int2 GetChunkOrigin(float2 point, int2 chunkSize, CaveLayoutConfig config)
    {
        if (!config.Topology.IsWrapped)
            return (int2)math.floor(point / chunkSize) * chunkSize;

        float2 normalized = config.Topology.Normalize(point);
        int2 relative = (int2)math.floor(normalized) - config.Topology.Min;
        return config.Topology.Min + (relative / chunkSize) * chunkSize;
    }

    private static int2 NormalizeChunkOrigin(int2 chunkOrigin, CaveLayoutConfig config)
    {
        return config.Topology.IsWrapped
            ? config.Topology.Normalize(chunkOrigin)
            : chunkOrigin;
    }

    private static float DistanceSq(float2 first, float2 second, CaveLayoutConfig config)
    {
        return math.lengthsq(ShortestDelta(first, second, config));
    }

    private static float2 ShortestDelta(float2 from, float2 to, CaveLayoutConfig config)
    {
        return config.Topology.ShortestDelta(from, to);
    }

    private static int2 GetRegionCount(CaveLayoutConfig config)
    {
        return config.Topology.IsWrapped
            ? math.max(new int2(1), (int2)math.round(
                new float2(config.Topology.Span.x, config.Topology.Span.y) / RegionSize))
            : new int2(int.MaxValue);
    }

    private static float2 GetRegionExtent(CaveLayoutConfig config, int2 regionCount)
    {
        return config.Topology.IsWrapped
            ? new float2(config.Topology.Span.x, config.Topology.Span.y) / regionCount
            : new float2(RegionSize);
    }

    private static float2 GetRegionMin(
        CaveLayoutConfig config,
        int2 region,
        float2 regionExtent)
    {
        return config.Topology.IsWrapped
            ? new float2(config.Topology.Min.x, config.Topology.Min.y) + region * regionExtent
            : region * RegionSize;
    }

    private static int2 CanonicalRegion(int2 region, int2 count, bool wrapped)
    {
        if (!wrapped)
            return region;

        int x = region.x % count.x;
        int y = region.y % count.y;
        if (x < 0) x += count.x;
        if (y < 0) y += count.y;
        return new int2(x, y);
    }

    private static uint HashRoom(
        CaveLayoutConfig config,
        int worldSeed,
        int regionX,
        int regionY,
        int salt)
    {
        int2 canonical = CanonicalRegion(
            new int2(regionX, regionY),
            GetRegionCount(config),
            config.Topology.IsWrapped);
        return Hash(worldSeed, canonical.x, canonical.y, salt);
    }

    private static float SamplePeriodicNoise(
        float2 point,
        float2 offset,
        float scale,
        WorldTopologyDomain topology)
    {
        if (!topology.IsWrapped)
            return TerrainNoiseKernel.SampleCNoise01((point + offset) * scale);

        float2 span = new float2(topology.Span.x, topology.Span.y);
        float2 repeat = math.max(new float2(1f), math.round(span * scale));
        float2 phase = offset * scale;
        phase -= math.floor(phase / repeat) * repeat;
        float2 samplePosition = (point - topology.Min) / span * repeat + phase;
        float value = noise.pnoise(samplePosition, repeat) * 0.5f + 0.5f;
        return math.isfinite(value) ? math.saturate(value) : TerrainNoiseKernel.DefaultChannelValue;
    }

    internal static uint Hash(int worldSeed, int x, int y, int salt)
    {
        unchecked
        {
            uint state = 2166136261u;
            state = (state ^ (uint)worldSeed) * 16777619u;
            state = (state ^ (uint)x) * 16777619u;
            state = (state ^ (uint)y) * 16777619u;
            state = (state ^ (uint)salt) * 16777619u;
            return state == 0u ? 0x9E3779B9u : state;
        }
    }

    internal static float NextUnitFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (float)0x1000000;
    }

    private struct RoomData
    {
        public float2 Center;
        public float RadiusX;
        public float RadiusY;
        public float AngleRadians;
    }
}

public static class DimensionPortalLayout
{
    public const int CandidateCount = 4;

    public static bool ShouldGenerateEntrance(Vector2Int chunkOrigin, int caveWorldSeed, float chance)
    {
        return ShouldGenerateEntrance(new int2(chunkOrigin.x, chunkOrigin.y), caveWorldSeed, chance);
    }

    public static Vector2Int GetCandidateCell(
        Vector2Int chunkOrigin,
        Vector2Int chunkSize,
        int caveWorldSeed,
        int candidateIndex)
    {
        int2 result = GetCandidateCell(
            new int2(chunkOrigin.x, chunkOrigin.y),
            new int2(chunkSize.x, chunkSize.y),
            caveWorldSeed,
            candidateIndex);
        return new Vector2Int(result.x, result.y);
    }

    internal static bool ShouldGenerateEntrance(int2 chunkOrigin, int caveWorldSeed, float chance)
    {
        float normalizedChance = math.saturate(chance);
        if (normalizedChance <= 0f)
            return false;
        if (normalizedChance >= 1f)
            return true;

        uint state = CaveLayoutSampler.Hash(caveWorldSeed, chunkOrigin.x, chunkOrigin.y, 0x45D9F3B);
        return CaveLayoutSampler.NextUnitFloat(ref state) < normalizedChance;
    }

    internal static int2 GetCandidateCell(
        int2 chunkOrigin,
        int2 chunkSize,
        int caveWorldSeed,
        int candidateIndex)
    {
        int width = math.max(1, chunkSize.x);
        int height = math.max(1, chunkSize.y);
        int marginX = width >= 5 ? 2 : 0;
        int marginY = height >= 5 ? 2 : 0;
        int availableWidth = math.max(1, width - marginX * 2);
        int availableHeight = math.max(1, height - marginY * 2);
        int normalizedIndex = math.max(0, candidateIndex);
        uint state = CaveLayoutSampler.Hash(
            caveWorldSeed,
            chunkOrigin.x,
            chunkOrigin.y,
            unchecked(0x27D4EB2D + normalizedIndex * 0x165667B1));
        int localX = marginX + math.min(
            availableWidth - 1,
            (int)math.floor(CaveLayoutSampler.NextUnitFloat(ref state) * availableWidth));
        int localY = marginY + math.min(
            availableHeight - 1,
            (int)math.floor(CaveLayoutSampler.NextUnitFloat(ref state) * availableHeight));
        return chunkOrigin + new int2(localX, localY);
    }
}
