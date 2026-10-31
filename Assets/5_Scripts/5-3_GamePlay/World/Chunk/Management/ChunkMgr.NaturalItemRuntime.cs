using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    #region 独立生态运行时
    private WorldNaturalItemRuntimeManager naturalItemRuntimes;
    internal WorldNaturalItemRuntimeManager NaturalItemRuntimes => naturalItemRuntimes ??=
        new WorldNaturalItemRuntimeManager(this, WorldRuntime);

    public int NaturalItemRuntimeChunkCount => naturalItemRuntimes?.Count ?? 0;
    public int PendingNaturalItemInitializationCount => naturalItemRuntimes?.PendingCount ?? 0;

    public bool TryGetNaturalItemRuntime(Vector2 position, out ChunkNaturalItemRuntime runtime)
    {
        runtime = null;
        return naturalItemRuntimes != null && TryResolveRuntimeAddress(position, out RuntimeWorldAddress address) &&
            naturalItemRuntimes.TryGet(address, out runtime);
    }

    // 分帧逻辑生成借用现有流送预算参数，真正卸载只发生在数据/世界生命周期。
    private void AdvanceNaturalItemRuntimes() => naturalItemRuntimes?.AdvanceFrame(
        RuntimeChunkPresentationContinuationBudget, RuntimeChunkPresentationContinuationMillisecondsBudget);

    private void ReleaseNaturalItemRuntimes()
    {
        WorldNaturalItemRuntimeManager previous = naturalItemRuntimes;
        naturalItemRuntimes = null;
        previous?.Dispose();
    }
    #endregion
}
