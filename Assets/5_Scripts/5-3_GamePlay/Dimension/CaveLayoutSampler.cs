using Unity.Mathematics;
using UnityEngine;

internal struct CaveLayoutConfig
{
    public float2 DefaultSpawnPosition;
    public float CaveSafeRadius;
    public float EntranceChunkChance;
    public float EntranceSafeRadius;
    public int2 ChunkSize;
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
        Vector2Int requestedChunkSize = default)
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
                math.max(1, Mathf.RoundToInt(rawChunkSize.y)))
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
        float2 point = new float2(worldCell.x + 0.5f, worldCell.y + 0.5f);
        if (IsInsidePortalNetwork(point, config, worldSeed))
            return true;

        float2 entrance = config.DefaultSpawnPosition;
        float entranceRadius = math.max(4f, config.CaveSafeRadius + 1f);
        if (math.lengthsq(point - entrance) <= entranceRadius * entranceRadius)
            return true;

        int2 entranceRegion = GetRegionCoordinates(entrance);
        RoomData entranceRoom = CreateRoom(entranceRegion.x, entranceRegion.y, worldSeed);
        if (IsInsideTunnel(point, entrance, entranceRoom.Center, Hash(worldSeed, entranceRegion.x, entranceRegion.y, 7919)))
            return true;

        int2 region = GetRegionCoordinates(point);
        for (int x = region.x - 1; x <= region.x + 1; x++)
        {
            for (int y = region.y - 1; y <= region.y + 1; y++)
            {
                RoomData room = CreateRoom(x, y, worldSeed);
                if (IsInsideRoom(point, room, worldSeed))
                    return true;

                RoomData right = CreateRoom(x + 1, y, worldSeed);
                if (IsInsideTunnel(point, room.Center, right.Center, Hash(worldSeed, x, y, 101)))
                    return true;

                RoomData up = CreateRoom(x, y + 1, worldSeed);
                if (IsInsideTunnel(point, room.Center, up.Center, Hash(worldSeed, x, y, 211)))
                    return true;

                if ((Hash(worldSeed, x, y, 307) & 3u) == 0u)
                {
                    RoomData diagonal = CreateRoom(x + 1, y + 1, worldSeed);
                    if (IsInsideTunnel(point, room.Center, diagonal.Center, Hash(worldSeed, x, y, 401)))
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

    internal static float GetDepositStrength(int2 worldCell, int worldSeed)
    {
        float2 seedOffset = TerrainNoiseKernel.GetSeedOffset(worldSeed, NoiseType.Height);
        float broad = TerrainNoiseKernel.SampleCNoise01(new float2(
            (worldCell.x + seedOffset.x + 1703f) * 0.052f,
            (worldCell.y + seedOffset.y - 2909f) * 0.052f));
        float detail = TerrainNoiseKernel.SampleCNoise01(new float2(
            (worldCell.x - seedOffset.x + 421f) * 0.14f,
            (worldCell.y - seedOffset.y + 947f) * 0.14f));
        return broad * 0.72f + detail * 0.28f;
    }

    private static bool IsInsidePortalNetwork(float2 point, CaveLayoutConfig config, int worldSeed)
    {
        float chance = math.saturate(config.EntranceChunkChance);
        if (chance <= 0f)
            return false;

        int2 chunkSize = math.max(new int2(1), config.ChunkSize);
        int2 currentChunk = GetChunkOrigin(point, chunkSize);
        float safeRadius = math.max(1f, config.EntranceSafeRadius);
        float connectionReach = RegionSize + MaximumRoomRadius + safeRadius;
        int searchX = math.max(1, (int)math.ceil(connectionReach / chunkSize.x));
        int searchY = math.max(1, (int)math.ceil(connectionReach / chunkSize.y));

        for (int chunkX = -searchX; chunkX <= searchX; chunkX++)
        {
            for (int chunkY = -searchY; chunkY <= searchY; chunkY++)
            {
                int2 chunkOrigin = currentChunk + new int2(chunkX * chunkSize.x, chunkY * chunkSize.y);
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
                    if (math.lengthsq(point - entranceCenter) <= safeRadius * safeRadius)
                        return true;

                    int2 region = GetRegionCoordinates(entranceCenter);
                    RoomData room = CreateRoom(region.x, region.y, worldSeed);
                    if (IsInsideTunnel(
                            point,
                            entranceCenter,
                            room.Center,
                            Hash(worldSeed, entranceCell.x, entranceCell.y, 7919 + candidateIndex * 397)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static RoomData CreateRoom(int regionX, int regionY, int worldSeed)
    {
        uint state = Hash(worldSeed, regionX, regionY, 17);
        float jitter = RegionSize * 0.32f;
        return new RoomData
        {
            Center = new float2(
                regionX * RegionSize + RegionSize * 0.5f + math.lerp(-jitter, jitter, NextUnitFloat(ref state)),
                regionY * RegionSize + RegionSize * 0.5f + math.lerp(-jitter, jitter, NextUnitFloat(ref state))),
            RadiusX = math.lerp(MinimumRoomRadius, MaximumRoomRadius, NextUnitFloat(ref state)),
            RadiusY = math.lerp(MinimumRoomRadius * 0.82f, MaximumRoomRadius * 0.94f, NextUnitFloat(ref state)),
            AngleRadians = math.lerp(-math.PI, math.PI, NextUnitFloat(ref state))
        };
    }

    private static bool IsInsideRoom(float2 point, RoomData room, int worldSeed)
    {
        float2 delta = point - room.Center;
        math.sincos(room.AngleRadians, out float sin, out float cos);
        float localX = delta.x * cos + delta.y * sin;
        float localY = -delta.x * sin + delta.y * cos;
        float normalizedDistance = math.sqrt(
            localX * localX / (room.RadiusX * room.RadiusX) +
            localY * localY / (room.RadiusY * room.RadiusY));
        float edgeNoise = SampleEdgeNoise(point, worldSeed, 0.16f, 503f);
        return normalizedDistance <= 1f + (edgeNoise - 0.5f) * 0.24f;
    }

    private static bool IsInsideTunnel(float2 point, float2 start, float2 end, uint state)
    {
        float2 direction = end - start;
        float length = math.length(direction);
        if (length <= 0.001f)
            return false;

        float2 perpendicular = new float2(-direction.y, direction.x) / length;
        float2 bendA = math.lerp(start, end, 0.34f) + perpendicular * math.lerp(-5.5f, 5.5f, NextUnitFloat(ref state));
        float2 bendB = math.lerp(start, end, 0.67f) + perpendicular * math.lerp(-5.5f, 5.5f, NextUnitFloat(ref state));
        float radius = math.lerp(MinimumTunnelRadius, MaximumTunnelRadius, NextUnitFloat(ref state));
        float edgeNoise = SampleEdgeNoise(point, (int)state, 0.21f, 883f);
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

    private static float SampleEdgeNoise(float2 point, int worldSeed, float scale, float salt)
    {
        float2 seedOffset = TerrainNoiseKernel.GetSeedOffset(worldSeed, NoiseType.Height);
        return TerrainNoiseKernel.SampleCNoise01(new float2(
            (point.x + seedOffset.x + salt) * scale,
            (point.y + seedOffset.y - salt) * scale));
    }

    private static int2 GetRegionCoordinates(float2 point)
    {
        return (int2)math.floor(point / RegionSize);
    }

    private static int2 GetChunkOrigin(float2 point, int2 chunkSize)
    {
        return (int2)math.floor(point / chunkSize) * chunkSize;
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
