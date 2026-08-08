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
    private ChunkGenerationProfileSnapshot activeGenerationSnapshot;
    private long runtimeEpoch;
    private WorldRuntimeHost runtimeHost;

    public WorldRuntime WorldRuntime => runtimeChunkManager?.World;
    public IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime> Chunks =>
        runtimeChunkManager?.Chunks ?? EmptyChunkRuntimeDictionary.Instance;
    public bool HasPendingChunkDataLoads => runtimeChunkManager?.HasPendingChunkLoads == true;
    public RuntimeChunkMgr RuntimeChunks => runtimeChunkManager;
    /// <summary>当前世界实际提交给后台区块生成器的完整参数快照。</summary>
    public ChunkGenerationProfileSnapshot ActiveGenerationProfile =>
        activeGenerationSnapshot ?? defaultGenerationSnapshot;
    public bool IsAuthoritativeSimulation
    {
        get => authoritativeSimulation;
        set => authoritativeSimulation = value;
    }

    /// <summary>初始化纯世界运行时，并把主线程宿主绑定到 ChunkMgr。</summary>
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

    /// <summary>销毁场景时释放后台生成任务、区块数据和画面绑定。</summary>
    protected override void OnDestroy()
    {
        ShutdownWorldRuntime();
        base.OnDestroy();
    }

    /// <summary>请求生成一个区块的数据；真正的计算会交给后台生成器。</summary>
    public Task<ChunkRuntime> RequestChunkDataAsync(RuntimeWorldAddress address, int worldSeed,
        ChunkGenerationProfileSnapshot profile = null,
        CancellationToken cancellationToken = default,
        ChunkGenerationTopologySnapshot topology = default)
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.RequestChunkDataAsync(address, worldSeed,
            profile ?? defaultGenerationSnapshot, cancellationToken, topology);
    }

    /// <summary>尝试从当前运行时缓存中找到指定地址的区块。</summary>
    public bool TryGetChunkRuntime(RuntimeWorldAddress address, out ChunkRuntime chunk)
    {
        chunk = null;
        return runtimeChunkManager != null && runtimeChunkManager.TryGetChunk(address, out chunk);
    }

    /// <summary>为指定区块领取一张使用票，防止它在使用期间被回收。</summary>
    public ChunkLease AcquireChunkLease(RuntimeWorldAddress address, ChunkLeaseKind kind)
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.AcquireLease(address, kind);
    }

    /// <summary>取消指定区块尚未完成的后台生成请求。</summary>
    public bool CancelChunkDataRequest(RuntimeWorldAddress address)
    {
        return runtimeChunkManager != null && runtimeChunkManager.CancelChunkRequest(address);
    }

    /// <summary>从运行时缓存中逐出指定区块，释放它占用的地形数据。</summary>
    public bool EvictChunkRuntime(RuntimeWorldAddress address)
    {
        return runtimeChunkManager != null && runtimeChunkManager.EvictChunk(address);
    }

    /// <summary>把世界坐标换算成所属维度和区块原点组成的标准地址。</summary>
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

    /// <summary>把世界坐标换算成所属区块的左下角原点。</summary>
    public Vector2Int ResolveRuntimeChunkOrigin(Vector2 worldPosition)
    {
        RuntimeWorldAddress address = ResolveWorldAddress(worldPosition);
        return new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y);
    }

    /// <summary>等待当前所有后台区块生成任务完成并提交结果。</summary>
    public Task SettleGenerationTasksAsync()
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.SettleGenerationTasksAsync();
    }

    /// <summary>取消所有后台生成任务，并逐帧等待它们安全结束。</summary>
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

    /// <summary>推进纯世界模拟，并修复后台生成完成后的画面绑定。</summary>
    internal void AdvanceWorldRuntime(float deltaSeconds)
    {
        runtimeChunkManager?.Advance(deltaSeconds, authoritativeSimulation);
        ReconcileRuntimeWindowBindings();
    }

    /// <summary>创建世界模型、确定性生成器和后台任务调度器。</summary>
    private void InitializeWorldRuntime()
    {
        if (runtimeChunkManager != null)
            return;

        runtimeEpoch = Math.Max(1, runtimeEpoch + 1);
        defaultGenerationSnapshot = defaultGenerationProfile != null
            ? defaultGenerationProfile.CreateSnapshot()
            : CreateFallbackGenerationSnapshot();
        activeGenerationSnapshot = defaultGenerationSnapshot;
        string worldId = SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(worldId))
            worldId = "world";
        var world = new WorldRuntime(worldId, runtimeEpoch);
        runtimeChunkManager = new RuntimeChunkMgr(world, new DeterministicChunkGenerator(),
            Mathf.Max(1, backgroundGenerationConcurrency), new UnityWorldAddressNormalizer());
    }

    /// <summary>切换场景时清空旧区块，并让新场景使用新的世界纪元。</summary>
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

    /// <summary>彻底关闭世界运行时，释放区块、任务和事件资源。</summary>
    private void ShutdownWorldRuntime()
    {
        ClearRuntimeWindowBindings();
        if (runtimeChunkManager == null)
            return;
        WorldRuntime world = runtimeChunkManager.World;
        runtimeChunkManager.Dispose();
        runtimeChunkManager = null;
        activeGenerationSnapshot = null;
        world.Dispose();
    }

    /// <summary>确保世界运行时已经创建；未创建时立即初始化。</summary>
    private void EnsureWorldRuntime()
    {
        if (runtimeChunkManager == null)
            InitializeWorldRuntime();
    }

    /// <summary>当正式配置不可用时创建一份最小的备用生成配置。</summary>
    private static ChunkGenerationProfileSnapshot CreateFallbackGenerationSnapshot()
    {
        Vector2 size = GetChunkSize();
        return new ChunkGenerationProfileSnapshot(
            "surface.fallback",
            DeterministicChunkGenerator.CurrentGenerationSignature,
            Mathf.Max(1, Mathf.RoundToInt(size.x)),
            Mathf.Max(1, Mathf.RoundToInt(size.y)),
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["entity.spawnCount"] = 1d
            });
    }

    /// <summary>把新建世界界面的坐标缩放写入纯生成快照，后台线程不再读取 Unity 单例。</summary>
    private static ChunkGenerationProfileSnapshot ApplyWorldCoordinateScale(
        ChunkGenerationProfileSnapshot profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        PlanetData planet = SaveDataMgr.Instance?.GetCurrentPlanetData();
        float coordinateScale = ChunkGenerator_Land.ResolveNoiseScale(planet);
        return profile.WithNumericParameter("world.coordinateScale", coordinateScale);
    }

    /// <summary>获取当前活动维度编号；没有维度管理器时退回使用场景名。</summary>
    private static string ResolveCurrentDimensionId()
    {
        DimensionManager dimensionManager = DimensionManager.Instance;
        if (dimensionManager != null && dimensionManager.ActiveAddress.IsValid)
            return dimensionManager.ActiveAddress.DimensionId;
        string scene = SceneManager.GetActiveScene().name;
        return string.IsNullOrWhiteSpace(scene) ? "surface" : scene;
    }

    /// <summary>读取当前有限世界边界，并转换成纯生成器可用的拓扑快照。</summary>
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
        /// <summary>空缓存中不存在任何区块地址。</summary>
        public bool ContainsKey(RuntimeWorldAddress key) => false;
        /// <summary>空缓存始终查询失败，用于避免返回 null 字典。</summary>
        public bool TryGetValue(RuntimeWorldAddress key, out ChunkRuntime value)
        { value = null; return false; }
        /// <summary>返回一个没有元素的枚举器。</summary>
        public IEnumerator<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>)
                Array.Empty<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>()).GetEnumerator();
        /// <summary>以非泛型方式返回一个没有元素的枚举器。</summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class UnityWorldAddressNormalizer : IWorldAddressNormalizer
    {
        /// <summary>把区块地址原点归一化到有限世界的标准坐标。</summary>
        public RuntimeWorldAddress Normalize(RuntimeWorldAddress address)
        {
            Vector2Int normalized = NormalizeChunkPosition(new Vector2Int(
                address.ChunkOrigin.X, address.ChunkOrigin.Y));
            return new RuntimeWorldAddress(address.DimensionId,
                new Int2(normalized.x, normalized.y));
        }
    }
}
