public partial class GameManager
{
    #region 仅本机维度加载表现
    private bool networkDimensionLoading;
    private string networkDimensionTarget;

    // 复用加载页的显示协议，不触碰全服 IsGameplayReady 和世界进入生命周期。
    internal bool BeginNetworkDimensionLoading(DimensionDefinition definition)
    {
        if (definition == null || networkDimensionLoading || isWorldEntryInProgress) return false;
        networkDimensionLoading = true;
        networkDimensionTarget = definition.DimensionId;
        ReportNetworkDimensionLoading("正在准备目标区域…", .1f);
        return true;
    }

    internal void ReportNetworkDimensionLoading(string status, float progress)
    {
        if (!networkDimensionLoading) return;
        PublishWorldEntryProgress(new WorldEntryProgressInfo("正在切换维度", status, progress,
            WorldEntryProgressState.Running, WorldEntryPresentationMode.Dimension, networkDimensionTarget));
    }

    internal void EndNetworkDimensionLoading(bool completed)
    {
        if (!networkDimensionLoading) return;
        networkDimensionLoading = false;
        PublishWorldEntryProgress(new WorldEntryProgressInfo(completed ? "维度切换完成" : "维度切换失败",
            completed ? "目标区域已经准备完毕。" : "目标区域未准备完成，请检查连接。", completed ? 1f : 0f,
            completed ? WorldEntryProgressState.Completed : WorldEntryProgressState.Failed,
            WorldEntryPresentationMode.Dimension, networkDimensionTarget));
        networkDimensionTarget = null;
    }
    #endregion
}
