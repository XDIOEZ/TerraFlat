using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 不使用 Unity、可以放到后台运行的区块生成器。
    /// 所有“随机”结果都来自世界种子和坐标，所以输入相同时，无论先生成哪个区块，结果都一样。
    /// </summary>
    public sealed class DeterministicChunkGenerator : IChunkPureGenerator
    {
        // 草地状态用 1 和 2；数字 0 专门表示“这个格子还没处理过”，方便排查问题。
        private const byte GrassEmpty = 1;
        private const byte GrassPresent = 2;
        // 这个数字表示“随机地形算法的版本”。只有确实要让旧世界换一种地形排列时才增加它。
        // 普通设置变化不应该改它，否则同一个旧世界的山川和气候会被整个重新随机。
        private const uint NoiseLayoutVersion = 5u;

        /// <summary>
        /// 从头生成一个完整区块。通常由后台工作人员调用。
        /// 如果中途取消或出错，会把临时内存清理掉，不留下垃圾数据。
        /// </summary>
        public ChunkGenerationResult Generate(ChunkGenerationRequest request,
            CancellationToken cancellationToken)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            ChunkGenerationSettingsSnapshot settings = profile.Settings;
            var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height);
            try
            {
                // 设置说是洞穴就生成洞穴；旧配置没有这个设置时，名字里有 cave 也当作洞穴。
                bool cave = settings.Mode == ChunkGenerationMode.Cave ||
                            request.Address.DimensionId.IndexOf("cave",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                for (int y = 0; y < profile.Height; y++)
                {
                    for (int x = 0; x < profile.Width; x++)
                    {
                        // 每处理 64 个格子看一次“是否取消”，既能及时停下，也不会每格都检查拖慢速度。
                        if (((y * profile.Width + x) & 63) == 0)
                            cancellationToken.ThrowIfCancellationRequested();

                        int worldX = request.Address.ChunkOrigin.X + x;
                        int worldY = request.Address.ChunkOrigin.Y + y;
                        if (cave)
                            GenerateCaveCell(request, settings, terrain, x, y, worldX, worldY);
                        else
                            GenerateSurfaceCell(request, settings, terrain, x, y, worldX, worldY);
                    }
                }

                // 地表全部铺好后再放遗迹等结构；洞穴不走这一步。
                if (!cave)
                {
                    ApplyStructures(request, settings, terrain, cancellationToken);
                }
                return new ChunkGenerationResult(request, terrain);
            }
            catch
            {
                terrain.Dispose();
                throw;
            }
        }

        private static void GenerateSurfaceCell(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            int x, int y, int worldX, int worldY)
        {
            // 如果世界会绕回另一边，先把越界坐标换回世界内，保证两侧地形能严丝合缝。
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            ulong seed = CreateSeed(request, 0x9e3779b9u);
            double height = Fractal(seed, worldX, worldY, settings.TerrainScale,
                settings.HeightOctaves, 2.03d, 0.51d, request.Topology);
            double temperatureNoise = Fractal(CreateSeed(request, 0x85ebca6bu), worldX, worldY,
                settings.ClimateScale, settings.ClimateOctaves, 2.07d, 0.5d, request.Topology);
            // 越靠近寒冷纬度、海拔越高，温度越低；最后把结果限制在 0 到 1 之间。
            double latitudeCooling = Math.Min(0.34d, Math.Abs(worldY) * 0.000025d);
            double temperature = Clamp01(temperatureNoise - latitudeCooling -
                                         Math.Max(0d, height - 0.68d) * 0.55d);
            double precipitation = Fractal(CreateSeed(request, 0xc2b2ae35u), worldX, worldY,
                settings.ClimateScale * 0.83d, settings.ClimateOctaves, 2.11d, 0.53d,
                request.Topology);
            double moisture = Clamp01(precipitation * 0.78d + (1d - height) * 0.22d);

            bool ocean = height < settings.SeaLevel;
            double riverField = Fractal(CreateSeed(request, 0x27d4eb2fu), worldX, worldY,
                settings.RiverScale, 3, 2.17d, 0.55d, request.Topology);
            double riverDistance = Math.Abs(riverField - 0.5d) * 2d;
            if (request.Topology.IsWrapped)
            {
                // 小型环绕世界有可能刚好没随机到河流，所以额外放一条会弯曲的主河道。
                // 这条河在世界边界能接回自己，保证地图上至少有一条连续淡水河。
                riverDistance = Math.Min(
                    riverDistance,
                    WrappedRiverBandDistance(request, settings, worldX, worldY));
            }
            // 河流会经过局部少雨地区，不能因为一个格子少雨就突然断掉。
            // 所以降水量只影响河有多宽、多深，不直接决定这里有没有河。
            double rainfallSupport = settings.RiverThreshold <= 0d
                ? 1d
                : Clamp01(precipitation / settings.RiverThreshold);
            double effectiveRiverWidth = settings.RiverWidth *
                                         (0.65d + rainfallSupport * 0.35d);
            bool river = settings.RiverEnabled && !ocean && height > settings.BeachLevel &&
                         riverDistance <= effectiveRiverWidth;
            bool beach = !ocean && !river && height < settings.BeachLevel;
            bool snow = !ocean && !river && temperature <= settings.SnowTemperature;

            // 一个格子可能同时符合几个条件，所以按顺序决定：先海洋、再河流，然后才是沙滩和气候地区。
            int biomeId;
            int groundTileId;
            TerrainCellFlags flags;
            short navigationCost = settings.DefaultNavigationCost;
            if (ocean)
            {
                biomeId = 0;
                groundTileId = settings.SaltWaterTileId;
                flags = TerrainCellFlags.Water;
                navigationCost = short.MaxValue;
            }
            else if (river)
            {
                biomeId = 1;
                groundTileId = settings.FreshWaterTileId;
                flags = TerrainCellFlags.Water;
                navigationCost = short.MaxValue;
            }
            else if (beach)
            {
                biomeId = 2;
                groundTileId = settings.SandTileId;
                flags = TerrainCellFlags.Walkable;
            }
            else if (snow)
            {
                biomeId = 6;
                groundTileId = settings.SnowTileId;
                flags = TerrainCellFlags.Walkable;
                navigationCost = (short)Math.Min(short.MaxValue, navigationCost + 1);
            }
            else
            {
                bool arid = precipitation < 0.28d;
                bool forest = moisture > 0.62d;
                biomeId = arid ? 3 : forest ? 5 : 4;
                groundTileId = arid ? settings.SandTileId : settings.GroundTileId;
                flags = TerrainCellFlags.Walkable;
            }

            // 核心格子保存游戏马上要用的结果；高度、温度等详细数值另外保存，供画面和其他系统读取。
            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0, 0, biomeId,
                navigationCost, flags));
            terrain.SetEnvironmentValue("height", x, y, (float)height);
            terrain.SetEnvironmentValue("temperature", x, y, (float)temperature);
            terrain.SetEnvironmentValue("temperature.celsius", x, y,
                (float)(-20d + temperature * 65d));
            terrain.SetEnvironmentValue("precipitation", x, y, (float)precipitation);
            terrain.SetEnvironmentValue("moisture", x, y, (float)moisture);
            terrain.SetEnvironmentValue("riverDepth", x, y,
                river
                    ? (float)((1d - riverDistance / Math.Max(effectiveRiverWidth, 0.0001d)) *
                              (0.7d + rainfallSupport * 0.3d))
                    : 0f);

            // 草长不长只看世界种子、坐标和湿度，不用会变化的全局随机数，所以每次结果相同。
            bool grass = (flags & TerrainCellFlags.Walkable) != 0 &&
                         groundTileId == settings.GroundTileId &&
                         Hash01(request.WorldSeed, worldX, worldY, 0x165667b1u) <
                         settings.GrassDensity * (0.55d + moisture * 0.75d);
            terrain.SetGrass(x, y, grass ? GrassPresent : GrassEmpty);
            terrain.SetEnvironmentValue("grass", x, y, grass ? 1f : 0f);
        }

        private static double WrappedRiverBandDistance(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            int worldX,
            int worldY)
        {
            // 把世界看成首尾相接的纸面，在上面画几条周期弯曲的河，并计算当前格离最近河心多远。
            ChunkGenerationTopologySnapshot topology = request.Topology;
            double localX = (worldX - topology.Min.X) / (double)topology.Span.X;
            double localY = (worldY - topology.Min.Y) / (double)topology.Span.Y;
            int bandCount = Math.Max(2, (int)Math.Round(
                topology.Span.Y * settings.RiverScale * 2d,
                MidpointRounding.AwayFromZero));
            int meanderCycles = Math.Max(1, (int)Math.Round(
                topology.Span.X * settings.RiverScale,
                MidpointRounding.AwayFromZero));
            double phase = Hash01(CreateSeed(request, 0x6a09e667u), 0, 0);
            double meander = Math.Sin((localX * meanderCycles + phase) * Math.PI * 2d) * 0.18d;
            double bandCoordinate = localY * bandCount - meander;
            double fractional = bandCoordinate - Math.Floor(bandCoordinate);
            return Math.Abs(fractional - 0.5d) * 2d;
        }

        private static void GenerateCaveCell(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            int x, int y, int worldX, int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            // 洞穴格子只有两种：能走的空地，或不能穿过的岩壁。岩壁下面仍保留一层洞穴地面。
            double cavern = Fractal(CreateSeed(request, 0x7f4a7c15u), worldX, worldY,
                settings.TerrainScale * 1.7d, 4, 2.08d, 0.52d, request.Topology);
            bool open = cavern >= settings.CaveOpenThreshold;
            TerrainCellFlags flags = open ? TerrainCellFlags.Walkable : TerrainCellFlags.Blocking;
            terrain.SetCell(x, y, new TerrainCell(settings.CaveFloorTileId, 0,
                open ? 0 : settings.CaveWallTileId, 100, open ? settings.DefaultNavigationCost :
                short.MaxValue, flags));
            terrain.SetEnvironmentValue("height", x, y, (float)cavern);
            terrain.SetEnvironmentValue("temperature", x, y, 0.38f);
            terrain.SetEnvironmentValue("temperature.celsius", x, y, 8f);
            terrain.SetEnvironmentValue("precipitation", x, y, 0f);
            terrain.SetEnvironmentValue("moisture", x, y, 0.3f);
            terrain.SetEnvironmentValue("grass", x, y, 0f);
            terrain.SetGrass(x, y, GrassEmpty);
        }

        private static void ApplyStructures(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            CancellationToken cancellationToken)
        {
            if (!settings.StructureEnabled || settings.StructureChance <= 0d)
                return;

            // 找出所有可能延伸进当前区块的遗迹区域，包括中心点落在区块外面的遗迹。
            int minWorldX = request.Address.ChunkOrigin.X;
            int minWorldY = request.Address.ChunkOrigin.Y;
            int maxWorldX = minWorldX + request.Profile.Width - 1;
            int maxWorldY = minWorldY + request.Profile.Height - 1;
            int region = settings.StructureRegionSize;
            int radius = settings.StructureRadius;
            int minRegionX = FloorDiv(minWorldX - radius, region);
            int maxRegionX = FloorDiv(maxWorldX + radius, region);
            int minRegionY = FloorDiv(minWorldY - radius, region);
            int maxRegionY = FloorDiv(maxWorldY + radius, region);
            for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
            {
                for (int regionY = minRegionY; regionY <= maxRegionY; regionY++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 每片大区域自己决定是否生成遗迹；世界种子相同，决定就永远相同。
                    if (Hash01(request.WorldSeed, regionX, regionY, 0x94d049bbu) >=
                        settings.StructureChance)
                        continue;
                    // 遗迹中心离区域边缘留出足够空间，避免主体跑到自己负责的区域外。
                    int span = Math.Max(1, region - radius * 2);
                    int anchorX = regionX * region + radius +
                                  (int)(Hash(request.WorldSeed, regionX, regionY, 0x369dea0fu) % (uint)span);
                    int anchorY = regionY * region + radius +
                                  (int)(Hash(request.WorldSeed, regionX, regionY, 0xdb4f0b91u) % (uint)span);
                    for (int worldY = anchorY - radius; worldY <= anchorY + radius; worldY++)
                    {
                        for (int worldX = anchorX - radius; worldX <= anchorX + radius; worldX++)
                        {
                            if (worldX < minWorldX || worldX > maxWorldX ||
                                worldY < minWorldY || worldY > maxWorldY)
                                continue;
                            int x = worldX - minWorldX;
                            int y = worldY - minWorldY;
                            TerrainCell current = terrain.GetCell(x, y);
                            // 这个简化版遗迹只更换陆地表面，不填河海，也不改变原来的障碍和走路规则。
                            if ((current.Flags & TerrainCellFlags.Water) != 0)
                                continue;
                            terrain.SetCell(x, y, new TerrainCell(settings.StructureGroundTileId,
                                current.BackTileId, current.BlockingTileId, current.BiomeId,
                                current.NavigationCost, current.Flags));
                            terrain.SetGrass(x, y, GrassEmpty);
                            terrain.SetEnvironmentValue("grass", x, y, 0f);
                        }
                    }

                }
            }
        }

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

        private static uint Hash(int seed, int x, int y, uint salt) =>
            Hash((ulong)(uint)seed ^ salt, x, y);

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

        private static double Hash01(int seed, int x, int y, uint salt) =>
            Hash(seed, x, y, salt) / (double)uint.MaxValue;
        private static double Hash01(ulong seed, int x, int y) =>
            Hash(seed, x, y) / (double)uint.MaxValue;
        private static double Smooth(double value) => value * value * (3d - 2d * value);
        private static double Lerp(double left, double right, double t) => left + (right - left) * t;
        private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
        private static int FloorDiv(int value, int divisor)
        {
            // C# 对负数除法会朝 0 取整，但地图区域需要永远向下取整，所以这里手动补正。
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0)) ? quotient - 1 : quotient;
        }
        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
