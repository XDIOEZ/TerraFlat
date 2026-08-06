using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeChunkMgr = FlatWorld.WorldModel.ChunkMgr;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    [Header("无头世界模型")]
    [SerializeField] private ChunkGenerationProfileSO defaultGenerationProfile;
    [SerializeField, Min(1)] private int backgroundGenerationConcurrency = 2;
    [SerializeField] private bool authoritativeSimulation = true;

    private RuntimeChunkMgr runtimeChunkManager;
    private ChunkGenerationProfileSnapshot defaultGenerationSnapshot;
    private long runtimeEpoch;
    private WorldRuntimeHost runtimeHost;

    public WorldRuntime WorldRuntime => runtimeChunkManager?.World;
    public IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime> Chunks =>
        runtimeChunkManager?.Chunks ?? EmptyChunkRuntimeDictionary.Instance;
    public bool HasPendingChunkDataLoads => runtimeChunkManager?.HasPendingChunkLoads == true;
    public RuntimeChunkMgr RuntimeChunks => runtimeChunkManager;
    public bool IsAuthoritativeSimulation
    {
        get => authoritativeSimulation;
        set => authoritativeSimulation = value;
    }

    protected override void Awake()
    {
        base.Awake();
        if (instance != this)
            return;
        InitializeWorldRuntime();
        runtimeHost = GetComponent<WorldRuntimeHost>();
        if (runtimeHost == null)
            runtimeHost = gameObject.AddComponent<WorldRuntimeHost>();
        runtimeHost.Bind(this);
    }

    protected override void OnDestroy()
    {
        ShutdownWorldRuntime();
        base.OnDestroy();
    }

    public Task<ChunkRuntime> RequestChunkDataAsync(RuntimeWorldAddress address, int worldSeed,
        ChunkGenerationProfileSnapshot profile = null,
        CancellationToken cancellationToken = default,
        ChunkGenerationTopologySnapshot topology = default)
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.RequestChunkDataAsync(address, worldSeed,
            profile ?? defaultGenerationSnapshot, cancellationToken, topology);
    }

    public bool TryGetChunkRuntime(RuntimeWorldAddress address, out ChunkRuntime chunk)
    {
        chunk = null;
        return runtimeChunkManager != null && runtimeChunkManager.TryGetChunk(address, out chunk);
    }

    public ChunkLease AcquireChunkLease(RuntimeWorldAddress address, ChunkLeaseKind kind)
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.AcquireLease(address, kind);
    }

    public bool CancelChunkDataRequest(RuntimeWorldAddress address)
    {
        return runtimeChunkManager != null && runtimeChunkManager.CancelChunkRequest(address);
    }

    public bool EvictChunkRuntime(RuntimeWorldAddress address)
    {
        return runtimeChunkManager != null && runtimeChunkManager.EvictChunk(address);
    }

    public RuntimeWorldAddress ResolveWorldAddress(Vector2 worldPosition, string dimensionId = null)
    {
        EnsureWorldRuntime();
        int width = Math.Max(1, defaultGenerationSnapshot.Width);
        int height = Math.Max(1, defaultGenerationSnapshot.Height);
        var origin = NormalizeChunkPosition(new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / width) * width,
            Mathf.FloorToInt(worldPosition.y / height) * height));
        return new RuntimeWorldAddress(
            string.IsNullOrWhiteSpace(dimensionId) ? ResolveCurrentDimensionId() : dimensionId,
            new Int2(origin.x, origin.y));
    }

    public Vector2Int ResolveRuntimeChunkOrigin(Vector2 worldPosition)
    {
        RuntimeWorldAddress address = ResolveWorldAddress(worldPosition);
        return new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y);
    }

    public Task SettleGenerationTasksAsync()
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.SettleGenerationTasksAsync();
    }

    public System.Collections.IEnumerator CancelAndSettleGenerationCoroutine()
    {
        if (runtimeChunkManager == null)
            yield break;
        runtimeChunkManager.CancelAllRequests();
        while (runtimeChunkManager.HasUnsettledGenerationTasks)
        {
            runtimeChunkManager.CommitCompleted();
            if (runtimeChunkManager.HasUnsettledGenerationTasks)
                yield return null;
        }
        runtimeChunkManager.CommitCompleted();
    }

    internal void AdvanceWorldRuntime(float deltaSeconds)
    {
        runtimeChunkManager?.Advance(deltaSeconds, authoritativeSimulation);
        ReconcileRuntimeWindowBindings();
    }

    private void InitializeWorldRuntime()
    {
        if (runtimeChunkManager != null)
            return;

        runtimeEpoch = Math.Max(1, runtimeEpoch + 1);
        defaultGenerationSnapshot = defaultGenerationProfile != null
            ? defaultGenerationProfile.CreateSnapshot()
            : CreateFallbackGenerationSnapshot();
        string worldId = SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(worldId))
            worldId = "world";
        var world = new WorldRuntime(worldId, runtimeEpoch);
        runtimeChunkManager = new RuntimeChunkMgr(world, new DeterministicChunkGenerator(),
            Mathf.Max(1, backgroundGenerationConcurrency), new UnityWorldAddressNormalizer());
    }

    private void ResetWorldRuntimeForSceneChange()
    {
        ClearRuntimeWindowBindings();
        if (runtimeChunkManager == null)
            return;
        runtimeChunkManager.ClearWindow();
        runtimeChunkManager.CancelAllRequests();
        runtimeChunkManager.CommitCompleted();
        runtimeEpoch++;
        runtimeChunkManager.World.BeginNewEpoch(runtimeEpoch);
    }

    private void ShutdownWorldRuntime()
    {
        ClearRuntimeWindowBindings();
        if (runtimeChunkManager == null)
            return;
        WorldRuntime world = runtimeChunkManager.World;
        runtimeChunkManager.Dispose();
        runtimeChunkManager = null;
        world.Dispose();
    }

    private void EnsureWorldRuntime()
    {
        if (runtimeChunkManager == null)
            InitializeWorldRuntime();
    }

    private static ChunkGenerationProfileSnapshot CreateFallbackGenerationSnapshot()
    {
        Vector2 size = GetChunkSize();
        return new ChunkGenerationProfileSnapshot(
            "surface.fallback",
            TerrainGenerationSignature.CurrentVersion,
            Mathf.Max(1, Mathf.RoundToInt(size.x)),
            Mathf.Max(1, Mathf.RoundToInt(size.y)),
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["entity.spawnCount"] = 1d
            });
    }

    private static string ResolveCurrentDimensionId()
    {
        DimensionManager dimensionManager = DimensionManager.Instance;
        if (dimensionManager != null && dimensionManager.ActiveAddress.IsValid)
            return dimensionManager.ActiveAddress.DimensionId;
        string scene = SceneManager.GetActiveScene().name;
        return string.IsNullOrWhiteSpace(scene) ? "surface" : scene;
    }

    private static ChunkGenerationTopologySnapshot ResolveActiveGenerationTopology()
    {
        return WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? new ChunkGenerationTopologySnapshot(
                new Int2(bounds.Min.x, bounds.Min.y),
                new Int2(bounds.Span.x, bounds.Span.y))
            : default;
    }

    private sealed class EmptyChunkRuntimeDictionary :
        IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime>
    {
        public static readonly EmptyChunkRuntimeDictionary Instance = new();
        public int Count => 0;
        public IEnumerable<RuntimeWorldAddress> Keys => Array.Empty<RuntimeWorldAddress>();
        public IEnumerable<ChunkRuntime> Values => Array.Empty<ChunkRuntime>();
        public ChunkRuntime this[RuntimeWorldAddress key] => throw new KeyNotFoundException();
        public bool ContainsKey(RuntimeWorldAddress key) => false;
        public bool TryGetValue(RuntimeWorldAddress key, out ChunkRuntime value)
        { value = null; return false; }
        public IEnumerator<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>)
                Array.Empty<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>()).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class UnityWorldAddressNormalizer : IWorldAddressNormalizer
    {
        public RuntimeWorldAddress Normalize(RuntimeWorldAddress address)
        {
            Vector2Int normalized = NormalizeChunkPosition(new Vector2Int(
                address.ChunkOrigin.X, address.ChunkOrigin.Y));
            return new RuntimeWorldAddress(address.DimensionId,
                new Int2(normalized.x, normalized.y));
        }
    }
}
