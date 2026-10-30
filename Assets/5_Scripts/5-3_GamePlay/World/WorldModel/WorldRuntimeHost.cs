using UnityEngine;

/// <summary>
/// 按所属世界分别推进帧流送和固定模拟。
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldRuntimeHost : MonoBehaviour, IGameplayFixedStepSimulationSystem
{
    private ChunkMgr owner;

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

    private void OnEnable() => GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.WorldSimulation);
    private void OnDisable() => GameplayFixedStepSimulation.Unregister(this);

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        UpdateCount++;
        LastUpdateFrame = Time.frameCount;
        LastUpdateTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        AdvanceOwnedWorld(false, 0f);
    }

    public void SimulateFixedStep(float deltaTime) => AdvanceOwnedWorld(true, deltaTime);

    private void AdvanceOwnedWorld(bool simulation, float deltaTime)
    {
        if (owner == null) return;
        if (!string.IsNullOrEmpty(owner.OwnedWorldKey))
        {
            // 已解绑的场景不能让旧宿主自动创建世界或继续推进新拥有者的数据。
            if (!GameplayWorldRegistry.TryGet(owner.OwnedWorldKey, out GameplayWorldRuntime world) ||
                !ReferenceEquals(world.Chunks, owner)) return;
            using (GameplayWorldRegistry.UseWorld(owner.OwnedWorldKey))
            {
                if (simulation) owner.SimulateWorldFixedStep(deltaTime);
                else owner.AdvanceWorldStreamingFrame();
            }
        }
        else if (owner.WorldRuntime == null || owner.WorldRuntime.WorldId == GameplayWorldRegistry.ExecutionWorldKey)
        {
            if (simulation) owner.SimulateWorldFixedStep(deltaTime);
            else owner.AdvanceWorldStreamingFrame();
        }
    }

    private void OnDestroy()
    {
        owner = null;
    }
}
