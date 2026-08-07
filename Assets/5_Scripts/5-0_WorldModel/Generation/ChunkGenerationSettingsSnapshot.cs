using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>要生成普通地表，还是地下洞穴。</summary>
    public enum ChunkGenerationMode
    {
        Surface,
        Cave
    }

    /// <summary>
    /// 把 Unity 面板里的零散设置整理成一份后台生成器能直接使用的配置。
    /// 这里会补上缺省值并限制不合理的数字。整理完以后，后台线程就不用再碰 Unity 对象。
    /// 原始设置字典仍然保留，方便 MOD 读取自己添加的参数。
    /// </summary>
    public sealed class ChunkGenerationSettingsSnapshot
    {
        internal ChunkGenerationSettingsSnapshot(IReadOnlyDictionary<string, double> numbers,
            IReadOnlyDictionary<string, string> texts)
        {
            // 所有默认值和数字范围都在这里一次处理好，后面生成每个格子时就不用反复检查。
            Mode = GetText(texts, "terrain.mode", "surface").Equals("cave",
                StringComparison.OrdinalIgnoreCase) ? ChunkGenerationMode.Cave : ChunkGenerationMode.Surface;
            GroundTileId = GetInt(numbers, "terrain.groundTileId", 1);
            FreshWaterTileId = GetInt(numbers, "terrain.waterTileId", 2);
            SaltWaterTileId = GetInt(numbers, "terrain.saltWaterTileId", FreshWaterTileId);
            SandTileId = GetInt(numbers, "terrain.sandTileId", GroundTileId);
            StoneTileId = GetInt(numbers, "terrain.stoneTileId", GroundTileId);
            SnowTileId = GetInt(numbers, "terrain.snowTileId", GroundTileId);
            CaveFloorTileId = GetInt(numbers, "cave.floorTileId", StoneTileId);
            CaveWallTileId = GetInt(numbers, "cave.wallTileId", StoneTileId);
            SeaLevel = Clamp01(GetDouble(numbers, "terrain.seaLevel", 0.30d));
            BeachLevel = Clamp01(GetDouble(numbers, "terrain.beachLevel", SeaLevel + 0.055d));
            SnowTemperature = Clamp01(GetDouble(numbers, "terrain.snowTemperature", 0.18d));
            TerrainScale = Positive(GetDouble(numbers, "terrain.noiseScale", 0.0085d), 0.0085d);
            ClimateScale = Positive(GetDouble(numbers, "climate.noiseScale", 0.004d), 0.004d);
            HeightOctaves = Clamp(GetInt(numbers, "terrain.octaves", 4), 1, 8);
            ClimateOctaves = Clamp(GetInt(numbers, "climate.octaves", 3), 1, 8);
            RiverEnabled = GetBool(numbers, "river.enabled", true);
            RiverScale = Positive(GetDouble(numbers, "river.noiseScale", 0.0025d), 0.0025d);
            RiverWidth = Clamp01(GetDouble(numbers, "river.width", 0.055d));
            RiverThreshold = Clamp01(GetDouble(numbers, "river.flowThreshold", 0.62d));
            GrassDensity = Clamp01(GetDouble(numbers, "grass.density", 0.24d));
            StructureEnabled = GetBool(numbers, "structure.enabled", true);
            StructureRegionSize = Math.Max(8, GetInt(numbers, "structure.regionSize", 96));
            StructureChance = Clamp01(GetDouble(numbers, "structure.spawnChance", 0.18d));
            StructureRadius = Clamp(GetInt(numbers, "structure.radius", 2), 1, 12);
            StructureGroundTileId = GetInt(numbers, "structure.groundTileId", SandTileId);
            ResourceSpawnTypeId = GetText(texts, "resource.spawnTypeId",
                GetText(texts, "entity.spawnTypeId", string.Empty));
            ResourceDensity = Clamp01(GetDouble(numbers, "resource.density",
                GetDouble(numbers, "entity.spawnCount", 0d) / 256d));
            ResourceMinSpacing = Clamp(GetInt(numbers, "resource.minSpacing", 5), 1, 64);
            CaveOpenThreshold = Clamp01(GetDouble(numbers, "cave.openThreshold", 0.52d));
            DefaultNavigationCost = (short)Clamp(GetInt(numbers,
                "navigation.defaultCost", 1), 1, short.MaxValue);
        }

        /// <summary>生成地表还是洞穴。</summary>
        public ChunkGenerationMode Mode { get; }
        // 下面这些都是地块的数字编号，不直接保存 Unity 里的 Tile 图片资源。
        public int GroundTileId { get; }
        public int FreshWaterTileId { get; }
        public int SaltWaterTileId { get; }
        public int SandTileId { get; }
        public int StoneTileId { get; }
        public int SnowTileId { get; }
        public int CaveFloorTileId { get; }
        public int CaveWallTileId { get; }
        /// <summary>高度低于这个数时生成海洋。</summary>
        public double SeaLevel { get; }
        /// <summary>高于海面但低于这个数时生成沙滩。</summary>
        public double BeachLevel { get; }
        /// <summary>温度低于这个数时可以生成雪地。</summary>
        public double SnowTemperature { get; }
        /// <summary>控制山地变化有多快；数值越小，大片地形通常越平缓。</summary>
        public double TerrainScale { get; }
        /// <summary>控制温度和降水区域变化有多快。</summary>
        public double ClimateScale { get; }
        /// <summary>高度随机图叠加多少层细节；越多越细，但计算也更多。</summary>
        public int HeightOctaves { get; }
        /// <summary>气候随机图叠加多少层细节。</summary>
        public int ClimateOctaves { get; }
        /// <summary>地表要不要生成河流。</summary>
        public bool RiverEnabled { get; }
        public double RiverScale { get; }
        public double RiverWidth { get; }
        public double RiverThreshold { get; }
        /// <summary>合适的地面上长出草的基本概率。</summary>
        public double GrassDensity { get; }
        /// <summary>是否生成遗迹等结构；同样的种子会得到同样的位置。</summary>
        public bool StructureEnabled { get; }
        public int StructureRegionSize { get; }
        public double StructureChance { get; }
        public int StructureRadius { get; }
        public int StructureGroundTileId { get; }
        /// <summary>要生成哪种资源；空字符串表示不生成。</summary>
        public string ResourceSpawnTypeId { get; }
        public double ResourceDensity { get; }
        public int ResourceMinSpacing { get; }
        /// <summary>洞穴随机值高于这个数时挖成空地，否则保留岩壁。</summary>
        public double CaveOpenThreshold { get; }
        /// <summary>普通地面默认有多难走；数字越大，寻路越不喜欢走。</summary>
        public short DefaultNavigationCost { get; }

        // 这些小方法只从当前这份设置里取值，不会偷偷读取全局设置。
        private static int GetInt(IReadOnlyDictionary<string, double> values, string key, int fallback) =>
            values.TryGetValue(key, out double value) ? (int)value : fallback;

        private static double GetDouble(IReadOnlyDictionary<string, double> values, string key,
            double fallback) => values.TryGetValue(key, out double value) ? value : fallback;

        private static bool GetBool(IReadOnlyDictionary<string, double> values, string key,
            bool fallback) => values.TryGetValue(key, out double value) ? value > 0.5d : fallback;

        private static string GetText(IReadOnlyDictionary<string, string> values, string key,
            string fallback) => values.TryGetValue(key, out string value) &&
                               !string.IsNullOrWhiteSpace(value) ? value : fallback;

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;

        private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
        private static double Positive(double value, double fallback) => value > 0d ? value : fallback;
    }
}
