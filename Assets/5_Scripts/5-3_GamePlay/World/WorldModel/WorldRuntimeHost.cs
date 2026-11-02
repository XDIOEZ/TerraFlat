using UnityEngine;

/// <summary>
/// 按所属世界分别推进帧流送和固定模拟。
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldRuntimeHost : MonoBehaviour, IGameplayFixedStepSimulationSystem
{
    private ChunkMgr owner;

    #region 分级世界调度

    private enum OwnedWorldStepKind { Streaming, Physics, Background, Environment }

    private sealed class OwnedWorldSubSystem : IGameplayFixedStepSimulationSystem, IGameplayFixedStepSimulationReadiness
    {
        private readonly WorldRuntimeHost host;
        private readonly OwnedWorldStepKind kind;
        public OwnedWorldSubSystem(WorldRuntimeHost host, OwnedWorldStepKind kind)
        { this.host = host; this.kind = kind; }
        public bool CanAccumulateFixedStepTime => host.CanAdvanceOwnedWorldSimulation();
        public void SimulateFixedStep(float deltaTime) => host.AdvanceOwnedWorld(kind, deltaTime);
    }

    private OwnedWorldSubSystem backgroundSimulation;
    private OwnedWorldSubSystem environmentSimulation;

    private bool CanAdvanceOwnedWorldSimulation()
    {
        if (owner == null || owner.WorldRuntime == null || owner.IsWorldRuntimeShuttingDown) return false;
        if (string.IsNullOrEmpty(owner.OwnedWorldKey))
            return owner.WorldRuntime.WorldId == GameplayWorldRegistry.ExecutionWorldKey;
        return GameplayWorldRegistry.TryGet(owner.OwnedWorldKey, out GameplayWorldRuntime world) &&
            ReferenceEquals(world.Chunks, owner);
    }

    #endregion

    #region 流送驱动诊断

    /// <summary>区分宿主未绑定、Update 没执行和提交阶段自身变慢，不在诊断中自动重绑。</summary>
    public bool HasOwner => owner != null;
    public long UpdateCount { get; private set; }
    public int LastUpdateFrame { get; private set; } = -1;
    public long LastUpdateTimestamp { get; private set; }
    public bool IsBoundTo(ChunkMgr manager) => ReferenceEquals(owner, manager);

    #endregion

    public void Bind(ChunkMgr chunkManager)
    {
        owner = chunkManager;
    }

    private void OnEnable()
    {
        backgroundSimulation ??= new OwnedWorldSubSystem(this, OwnedWorldStepKind.Background);
        environmentSimulation ??= new OwnedWorldSubSystem(this, OwnedWorldStepKind.Environment);
        GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.WorldSimulation);
        GameplayFixedStepSimulation.Register(backgroundSimulation, GameplayFixedStepSimulationPhase.WorldSimulation,
            GameplayFixedStepTickRate.Hz20);
        GameplayFixedStepSimulation.Register(environmentSimulation, GameplayFixedStepSimulationPhase.WorldSimulation,
            GameplayFixedStepTickRate.Hz10);
    }
    private void OnDisable()
    {
        GameplayFixedStepSimulation.Unregister(this);
        GameplayFixedStepSimulation.Unregister(backgroundSimulation);
        GameplayFixedStepSimulation.Unregister(environmentSimulation);
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        UpdateCount++;
        LastUpdateFrame = Time.frameCount;
        LastUpdateTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        AdvanceOwnedWorld(OwnedWorldStepKind.Streaming, 0f);
    }

    public void SimulateFixedStep(float deltaTime) => AdvanceOwnedWorld(OwnedWorldStepKind.Physics, deltaTime);

    private void AdvanceOwnedWorld(OwnedWorldStepKind kind, float deltaTime)
    {
        if (owner == null) return;
        if (!string.IsNullOrEmpty(owner.OwnedWorldKey))
        {
            // 已解绑的场景不能让旧宿主自动创建世界或继续推进新拥有者的数据。
            if (!GameplayWorldRegistry.TryGet(owner.OwnedWorldKey, out GameplayWorldRuntime world) ||
                !ReferenceEquals(world.Chunks, owner)) return;
            using (GameplayWorldRegistry.UseWorld(owner.OwnedWorldKey))
            {
                AdvanceOwner(kind, deltaTime);
            }
        }
        else if (owner.WorldRuntime == null || owner.WorldRuntime.WorldId == GameplayWorldRegistry.ExecutionWorldKey)
        {
            AdvanceOwner(kind, deltaTime);
        }
    }

    private void AdvanceOwner(OwnedWorldStepKind kind, float deltaTime)
    {
        switch (kind)
        {
            case OwnedWorldStepKind.Streaming: owner.AdvanceWorldStreamingFrame(); break;
            case OwnedWorldStepKind.Physics: owner.SimulateWorldFixedStep(deltaTime); break;
            case OwnedWorldStepKind.Background: owner.SimulateWorldBackgroundStep(deltaTime); break;
            case OwnedWorldStepKind.Environment: owner.SimulateWorldEnvironmentStep(deltaTime); break;
        }
    }

    private void OnDestroy()
    {
        owner = null;
    }
}
