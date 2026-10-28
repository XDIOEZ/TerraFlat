using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 不使用 Unity、可以放到后台运行的区块生成器。
    /// 所有“随机”结果都来自世界种子和坐标，所以输入相同时，无论先生成哪个区块，结果都一样。
    /// </summary>
    public sealed partial class DeterministicChunkGenerator : IChunkPureGenerator,
        IChunkEcologyNeighborhoodTagResolver
    {
        #region 生成配置与缓存身份

        /// <summary>纯区块生成规则版本；气候、群系、河流或生态空间分布规则改变时递增。</summary>
        public const int CurrentGenerationSignature = 81;

        private static long nextGenerationCacheInstanceId;
        // 缓存用实例编号区分冻结扩展，不把生成器强引用留在共享缓存里。
        internal long GenerationCacheInstanceId { get; } = Interlocked.Increment(ref nextGenerationCacheInstanceId);
        private readonly LiquidTypeCatalog liquidTypes;
        private readonly Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken> terrainDecorator;
        private readonly RegionalWeatherSettings weatherSettings;
        /// <summary>资源就绪后注入会话液体表；离线纯算法测试可以使用本体最小目录。</summary>
        public DeterministicChunkGenerator(LiquidTypeCatalog liquidTypes = null,
            Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken> terrainDecorator = null,
            RegionalWeatherSettings weatherSettings = null)
        {
            this.liquidTypes = liquidTypes ?? LiquidTypeCatalog.BuiltIn;
            this.terrainDecorator = terrainDecorator;
            this.weatherSettings = (weatherSettings ?? new RegionalWeatherSettings()).Snapshot();
        }

        #endregion

        /// <summary>切换世界时取消当前宏观水文并释放旧纪元缓存。</summary>
        public void BeginNewWorldHydrologyEpoch(long retiredEpoch)
        {
            hydrologyRegions.RetireWorld(retiredEpoch);
            ClearEcologyHostTagCache();
            ClearCaveBaseEcologyCache();
            precipitationCache.Clear();
            while (precipitationCacheOrder.TryDequeue(out _)) { }
            oceanChunkCache.Clear();
            while (oceanChunkCacheOrder.TryDequeue(out _)) { }
            lavaBasins.Clear();
            oilBasins.Clear();
            while (oilBasinOrder.TryDequeue(out _)) { }
            while (lavaBasinOrder.TryDequeue(out _)) { }
        }

        /// <summary>冷河网区域串行领取一个生成名额；缓存就绪后同区域区块可并行完成。</summary>
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) =>
            hydrologyRegions.GetPendingGroupKey(request);

        #region 生成流水线

        // 草地状态用 1 和 2；数字 0 专门表示“这个格子还没处理过”，方便排查问题。
        private const byte GrassEmpty = ChunkTerrainData.GrassEmpty;
        private const byte GrassPresent = ChunkTerrainData.GrassPresent;
        // 这个数字表示“随机地形算法的版本”。只有确实要让旧世界换一种地形排列时才增加它。
        // 普通设置变化不应该改它，否则同一个旧世界的山川和气候会被整个重新随机。
        private const uint NoiseLayoutVersion = 5u;

        /// <summary>
        /// 从头生成一个完整区块。通常由后台工作人员调用。
        /// 如果中途取消或出错，会把临时内存清理掉，不留下垃圾数据。
        /// </summary>
        public ChunkGenerationResult Generate(ChunkGenerationRequest request,
            CancellationToken cancellationToken, ChunkGenerationTiming timing = null)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height, liquidTypes);
            ChunkGenerationTiming previousTiming = ChunkGenerationTiming.CurrentOnThread;
            ChunkGenerationTiming.CurrentOnThread = timing;
            try
            {
                bool cave = IsCaveGeneration(request);
                PopulateTerrain(request, terrain, cave, cancellationToken, timing);
                cancellationToken.ThrowIfCancellationRequested();
                using (timing?.MeasureStage("ecology.host_tags") ?? default)
                    CacheEcologyHostTags(request, terrain, cancellationToken);
                using (timing?.MeasureStage("terrain.weather_profile") ?? default)
                    terrain.InitializeWeatherProfile(weatherSettings,
                        !profile.NumericParameters.TryGetValue("weather.enabled", out double weatherEnabled) || weatherEnabled > 0d);
                ChunkEcologyData ecology;
                using (timing?.MeasureStage("ecology") ?? default)
                {
                    if (cave)
                    {
                        // 洞穴阶段合并当前 Profile 的植物规则、入口、藤蔓与矿物放置记录。
                        ecology = CaveGenerationFeatureGenerator.GenerateCave(
                            request, terrain, cancellationToken, this);
                    }
                    else
                    {
                        ChunkEcologyData surfaceEcology;
                        using (timing?.MeasureStage("ecology.input") ?? default)
                            surfaceEcology = ChunkEcologyGenerator.Generate(
                                request, terrain, profile.EcologyGlobalMultiplier,
                                profile.EcologyRules, cancellationToken, this);
                        // 天然矿洞入口优先占用候选格，避免与树木、灌木等生态物重叠。
                        ecology = CaveGenerationFeatureGenerator.AppendWorldPortals(
                            request, terrain, surfaceEcology, cancellationToken);
                    }
                }
                using (timing?.MeasureStage("ecology.shared_resources") ?? default)
                    ecology = AppendSharedResources(request, terrain, ecology, cancellationToken);
                return new ChunkGenerationResult(request, terrain, ecology);
            }
            catch
            {
                terrain.Dispose();
                throw;
            }
            finally
            {
                ChunkGenerationTiming.CurrentOnThread = previousTiming;
            }
        }

        #endregion

        #region 自然物邻区查询

        /// <summary>优先复用天然宿主标签，候选存在且缓存未命中时才重建邻区地形，不递归生成伴生物。</summary>
        public bool HasHostTagInChunk(ChunkGenerationRequest request, string tag,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfEcologyEpochRetired(request);
            var cacheKey = new EcologyHostTagKey(request, tag);
            if (ecologyHostTagCache.TryGetValue(cacheKey, out bool cached))
            {
                ThrowIfEcologyEpochRetired(request);
                return cached;
            }
            if (!ChunkEcologyGenerator.MayHaveHostTagInChunk(request,
                    request.Profile.EcologyGlobalMultiplier, request.Profile.EcologyRules,
                    tag, cancellationToken))
            {
                StoreEcologyHostTag(cacheKey, false, cancellationToken);
                return false;
            }
            var terrain = new ChunkTerrainBuffer(request.Profile.Width,
                request.Profile.Height, liquidTypes);
            try
            {
                using (ChunkGenerationTiming.CurrentOnThread?.MeasureStage("ecology.neighbor_terrain", accumulate: true) ?? default)
                    PopulateTerrain(request, terrain, IsCaveGeneration(request),
                        cancellationToken, null);
                bool found = ChunkEcologyGenerator.HasHostTagInChunk(request, terrain,
                    request.Profile.EcologyGlobalMultiplier, request.Profile.EcologyRules,
                    tag, cancellationToken);
                StoreEcologyHostTag(cacheKey, found, cancellationToken);
                return found;
            }
            finally { terrain.Dispose(); }
        }

        /// <summary>复用正式生成的地形、水文与结构顺序，保证邻区花朵查询与实际结果一致。</summary>
        private void PopulateTerrain(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, bool cave, CancellationToken cancellationToken,
            ChunkGenerationTiming timing)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            terrain.GenerationMode = profile.Settings.Mode;
            ChunkGenerationSettingsSnapshot settings = profile.Settings;
            GeneratedHydrologyMap riverMap = cave ? null :
                BuildSurfaceHydrologyMap(request, settings, cancellationToken, timing);
            using (timing?.MeasureStage("terrain.cells") ?? default)
            {
                if (cave)
                    GenerateCaveTerrain(request, settings, terrain, cancellationToken);
                else
                    GenerateSurfaceChunk(request, settings, terrain, riverMap,
                        cancellationToken, timing);
            }
            if (!cave)
            {
                using (timing?.MeasureStage("terrain.structures") ?? default)
                    ApplyStructures(request, settings, terrain, cancellationToken);
            }
            // 正式生成与邻区查询共用扩展，生态只读取最终地形。
            terrainDecorator?.Invoke(request, terrain, cancellationToken);
        }

        /// <summary>生成算法只由最终预设模式决定。</summary>
        private static bool IsCaveGeneration(ChunkGenerationRequest request) =>
            request.Profile.Settings.Mode == ChunkGenerationMode.Cave;

        #endregion

        #region 地表位置查询

        public readonly struct BiomeSearchResult
        {
            public BiomeSearchResult(bool found, Int2 cell, long sampledCount)
            {
                Found = found;
                Cell = cell;
                SampledCount = sampledCount;
            }

            public bool Found { get; }
            public Int2 Cell { get; }
            public long SampledCount { get; }
        }

        /// <summary>
        /// 从锚点沿固定斜线逐格寻找首个可走陆地，单格规则与正式区块一致。
        /// 结构不修改液体和可走标记，生态只生成物品放置记录，因此无需生成完整区块。
        /// </summary>
        public bool TryFindWalkableSurfaceAlongLine(
            string dimensionId,
            int worldSeed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology,
            Int2 anchor,
            int sampleBudget,
            out Int2 worldCell,
            CancellationToken cancellationToken = default)
        {
            return TryFindWalkableSurfaceAlongLine(dimensionId, worldSeed, profile,
                topology, anchor, sampleBudget, out worldCell,
                cancellationToken, 1);
        }

        /// <summary>指定正式世界纪元，允许出生搜索与随后的区块生成复用同一水文区域缓存。</summary>
        public bool TryFindWalkableSurfaceAlongLine(
            string dimensionId,
            int worldSeed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology,
            Int2 anchor,
            int sampleBudget,
            out Int2 worldCell,
            CancellationToken cancellationToken,
            long worldEpoch)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (worldEpoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldEpoch));
            if (profile.Settings.Mode != ChunkGenerationMode.Surface)
            {
                worldCell = anchor;
                return false;
            }

            dimensionId = string.IsNullOrWhiteSpace(dimensionId) ? "surface" : dimensionId;
            sampleBudget = Math.Max(1, sampleBudget);
            worldCell = anchor;
            var requests = new Dictionary<Int2, ChunkGenerationRequest>();
            var hydrologyMaps = new Dictionary<Int2, GeneratedHydrologyMap>();
            foreach (Int2 candidate in EnumerateSurfaceSpawnLine(anchor, topology, sampleBudget,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int2 origin = ResolveSearchChunkOrigin(candidate, profile, topology);
                int localX = candidate.X - origin.X;
                int localY = candidate.Y - origin.Y;
                if ((uint)localX >= (uint)profile.Width ||
                    (uint)localY >= (uint)profile.Height)
                    continue;

                if (!requests.TryGetValue(origin, out ChunkGenerationRequest request))
                {
                    request = new ChunkGenerationRequest(
                        worldEpoch,
                        new WorldAddress(dimensionId, origin),
                        worldSeed == 0 ? 1 : worldSeed,
                        1,
                        profile,
                        topology);
                    requests.Add(origin, request);
                }

                if (SampleHeight(request, profile.Settings, candidate.X, candidate.Y) <
                    profile.Settings.SeaLevel)
                    continue;

                if (!hydrologyMaps.TryGetValue(origin, out GeneratedHydrologyMap riverMap))
                {
                    riverMap = BuildSurfaceHydrologyMap(request, profile.Settings,
                        cancellationToken);
                    hydrologyMaps.Add(origin, riverMap);
                }

                // 直接读取正式单格结果，找到陆地即停，不额外复制整套环境层。
                SurfaceCellOutput output = SampleSurfaceCellOutput(request, profile.Settings, riverMap,
                    0, 0, candidate.X, candidate.Y, liquidTypes);
                if (output.LiquidLevel > 0 || !output.Cell.IsWalkable)
                    continue;

                worldCell = candidate;
                return true;
            }

            return false;
        }

        /// <summary>只在需要下一格时沿右上斜线前进一步；环绕回起点或坐标用尽后结束。</summary>
        private static IEnumerable<Int2> EnumerateSurfaceSpawnLine(
            Int2 anchor,
            ChunkGenerationTopologySnapshot topology,
            int sampleBudget,
            CancellationToken cancellationToken)
        {
            var start = new Int2(topology.NormalizeX(anchor.X), topology.NormalizeY(anchor.Y));
            Int2 candidate = start;
            for (int sampled = 0; sampled < sampleBudget; sampled++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candidate;
                long nextX = (long)candidate.X + 1;
                long nextY = (long)candidate.Y + 1;
                if (topology.IsWrapped)
                {
                    if (nextX >= (long)topology.Min.X + topology.Span.X) nextX = topology.Min.X;
                    if (nextY >= (long)topology.Min.Y + topology.Span.Y) nextY = topology.Min.Y;
                }
                else if (nextX > int.MaxValue || nextY > int.MaxValue)
                    yield break;
                candidate = new Int2((int)nextX, (int)nextY);
                if (candidate.Equals(start)) yield break;
            }
        }

        /// <summary>按世界拓扑和 Profile 尺寸计算候选格所属的规范区块原点。</summary>
        private static Int2 ResolveSearchChunkOrigin(Int2 position,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology)
        {
            int anchorX = topology.IsWrapped ? topology.Min.X : 0;
            int anchorY = topology.IsWrapped ? topology.Min.Y : 0;
            int originX = anchorX + FloorDiv(position.X - anchorX, profile.Width) * profile.Width;
            int originY = anchorY + FloorDiv(position.Y - anchorY, profile.Height) * profile.Height;
            return new Int2(topology.NormalizeX(originX), topology.NormalizeY(originY));
        }

        #endregion

        #region 确定性噪声与数值工具

        /// <summary>叠加多层不同频率的噪声，生成平滑的地形或气候数值。</summary>
        private static double Fractal(ulong seed, int worldX, int worldY, double scale,
            int octaves, double lacunarity, double persistence,
            ChunkGenerationTopologySnapshot topology)
        {
            // 把几张“大小起伏不同的随机地图”叠在一起：大图决定山势，小图补充细节。
            // 最后把结果缩放到大约 0 到 1，方便后续用统一阈值判断。
            double value = 0d;
            double amplitude = 1d;
            double frequency = scale;
            double total = 0d;
            for (int octave = 0; octave < octaves; octave++)
            {
                ulong octaveSeed = seed + (ulong)octave * 0x9e3779b97f4a7c15UL;
                double sample;
                if (topology.IsWrapped)
                {
                    // 有限世界把随机图也做成首尾相接，保证左右、上下和四个角都没有断缝。
                    int repeatX = Math.Max(1, (int)Math.Round(
                        topology.Span.X * frequency, MidpointRounding.AwayFromZero));
                    int repeatY = Math.Max(1, (int)Math.Round(
                        topology.Span.Y * frequency, MidpointRounding.AwayFromZero));
                    double periodicX = (worldX - topology.Min.X) /
                                       (double)topology.Span.X * repeatX;
                    double periodicY = (worldY - topology.Min.Y) /
                                       (double)topology.Span.Y * repeatY;
                    sample = ValueNoisePeriodic(octaveSeed, periodicX, periodicY,
                        repeatX, repeatY);
                }
                else
                {
                    sample = ValueNoise(octaveSeed, worldX * frequency, worldY * frequency);
                }
                value += sample * amplitude;
                total += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return total <= 0d ? 0d : value / total;
        }

        /// <summary>计算普通二维值噪声，并在四个随机角点之间平滑插值。</summary>
        private static double ValueNoise(ulong seed, double x, double y)
        {
            // 先算采样点四个角的随机值，再在它们之间平滑过渡，避免地形一格一格突然跳变。
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double tx = Smooth(x - x0);
            double ty = Smooth(y - y0);
            double a = Hash01(seed, x0, y0);
            double b = Hash01(seed, x0 + 1, y0);
            double c = Hash01(seed, x0, y0 + 1);
            double d = Hash01(seed, x0 + 1, y0 + 1);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        /// <summary>计算首尾相接的二维值噪声，保证环绕世界边界没有断缝。</summary>
        private static double ValueNoisePeriodic(ulong seed, double x, double y,
            int repeatX, int repeatY)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double tx = Smooth(x - x0);
            double ty = Smooth(y - y0);
            // 超过随机图边界的角会绕回开头，这样首尾两边能自然接上。
            int x1 = PositiveMod(x0 + 1, repeatX);
            int y1 = PositiveMod(y0 + 1, repeatY);
            x0 = PositiveMod(x0, repeatX);
            y0 = PositiveMod(y0, repeatY);
            double a = Hash01(seed, x0, y0);
            double b = Hash01(seed, x1, y0);
            double c = Hash01(seed, x0, y1);
            double d = Hash01(seed, x1, y1);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        /// <summary>组合世界种子、维度、算法版本和用途编号，生成本步骤专用种子。</summary>
        private static ulong CreateSeed(ChunkGenerationRequest request, uint salt)
        {
            // 把世界种子、算法版本、本步骤编号和世界层名字混在一起，得到本步骤专用的随机种子。
            // 这样不同世界、地表和洞穴、温度和高度之间不会误用同一套随机图。
            ulong value = 14695981039346656037UL;
            unchecked
            {
                value = (value ^ (uint)request.WorldSeed) * 1099511628211UL;
                value = (value ^ NoiseLayoutVersion) * 1099511628211UL;
                value = (value ^ salt) * 1099511628211UL;
                for (int i = 0; i < request.Address.DimensionId.Length; i++)
                    value = (value ^ request.Address.DimensionId[i]) * 1099511628211UL;
            }
            return value == 0 ? 0xd1b54a32d192ed03UL : value;
        }

        /// <summary>带用途编号的整数哈希，方便旧式整数种子调用。</summary>
        private static uint Hash(int seed, int x, int y, uint salt) =>
            Hash((ulong)(uint)seed ^ salt, x, y);

        /// <summary>把种子和坐标混合成稳定的伪随机整数。</summary>
        private static uint Hash(ulong seed, int x, int y)
        {
            // 用固定的整数运算把种子和坐标打乱成随机数字；不开游戏自带随机数，所以每次运行都一致。
            unchecked
            {
                ulong value = seed;
                value ^= (ulong)(uint)x * 0x9e3779b185ebca87UL;
                value ^= (ulong)(uint)y * 0xc2b2ae3d27d4eb4fUL;
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                value ^= value >> 31;
                return (uint)(value >> 32);
            }
        }

        /// <summary>返回带用途编号的 0 到 1 伪随机数。</summary>
        private static double Hash01(int seed, int x, int y, uint salt) =>
            Hash(seed, x, y, salt) / (double)uint.MaxValue;
        /// <summary>返回基于长整数种子和坐标的 0 到 1 伪随机数。</summary>
        private static double Hash01(ulong seed, int x, int y) =>
            Hash(seed, x, y) / (double)uint.MaxValue;
        /// <summary>把插值比例平滑处理，让噪声变化更自然。</summary>
        private static double Smooth(double value) => value * value * (3d - 2d * value);
        /// <summary>按比例计算两个数之间的中间值。</summary>
        private static double Lerp(double left, double right, double t) => left + (right - left) * t;
        /// <summary>天然水体只写十个整数等级，正深度至少保留一级。</summary>
        private static int QuantizeGeneratedLiquidLevel(double depth) => LiquidCellValue.FromGeneratedDepth(depth);

        /// <summary>天然浅水底床降低两层，超过半满的深水底床降低三层。</summary>
        private static int ResolveNaturalBedDepth(int liquidLevel) =>
            liquidLevel <= 0 ? 0 : liquidLevel <= LiquidCellValue.MaximumLevel / 2 ? 2 : 3;

        /// <summary>把数值限制在 0 到 1 之间。</summary>
        private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
        /// <summary>执行真正的向下取整除法，确保负坐标也能正确划分区域。</summary>
        private static int FloorDiv(int value, int divisor)
        {
            // C# 对负数除法会朝 0 取整，但地图区域需要永远向下取整，所以这里手动补正。
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0)) ? quotient - 1 : quotient;
        }
        /// <summary>计算始终为非负数的取模结果，用于环绕坐标和方向表索引。</summary>
        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
        #endregion
    }
}
