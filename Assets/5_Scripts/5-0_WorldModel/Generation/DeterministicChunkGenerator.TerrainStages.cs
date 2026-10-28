using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 洞穴地表与岩壁

        // 地形阶段只接收冻结请求和当前缓冲，不读取世界或场景状态。
        private static void GenerateCaveTerrain(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            CancellationToken cancellationToken)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
                    for (int y = 0; y < profile.Height; y++)
                    for (int x = 0; x < profile.Width; x++)
                    {
                        if (((y * profile.Width + x) & 63) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        int worldX = request.Address.ChunkOrigin.X + x;
                        int worldY = request.Address.ChunkOrigin.Y + y;
                        GenerateCaveCell(request, settings, terrain, x, y, worldX, worldY);
                    }
        }


        /// <summary>根据群系交界主通道、稀疏支路和入口安全网络生成洞穴地面或岩壁格。</summary>
        private static void GenerateCaveCell(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            int x, int y, int worldX, int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            CaveSurfaceInfluenceSample surfaceInfluence =
                CaveLayoutKernel.SampleSurfaceInfluence(request, worldX, worldY);
            // 洞穴岩壁下保留石地；开放洞室可按世界区域形成跨 Chunk 连续的地下河与地下湖。
            bool open = CaveLayoutKernel.IsOpenAtWorld(
                request, settings, worldX, worldY, surfaceInfluence);
            CaveRiverSample caveRiver = open
                ? CaveLayoutKernel.SampleRiver(request, settings, worldX, worldY)
                : default;
            double groundliquidDepth = open
                ? CaveLayoutKernel.SampleGroundliquidDepth(
                    request, settings, worldX, worldY, surfaceInfluence)
                : 0d;
            bool groundwater = groundliquidDepth > 0d;
            bool river = caveRiver.IsRiver;
            int liquidLevel = QuantizeGeneratedLiquidLevel(Math.Max(groundliquidDepth, caveRiver.Depth));
            bool water = liquidLevel > 0;
            bool dirtGround = open && !water && CaveLayoutKernel.ShouldPlaceDirtGround(
                request, settings, worldX, worldY);
            TerrainCellFlags flags = !open
                ? TerrainCellFlags.Blocking
                : TerrainCellFlags.Walkable;
            // 河岸泥土只替换底材，开放通道继续使用正常导航与空阻挡层。
            int groundTileId = dirtGround ? settings.CaveDirtGroundTileId : settings.CaveFloorTileId;
            int blockingTileId = !open ? settings.CaveWallTileId : 0;
            short navigationCost = !open
                ? short.MaxValue
                : settings.DefaultNavigationCost;
            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0,
                blockingTileId, 100,
                navigationCost, flags));
            terrain.SetLiquid(x, y, water ? terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId) : 0,
                liquidLevel);
            terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId, x, y,
                ResolveNaturalBedDepth(liquidLevel));
            terrain.SetEnvironmentValue("temperature", x, y, 0.38f);
            terrain.SetEnvironmentValue("temperature.celsius", x, y, 8f);
            terrain.SetEnvironmentValue("precipitation", x, y, 0f);
            terrain.SetEnvironmentValue("moisture", x, y, water ? 1f : dirtGround ? 0.72f : 0.3f);
            terrain.SetEnvironmentValue("riverFlow", x, y, river ? (float)caveRiver.Flow : 0f);
            terrain.SetEnvironmentValue("riverFlowX", x, y, river ? (float)caveRiver.FlowX : 0f);
            terrain.SetEnvironmentValue("riverFlowY", x, y, river ? (float)caveRiver.FlowY : 0f);
            terrain.SetEnvironmentValue("riverKind", x, y, river ? 1f : groundwater ? 2f : 0f);
            terrain.SetEnvironmentValue("groundwater", x, y, groundwater ? 1f : 0f);
            terrain.SetEnvironmentValue("caveDirtGround", x, y, dirtGround ? 1f : 0f);
            terrain.SetEnvironmentValue("grass", x, y, 0f);
            terrain.SetGrass(x, y, GrassEmpty);
        }

        #endregion

        #region 地表结构覆盖

        /// <summary>在地表生成完成后，按种子在区块中放置确定性的简化结构区域。</summary>
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
                            if (terrain.GetLiquidDepth(x, y) > 0f)
                                continue;
                            terrain.SetCell(x, y, new TerrainCell(settings.StructureGroundTileId,
                                current.BackTileId, current.BlockingTileId, current.BiomeId,
                                current.NavigationCost, current.Flags));
                            terrain.SetGrass(x, y, GrassEmpty);
                            terrain.SetEnvironmentValue("grass", x, y, 0f);
                            terrain.SetEnvironmentValue("structure", x, y, 1f);
                        }
                    }

                }
            }
        }

        #endregion
    }
}
