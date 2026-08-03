using UnityEngine;

public static class CaveLayoutSampler
{
    private const int RegionSize = 20;
    private const float MinimumRoomRadius = 3.8f;
    private const float MaximumRoomRadius = 6.8f;
    private const float MinimumTunnelRadius = 1.35f;
    private const float MaximumTunnelRadius = 2.15f;

    #region 洞穴采样

    public static bool IsOpenAtWorld(
        Vector2Int worldCell,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int portalChunkSize = default)
    {
        Vector2 point = new Vector2(worldCell.x + 0.5f, worldCell.y + 0.5f);
        if (IsInsidePortalNetwork(point, definition, worldSeed, portalChunkSize))
            return true;

        Vector2 entrance = definition != null
            ? (Vector2)definition.DefaultSpawnPosition
            : new Vector2(0.5f, 0.5f);
        float entranceRadius = Mathf.Max(4f, (definition?.CaveSafeRadius ?? 4f) + 1f);
        if ((point - entrance).sqrMagnitude <= entranceRadius * entranceRadius)
            return true;

        GetRegionCoordinates(entrance, out int entranceRegionX, out int entranceRegionY);
        RoomData entranceRoom = CreateRoom(entranceRegionX, entranceRegionY, worldSeed);
        if (IsInsideTunnel(point, entrance, entranceRoom.Center, Hash(worldSeed, entranceRegionX, entranceRegionY, 7919)))
            return true;

        GetRegionCoordinates(point, out int regionX, out int regionY);
        for (int x = regionX - 1; x <= regionX + 1; x++)
        {
            for (int y = regionY - 1; y <= regionY + 1; y++)
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

    private static bool IsInsidePortalNetwork(
        Vector2 point,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int requestedChunkSize)
    {
        float chance = Mathf.Clamp01(definition?.CaveEntranceChunkChance ?? 0f);
        if (chance <= 0f)
            return false;

        Vector2 rawChunkSize = requestedChunkSize == default
            ? ChunkMgr.GetChunkSize()
            : requestedChunkSize;
        Vector2Int chunkSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(rawChunkSize.y)));
        Vector2Int currentChunk = Chunk.GetChunkPosition(point, chunkSize);
        float safeRadius = Mathf.Max(1f, definition?.CaveEntranceSafeRadius ?? 3f);
        float connectionReach = RegionSize + MaximumRoomRadius + safeRadius;
        int searchX = Mathf.Max(1, Mathf.CeilToInt(connectionReach / chunkSize.x));
        int searchY = Mathf.Max(1, Mathf.CeilToInt(connectionReach / chunkSize.y));

        for (int chunkX = -searchX; chunkX <= searchX; chunkX++)
        {
            for (int chunkY = -searchY; chunkY <= searchY; chunkY++)
            {
                Vector2Int chunkOrigin = new Vector2Int(
                    currentChunk.x + chunkX * chunkSize.x,
                    currentChunk.y + chunkY * chunkSize.y);
                if (!DimensionPortalLayout.ShouldGenerateEntrance(chunkOrigin, worldSeed, chance))
                    continue;

                for (int candidateIndex = 0;
                     candidateIndex < DimensionPortalLayout.CandidateCount;
                     candidateIndex++)
                {
                    Vector2Int entranceCell = DimensionPortalLayout.GetCandidateCell(
                        chunkOrigin,
                        chunkSize,
                        worldSeed,
                        candidateIndex);
                    Vector2 entranceCenter = new Vector2(entranceCell.x + 0.5f, entranceCell.y + 0.5f);
                    if ((point - entranceCenter).sqrMagnitude <= safeRadius * safeRadius)
                        return true;

                    GetRegionCoordinates(entranceCenter, out int regionX, out int regionY);
                    RoomData room = CreateRoom(regionX, regionY, worldSeed);
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

    public static bool IsWallEdge(
        Vector2Int worldCell,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int portalChunkSize = default)
    {
        if (!IsOpenAtWorld(worldCell, definition, worldSeed, portalChunkSize))
            return false;

        return !IsOpenAtWorld(worldCell + Vector2Int.left, definition, worldSeed, portalChunkSize) ||
               !IsOpenAtWorld(worldCell + Vector2Int.right, definition, worldSeed, portalChunkSize) ||
               !IsOpenAtWorld(worldCell + Vector2Int.up, definition, worldSeed, portalChunkSize) ||
               !IsOpenAtWorld(worldCell + Vector2Int.down, definition, worldSeed, portalChunkSize);
    }

    public static float GetDepositStrength(Vector2Int worldCell, int worldSeed)
    {
        float seedOffset = Mathf.Abs(worldSeed % 100000) * 0.001f;
        float broad = Mathf.PerlinNoise(
            (worldCell.x + seedOffset + 1703f) * 0.052f,
            (worldCell.y - seedOffset - 2909f) * 0.052f);
        float detail = Mathf.PerlinNoise(
            (worldCell.x - seedOffset + 421f) * 0.14f,
            (worldCell.y + seedOffset + 947f) * 0.14f);
        return broad * 0.72f + detail * 0.28f;
    }

    #endregion

    #region 房间与隧道

    private static RoomData CreateRoom(int regionX, int regionY, int worldSeed)
    {
        uint state = Hash(worldSeed, regionX, regionY, 17);
        float jitter = RegionSize * 0.32f;
        Vector2 center = new Vector2(
            regionX * RegionSize + RegionSize * 0.5f + Mathf.Lerp(-jitter, jitter, NextUnitFloat(ref state)),
            regionY * RegionSize + RegionSize * 0.5f + Mathf.Lerp(-jitter, jitter, NextUnitFloat(ref state)));

        return new RoomData
        {
            Center = center,
            RadiusX = Mathf.Lerp(MinimumRoomRadius, MaximumRoomRadius, NextUnitFloat(ref state)),
            RadiusY = Mathf.Lerp(MinimumRoomRadius * 0.82f, MaximumRoomRadius * 0.94f, NextUnitFloat(ref state)),
            AngleRadians = Mathf.Lerp(-Mathf.PI, Mathf.PI, NextUnitFloat(ref state))
        };
    }

    private static bool IsInsideRoom(Vector2 point, RoomData room, int worldSeed)
    {
        Vector2 delta = point - room.Center;
        float cos = Mathf.Cos(room.AngleRadians);
        float sin = Mathf.Sin(room.AngleRadians);
        float localX = delta.x * cos + delta.y * sin;
        float localY = -delta.x * sin + delta.y * cos;
        float normalizedDistance = Mathf.Sqrt(
            localX * localX / (room.RadiusX * room.RadiusX) +
            localY * localY / (room.RadiusY * room.RadiusY));
        float edgeNoise = SampleEdgeNoise(point, worldSeed, 0.16f, 503f);
        return normalizedDistance <= 1f + (edgeNoise - 0.5f) * 0.24f;
    }

    private static bool IsInsideTunnel(Vector2 point, Vector2 start, Vector2 end, uint state)
    {
        Vector2 direction = end - start;
        float length = direction.magnitude;
        if (length <= 0.001f)
            return false;

        Vector2 perpendicular = new Vector2(-direction.y, direction.x) / length;
        Vector2 bendA = Vector2.Lerp(start, end, 0.34f) +
                        perpendicular * Mathf.Lerp(-5.5f, 5.5f, NextUnitFloat(ref state));
        Vector2 bendB = Vector2.Lerp(start, end, 0.67f) +
                        perpendicular * Mathf.Lerp(-5.5f, 5.5f, NextUnitFloat(ref state));
        float radius = Mathf.Lerp(MinimumTunnelRadius, MaximumTunnelRadius, NextUnitFloat(ref state));
        float edgeNoise = SampleEdgeNoise(point, (int)state, 0.21f, 883f);
        float effectiveRadius = radius + (edgeNoise - 0.5f) * 0.55f;

        float distance = Mathf.Min(
            DistanceToSegment(point, start, bendA),
            Mathf.Min(
                DistanceToSegment(point, bendA, bendB),
                DistanceToSegment(point, bendB, end)));
        return distance <= effectiveRadius;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0.0001f)
            return Vector2.Distance(point, start);

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return Vector2.Distance(point, start + segment * t);
    }

    private static float SampleEdgeNoise(Vector2 point, int worldSeed, float scale, float salt)
    {
        float seedOffset = Mathf.Abs(worldSeed % 100000) * 0.001f;
        return Mathf.PerlinNoise(
            (point.x + seedOffset + salt) * scale,
            (point.y - seedOffset - salt) * scale);
    }

    private static void GetRegionCoordinates(Vector2 point, out int regionX, out int regionY)
    {
        regionX = Mathf.FloorToInt(point.x / RegionSize);
        regionY = Mathf.FloorToInt(point.y / RegionSize);
    }

    #endregion

    #region 确定性随机

    private static uint Hash(int worldSeed, int x, int y, int salt)
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

    private static float NextUnitFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (float)0x1000000;
    }

    private struct RoomData
    {
        public Vector2 Center;
        public float RadiusX;
        public float RadiusY;
        public float AngleRadians;
    }

    #endregion
}

/// <summary>
/// 地表矿洞入口的确定性布局。概率只决定某个 Chunk 是否拥有入口，
/// 候选格由同一矿洞种子计算，因此地表与矿洞天然保持 1:1 坐标。
/// </summary>
public static class DimensionPortalLayout
{
    public const int CandidateCount = 4;

    public static bool ShouldGenerateEntrance(Vector2Int chunkOrigin, int caveWorldSeed, float chance)
    {
        float normalizedChance = Mathf.Clamp01(chance);
        if (normalizedChance <= 0f)
            return false;
        if (normalizedChance >= 1f)
            return true;

        uint state = Hash(caveWorldSeed, chunkOrigin.x, chunkOrigin.y, 0x45D9F3B);
        return NextUnitFloat(ref state) < normalizedChance;
    }

    public static Vector2Int GetCandidateCell(
        Vector2Int chunkOrigin,
        Vector2Int chunkSize,
        int caveWorldSeed,
        int candidateIndex)
    {
        int width = Mathf.Max(1, chunkSize.x);
        int height = Mathf.Max(1, chunkSize.y);
        int marginX = width >= 5 ? 2 : 0;
        int marginY = height >= 5 ? 2 : 0;
        int availableWidth = Mathf.Max(1, width - marginX * 2);
        int availableHeight = Mathf.Max(1, height - marginY * 2);
        int normalizedIndex = Mathf.Max(0, candidateIndex);
        uint state = Hash(
            caveWorldSeed,
            chunkOrigin.x,
            chunkOrigin.y,
            unchecked(0x27D4EB2D + normalizedIndex * 0x165667B1));
        int localX = marginX + Mathf.Min(
            availableWidth - 1,
            Mathf.FloorToInt(NextUnitFloat(ref state) * availableWidth));
        int localY = marginY + Mathf.Min(
            availableHeight - 1,
            Mathf.FloorToInt(NextUnitFloat(ref state) * availableHeight));
        return chunkOrigin + new Vector2Int(localX, localY);
    }

    private static uint Hash(int worldSeed, int x, int y, int salt)
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

    private static float NextUnitFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (float)0x1000000;
    }
}
