using System;

public partial class GameManager
{
    internal bool BeginDimensionTransitionLoading(string targetDisplayName)
    {
        return BeginWorldEntryLoading(
            "正在切换维度",
            $"正在前往：{targetDisplayName}",
            0.1f);
    }

    internal void SetDimensionTransitionLoading(string status, float progress)
    {
        SetWorldLoadingView("正在切换维度", status, progress);
    }

    internal void NotifyDimensionWorldExiting()
    {
        Event_GameWorldExit?.Invoke();
    }

    internal void NotifyDimensionWorldEntered()
    {
        Event_GameWorldEnter?.Invoke();
    }

    internal void NotifyDimensionPlayerEntered(Player player)
    {
        Event_PlayerEnterWorld?.Invoke(player);
    }

    internal void FailDimensionTransitionLoading(string message, Exception exception = null)
    {
        FailWorldEntryLoading(message, exception);
    }
}
