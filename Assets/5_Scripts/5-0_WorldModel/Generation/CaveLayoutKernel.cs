using System;

namespace FlatWorld.WorldModel
{
    /// <summary>洞穴单格对应的地表高度参考；缺少配对时保持旧洞穴规则。</summary>
    internal readonly struct CaveSurfaceInfluenceSample
    {
        /// <summary>保存一格地表高度及其海洋、山地判定线。</summary>
        public CaveSurfaceInfluenceSample(double height, double seaLevel, double mountainLevel)
        {
            HasHeightReference = true;
            Height = height;
            SeaLevel = seaLevel;
            MountainLevel = mountainLevel;
        }

        /// <summary>是否成功取得冻结地表参数对应的高度。</summary>
        public bool HasHeightReference { get; }
        /// <summary>地表高度图在当前世界格的采样值。</summary>
        public double Height { get; }
        /// <summary>冻结地表 Profile 的海平面。</summary>
        public double SeaLevel { get; }
        /// <summary>冻结地表 Profile 的山地高度线。</summary>
        public double MountainLevel { get; }
        /// <summary>当前格的地表是否属于海洋。</summary>
        public bool IsOcean => HasHeightReference && Height < SeaLevel;
        /// <summary>当前高度是否位于允许生成地下水的地表高度带。</summary>
        public bool AllowsGroundwater => !HasHeightReference ||
            Height >= SeaLevel && Height < MountainLevel;
    }

    /// <summary>
    /// 迁移旧矿洞的纯数据布局内核。
    /// 保留“椭圆房间 + 两段弯曲隧道 + 入口安全区 + 洞壁矿脉”的设计，不依赖 Unity、Map 或对象池，
    /// 因此同一种子在任何区块生成顺序下都能得到相同结果。
    /// </summary>
    public static class CaveLayoutKernel
    {
        #region 常量与基础类型

        /// <summary>每个正式概率格保留四个候选点，地表和洞穴都会确定性选择其中第一个可用点。</summary>
        public const int PortalCandidateCount = 4;

        private readonly struct Point
        {
            public Point(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }

            public static Point operator +(Point left, Point right) =>
                new(left.X + right.X, left.Y + right.Y);
            public static Point operator -(Point left, Point right) =>
                new(left.X - right.X, left.Y - right.Y);
            public static Point operator *(Point value, double scalar) =>
                new(value.X * scalar, value.Y * scalar);
        }

        private readonly struct Room
        {
            public Room(Point center, double radiusX, double radiusY, double angleRadians)
            {
                Center = center;
                RadiusX = radiusX;
                RadiusY = radiusY;
                AngleRadians = angleRadians;
            }

            public Point Center { get; }
            public double RadiusX { get; }
            public double RadiusY { get; }
            public double AngleRadians { get; }
        }

        #endregion

        #region 洞穴布局

        /// <summary>只使用冻结的地表 Profile、种子与拓扑复算当前格高度，不访问运行时地表区块。</summary>
        internal static CaveSurfaceInfluenceSample SampleSurfaceInfluence(
            ChunkGenerationRequest request, int worldX, int worldY)
        {
            CavePortalPairingSnapshot pairing = request.Profile.PortalPairing;
            if (pairing == null ||
                pairing.SurfaceProfile.Settings.Mode != ChunkGenerationMode.Surface)
            {
                return default;
            }

            ChunkGenerationRequest surfaceRequest = pairing.CreateSurfaceRequest(
                request, request.Address.ChunkOrigin);
            ChunkGenerationSettingsSnapshot surfaceSettings = pairing.SurfaceProfile.Settings;
            double height = DeterministicChunkGenerator.SampleHeight(
                surfaceRequest, surfaceSettings, worldX, worldY);
            return new CaveSurfaceInfluenceSample(
                height, surfaceSettings.SeaLevel, surfaceSettings.MountainLevel);
        }

        /// <summary>判断世界格是否属于旧版房间、隧道或入口安全网络中的可行走洞穴。</summary>
        public static bool IsOpenAtWorld(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            CaveSurfaceInfluenceSample surfaceInfluence =
                SampleSurfaceInfluence(request, worldX, worldY);
            return IsOpenAtWorld(request, settings, worldX, worldY, surfaceInfluence);
        }

        /// <summary>使用已采样地表高度判断洞穴开放状态，避免同一格重复计算高度噪声。</summary>
        internal static bool IsOpenAtWorld(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY,
            CaveSurfaceInfluenceSample surfaceInfluence)
        {
            Int2 normalized = Normalize(request.Topology, new Int2(worldX, worldY));
            Point point = new(normalized.X + 0.5d, normalized.Y + 0.5d);
            int portalSeed = GetPortalSeed(request, settings);
            if (IsInsidePortalNetwork(request, settings, point, portalSeed))
                return true;

            Point defaultSpawn = Normalize(request.Topology,
                new Point(settings.CaveSpawnX, settings.CaveSpawnY));
            double spawnRadius = Math.Max(4d, settings.CaveSpawnSafeRadius + 1d);
            if (DistanceSquared(request.Topology, point, defaultSpawn) <= spawnRadius * spawnRadius)
                return true;

            Int2 entranceRegion = GetRegionCoordinates(request.Topology, settings, defaultSpawn);
            Room entranceRoom = CreateRoom(request.Topology, settings, entranceRegion.X,
                entranceRegion.Y, request.WorldSeed);
            if (IsInsideTunnel(request.Topology, settings, point, defaultSpawn,
                    entranceRoom.Center, Hash(request.WorldSeed, entranceRegion.X,
                        entranceRegion.Y, 7919)))
            {
                return true;
            }

            Int2 region = GetRegionCoordinates(request.Topology, settings, point);
            if (ShouldSealSurfaceOceanRegion(
                    request, settings, region, surfaceInfluence))
            {
                return false;
            }

            for (int regionX = region.X - 1; regionX <= region.X + 1; regionX++)
            {
                for (int regionY = region.Y - 1; regionY <= region.Y + 1; regionY++)
                {
                    Room room = CreateRoom(request.Topology, settings, regionX, regionY,
                        request.WorldSeed);
                    if (IsInsideRoom(request.Topology, settings, point, room, request.WorldSeed))
                        return true;

                    Room right = CreateRoom(request.Topology, settings, regionX + 1, regionY,
                        request.WorldSeed);
                    if (IsInsideTunnel(request.Topology, settings, point, room.Center, right.Center,
                            HashRoom(request.Topology, settings, request.WorldSeed, regionX,
                                regionY, 101)))
                    {
                        return true;
                    }

                    Room up = CreateRoom(request.Topology, settings, regionX, regionY + 1,
                        request.WorldSeed);
                    if (IsInsideTunnel(request.Topology, settings, point, room.Center, up.Center,
                            HashRoom(request.Topology, settings, request.WorldSeed, regionX,
                                regionY, 211)))
                    {
                        return true;
                    }

                    if ((HashRoom(request.Topology, settings, request.WorldSeed, regionX,
                            regionY, 307) & 3u) == 0u)
                    {
                        Room diagonal = CreateRoom(request.Topology, settings, regionX + 1,
                            regionY + 1, request.WorldSeed);
                        if (IsInsideTunnel(request.Topology, settings, point, room.Center,
                                diagonal.Center, HashRoom(request.Topology, settings,
                                    request.WorldSeed, regionX, regionY, 401)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>洞穴空地四邻接任一岩壁时视为洞壁，可放置矿脉。</summary>
        public static bool IsWallEdge(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            if (!IsOpenAtWorld(request, settings, worldX, worldY))
                return false;

            return !IsOpenAtWorld(request, settings, worldX - 1, worldY) ||
                   !IsOpenAtWorld(request, settings, worldX + 1, worldY) ||
                   !IsOpenAtWorld(request, settings, worldX, worldY + 1) ||
                   !IsOpenAtWorld(request, settings, worldX, worldY - 1);
        }

        /// <summary>检查默认出生点附近，保持旧版不在出生安全区刷矿的规则。</summary>
        public static bool IsInsideDefaultSpawnSafeArea(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            Point point = new(worldX + 0.5d, worldY + 0.5d);
            Point spawn = Normalize(request.Topology,
                new Point(settings.CaveSpawnX, settings.CaveSpawnY));
            double radius = settings.CaveSpawnSafeRadius;
            return DistanceSquared(request.Topology, point, spawn) <= radius * radius;
        }

        /// <summary>
        /// 在洞室内部采样确定性的地下湖水深；出生区、天然出口及其连接通道始终保持干燥。
        /// 以世界区域而非区块坐标选湖，保证湖面跨 Chunk 连续且不受加载顺序影响。
        /// </summary>
        public static double SampleGroundwaterDepth(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            CaveSurfaceInfluenceSample surfaceInfluence =
                SampleSurfaceInfluence(request, worldX, worldY);
            return SampleGroundwaterDepth(
                request, settings, worldX, worldY, surfaceInfluence);
        }

        /// <summary>使用已采样地表高度限制地下水，只允许海平面至山地线之间的地表高度带。</summary>
        internal static double SampleGroundwaterDepth(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY,
            CaveSurfaceInfluenceSample surfaceInfluence)
        {
            if (!settings.CaveGroundwaterEnabled || settings.CaveGroundwaterRoomChance <= 0d ||
                !surfaceInfluence.AllowsGroundwater ||
                IsInsideDefaultSpawnSafeArea(request, settings, worldX, worldY))
                return 0d;

            Int2 normalized = Normalize(request.Topology, new Int2(worldX, worldY));
            Point point = new(normalized.X + 0.5d, normalized.Y + 0.5d);
            if (IsInsidePortalNetwork(request, settings, point, GetPortalSeed(request, settings)))
                return 0d;

            Int2 region = GetRegionCoordinates(request.Topology, settings, point);
            double deepest = 0d;
            for (int regionX = region.X - 1; regionX <= region.X + 1; regionX++)
            for (int regionY = region.Y - 1; regionY <= region.Y + 1; regionY++)
            {
                uint state = HashRoom(request.Topology, settings, request.WorldSeed,
                    regionX, regionY, 0x2f6e2b1);
                if (NextUnitDouble(ref state) >= settings.CaveGroundwaterRoomChance)
                    continue;

                Room room = CreateRoom(request.Topology, settings, regionX, regionY,
                    request.WorldSeed);
                double radiusRatio = Lerp(settings.CaveGroundwaterMinRadiusRatio,
                    settings.CaveGroundwaterMaxRadiusRatio, NextUnitDouble(ref state));
                double offsetX = Lerp(-0.16d, 0.16d, NextUnitDouble(ref state)) * room.RadiusX;
                double offsetY = Lerp(-0.16d, 0.16d, NextUnitDouble(ref state)) * room.RadiusY;
                Point lakeCenter = room.Center + new Point(offsetX, offsetY);
                Point delta = ShortestDelta(request.Topology, lakeCenter, point);
                double sin = Math.Sin(room.AngleRadians);
                double cos = Math.Cos(room.AngleRadians);
                double localX = delta.X * cos + delta.Y * sin;
                double localY = -delta.X * sin + delta.Y * cos;
                double radiusX = Math.Max(0.5d, room.RadiusX * radiusRatio);
                double radiusY = Math.Max(0.5d, room.RadiusY * radiusRatio * 0.82d);
                double normalizedDistance = Math.Sqrt(
                    localX * localX / (radiusX * radiusX) +
                    localY * localY / (radiusY * radiusY));
                double shorelineNoise = SampleNoise01(request.Topology, point,
                    request.WorldSeed, 0.19d, 0x51f15e);
                double shoreline = 1d + (shorelineNoise - 0.5d) * 0.22d;
                if (normalizedDistance >= shoreline)
                    continue;

                double centerStrength = Clamp01(1d - normalizedDistance / shoreline);
                double depth = Lerp(settings.CaveGroundwaterMinDepth,
                    settings.CaveGroundwaterMaxDepth, Math.Sqrt(centerStrength));
                deepest = Math.Max(deepest, depth);
            }

            return deepest;
        }

        /// <summary>在干燥洞壁边缘确定性生成藤蔓；地下水两格内提高概率。</summary>
        public static bool ShouldPlaceVine(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            if (!settings.CaveVineEnabled || settings.CaveVineWallChance <= 0d ||
                !IsWallEdge(request, settings, worldX, worldY) ||
                IsInsideDefaultSpawnSafeArea(request, settings, worldX, worldY) ||
                SampleGroundwaterDepth(request, settings, worldX, worldY) > 0d)
                return false;

            Point point = new(worldX + 0.5d, worldY + 0.5d);
            if (IsInsidePortalNetwork(request, settings, point, GetPortalSeed(request, settings)))
                return false;

            bool nearWater = false;
            for (int offsetY = -2; offsetY <= 2 && !nearWater; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                if (offsetX * offsetX + offsetY * offsetY > 4)
                    continue;
                if (SampleGroundwaterDepth(request, settings, worldX + offsetX,
                        worldY + offsetY) > 0d)
                {
                    nearWater = true;
                    break;
                }
            }

            // 水池周围保持原有湿润生成量；其他干燥区域只保留配置比例。
            double chance = settings.CaveVineWallChance *
                (nearWater ? settings.CaveVineWetMultiplier : settings.CaveVineDryMultiplier);
            Int2 normalized = Normalize(request.Topology, new Int2(worldX, worldY));
            uint state = Hash(request.WorldSeed, normalized.X, normalized.Y, 0x18d5a37);
            return NextUnitDouble(ref state) < Math.Min(1d, chance);
        }

        #endregion

        #region 天然传送门布局

        /// <summary>取跨维度共用的入口随机种子；运行时会把基础世界种子注入两套 Profile。</summary>
        public static int GetPortalSeed(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings)
        {
            int source = settings.CavePortalBaseSeed == 0
                ? request.WorldSeed
                : settings.CavePortalBaseSeed;
            Int2 portalChunkSize = GetPortalChunkSize(request, settings);
            uint value = Hash(source, portalChunkSize.X, portalChunkSize.Y,
                settings.CavePortalSeedSalt);
            int result = unchecked((int)value);
            return result == 0 ? 1 : result;
        }

        /// <summary>
        /// 返回天然传送门概率格的正式尺寸。
        /// 正常运行时沿用 Profile 区块尺寸；连续大范围预览可显式固定为原始区块尺寸，避免概率被临时大区块稀释。
        /// </summary>
        public static Int2 GetPortalChunkSize(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings)
        {
            int width = settings.CavePortalChunkWidth > 0
                ? settings.CavePortalChunkWidth
                : request.Profile.Width;
            int height = settings.CavePortalChunkHeight > 0
                ? settings.CavePortalChunkHeight
                : request.Profile.Height;
            return new Int2(Math.Max(1, width), Math.Max(1, height));
        }

        /// <summary>给定区块是否会生成天然矿洞入口。</summary>
        public static bool ShouldGeneratePortal(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 chunkOrigin)
        {
            if (!settings.CavePortalEnabled || settings.CavePortalChunkChance <= 0d)
                return false;
            if (settings.CavePortalChunkChance >= 1d)
                return true;

            Int2 normalized = Normalize(request.Topology, chunkOrigin);
            uint state = Hash(GetPortalSeed(request, settings), normalized.X, normalized.Y,
                0x045d9f3b);
            return NextUnitDouble(ref state) < settings.CavePortalChunkChance;
        }

        /// <summary>计算一个候选点在区块中的稳定格坐标。</summary>
        public static Int2 GetPortalCandidate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 chunkOrigin, int candidateIndex)
        {
            Int2 portalChunkSize = GetPortalChunkSize(request, settings);
            int width = portalChunkSize.X;
            int height = portalChunkSize.Y;
            int marginX = width >= 5 ? 2 : 0;
            int marginY = height >= 5 ? 2 : 0;
            int availableWidth = Math.Max(1, width - marginX * 2);
            int availableHeight = Math.Max(1, height - marginY * 2);
            int normalizedIndex = Math.Max(0, candidateIndex);
            Int2 normalizedOrigin = Normalize(request.Topology, chunkOrigin);
            uint state = Hash(GetPortalSeed(request, settings), normalizedOrigin.X,
                normalizedOrigin.Y, unchecked(0x27d4eb2d + normalizedIndex * 0x165667b1));
            int localX = marginX + Math.Min(availableWidth - 1,
                (int)Math.Floor(NextUnitDouble(ref state) * availableWidth));
            int localY = marginY + Math.Min(availableHeight - 1,
                (int)Math.Floor(NextUnitDouble(ref state) * availableHeight));
            return Normalize(request.Topology,
                new Int2(normalizedOrigin.X + localX, normalizedOrigin.Y + localY));
        }

        #endregion

        #region 矿脉噪声

        /// <summary>复刻旧洞穴的粗细两层矿脉强度，用于先筛掉没有矿脉的洞壁。</summary>
        public static double GetDepositStrength(ChunkGenerationRequest request, int worldX,
            int worldY)
        {
            Point point = new(worldX, worldY);
            double broad = SampleNoise01(request.Topology, point, request.WorldSeed,
                0.052d, 1703);
            double detail = SampleNoise01(request.Topology, point, request.WorldSeed,
                0.14d, -2909);
            return broad * 0.72d + detail * 0.28d;
        }

        /// <summary>按单条规则采样矿脉；阈值由调用方按旧版顺序进行筛选。</summary>
        public static double SampleVein(ChunkGenerationRequest request, int worldX, int worldY,
            CaveResourceRuleSnapshot rule)
        {
            if (rule == null)
                return 0d;
            return SampleNoise01(request.Topology, new Point(worldX, worldY),
                request.WorldSeed, rule.VeinScale, rule.NoiseOffset);
        }

        #endregion

        #region 稳定哈希

        /// <summary>生成稳定 Item GUID；规则 ID 参与哈希避免同格不同物品互相覆盖。</summary>
        public static int CreatePlacementGuid(ChunkGenerationRequest request, int worldX,
            int worldY, string ruleId, int sequence = 0)
        {
            unchecked
            {
                uint state = Hash(request.WorldSeed, worldX + sequence * 31,
                    worldY - sequence * 17, 0x4a3f1c2d);
                string id = ruleId ?? string.Empty;
                for (int i = 0; i < id.Length; i++)
                    state = (state ^ id[i]) * 16777619u;
                int guid = (int)(state & 0x7fffffffU);
                return guid == 0 ? 1 : guid;
            }
        }

        /// <summary>与旧洞穴使用同一类 FNV/xorshift 确定性随机，避免 Unity 随机状态。</summary>
        public static uint Hash(int seed, int x, int y, int salt)
        {
            unchecked
            {
                uint state = 2166136261u;
                state = (state ^ (uint)seed) * 16777619u;
                state = (state ^ (uint)x) * 16777619u;
                state = (state ^ (uint)y) * 16777619u;
                state = (state ^ (uint)salt) * 16777619u;
                return state == 0u ? 0x9e3779b9u : state;
            }
        }

        public static double NextUnitDouble(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00ffffffU) / 16777216d;
        }

        #endregion

        #region 房间、隧道与拓扑辅助

        /// <summary>按洞穴逻辑区域稳定决定海洋下方是否封为石墙，避免逐格随机产生碎裂孔洞。</summary>
        private static bool ShouldSealSurfaceOceanRegion(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 region,
            CaveSurfaceInfluenceSample surfaceInfluence)
        {
            if (!surfaceInfluence.IsOcean || settings.CaveSurfaceOceanWallChance <= 0d)
                return false;
            if (settings.CaveSurfaceOceanWallChance >= 1d)
                return true;

            uint state = HashRoom(request.Topology, settings, request.WorldSeed,
                region.X, region.Y, 0x62f5a91);
            return NextUnitDouble(ref state) < settings.CaveSurfaceOceanWallChance;
        }

        private static bool IsInsidePortalNetwork(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Point point, int portalSeed)
        {
            if (!settings.CavePortalEnabled || settings.CavePortalChunkChance <= 0d)
                return false;

            Int2 chunkSize = GetPortalChunkSize(request, settings);
            Int2 currentChunk = GetChunkOrigin(request.Topology, point, chunkSize);
            double safeRadius = settings.CavePortalSafeRadius;
            double reach = settings.CaveRegionSize + settings.CaveRoomMaxRadius + safeRadius;
            int searchX = Math.Max(1, (int)Math.Ceiling(reach / chunkSize.X));
            int searchY = Math.Max(1, (int)Math.Ceiling(reach / chunkSize.Y));
            for (int offsetX = -searchX; offsetX <= searchX; offsetX++)
            {
                for (int offsetY = -searchY; offsetY <= searchY; offsetY++)
                {
                    Int2 origin = Normalize(request.Topology, new Int2(
                        currentChunk.X + offsetX * chunkSize.X,
                        currentChunk.Y + offsetY * chunkSize.Y));
                    if (!ShouldGeneratePortal(request, settings, origin))
                        continue;

                    for (int candidateIndex = 0; candidateIndex < PortalCandidateCount;
                         candidateIndex++)
                    {
                        Int2 cell = GetPortalCandidate(request, settings, origin,
                            candidateIndex);
                        Point entrance = Normalize(request.Topology,
                            new Point(cell.X + 0.5d, cell.Y + 0.5d));
                        if (DistanceSquared(request.Topology, point, entrance) <=
                            safeRadius * safeRadius)
                        {
                            return true;
                        }

                        Int2 region = GetRegionCoordinates(request.Topology, settings, entrance);
                        Room room = CreateRoom(request.Topology, settings, region.X, region.Y,
                            request.WorldSeed);
                        if (IsInsideTunnel(request.Topology, settings, point, entrance,
                                room.Center, Hash(portalSeed, cell.X, cell.Y,
                                    7919 + candidateIndex * 397)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static Room CreateRoom(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, int regionX, int regionY, int worldSeed)
        {
            Int2 count = GetRegionCount(topology, settings);
            Int2 region = CanonicalRegion(new Int2(regionX, regionY), count, topology.IsWrapped);
            uint state = Hash(worldSeed, region.X, region.Y, 17);
            Point extent = GetRegionExtent(topology, settings, count);
            Point jitter = extent * 0.32d;
            Point center = GetRegionMin(topology, settings, region, extent) + extent * 0.5d +
                new Point(Lerp(-jitter.X, jitter.X, NextUnitDouble(ref state)),
                    Lerp(-jitter.Y, jitter.Y, NextUnitDouble(ref state)));
            return new Room(center,
                Lerp(settings.CaveRoomMinRadius, settings.CaveRoomMaxRadius,
                    NextUnitDouble(ref state)),
                Lerp(settings.CaveRoomMinRadius * 0.82d,
                    settings.CaveRoomMaxRadius * 0.94d, NextUnitDouble(ref state)),
                Lerp(-Math.PI, Math.PI, NextUnitDouble(ref state)));
        }

        private static bool IsInsideRoom(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, Point point, Room room, int worldSeed)
        {
            Point delta = ShortestDelta(topology, room.Center, point);
            double sin = Math.Sin(room.AngleRadians);
            double cos = Math.Cos(room.AngleRadians);
            double localX = delta.X * cos + delta.Y * sin;
            double localY = -delta.X * sin + delta.Y * cos;
            double normalizedDistance = Math.Sqrt(
                localX * localX / (room.RadiusX * room.RadiusX) +
                localY * localY / (room.RadiusY * room.RadiusY));
            double edgeNoise = SampleNoise01(topology, point, worldSeed, 0.16d, 503);
            return normalizedDistance <= 1d + (edgeNoise - 0.5d) * 0.24d;
        }

        private static bool IsInsideTunnel(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, Point point, Point start, Point end,
            uint state)
        {
            end = start + ShortestDelta(topology, start, end);
            point = start + ShortestDelta(topology, start, point);
            Point direction = end - start;
            double length = Length(direction);
            if (length <= 0.001d)
                return false;

            Point perpendicular = new(-direction.Y / length, direction.X / length);
            Point bendA = start + direction * 0.34d + perpendicular *
                Lerp(-5.5d, 5.5d, NextUnitDouble(ref state));
            Point bendB = start + direction * 0.67d + perpendicular *
                Lerp(-5.5d, 5.5d, NextUnitDouble(ref state));
            double radius = Lerp(settings.CaveTunnelMinRadius, settings.CaveTunnelMaxRadius,
                NextUnitDouble(ref state));
            double edgeNoise = SampleNoise01(topology, point, unchecked((int)state),
                0.21d, 883);
            double effectiveRadius = radius + (edgeNoise - 0.5d) * 0.55d;
            double distance = Math.Min(DistanceToSegment(point, start, bendA),
                Math.Min(DistanceToSegment(point, bendA, bendB),
                    DistanceToSegment(point, bendB, end)));
            return distance <= effectiveRadius;
        }

        private static Int2 GetRegionCoordinates(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, Point point)
        {
            if (!topology.IsWrapped)
            {
                return new Int2(FloorDiv(point.X, settings.CaveRegionSize),
                    FloorDiv(point.Y, settings.CaveRegionSize));
            }

            Int2 count = GetRegionCount(topology, settings);
            Point extent = GetRegionExtent(topology, settings, count);
            Point normalized = Normalize(topology, point);
            return CanonicalRegion(new Int2(
                    (int)Math.Floor((normalized.X - topology.Min.X) / extent.X),
                    (int)Math.Floor((normalized.Y - topology.Min.Y) / extent.Y)),
                count,
                true);
        }

        private static Int2 GetChunkOrigin(ChunkGenerationTopologySnapshot topology,
            Point point, Int2 chunkSize)
        {
            if (!topology.IsWrapped)
            {
                return new Int2(FloorDiv(point.X, chunkSize.X) * chunkSize.X,
                    FloorDiv(point.Y, chunkSize.Y) * chunkSize.Y);
            }

            Point normalized = Normalize(topology, point);
            int relativeX = (int)Math.Floor(normalized.X) - topology.Min.X;
            int relativeY = (int)Math.Floor(normalized.Y) - topology.Min.Y;
            return new Int2(topology.Min.X + relativeX / chunkSize.X * chunkSize.X,
                topology.Min.Y + relativeY / chunkSize.Y * chunkSize.Y);
        }

        private static Int2 GetRegionCount(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings)
        {
            return topology.IsWrapped
                ? new Int2(Math.Max(1, (int)Math.Round(topology.Span.X /
                    (double)settings.CaveRegionSize, MidpointRounding.AwayFromZero)),
                    Math.Max(1, (int)Math.Round(topology.Span.Y /
                        (double)settings.CaveRegionSize, MidpointRounding.AwayFromZero)))
                : new Int2(int.MaxValue, int.MaxValue);
        }

        private static Point GetRegionExtent(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, Int2 regionCount)
        {
            return topology.IsWrapped
                ? new Point(topology.Span.X / (double)regionCount.X,
                    topology.Span.Y / (double)regionCount.Y)
                : new Point(settings.CaveRegionSize, settings.CaveRegionSize);
        }

        private static Point GetRegionMin(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, Int2 region, Point regionExtent)
        {
            return topology.IsWrapped
                ? new Point(topology.Min.X + region.X * regionExtent.X,
                    topology.Min.Y + region.Y * regionExtent.Y)
                : new Point(region.X * settings.CaveRegionSize,
                    region.Y * settings.CaveRegionSize);
        }

        private static uint HashRoom(ChunkGenerationTopologySnapshot topology,
            ChunkGenerationSettingsSnapshot settings, int worldSeed, int regionX, int regionY,
            int salt)
        {
            Int2 canonical = CanonicalRegion(new Int2(regionX, regionY),
                GetRegionCount(topology, settings), topology.IsWrapped);
            return Hash(worldSeed, canonical.X, canonical.Y, salt);
        }

        private static Int2 CanonicalRegion(Int2 region, Int2 count, bool wrapped)
        {
            if (!wrapped)
                return region;
            return new Int2(PositiveMod(region.X, count.X), PositiveMod(region.Y, count.Y));
        }

        private static Point Normalize(ChunkGenerationTopologySnapshot topology, Point point)
        {
            return !topology.IsWrapped
                ? point
                : new Point(Wrap(point.X, topology.Min.X, topology.Span.X),
                    Wrap(point.Y, topology.Min.Y, topology.Span.Y));
        }

        private static Int2 Normalize(ChunkGenerationTopologySnapshot topology, Int2 point)
        {
            return new Int2(topology.NormalizeX(point.X), topology.NormalizeY(point.Y));
        }

        private static Point ShortestDelta(ChunkGenerationTopologySnapshot topology,
            Point from, Point to)
        {
            double deltaX = to.X - from.X;
            double deltaY = to.Y - from.Y;
            if (!topology.IsWrapped)
                return new Point(deltaX, deltaY);

            deltaX = ShortestWrappedDelta(deltaX, topology.Span.X);
            deltaY = ShortestWrappedDelta(deltaY, topology.Span.Y);
            return new Point(deltaX, deltaY);
        }

        private static double DistanceSquared(ChunkGenerationTopologySnapshot topology,
            Point first, Point second)
        {
            Point delta = ShortestDelta(topology, first, second);
            return delta.X * delta.X + delta.Y * delta.Y;
        }

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            Point segment = end - start;
            double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
            if (lengthSquared <= 0.0001d)
                return Length(point - start);
            double t = Clamp01(((point.X - start.X) * segment.X +
                (point.Y - start.Y) * segment.Y) / lengthSquared);
            return Length(point - (start + segment * t));
        }

        private static double SampleNoise01(ChunkGenerationTopologySnapshot topology,
            Point point, int seed, double scale, int salt)
        {
            scale = scale <= 0d ? 0.01d : scale;
            Point normalized = Normalize(topology, point);
            double x;
            double y;
            int repeatX = 0;
            int repeatY = 0;
            if (topology.IsWrapped)
            {
                repeatX = Math.Max(1, (int)Math.Round(topology.Span.X * scale,
                    MidpointRounding.AwayFromZero));
                repeatY = Math.Max(1, (int)Math.Round(topology.Span.Y * scale,
                    MidpointRounding.AwayFromZero));
                x = (normalized.X - topology.Min.X) / topology.Span.X * repeatX;
                y = (normalized.Y - topology.Min.Y) / topology.Span.Y * repeatY;
            }
            else
            {
                x = normalized.X * scale;
                y = normalized.Y * scale;
            }

            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double tx = Smooth(x - x0);
            double ty = Smooth(y - y0);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            if (repeatX > 0)
            {
                x0 = PositiveMod(x0, repeatX);
                x1 = PositiveMod(x1, repeatX);
            }
            if (repeatY > 0)
            {
                y0 = PositiveMod(y0, repeatY);
                y1 = PositiveMod(y1, repeatY);
            }

            double bottom = Lerp(Hash01(seed, x0, y0, salt), Hash01(seed, x1, y0, salt), tx);
            double top = Lerp(Hash01(seed, x0, y1, salt), Hash01(seed, x1, y1, salt), tx);
            return Lerp(bottom, top, ty);
        }

        private static double Hash01(int seed, int x, int y, int salt)
        {
            return Hash(seed, x, y, salt) / (double)uint.MaxValue;
        }

        private static int FloorDiv(double value, int divisor) =>
            (int)Math.Floor(value / divisor);

        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static double Wrap(double value, int min, int span)
        {
            double offset = value - min;
            double wrapped = offset - Math.Floor(offset / span) * span;
            return min + wrapped;
        }

        private static double ShortestWrappedDelta(double value, int span)
        {
            double half = span * 0.5d;
            while (value > half)
                value -= span;
            while (value < -half)
                value += span;
            return value;
        }

        private static double Length(Point point) =>
            Math.Sqrt(point.X * point.X + point.Y * point.Y);
        private static double Lerp(double left, double right, double t) =>
            left + (right - left) * t;
        private static double Smooth(double value) => value * value * (3d - 2d * value);
        private static double Clamp01(double value) =>
            value < 0d ? 0d : value > 1d ? 1d : value;

        #endregion
    }
}
