using System;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    #region 默认关闭的液体实验
    [Header("实验：液体流动（默认关闭）")]
    [SerializeField, Tooltip("启用后会修改真实液深并进入正常存档差量；只应在实验世界开启。")]
    private bool enableExperimentalLiquidFlow;
    [SerializeField] private LiquidFlowSettings experimentalLiquidFlowSettings = new(); // 参数在启用时冻结。
    private WorldLiquidFlowExperiment liquidFlowExperiment;
    private bool liquidFlowFailed; // 当前配置失败后停用，显式重启才能重试。

    /// <summary>只读诊断入口；关闭时为 null，不创建模拟器。</summary>
    public WorldLiquidFlowExperiment LiquidFlowExperiment => liquidFlowExperiment;

    /// <summary>开发工具/MOD 的稳定开关入口；默认不写 Prefab，也不自动开启存档。</summary>
    public void SetExperimentalLiquidFlowEnabled(bool enabled, LiquidFlowSettings settings = null)
    {
        if (settings != null) experimentalLiquidFlowSettings = settings.Snapshot();
        StopLiquidFlowExperiment();
        liquidFlowFailed = false;
        enableExperimentalLiquidFlow = enabled;
        if (enabled) AdvanceLiquidFlowExperiment(0f);
    }

    /// <summary>只有打开实验开关后才接受开发者唤醒；天然静水不会因查询而自动启动。</summary>
    public void WakeExperimentalLiquidFlow(Vector2 worldPosition) =>
        liquidFlowExperiment?.Wake(Vector2Int.FloorToInt(worldPosition));

    /// <summary>漂浮物、玩家推动及 Shader 共用的真实动态流向查询，零流量时回退各自既有行为。</summary>
    public bool TryGetExperimentalLiquidFlow(Vector2 worldPosition, out Vector2 flow)
    {
        flow = Vector2.zero;
        return liquidFlowExperiment != null && liquidFlowExperiment.TryGetFlow(Vector2Int.FloorToInt(worldPosition), out flow);
    }

    /// <summary>跟随世界宿主帧时间推进，不使用 FixedUpdate，不在默认关闭时创建任何缓存或订阅。</summary>
    private void AdvanceLiquidFlowExperiment(float deltaSeconds)
    {
        if (!enableExperimentalLiquidFlow || !authoritativeSimulation || !GameNetwork.HasStateAuthority ||
            runtimeWindowTargets.Count == 0 || IsWorldRuntimeShuttingDown)
        {
            StopLiquidFlowExperiment();
            if (!enableExperimentalLiquidFlow) liquidFlowFailed = false;
            return;
        }
        if (liquidFlowFailed) return;
        try
        {
            liquidFlowExperiment ??= new WorldLiquidFlowExperiment(this, experimentalLiquidFlowSettings,
                ResolveCurrentDimensionId());
            liquidFlowExperiment.Advance(deltaSeconds);
        }
        catch (Exception exception)
        {
            StopLiquidFlowExperiment();
            liquidFlowFailed = true;
            Debug.LogError($"[LiquidFlow] 实验已停用并释放租约：{exception}", this);
        }
    }

    /// <summary>邻区必须使用当前世界的冻结 Profile、维度种子与规范拓扑，不能另算一份地图。</summary>
    internal Task<ChunkRuntime> RequestLiquidFlowChunkAsync(RuntimeWorldAddress address, CancellationToken token)
    {
        int seed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (seed == 0) seed = 1;
        if (DimensionManager.Instance != null) seed = DimensionManager.Instance.GetActiveGenerationSeed(seed);
        return RequestChunkDataAsync(address, seed, ActiveGenerationProfile, token, ResolveActiveGenerationTopology());
    }

    /// <summary>正常加载已恢复差量的区块直接通知实验器，避免在恢复期间触发模拟。</summary>
    private void NotifyLiquidFlowChunkReady(ChunkRuntime chunk) => liquidFlowExperiment?.Observe(chunk);

    /// <summary>在清空世界之前释放，防止异步邻区结果写入新世界。</summary>
    private void StopLiquidFlowExperiment()
    {
        WorldLiquidFlowExperiment previous = liquidFlowExperiment;
        liquidFlowExperiment = null;
        previous?.Dispose();
    }
    #endregion
}
