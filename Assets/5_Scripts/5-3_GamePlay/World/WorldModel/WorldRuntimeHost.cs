using UnityEngine;

/// <summary>
/// Unity lifecycle boundary for the engine-free world. It forwards frame time and main-thread
/// commits only; all gameplay decisions live in FlatWorld.WorldModel systems.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldRuntimeHost : MonoBehaviour
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

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        UpdateCount++;
        LastUpdateFrame = Time.frameCount;
        LastUpdateTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        owner?.AdvanceWorldRuntime(Time.deltaTime);
    }

    private void OnDestroy()
    {
        owner = null;
    }
}
