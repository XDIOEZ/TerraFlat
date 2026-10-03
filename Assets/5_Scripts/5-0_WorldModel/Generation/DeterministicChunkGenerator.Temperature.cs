namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 群系温度过渡
        private const int BiomeTemperatureBlendRadius = 8;

        private static double ResolveBiomeTemperature(SurfaceClimateSample sample) => sample.BaseBiome switch
        {
            SurfaceBiomeKind.Snow => -10d,
            SurfaceBiomeKind.Stone => 10d,
            _ => sample.TemperatureCelsius
        };

        // 单格出生查询与完整区块使用同一个世界坐标窗口，环绕边界由气候采样归一化。
        private static double SampleBlendedBiomeTemperature(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            double total = 0d;
            for (int y = -BiomeTemperatureBlendRadius; y <= BiomeTemperatureBlendRadius; y++)
            for (int x = -BiomeTemperatureBlendRadius; x <= BiomeTemperatureBlendRadius; x++)
            {
                var sample = SampleSurfaceClimate(request, settings, worldX + x, worldY + y);
                sample.BaseBiome = SampleBaseSurfaceBiome(request, settings, worldX + x, worldY + y,
                    out _, out _);
                total += ResolveBiomeTemperature(sample);
            }
            int diameter = BiomeTemperatureBlendRadius * 2 + 1;
            return total / (diameter * diameter);
        }
        #endregion
    }
}
