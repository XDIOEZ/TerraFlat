using System;

public partial class GameManager
{
    internal bool BeginDimensionTransitionLoading(DimensionDefinition targetDefinition)
    {
        if (targetDefinition == null)
            throw new ArgumentNullException(nameof(targetDefinition));

        return BeginWorldEntry(
            "正在切换维度",
            $"正在前往：{targetDefinition.DisplayName}",
            0.1f,
            WorldEntryPresentationMode.Dimension,
            targetDefinition.DimensionId,
            false);
    }

    internal void SetDimensionTransitionLoading(string status, float progress)
    {
        ReportWorldEntryProgress("正在切换维度", status, progress);
    }

    internal void NotifyDimensionWorldExiting()
    {
        Event_GameWorldExit?.Invoke();
        ChunkGenerator_River.ClearHydrologyCache();
    }

    internal void NotifyDimensionWorldEntered()
    {
        Event_GameWorldEnter?.Invoke();
    }

    internal void NotifyDimensionPlayerEntered(Player player)
    {
        Event_PlayerEnterWorld?.Invoke(player);
    }

    internal void CompleteDimensionTransitionLoading()
    {
        CompleteWorldEntry("维度切换完成", "目标维度已经准备完毕。");
    }

    internal void FailDimensionTransitionLoading(string message, Exception exception = null)
    {
        FailWorldEntry(message, exception);
    }
}
