using System;
using System.Collections.Generic;
using System.Reflection;
using FlatWorld.WorldModel;

namespace UnityEngine
{
    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType value) { } }
}

namespace FlatWorld.WorldModel
{
    public enum EcologyDistributionMode { Uniform, Patch }
    public enum EcologyItemCountDistribution { Fixed, DiscreteTable }
    public sealed class ChunkGenerationRequest { }
    public sealed class ChunkTerrainBuffer { }
    public sealed class ChunkGenerationProfileSnapshot
    {
        public ChunkGenerationProfileSnapshot(string value) { Value = value; }
        public ChunkGenerationProfileSnapshot(string value, int signature, int width, int height) { Value = value; }
        public string Value { get; }
        public int Width => 16;
        public int Height => 16;
        public ChunkGenerationSettingsSnapshot Settings = new();
        public Dictionary<string, double> NumericParameters = new();
        public CavePortalPairingSnapshot Pairing;
        public ChunkGenerationProfileSnapshot WithGenerationConfiguration(Dictionary<string, double> numbers, object text, object minerals) =>
            new(Value) { NumericParameters = numbers, Settings = Settings };
        public ChunkGenerationProfileSnapshot WithCavePortalPairing(CavePortalPairingSnapshot pairing) =>
            new(Value) { NumericParameters = NumericParameters, Settings = Settings, Pairing = pairing };
    }
    public sealed class ChunkGenerationSettingsSnapshot
    {
        public bool CavePortalEnabled = true;
        public double CavePortalChunkChance = .5, CavePortalSafeRadius = 2, WorldSpatialDistanceScale = 1;
        public int CavePortalChunkWidth, CavePortalChunkHeight, CavePortalSeedSalt;
    }
    public readonly struct Int2 : IEquatable<Int2>
    {
        public readonly int X, Y;
        public Int2(int x, int y) { X = x; Y = y; }
        public bool Equals(Int2 other) => X == other.X && Y == other.Y;
    }
    public readonly struct ChunkGenerationTopologySnapshot
    {
        public ChunkGenerationTopologySnapshot(Int2 min, Int2 span) { Min = min; Span = span; IsWrapped = true; }
        public bool IsWrapped { get; }
        public Int2 Min { get; }
        public Int2 Span { get; }
    }
    public sealed class DeterministicChunkGenerator { }
    public sealed class CavePortalPairingSnapshot
    {
        public CavePortalPairingSnapshot(string dimension, int seed, ChunkGenerationProfileSnapshot surface,
            ChunkGenerationTopologySnapshot topology, DeterministicChunkGenerator generator)
        { Dimension = dimension; Seed = seed; Surface = surface; Topology = topology; Generator = generator; }
        public string Dimension; public int Seed; public ChunkGenerationProfileSnapshot Surface;
        public ChunkGenerationTopologySnapshot Topology; public DeterministicChunkGenerator Generator;
    }
}

namespace FlatWorld.Networking { public static class GameNetwork { public static bool HasStateAuthority = true; } }
namespace FlatWorld.Spaceflight
{
    public sealed class SpaceCatalog
    {
        public static SpaceCatalog Current = new();
        public static SpaceCatalog LoadDefault() => Current;
    }
}

public sealed class PlanetData
{
    public string Name = "world", GenerationPresetId = "preset";
    public UnityEngine.Vector2Int ChunkSize = new(16, 16);
    public float SpatialDistanceScale = 1;
    public int TopologySize;
    public Dictionary<string, double> GenerationNumericOverrides = new(StringComparer.Ordinal);
    public Dictionary<string, string> GenerationTextOverrides = new(StringComparer.Ordinal);
    public EcologyWorldSaveData Ecology = new();
}
public sealed class DimensionDefinition
{
    public bool PairPortalsWithTargetWorld;
    public string PortalItemId, PortalTargetDimensionId;
}
public sealed class DimensionManager
{
    public static DimensionManager Instance = new();
    public DimensionDefinition Definition = new();
    public bool TryGetDefinitionForWorldKey(string world, out DimensionDefinition result)
    { result = Definition; return true; }
    public int GetGenerationSeedForDimension(int seed, string dimension) => seed + (dimension?.Length ?? 0);
}
public sealed class GameSaveData
{
    public int Seed = 1;
    public bool UsesFrozenWorldGenerationConfiguration = true;
}
public sealed class SaveDataMgr
{
    public static SaveDataMgr Instance = new();
    public GameSaveData SaveData = new();
    public PlanetData CurrentPlanet;
    public PlanetData GetCurrentPlanetData() => CurrentPlanet;
}
public sealed class GameRes { public static GameRes ExistingInstance = new(); public int ResourceReloadVersion; }
public static class NaturalGenerationRuleCatalogService { public static object Catalog = new(); }
public sealed class WorldGenerationNumericParameterSaveData { public string Id; public double Value; }
public sealed class WorldGenerationTextParameterSaveData { public string Id, Value; }
public sealed class CaveResourceRuleSaveData
{ public string RuleId, ItemId; public double VeinThreshold, VeinScale; public int NoiseOffset; }
public sealed class WorldGenerationProfileSaveData
{
    public int DataVersion = 1; public string ProfileId = "preset"; public ulong GenerationFingerprint = 3;
    public List<WorldGenerationNumericParameterSaveData> NumericParameters = new();
    public List<WorldGenerationTextParameterSaveData> TextParameters = new();
    public List<CaveResourceRuleSaveData> CaveResourceRules = new();
}
public sealed class EcologyWorldSaveData
{
    public const int CurrentDataVersion = 1;
    public int DataVersion = 1; public string ProfileId = "preset"; public ulong ConfigurationFingerprint = 2;
    public double GlobalMultiplier = 1; public List<EcologyRuleSaveData> Rules = new();
    public WorldGenerationProfileSaveData Generation = new();
    public bool HasConfiguration => DataVersion != 0 && Rules != null;
}
public partial class ChunkMgr
{
    private readonly WorldGenerationProfileCache portalSourceGenerationCache = new();
    private ChunkGenerationProfileSnapshot pairedCaveSource, pairedSurfaceSource, pairedGenerationProfile;
    private DeterministicChunkGenerator pairedGenerator, runtimeGenerator = new();
    private ChunkGenerationTopologySnapshot pairedTopology;
    private string pairedDimension;
    private int pairedSeed;
    public PlanetData SourcePlanet;
    public ChunkGenerationProfileSnapshot Pair(ChunkGenerationProfileSnapshot profile, int seed) =>
        AttachCavePortalPairing(profile, seed);
    public void ReplaceGenerator() => runtimeGenerator = new();
    private PlanetData GetPortalSourcePlanetData(DimensionManager manager, string dimension) => SourcePlanet;
    private static ChunkGenerationTopologySnapshot ResolveGenerationTopology(PlanetData planet) =>
        planet.TopologySize > 0 ? new(new Int2(0, 0), new Int2(planet.TopologySize, planet.TopologySize)) : default;
    public static int Builds;
    private static ChunkGenerationProfileSnapshot CreateFrozenWorldGenerationProfile(PlanetData planet,
        out ChunkGenerationProfileSnapshot tiles)
    {
        Builds++;
        var result = new ChunkGenerationProfileSnapshot(planet.GenerationPresetId);
        tiles = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(result);
        if (!planet.Ecology.HasConfiguration && FlatWorld.Networking.GameNetwork.HasStateAuthority)
        { planet.Ecology.DataVersion = 1; planet.Ecology.Rules = new(); }
        return tiles;
    }
}

internal static class Program
{
    private static int assertions;
    private static void Assert(bool value, string label)
    { assertions++; if (!value) throw new Exception(label); }

    public static void Main()
    {
        var planet = new PlanetData();
        planet.GenerationNumericOverrides["height"] = 1;
        planet.GenerationTextOverrides["type"] = "surface";
        planet.Ecology.Rules.Add(new EcologyRuleSaveData { RuleId = "rule", ItemId = "item",
            ProvidedTags = new() { "tag" }, MaxRiverFloodplainStrength = .8, MaximumEnvironmentValue = 3 });
        planet.Ecology.Generation.NumericParameters.Add(new() { Id = "number", Value = 4 });
        planet.Ecology.Generation.TextParameters.Add(new() { Id = "text", Value = "value" });
        planet.Ecology.Generation.CaveResourceRules.Add(new() { RuleId = "ore", ItemId = "iron", VeinScale = 1 });
        var cache = new WorldGenerationProfileCache();
        ChunkGenerationProfileSnapshot Get() => ChunkMgr.GetCachedWorldGenerationProfile(cache, planet, SaveDataMgr.Instance.SaveData.Seed, out _);
        var first = Get();
        Assert(ReferenceEquals(first, Get()) && ChunkMgr.Builds == 1, "identical input must reuse profile");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++) Assert(ReferenceEquals(first, Get()), "hot cache hit");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated == 0, $"hot cache allocated {allocated} bytes");

        void Invalidates(Action change, string label)
        {
            var previous = Get(); int builds = ChunkMgr.Builds;
            change(); var next = Get();
            Assert(!ReferenceEquals(previous, next) && ChunkMgr.Builds == builds + 1, label);
            Assert(ReferenceEquals(next, Get()), label + " must settle");
        }

        Invalidates(() => planet.GenerationNumericOverrides["height"] = 2, "numeric override");
        planet.GenerationNumericOverrides["height"] = 0d; Get();
        Invalidates(() => planet.GenerationNumericOverrides["height"] = BitConverter.Int64BitsToDouble(long.MinValue),
            "signed zero changes generation fingerprint");
        planet.GenerationNumericOverrides = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["HEIGHT"] = 2 }; Get();
        Invalidates(() => { planet.GenerationNumericOverrides.Remove("HEIGHT");
            planet.GenerationNumericOverrides["height"] = 2; }, "case-sensitive output key");
        Invalidates(() => planet.GenerationTextOverrides["type"] = "cave", "text override");
        Invalidates(() => planet.GenerationPresetId = "new-preset", "preset");
        Invalidates(() => planet.ChunkSize.x++, "chunk size");
        Invalidates(() => planet.SpatialDistanceScale = 2, "spatial scale");
        Invalidates(() => SaveDataMgr.Instance.SaveData.Seed++, "seed");
        Invalidates(() => GameRes.ExistingInstance.ResourceReloadVersion++, "resource revision");
        Invalidates(() => GameRes.ExistingInstance = new(), "resource identity");
        Invalidates(() => FlatWorld.Spaceflight.SpaceCatalog.Current = new(), "catalog identity");
        Invalidates(() => NaturalGenerationRuleCatalogService.Catalog = new(), "natural catalog identity");
        Invalidates(() => DimensionManager.Instance.Definition.PortalItemId = "portal", "dimension field");
        Invalidates(() => planet.Ecology.GlobalMultiplier = 2, "frozen multiplier");
        Invalidates(() => planet.Ecology.Generation.NumericParameters[0].Value++, "frozen numeric value");
        Invalidates(() => planet.Ecology.Generation.TextParameters[0].Value = "changed", "frozen text value");
        Invalidates(() => planet.Ecology.Generation.CaveResourceRules[0].NoiseOffset++, "frozen mineral");
        Invalidates(() => planet.Ecology.Rules[0].ProvidedTags[0] = "other", "frozen ecology tags");
        Invalidates(() => SaveDataMgr.Instance.SaveData.UsesFrozenWorldGenerationConfiguration = false, "frozen mode off");
        Invalidates(() => SaveDataMgr.Instance.SaveData.UsesFrozenWorldGenerationConfiguration = true, "frozen mode on");
        Invalidates(() => planet.Ecology = new EcologyWorldSaveData(), "frozen object replaced");
        Invalidates(() => SaveDataMgr.Instance.SaveData = new GameSaveData(), "save replaced");
        Invalidates(() => planet = new PlanetData(), "same-name world replaced");

        planet.Ecology.Rules.Add(new EcologyRuleSaveData());
        Get();
        foreach (FieldInfo field in typeof(EcologyRuleSaveData).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.Name == "ProvidedTags") continue;
            var rule = planet.Ecology.Rules[0]; object original = field.GetValue(rule);
            object changed = field.FieldType == typeof(string) ? "modified" :
                field.FieldType == typeof(bool) ? !(bool)original :
                field.FieldType == typeof(int) ? (int)original + 1 :
                field.FieldType == typeof(double) ? (double)original + .5 :
                field.FieldType == typeof(double?) ? (double?)1.25 :
                field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, 1) : throw new Exception(field.Name);
            var previous = Get(); field.SetValue(rule, changed);
            Assert(!ReferenceEquals(previous, Get()), "frozen field " + field.Name);
            field.SetValue(rule, original); Get();
        }

        bool active = true;
        Func<ChunkGenerationProfileSnapshot, ChunkGenerationProfileSnapshot> hook =
            p => active ? new ChunkGenerationProfileSnapshot("hooked") : p;
        WorldGenerationRuntimeHooks.BeforeWorldModelGeneration += hook;
        try
        {
            int builds = ChunkMgr.Builds;
            Assert(Get().Value == "hooked", "hook applied");
            active = false;
            Assert(Get().Value != "hooked" && ChunkMgr.Builds == builds + 2, "mutable hook must bypass cache");
        }
        finally { WorldGenerationRuntimeHooks.BeforeWorldModelGeneration -= hook; }
        var restored = Get(); Assert(ReferenceEquals(restored, Get()), "cache resumes after hook");

        planet.Ecology.Rules = null;
        int beforeCapture = ChunkMgr.Builds;
        var captured = Get(); var reapplied = Get();
        Assert(!ReferenceEquals(captured, reapplied) && ChunkMgr.Builds == beforeCapture + 2,
            "first capture must be reapplied before caching");
        Assert(ReferenceEquals(reapplied, Get()), "reapplied configuration must be cached");

        var manager = new ChunkMgr();
        var cave = new ChunkGenerationProfileSnapshot("cave");
        SaveDataMgr.Instance.CurrentPlanet = new PlanetData();
        DimensionManager.Instance.Definition.PairPortalsWithTargetWorld = true;
        DimensionManager.Instance.Definition.PortalTargetDimensionId = "surface";
        Assert(ReferenceEquals(cave, manager.Pair(cave, 1)), "missing source must not pair");
        manager.SourcePlanet = new PlanetData { Name = "source" };
        var paired = manager.Pair(cave, 1);
        Assert(paired.Pairing != null && ReferenceEquals(paired, manager.Pair(cave, 1)), "source loaded and paired once");
        manager.SourcePlanet.GenerationNumericOverrides["surface"] = 1;
        var changedPair = manager.Pair(cave, 1);
        Assert(!ReferenceEquals(paired, changedPair), "source override refreshes pairing");
        paired = changedPair; manager.SourcePlanet.TopologySize = 64;
        changedPair = manager.Pair(cave, 1);
        Assert(!ReferenceEquals(paired, changedPair) && changedPair.Pairing.Topology.IsWrapped, "topology refreshes pairing");
        paired = changedPair; manager.SourcePlanet = new PlanetData { Name = "source", TopologySize = 64 };
        changedPair = manager.Pair(cave, 1);
        Assert(!ReferenceEquals(paired, changedPair), "same-name source replacement refreshes pairing");
        paired = changedPair; manager.ReplaceGenerator(); changedPair = manager.Pair(cave, 1);
        Assert(!ReferenceEquals(paired, changedPair), "generator replacement refreshes pairing");
        paired = changedPair; changedPair = manager.Pair(cave, 2);
        Assert(!ReferenceEquals(paired, changedPair) && changedPair.Pairing.Seed == 9, "seed refreshes pairing");

        var probe = typeof(GameplayTestSystemProbe).GetMethod("ServiceGenerationProfileCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        var sample = (Newtonsoft.Json.Linq.JObject)probe.Invoke(null, new object[] { new Newtonsoft.Json.Linq.JObject() });
        foreach (var property in sample.Properties())
            Assert(property.Name == "allocatedBytes" ? (long)property.Value == 0 : (bool)property.Value,
                "production rules probe " + property.Name);
        Console.WriteLine($"PASS: {assertions} assertions; 20,000 cache hits allocated {allocated} bytes");
    }
}
