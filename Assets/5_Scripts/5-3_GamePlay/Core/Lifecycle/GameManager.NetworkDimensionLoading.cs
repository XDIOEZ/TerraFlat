public partial class GameManager
{
    #region 仅本机维度加载表现
    private bool networkDimensionLoading;
    private string networkDimensionTarget;

    // 复用加载页的显示协议，不触碰全服 IsGameplayReady 和世界进入生命周期。
    internal bool BeginNetworkDimensionLoading(DimensionDefinition definition)
    {
        if (definition == null || isWorldEntryInProgress) return false;
        if (networkDimensionLoading) return networkDimensionTarget == definition.DimensionId;
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

    internal void EndNetworkDimensionLoading(bool completed, string failureReason = null)
    {
        if (!networkDimensionLoading) return;
        networkDimensionLoading = false;
        PublishWorldEntryProgress(new WorldEntryProgressInfo(completed ? "维度切换完成" : "维度切换失败",
            completed ? "目标区域已经准备完毕。" : GetNetworkDimensionFailureText(failureReason), completed ? 1f : 0f,
            completed ? WorldEntryProgressState.Completed : WorldEntryProgressState.Failed,
            WorldEntryPresentationMode.Dimension, networkDimensionTarget));
        networkDimensionTarget = null;
    }

    // 加载失败保留准备阶段的异常，不能把资源或场景错误一概归为连接问题。
    internal static string GetNetworkDimensionFailureText(string failureReason)
        => string.IsNullOrWhiteSpace(failureReason)
            ? FlatWorld.Localization.FlatWorldLocalizationService.GetUiText("目标区域准备未完成，请查看日志。")
            : failureReason;
    #endregion
}
