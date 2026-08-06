using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public enum ChunkGenerationMode
    {
        Surface,
        Cave
    }

    /// <summary>
    /// Strongly typed, engine-free generation configuration captured on the Unity main thread.
    /// The source dictionaries remain available for mod-defined stages; built-in stages never
    /// need to inspect a ScriptableObject or another Unity object.
    /// </summary>
    public sealed class ChunkGenerationSettingsSnapshot
    {
        internal ChunkGenerationSettingsSnapshot(IReadOnlyDictionary<string, double> numbers,
            IReadOnlyDictionary<string, string> texts)
        {
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

        public ChunkGenerationMode Mode { get; }
        public int GroundTileId { get; }
        public int FreshWaterTileId { get; }
        public int SaltWaterTileId { get; }
        public int SandTileId { get; }
        public int StoneTileId { get; }
        public int SnowTileId { get; }
        public int CaveFloorTileId { get; }
        public int CaveWallTileId { get; }
        public double SeaLevel { get; }
        public double BeachLevel { get; }
        public double SnowTemperature { get; }
        public double TerrainScale { get; }
        public double ClimateScale { get; }
        public int HeightOctaves { get; }
        public int ClimateOctaves { get; }
        public bool RiverEnabled { get; }
        public double RiverScale { get; }
        public double RiverWidth { get; }
        public double RiverThreshold { get; }
        public double GrassDensity { get; }
        public bool StructureEnabled { get; }
        public int StructureRegionSize { get; }
        public double StructureChance { get; }
        public int StructureRadius { get; }
        public int StructureGroundTileId { get; }
        public string ResourceSpawnTypeId { get; }
        public double ResourceDensity { get; }
        public int ResourceMinSpacing { get; }
        public double CaveOpenThreshold { get; }
        public short DefaultNavigationCost { get; }

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
