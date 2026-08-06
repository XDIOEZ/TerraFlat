using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// Engine-free generation pipeline. Every sample is derived from absolute world coordinates,
    /// so chunk/task completion order cannot affect terrain, climate, structures or entity ids.
    /// </summary>
    public sealed class DeterministicChunkGenerator : IChunkPureGenerator
    {
        private const byte GrassEmpty = 1;
        private const byte GrassPresent = 2;

        public ChunkGenerationResult Generate(ChunkGenerationRequest request,
            CancellationToken cancellationToken)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            ChunkGenerationSettingsSnapshot settings = profile.Settings;
            var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height);
            try
            {
                bool cave = settings.Mode == ChunkGenerationMode.Cave ||
                            request.Address.DimensionId.IndexOf("cave",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                for (int y = 0; y < profile.Height; y++)
                {
                    for (int x = 0; x < profile.Width; x++)
                    {
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
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            ulong seed = CreateSeed(request, 0x9e3779b9u);
            double height = Fractal(seed, worldX, worldY, settings.TerrainScale,
                settings.HeightOctaves, 2.03d, 0.51d, request.Topology);
            double temperatureNoise = Fractal(CreateSeed(request, 0x85ebca6bu), worldX, worldY,
                settings.ClimateScale, settings.ClimateOctaves, 2.07d, 0.5d, request.Topology);
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
            bool river = settings.RiverEnabled && !ocean && height > settings.BeachLevel &&
                         precipitation >= settings.RiverThreshold &&
                         riverDistance <= settings.RiverWidth;
            bool beach = !ocean && !river && height < settings.BeachLevel;
            bool snow = !ocean && !river && temperature <= settings.SnowTemperature;

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

            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0, 0, biomeId,
                navigationCost, flags));
            terrain.SetEnvironmentValue("height", x, y, (float)height);
            terrain.SetEnvironmentValue("temperature", x, y, (float)temperature);
            terrain.SetEnvironmentValue("temperature.celsius", x, y,
                (float)(-20d + temperature * 65d));
            terrain.SetEnvironmentValue("precipitation", x, y, (float)precipitation);
            terrain.SetEnvironmentValue("moisture", x, y, (float)moisture);
            terrain.SetEnvironmentValue("riverDepth", x, y,
                river ? (float)(1d - riverDistance / Math.Max(settings.RiverWidth, 0.0001d)) : 0f);

            bool grass = (flags & TerrainCellFlags.Walkable) != 0 &&
                         groundTileId == settings.GroundTileId &&
                         Hash01(request.WorldSeed, worldX, worldY, 0x165667b1u) <
                         settings.GrassDensity * (0.55d + moisture * 0.75d);
            terrain.SetGrass(x, y, grass ? GrassPresent : GrassEmpty);
            terrain.SetEnvironmentValue("grass", x, y, grass ? 1f : 0f);
        }

        private static void GenerateCaveCell(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            int x, int y, int worldX, int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
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
                    if (Hash01(request.WorldSeed, regionX, regionY, 0x94d049bbu) >=
                        settings.StructureChance)
                        continue;
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
            ulong value = 14695981039346656037UL;
            unchecked
            {
                value = (value ^ (uint)request.WorldSeed) * 1099511628211UL;
                value = (value ^ (uint)request.Profile.Signature) * 1099511628211UL;
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
