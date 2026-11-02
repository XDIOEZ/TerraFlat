using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>把现有挡风格投影成可缓存的绕流纹理，供风线 Shader 一次采样。</summary>
public sealed class WindStreakObstacleField
{
    #region 挡风格与相机缓存

    private const int PaddingCells = 12;
    private const int MaximumGridSide = 224;
    private const float MaximumLateralBend = 6f;
    private const float ApproachDistance = 5f;
    private const float RejoinDistance = 7f;
    private const float SideInfluenceDistance = 3f;
    private const float MiddleBlendDistance = 0.75f;
    private const float WindDirectionRebuildDot = 0.9962f;

    private readonly List<(ChunkTerrainData terrain, long revision)> terrainVersions = new();
    private Texture2D flowTexture;
    private bool[] blockedCells;
    private Color32[] flowPixels;
    private bool[] visitedScratch;
    private int[] queueScratch;
    private float[] bendScratch;
    private readonly List<ObstacleSpan> spanScratch = new();
    private string cachedWorld;
    private Vector2 cachedDirection;
    private uint cachedBuildingRevision;
    private int cachedChunkCount = -1;
    private int originX, originY, width, height;

    public bool HasObstacles { get; private set; }
    public Texture FlowTexture => HasObstacles ? flowTexture : null;
    public Vector4 WorldRect => width > 0 && height > 0
        ? new Vector4(originX, originY, 1f / width, 1f / height) : Vector4.zero;

    public void Reset()
    {
        CoreUtils.Destroy(flowTexture);
        flowTexture = null;
        blockedCells = null;
        flowPixels = null;
        visitedScratch = null;
        queueScratch = null;
        bendScratch = null;
        spanScratch.Clear();
        terrainVersions.Clear();
        cachedWorld = null;
        cachedDirection = Vector2.zero;
        cachedChunkCount = -1;
        cachedBuildingRevision = 0;
        originX = originY = width = height = 0;
        HasObstacles = false;
    }

    public void RefreshIfDirty(Camera camera, string worldKey, Vector2 windDirection)
    {
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null || chunks.IsWorldRuntimeShuttingDown || !camera.orthographic ||
            string.IsNullOrEmpty(worldKey) ||
            !string.Equals(chunks.OwnedWorldKey ?? chunks.WorldRuntime?.WorldId, worldKey,
                StringComparison.Ordinal))
        {
            Reset();
            return;
        }

        // 整格对齐让镜头在格内移动时不重建纹理，留白覆盖视口及墙前、墙后的弯流。
        Vector2 cameraPosition = camera.transform.position;
        float halfHeight = camera.orthographicSize;
        float halfWidth = halfHeight * camera.aspect;
        Vector3 right = camera.transform.right, up = camera.transform.up;
        float extentX = Mathf.Abs(right.x) * halfWidth + Mathf.Abs(up.x) * halfHeight;
        float extentY = Mathf.Abs(right.y) * halfWidth + Mathf.Abs(up.y) * halfHeight;
        int nextWidth = Mathf.CeilToInt(extentX * 2f) + PaddingCells * 2 + 2;
        int nextHeight = Mathf.CeilToInt(extentY * 2f) + PaddingCells * 2 + 2;
        if (nextWidth > MaximumGridSide || nextHeight > MaximumGridSide || nextWidth < 1 || nextHeight < 1)
        {
            HasObstacles = false;
            return;
        }

        int nextOriginX = Mathf.FloorToInt(cameraPosition.x - extentX) - PaddingCells - 1;
        int nextOriginY = Mathf.FloorToInt(cameraPosition.y - extentY) - PaddingCells - 1;
        uint nextRevision = BuildingOccupancyRegistry.GetWindBlockingRevision(worldKey);
        bool dirty = cachedWorld != worldKey || originX != nextOriginX || originY != nextOriginY ||
                     width != nextWidth || height != nextHeight || cachedChunkCount != chunks.Chunks.Count ||
                     cachedBuildingRevision != nextRevision ||
                     Vector2.Dot(cachedDirection, windDirection) < WindDirectionRebuildDot;
        if (!dirty)
        {
            foreach (var observed in terrainVersions)
            {
                if (observed.terrain == null || observed.terrain.IsDisposed ||
                    observed.terrain.BlockingRevision != observed.revision)
                {
                    dirty = true;
                    break;
                }
            }
        }
        if (!dirty) return;

        originX = nextOriginX;
        originY = nextOriginY;
        width = nextWidth;
        height = nextHeight;
        cachedWorld = worldKey;
        cachedDirection = windDirection;
        cachedBuildingRevision = nextRevision;
        cachedChunkCount = chunks.Chunks.Count;
        int count = width * height;
        if (blockedCells == null || blockedCells.Length != count)
        {
            blockedCells = new bool[count];
            flowPixels = new Color32[count];
        }

        bool hasBlocking = false;
        using (GameplayWorldRegistry.UseExistingWorldOrMachineScope(worldKey))
        {
            if (!chunks.TryCreateTerrainPresenceQuery(out ChunkMgr.RuntimeTerrainPresenceQuery terrain))
            {
                HasObstacles = false;
                return;
            }

            WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
            BuildingOccupancyRegistry.WindBlockingQuery buildings =
                BuildingOccupancyRegistry.CaptureWindBlockingQuery(worldKey);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(topology.NormalizeX(originX + x), topology.NormalizeY(originY + y));
                bool loaded = terrain.TryGetTerrainWindBlockingNormalized(cell, out bool wall);
                bool blocked = loaded && (wall || buildings.IsBlocked(cell));
                blockedCells[y * width + x] = blocked;
                hasBlocking |= blocked;
            }
        }

        terrainVersions.Clear();
        foreach (var pair in chunks.Chunks)
        {
            ChunkRuntime chunk = pair.Value;
            ChunkTerrainData terrain = chunk?.Terrain;
            if (terrain == null || terrain.IsDisposed || chunk.DataStatus != ChunkDataStatus.Ready) continue;
            int x = chunk.Address.ChunkOrigin.X, y = chunk.Address.ChunkOrigin.Y;
            if (x + terrain.Width < originX || y + terrain.Height < originY ||
                x > originX + width || y > originY + height) continue;
            terrainVersions.Add((terrain, terrain.BlockingRevision));
        }

        HasObstacles = hasBlocking;
        if (!hasBlocking) return;
        BuildFlowPixels(blockedCells, width, height, new Vector2Int(originX, originY),
            windDirection, flowPixels);
        if (flowTexture == null || flowTexture.width != width || flowTexture.height != height)
        {
            CoreUtils.Destroy(flowTexture);
            flowTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "Wind Streak Obstacle Flow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
        }
        flowTexture.SetPixels32(flowPixels);
        flowTexture.Apply(false, false);
    }

    #endregion

    #region 沿墙体轮廓分流并重新汇入主风向

    private readonly struct ObstacleSpan
    {
        public readonly float MinAlong, MaxAlong, MinAcross, MaxAcross;
        public ObstacleSpan(float minAlong, float maxAlong, float minAcross, float maxAcross)
        {
            MinAlong = minAlong;
            MaxAlong = maxAlong;
            MinAcross = minAcross;
            MaxAcross = maxAcross;
        }
    }

    // 同一套纯格计算供实际 GPU 纹理和 Lua 规则采样，不需要读取 Scene 或 GameObject。
    internal void BuildFlowPixels(bool[] blocked, int mapWidth, int mapHeight,
        Vector2Int origin, Vector2 windDirection, Color32[] pixels)
    {
        Vector2 direction = windDirection.normalized;
        Vector2 across = new(-direction.y, direction.x);
        float tileHalfProjection = 0.5f * (Mathf.Abs(direction.x) + Mathf.Abs(direction.y));
        int count = mapWidth * mapHeight;
        if (visitedScratch == null || visitedScratch.Length != count)
        {
            visitedScratch = new bool[count];
            queueScratch = new int[count];
            bendScratch = new float[count];
        }
        Array.Clear(visitedScratch, 0, count);
        Array.Clear(bendScratch, 0, count);
        spanScratch.Clear();
        bool[] visited = visitedScratch;
        int[] queue = queueScratch;

        for (int index = 0; index < count; index++)
        {
            if (!blocked[index] || visited[index]) continue;
            int first = 0, last = 1;
            queue[0] = index;
            visited[index] = true;
            float minAlong = float.PositiveInfinity, maxAlong = float.NegativeInfinity;
            float minAcross = float.PositiveInfinity, maxAcross = float.NegativeInfinity;
            while (first < last)
            {
                int current = queue[first++];
                int x = current % mapWidth, y = current / mapWidth;
                Vector2 point = new(origin.x + x + 0.5f, origin.y + y + 0.5f);
                float along = Vector2.Dot(point, direction), cross = Vector2.Dot(point, across);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
                minAcross = Mathf.Min(minAcross, cross);
                maxAcross = Mathf.Max(maxAcross, cross);

                // 对角接缝同样属于连续挡风墙，不能把墙角的零宽缝隙当作新风道。
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx != 0 || dy != 0) && (uint)nx < mapWidth && (uint)ny < mapHeight)
                        Enqueue(ny * mapWidth + nx);
                }
            }
            spanScratch.Add(new ObstacleSpan(minAlong - tileHalfProjection, maxAlong + tileHalfProjection,
                minAcross - tileHalfProjection, maxAcross + tileHalfProjection));

            void Enqueue(int next)
            {
                if (!blocked[next] || visited[next]) return;
                visited[next] = true;
                queue[last++] = next;
            }
        }

        foreach (ObstacleSpan span in spanScratch)
        {
            // 每段墙只修改附近格子，不做「全屏格数 × 全部墙段」的重复遍历。
            float minAlong = span.MinAlong - ApproachDistance, maxAlong = span.MaxAlong + RejoinDistance;
            float minCross = span.MinAcross - SideInfluenceDistance;
            float maxCross = span.MaxAcross + SideInfluenceDistance;
            int minX = Mathf.Clamp(Mathf.FloorToInt(
                Mathf.Min(direction.x * minAlong, direction.x * maxAlong) +
                Mathf.Min(across.x * minCross, across.x * maxCross) - origin.x) - 1, 0, mapWidth - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(
                Mathf.Max(direction.x * minAlong, direction.x * maxAlong) +
                Mathf.Max(across.x * minCross, across.x * maxCross) - origin.x) + 1, 0, mapWidth - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(
                Mathf.Min(direction.y * minAlong, direction.y * maxAlong) +
                Mathf.Min(across.y * minCross, across.y * maxCross) - origin.y) - 1, 0, mapHeight - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(
                Mathf.Max(direction.y * minAlong, direction.y * maxAlong) +
                Mathf.Max(across.y * minCross, across.y * maxCross) - origin.y) + 1, 0, mapHeight - 1);

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                int index = y * mapWidth + x;
                Vector2 point = new(origin.x + x + 0.5f, origin.y + y + 0.5f);
                float along = Vector2.Dot(point, direction), cross = Vector2.Dot(point, across);
                if (along <= span.MinAlong - ApproachDistance || along >= span.MaxAlong + RejoinDistance)
                    continue;

                float middle = (span.MinAcross + span.MaxAcross) * 0.5f;
                float side = cross < middle ? -1f : 1f;
                float edge = side < 0f ? span.MinAcross : span.MaxAcross;
                float distanceOutside = Mathf.Max(0f, side * (cross - edge));
                if (distanceOutside >= SideInfluenceDistance) continue;

                float enter = Smooth01((along - span.MinAlong + ApproachDistance) / ApproachDistance);
                float leave = 1f - Smooth01((along - span.MaxAlong) / RejoinDistance);
                float sideFade = 1f - Smooth01(distanceOutside / SideInfluenceDistance);
                float split = Smooth01(Mathf.Abs(cross - middle) / MiddleBlendDistance);
                float candidate = side * Mathf.Min(MaximumLateralBend, Mathf.Abs(edge - middle) + 0.5f) *
                                  enter * leave * sideFade * split;
                if (Mathf.Abs(candidate) > Mathf.Abs(bendScratch[index])) bendScratch[index] = candidate;
            }
        }
        for (int index = 0; index < count; index++)
        {
            // R 存横向弯流，G 存墙体遮挡；Shader 使用双线性插值实现平滑破风。
            byte encodedBend = (byte)Mathf.Clamp(Mathf.RoundToInt(
                128f + bendScratch[index] * (127f / MaximumLateralBend)), 0, 255);
            pixels[index] = new Color32(encodedBend, blocked[index] ? (byte)255 : (byte)0, 0, 255);
        }
    }

    private static float Smooth01(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3f - 2f * t);
    }

    #endregion
}
