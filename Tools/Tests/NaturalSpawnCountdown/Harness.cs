using MemoryPack;
using UnityEngine;
using FlatWorld.WorldModel;

// 世界、加载窗口与随机输入由替身提供；倒计时、候选收集、调度、重试和存档均编译生产源码。
public partial class MonsterSpawnerManager
{
    private readonly List<SpawnerConfig> _spawnerConfigs = new();
    private readonly Dictionary<string, SpawnerProgressSaveData> _runtimeStates = new();
    private readonly Dictionary<string, float> _nextSpawnRetryTime = new();
    private readonly Dictionary<SpawnerConfig, int> _remainingSearchAttempts = new();
    private readonly List<SpawnerConfig.SpawnEntry> _availableEntries = new();
    private readonly List<Vector3> _playerPositions = new() { Vector3.zero };
    private readonly Dictionary<string, Vector3> habitats = new();
    private readonly HashSet<string> blockedEntries = new();
    private readonly SpawnerRuntimeSettings _settings = new() { NaturalSpawnIntervalGameSeconds = 100f, GlobalAliveLimit = 40, SpawnRetryInterval = .5f };
    private readonly ChunkMgr _chunkManager = new();
    private float _nextCreatureBirthTime;
    private object _naturalCreatureBirth;
    private bool dark;
    private int population;
    private int requests;
    private string requestedSpecies;
    private Vector3 requestedPosition;
    private static int checks;

    private bool IsGlobalDark(string _) => dark;
    private int CountPopulationLimitedAlive() => population;
    private int CountGroupAlive(SpawnerConfig _) => 0;
    private int GetEffectiveGroupLimit(SpawnerConfig config) => config.GroupAliveLimit;
    private bool CanSpawnEntry(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, SpawnerProgressSaveData state) => !blockedEntries.Contains(entry.PrefabName);
    private bool IsWithinLocalSpeciesLimit(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 position) => true;
    private bool TryGetValidSpawnPosition(SpawnerConfig config, out Vector3 position) => habitats.TryGetValue(config.PersistentId, out position);
    private SpawnerProgressSaveData GetOrCreateState(SpawnerConfig config)
    {
        if (!_runtimeStates.TryGetValue(config.PersistentId, out var state))
            _runtimeStates[config.PersistentId] = state = new SpawnerProgressSaveData();
        return state;
    }
    private string GetConfigKey(SpawnerConfig config) => config.PersistentId;
    private bool ObserveSpawn(SpawnerConfig config, string message) => false;
    private bool TryGetCurrentTimeData(out string scene, out TimeData time)
    {
        scene = "surface"; time = new TimeData(); return true;
    }
    private bool IsNearAnyPlayer(Vector3 p, float radius) => _playerPositions.Any(player =>
        (p.x - player.x) * (p.x - player.x) + (p.y - player.y) * (p.y - player.y) < radius * radius);
    private bool ProcessPendingSpawns(SpawnerConfig config, SpawnerProgressSaveData state, string scene, int day,
        SpawnerConfig.SpawnEntry entry, Vector3? selectedPosition = null)
    {
        requests++; requestedSpecies = entry.PrefabName; requestedPosition = selectedPosition ?? default; return true;
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    public static void RunTests()
    {
        var settings = new SpawnerRuntimeSettings { NaturalSpawnIntervalGameSeconds = 100f };
        var clock = new SpawnerCountdownProgress();
        Check(!AdvanceCountdownSchedule(clock, 0d, settings, 1f) && clock.RemainingGameSeconds == 100d, "full initial interval");
        Check(!AdvanceCountdownSchedule(clock, 99.75d, settings, 1f) && clock.RemainingGameSeconds == .25d, "not early");
        var worldSave = new MonsterSpawnerSaveData { NaturalCountdown = clock };
        var restored = MemoryPackSerializer.Deserialize<MonsterSpawnerSaveData>(MemoryPackSerializer.Serialize(worldSave));
        Check(restored.NaturalCountdown.RemainingGameSeconds == .25d, "real MemoryPack remaining time");
        restored.NaturalCountdown.LastObservedGameSeconds = -1d;
        AdvanceCountdownSchedule(restored.NaturalCountdown, 999999d, settings, 1f);
        Check(restored.NaturalCountdown.RemainingGameSeconds == .25d, "no offline catchup");
        Check(AdvanceCountdownSchedule(clock, 100d, settings, 1f) && clock.PendingCount == 0 && clock.SelectedSpeciesId == null, "expiry does not preselect a species");

        var config = new SpawnerConfig { PersistentId = "animals", SpawnChance = 1f, SpawnCount = 3 };
        var entry = new SpawnerConfig.SpawnEntry { PrefabName = "Bird", Probability = 1f };
        var state = new SpawnerProgressSaveData();
        int rolls = 0;
        Check(TryBeginCountdownBatch(clock, config, entry, state, 1f, () => { rolls++; return 1f; }), "begin one batch");
        Check(rolls == 0 && clock.PendingCount == 3 && state.PendingSpawnCount == 3, "certain chance and fixed size require no random calls");
        Check(!TryBeginCountdownBatch(clock, config, entry, state, 1f, () => 0f), "no second batch while pending");
        Check(IsCountdownBatchFor(clock, "animals", "Bird") && !IsCountdownBatchFor(clock, "other", "Bird"), "batch owner identity");
        clock.RecordBatchBirth(10.5f, 20.5f, "surface");
        AdvanceCountdownSchedule(clock, 10000d, settings, 1f);
        Check(clock.PendingCount == 3 && clock.RemainingGameSeconds == 0d, "blocked batch cannot accumulate rounds");
        worldSave.ConfigStates[config.PersistentId] = state;
        restored = MemoryPackSerializer.Deserialize<MonsterSpawnerSaveData>(MemoryPackSerializer.Serialize(worldSave));
        Check(restored.NaturalCountdown.SelectedSpeciesId == "Bird" && restored.NaturalCountdown.SelectedConfigId == "animals", "serialized selection");
        Check(restored.NaturalCountdown.HasBatchAnchor && restored.NaturalCountdown.BatchSpawnCells.Count == 1 &&
            restored.ConfigStates["animals"].PendingSpawnCount == 3, "serialized batch and rule ledger");
        CompleteCountdownSpawnAttempt(clock, state, false, 3, 10000d);
        Check(state.LifetimeSpawnCount == 0 && state.AvailableBudget == -1 && clock.PendingCount == 2, "chance miss is not a birth or budget charge");
        CompleteCountdownSpawnAttempt(clock, state, true, 3, 10000d);
        CompleteCountdownSpawnAttempt(clock, state, true, 3, 10001d);
        Check(state.LifetimeSpawnCount == 2 && clock.RemainingGameSeconds == 100d && clock.PendingCount == 0, "one restart after the full batch");
        Check(clock.SelectedSpeciesId == null && !clock.HasBatchAnchor && clock.BatchSpawnCells.Count == 0, "all selected-batch state cleared");
        Check(!AdvanceCountdownSchedule(clock, 10100.75d, settings, 1f) && AdvanceCountdownSchedule(clock, 10101d, settings, 1f), "restart anchored to actual completion time");
        config.SpawnChance = .5f;
        Check(!TryBeginCountdownBatch(clock, config, entry, state, 1f, () => 1f) && clock.RemainingGameSeconds == 100d, "round chance miss restarts the global clock");
        Check(!TryBeginCountdownBatch(clock, config, entry, state, 1f, () => 0f), "cannot reroll a failed round immediately");
        clock.Advance(10201d, 100d); config.SpawnChance = 0f;
        Check(!TryBeginCountdownBatch(clock, config, entry, state, 1f, () => { rolls++; return 0f; }) && rolls == 0, "disabled rule never rolls");

        clock = new SpawnerCountdownProgress();
        AdvanceCountdownSchedule(clock, 0d, settings, 1f);
        AdvanceCountdownSchedule(clock, 25d, settings, 1f);
        AdvanceCountdownSchedule(clock, 999d, settings, 0f);
        AdvanceCountdownSchedule(clock, 99999d, settings, 1f);
        Check(clock.RemainingGameSeconds == 75d, "zero frequency freezes observation");
        AdvanceCountdownSchedule(clock, 99999d, settings, 2f);
        Check(clock.RemainingGameSeconds == 37.5d && clock.IntervalGameSeconds == 50d, "frequency scales the same clock proportionally");
        clock.Advance(99998d, 50d);
        Check(clock.RemainingGameSeconds == 37.5d, "clock rewind cannot add elapsed time");
        var precise = new SpawnerCountdownProgress();
        precise.Advance(1e12d, 1d);
        Check(!precise.Advance(1e12d + .5d, 1d) && precise.Advance(1e12d + 1d, 1d), "long-session double precision");
        foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { new SpawnerCountdownProgress().Advance(0d, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "invalid timer interval rejected");
        }
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { GetCountdownInterval(settings, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "invalid frequency rejected");
        }

        var candidates = new List<NaturalSpawnCandidate>
        {
            new(config, new SpawnerConfig.SpawnEntry { Probability = 1f }, default),
            new(config, new SpawnerConfig.SpawnEntry { Probability = 3f }, default),
            new(new SpawnerConfig(), new SpawnerConfig.SpawnEntry { Probability = 2f }, default)
        };
        int[] counts = new int[3];
        for (int i = 0; i < 6000; i++) counts[SelectCountdownCandidate(candidates, (i + .5f) / 6000f)]++;
        Check(counts.SequenceEqual(new[] { 1000, 3000, 2000 }), "one weighted pool across rule files");
        Check(SelectCountdownCandidate(candidates, 0f) == 0 && SelectCountdownCandidate(candidates, 1f) == 2, "weighted boundaries");
        candidates.Clear(); Check(SelectCountdownCandidate(candidates, 0f) == -1, "no candidate means no species");
        candidates.Add(new(config, entry, default));
        Check(SelectCountdownCandidate(candidates, 1f) == 0, "single eligible habitat candidate");

        config.SpawnChance = 1f;
        clock = new SpawnerCountdownProgress { RemainingGameSeconds = 7.5d, IntervalGameSeconds = 80d, LastObservedGameSeconds = 500d };
        byte[] before = MemoryPackSerializer.Serialize(clock);
        var monitor = ReadCountdownMonitor(config, entry, clock, settings, 2f);
        var other = ReadCountdownMonitor(config, new SpawnerConfig.SpawnEntry { PrefabName = "Fish", Probability = 100f }, clock, settings, 0f);
        Check(monitor.RemainingGameSeconds == other.RemainingGameSeconds && other.IntervalGameSeconds == 80d, "all rows read one real timer regardless of weight");
        Check(before.SequenceEqual(MemoryPackSerializer.Serialize(clock)), "monitor cannot mutate progress");
        Check(ReadCountdownMonitor(config, entry, null, settings, 1f).RemainingGameSeconds == -1d, "missing timer is not due");

        var manager = new MonsterSpawnerManager();
        var land = new SpawnerConfig { PersistentId = "land", SpawnCount = 1, SpawnChance = 1f };
        land.SpawnEntries.Add(new SpawnerConfig.SpawnEntry { PrefabName = "Wolf", Probability = 100f });
        var sea = new SpawnerConfig { PersistentId = "sea", SpawnCount = 2, SpawnChance = 1f };
        sea.SpawnEntries.Add(new SpawnerConfig.SpawnEntry { PrefabName = "Fish", Probability = 1f });
        manager._spawnerConfigs.AddRange(new[] { land, sea });
        manager._naturalCountdown.Advance(0d, 100d);
        Check(!manager.ProcessDueSpawnWork("surface", 0) && manager.requests == 0, "no automatic birth before due");
        manager._naturalCountdown.Advance(100d, 100d);
        Time.unscaledTime = 1f;
        manager._nextCreatureBirthTime = 5f;
        Check(!manager.ProcessDueSpawnWork("surface", 0) && manager.requests == 0, "creation throttle never introduces a second spawn timer");
        manager._nextCreatureBirthTime = 0f;
        manager._naturalCreatureBirth = new object();
        Check(!manager.ProcessDueSpawnWork("surface", 0) && manager.requests == 0, "in-flight creation blocks duplicate scheduling");
        manager._naturalCreatureBirth = null;
        Check(!manager.ProcessDueSpawnWork("surface", 0) && manager._naturalCountdown.SelectedSpeciesId == null, "no loaded habitat keeps one unbound due opportunity");
        manager.habitats["sea"] = new Vector3(50f, 60f);
        Time.unscaledTime = 2f;
        Check(manager.ProcessDueSpawnWork("surface", 0) && manager.requestedSpecies == "Fish", "invalid high-weight land species excluded before selection");
        Check(manager.requestedPosition.x == 50f && manager.requestedPosition.y == 60f, "selected valid position reaches the creation request");
        Check(manager._naturalCountdown.PendingCount == 2 && manager._runtimeStates["sea"].PendingSpawnCount == 2, "only selected rule has a batch");
        manager.habitats.Clear();
        Check(manager.ProcessDueSpawnWork("surface", 0) && manager.requestedSpecies == "Fish" && manager.requests == 2, "current group does not reroll species");
        GameRuleConfiguration.Current.World.SpawnFrequencyMultiplier = 0f;
        Check(!manager.ProcessDueSpawnWork("surface", 0) && manager.requests == 2, "disabled global schedule stops pending births");
        GameRuleConfiguration.Current.World.SpawnFrequencyMultiplier = 1f;
        sea.SpawnSearchRetryCount = 2;
        manager.RecordNaturalSpawnFailure(sea, manager.GetOrCreateState(sea));
        Check(manager._naturalCountdown.PendingCount == 2, "temporary failure retains the same batch");
        manager.RecordNaturalSpawnFailure(sea, manager.GetOrCreateState(sea));
        Check(manager._naturalCountdown.PendingCount == 0 && manager._naturalCountdown.SelectedSpeciesId == null &&
            manager.GetOrCreateState(sea).PendingSpawnCount == 0, "persistently invalid habitat cannot lock the global scheduler");
        Check(manager._naturalCountdown.RemainingGameSeconds == 100d && manager.GetOrCreateState(sea).LifetimeSpawnCount == 0, "abandoned request is not a birth or immediate reroll");
        manager.habitats["land"] = default; manager.habitats["sea"] = default;
        land.RequireGlobalDarkness = true; sea.SpawnChance = 0f;
        manager.CollectNaturalSpawnCandidates("surface");
        Check(manager._naturalSpawnCandidates.Count == 0, "darkness and disabled-rule filtering");
        manager.dark = true; sea.SpawnChance = 1f; manager.blockedEntries.Add("Wolf");
        manager.CollectNaturalSpawnCandidates("surface");
        Check(manager._naturalSpawnCandidates.Count == 1 && manager._naturalSpawnCandidates[0].Entry.PrefabName == "Fish", "backend/species eligibility filtering");
        manager.population = 40; manager.CollectNaturalSpawnCandidates("surface");
        Check(manager._naturalSpawnCandidates.Count == 0, "global population cap before lottery");
        sea.IgnorePopulationLimits = true; manager.CollectNaturalSpawnCandidates("surface");
        Check(manager._naturalSpawnCandidates.Count == 1, "explicit cap exemption remains intact");

        var chunk = new ChunkRuntime { Address = new WorldAddress("surface", new Int2(20, 0)), DataStatus = ChunkDataStatus.Generating };
        Check(!IsLoadedNaturalSpawnChunk(chunk, "surface"), "generating chunk rejected");
        chunk.DataStatus = ChunkDataStatus.Ready;
        Check(!IsLoadedNaturalSpawnChunk(chunk, "surface"), "missing terrain rejected");
        chunk.Terrain = new ChunkTerrainData();
        Check(IsLoadedNaturalSpawnChunk(chunk, "surface") && !IsLoadedNaturalSpawnChunk(chunk, "cave"), "ready terrain with world ownership");
        chunk.Terrain.IsDisposed = true;
        Check(!IsLoadedNaturalSpawnChunk(chunk, "surface"), "unloaded terrain rejected");
        chunk.Terrain.IsDisposed = false;
        manager._chunkManager.Chunks["ready"] = chunk;
        manager._chunkManager.Chunks["far"] = new ChunkRuntime { Address = new WorldAddress("surface", new Int2(9999, 9999)), DataStatus = ChunkDataStatus.Ready, Terrain = new() };
        Check(manager.TryGetLoadedSpawnCandidate(land, out Vector3 point) && point.x == 20.5f && manager._naturalSpawnChunks.Count == 1, "only nearby loaded chunks sampled");
        chunk.DataStatus = ChunkDataStatus.Generating;
        Check(!manager.TryGetLoadedSpawnCandidate(land, out _), "no new chunk requested when loaded pool is empty");
        var anotherWorld = new MonsterSpawnerManager();
        Check(anotherWorld._naturalCountdown.RemainingGameSeconds == -1d && manager._naturalCountdown.RemainingGameSeconds == 100d, "worlds own isolated clocks");
        int cursor = 0; int[] work = new int[3];
        for (int i = 0; i < 9; i++) work[NextLifecycleWork(ref cursor)]++;
        Check(work.All(count => count == 3), "lifecycle fairness preserved");
        Console.WriteLine($"PASS: {checks} production-scheduler assertions, weighted draws, real MemoryPack roundtrips, loaded-candidate filtering and recovery.");
    }
}

public static class Program { public static void Main() => MonsterSpawnerManager.RunTests(); }
public enum AiRuntimeBackendKind { GameObject, Entities }
public sealed class SpawnerRuntimeSettings { public float NaturalSpawnIntervalGameSeconds, SpawnRetryInterval; public int GlobalAliveLimit; }
public static class GameRuleMath { public static int ScaleCount(int count, float scale, int minimum = 0) => Math.Max(minimum, (int)Math.Round(count * scale)); }
public sealed class WorldRules { public float SpawnFrequencyMultiplier = 1f, SpawnPopulationMultiplier = 1f; }
public sealed class Rules { public WorldRules World = new(); }
public static class GameRuleConfiguration { public static Rules Current = new(); }
public sealed class TimeData { public double GetTotalGameTimeSeconds() => UnityEngine.Time.unscaledTime; }
public sealed class ChunkMgr
{
    public Dictionary<string, ChunkRuntime> Chunks = new();
    public WorldAddress ResolveWorldAddress(Vector3 _) => new("surface", default);
}
namespace FlatWorld.WorldModel
{
    public enum ChunkDataStatus { Generating, Ready }
    public readonly record struct Int2(int X, int Y);
    public readonly record struct WorldAddress(string DimensionId, Int2 ChunkOrigin);
    public sealed class ChunkTerrainData { public int Width = 16, Height = 16; public bool IsDisposed; }
    public sealed class ChunkRuntime { public WorldAddress Address; public ChunkDataStatus DataStatus; public ChunkTerrainData Terrain; }
}
namespace UnityEngine
{
    public class ScriptableObject { }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
    }
    public static class Time { public static float unscaledTime; }
    public static class Random { public static float value = .5f; public static int Range(int min, int max) => min; }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}
