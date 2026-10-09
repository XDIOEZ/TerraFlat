using System;
using System.Threading;
using FlatWorld.WorldModel;

namespace FlatWorld.Spaceflight
{
    public static class SpaceSurfaceTerrain
    {
        #region 星体纯地形扩展
        // 星体身份只用于配置，后台只消费冻结参数，不访问 Unity 或活动世界。
        public static void Apply(ChunkGenerationRequest request, ChunkTerrainBuffer terrain, CancellationToken cancellation)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            if (profile.Settings.Mode != ChunkGenerationMode.Surface || !profile.TextParameters.ContainsKey("space.bodyId")) return;
            bool uniform = Number(profile, "space.uniformSurface", 0d) > 0d;
            bool climate = Number(profile, "space.climateOverride", 0d) > 0d || uniform;
            bool craters = Number(profile, "space.crater.enabled", 0d) > 0d;
            bool bands = Number(profile, "space.surfaceBands", 0d) > 0d;
            if (!uniform && !climate && !craters && !bands) return;
            double temperature = Number(profile, "space.surfaceTemperatureCelsius", 26d);
            double variation = Number(profile, "space.temperatureVariationCelsius", 0d);
            int uniformTile = (int)Number(profile, "space.uniformTileId", profile.Settings.GroundTileId);
            for (int y = 0; y < terrain.Height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < terrain.Width; x++)
                {
                    int worldX = request.Topology.NormalizeX(request.Address.ChunkOrigin.X + x);
                    int worldY = request.Topology.NormalizeY(request.Address.ChunkOrigin.Y + y);
                    TerrainCell original = terrain.GetCell(x, y);
                    terrain.TryGetEnvironmentValue("height", x, y, out float sampledHeight);
                    double height = sampledHeight;
                    int ground = climate ? profile.Settings.GroundTileId : original.GroundTileId;
                    if (climate && height >= profile.Settings.MountainLevel) ground = profile.Settings.StoneTileId;
                    if (craters)
                    {
                        double crater = CraterOffset(request, worldX, worldY, out bool rim);
                        height = Math.Clamp(height + crater, 0d, 1d);
                        if (rim) ground = (int)Number(profile, "space.crater.rimTileId", ground);
                        terrain.SetEnvironmentValue("space.crater", x, y, (float)crater);
                        terrain.SetEnvironmentValue("height", x, y, (float)height);
                    }
                    if (climate)
                    {
                        double regional = SmoothRegion(request, worldX, worldY, 96d);
                        double celsius = temperature + (regional * 2d - 1d) * variation;
                        terrain.SetEnvironmentValue("temperature.celsius", x, y, (float)celsius);
                        terrain.SetEnvironmentValue("temperature", x, y, (float)Math.Clamp((regional + 0.25d) / 1.5d, 0d, 1d));
                        terrain.SetEnvironmentValue("precipitation", x, y, 0f);
                        terrain.SetEnvironmentValue("basePrecipitation", x, y, 0f);
                        terrain.SetEnvironmentValue("moisture", x, y, 0f);
                        terrain.SetEnvironmentValue(SnowDepthLayer.LayerId, x, y, 0f);
                        terrain.SetGrass(x, y, ChunkTerrainData.GrassEmpty);
                    }
                    double bandY = request.Topology.IsWrapped
                        ? (worldY - request.Topology.Min.Y) * Math.PI * 2d * Math.Max(1d, Math.Round(request.Topology.Span.Y / (Math.PI * 84d))) / request.Topology.Span.Y
                        : worldY / 42d;
                    if (bands && Math.Sin(bandY + SmoothRegion(request, worldX, worldY, 160d) * 3d) > 0.5d)
                        ground = profile.Settings.StoneTileId;
                    if (uniform)
                    {
                        ground = uniformTile;
                        terrain.SetLiquid(x, y, 0, 0f);
                    }
                    if (climate || uniform || craters || bands)
                        terrain.SetCell(x, y, new TerrainCell(ground, uniform ? 0 : original.BackTileId,
                            uniform ? 0 : original.BlockingTileId, (int)SurfaceBiomeKind.Stone,
                            original.NavigationCost == 0 ? (short)1000 : original.NavigationCost,
                            uniform ? TerrainCellFlags.Walkable : original.Flags));
                }
            }
        }
        #endregion

        #region 确定性环形山
        private static double CraterOffset(ChunkGenerationRequest request, int x, int y, out bool rim)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            double spacing = Math.Max(16d, Number(profile, "space.crater.spacing", 120d));
            double minRadius = Math.Max(2d, Number(profile, "space.crater.minimumRadius", 10d));
            double maxRadius = Math.Max(minRadius, Number(profile, "space.crater.maximumRadius", 34d));
            double depth = Math.Max(0d, Number(profile, "space.crater.depth", 0.3d));
            int countX = request.Topology.IsWrapped ? Math.Max(1, (int)Math.Ceiling(request.Topology.Span.X / spacing)) : 0;
            int countY = request.Topology.IsWrapped ? Math.Max(1, (int)Math.Ceiling(request.Topology.Span.Y / spacing)) : 0;
            double spacingX = countX > 0 ? request.Topology.Span.X / (double)countX : spacing;
            double spacingY = countY > 0 ? request.Topology.Span.Y / (double)countY : spacing;
            double minX = request.Topology.IsWrapped ? request.Topology.Min.X : 0d;
            double minY = request.Topology.IsWrapped ? request.Topology.Min.Y : 0d;
            int cellX = (int)Math.Floor((x - minX) / spacingX);
            int cellY = (int)Math.Floor((y - minY) / spacingY);
            double result = 0d;
            rim = false;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int regionX = countX > 0 ? Mod(cellX + dx, countX) : cellX + dx;
                int regionY = countY > 0 ? Mod(cellY + dy, countY) : cellY + dy;
                double centerX = minX + (regionX + 0.15d + Roll(request.WorldSeed, regionX, regionY, 1) * 0.7d) * spacingX;
                double centerY = minY + (regionY + 0.15d + Roll(request.WorldSeed, regionX, regionY, 2) * 0.7d) * spacingY;
                double distanceX = x - centerX, distanceY = y - centerY;
                if (countX > 0) distanceX -= Math.Round(distanceX / request.Topology.Span.X) * request.Topology.Span.X;
                if (countY > 0) distanceY -= Math.Round(distanceY / request.Topology.Span.Y) * request.Topology.Span.Y;
                double sample = Roll(request.WorldSeed, regionX, regionY, 3);
                double radius = minRadius + (maxRadius - minRadius) * (1d - Math.Sqrt(1d - sample));
                radius = Math.Min(radius, Math.Min(spacingX, spacingY) * 0.45d);
                double ratio = Math.Sqrt(distanceX * distanceX + distanceY * distanceY) / radius;
                if (ratio > 1.15d) continue;
                if (ratio < 0.85d) result = Math.Min(result, -depth * (1d - ratio * ratio / 0.7225d));
                else { result = Math.Max(result, depth * 0.35d * (1d - Math.Abs(ratio - 1d) / 0.15d)); rim = true; }
            }
            return result;
        }

        private static double SmoothRegion(ChunkGenerationRequest request, int x, int y, double spacing)
        {
            int countX = request.Topology.IsWrapped ? Math.Max(1, (int)Math.Ceiling(request.Topology.Span.X / spacing)) : 0;
            int countY = request.Topology.IsWrapped ? Math.Max(1, (int)Math.Ceiling(request.Topology.Span.Y / spacing)) : 0;
            double px = countX > 0 ? (x - request.Topology.Min.X) * (double)countX / request.Topology.Span.X : x / spacing;
            double py = countY > 0 ? (y - request.Topology.Min.Y) * (double)countY / request.Topology.Span.Y : y / spacing;
            int ix = (int)Math.Floor(px), iy = (int)Math.Floor(py);
            double fx = px - ix, fy = py - iy;
            fx = fx * fx * (3d - 2d * fx); fy = fy * fy * (3d - 2d * fy);
            int nextX = countX > 0 ? Mod(ix + 1, countX) : ix + 1, nextY = countY > 0 ? Mod(iy + 1, countY) : iy + 1;
            if (countX > 0) ix = Mod(ix, countX);
            if (countY > 0) iy = Mod(iy, countY);
            double top = Roll(request.WorldSeed, ix, iy, 7) * (1d - fx) + Roll(request.WorldSeed, nextX, iy, 7) * fx;
            double bottom = Roll(request.WorldSeed, ix, nextY, 7) * (1d - fx) + Roll(request.WorldSeed, nextX, nextY, 7) * fx;
            return top * (1d - fy) + bottom * fy;
        }
        private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;
        private static double Number(ChunkGenerationProfileSnapshot profile, string key, double fallback) =>
            profile.NumericParameters.TryGetValue(key, out double value) ? value : fallback;
        private static double Roll(int seed, int x, int y, int salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)salt * 2246822519u;
                value = (value ^ value >> 13) * 1274126177u;
                return (value ^ value >> 16) / (double)uint.MaxValue;
            }
        }
        #endregion
    }
}
