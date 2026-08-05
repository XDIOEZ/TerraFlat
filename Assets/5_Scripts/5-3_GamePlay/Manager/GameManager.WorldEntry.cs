using System;
using System.Collections;
using UnityEngine;

public enum WorldEntryProgressState
{
    Running,
    Completed,
    Failed
}

/// <summary>
/// 与具体 UI 无关的世界进入进度快照。呈现层可订阅它，测试也可直接观察它。
/// </summary>
public readonly struct WorldEntryProgressInfo
{
    public string Title { get; }
    public string Status { get; }
    public float Progress { get; }
    public WorldEntryProgressState State { get; }

    public WorldEntryProgressInfo(
        string title,
        string status,
        float progress,
        WorldEntryProgressState state)
    {
        Title = title ?? string.Empty;
        Status = status ?? string.Empty;
        Progress = Mathf.Clamp01(progress);
        State = state;
    }
}

public partial class GameManager
{
    private bool isWorldEntryInProgress;
    private Coroutine worldEntryCompletionCoroutine;

    public bool IsWorldEntryInProgress => isWorldEntryInProgress;

    /// <summary>
    /// 世界进入生命周期的只读通知。核心流程不要求存在任何订阅者。
    /// </summary>
    public event Action<WorldEntryProgressInfo> WorldEntryProgressChanged;

    partial void InitializeWorldEntryPresentation();
    partial void DisposeWorldEntryPresentation();

    private bool BeginWorldEntry(string title, string status, float progress)
    {
        if (isWorldEntryInProgress)
        {
            Debug.LogWarning("[GameManager] 世界进入流程已在执行，忽略重复请求。");
            return false;
        }

        isWorldEntryInProgress = true;
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        Event_PlayerEnterWorld += OnWorldEntryPlayerReady;
        PublishWorldEntryProgress(
            new WorldEntryProgressInfo(title, status, progress, WorldEntryProgressState.Running));
        return true;
    }

    private void ReportWorldEntryProgress(string title, string status, float progress)
    {
        if (!isWorldEntryInProgress)
            return;

        PublishWorldEntryProgress(
            new WorldEntryProgressInfo(title, status, progress, WorldEntryProgressState.Running));
    }

    private void OnWorldEntryPlayerReady(Player player)
    {
        if (!isWorldEntryInProgress)
            return;

        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (worldEntryCompletionCoroutine != null)
            StopCoroutine(worldEntryCompletionCoroutine);
        worldEntryCompletionCoroutine = StartCoroutine(CompleteWorldEntryCoroutine());
    }

    private IEnumerator CompleteWorldEntryCoroutine()
    {
        ReportWorldEntryProgress("正在进入世界", "正在加载玩家周围区域…", 0.78f);
        yield return null;

        float displayedProgress = 0.78f;
        while (ChunkMgr.Instance != null && ChunkMgr.Instance.HasPendingChunkLoads)
        {
            displayedProgress = Mathf.MoveTowards(
                displayedProgress,
                0.95f,
                Mathf.Max(0.002f, Time.unscaledDeltaTime * 0.08f));
            ReportWorldEntryProgress("正在进入世界", "正在生成并加载周围区块…", displayedProgress);
            yield return null;
        }

        // 给 Chunk Ready 事件、导航刷新和延迟销毁各一个收尾帧。
        yield return null;
        yield return null;

        worldEntryCompletionCoroutine = null;
        isWorldEntryInProgress = false;
        PublishWorldEntryProgress(
            new WorldEntryProgressInfo(
                "加载完成",
                "世界已经准备完毕。",
                1f,
                WorldEntryProgressState.Completed));
    }

    private void FailWorldEntry(string message, Exception exception = null)
    {
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (worldEntryCompletionCoroutine != null)
        {
            StopCoroutine(worldEntryCompletionCoroutine);
            worldEntryCompletionCoroutine = null;
        }

        if (exception != null)
            Debug.LogException(exception, this);
        Debug.LogError($"[GameManager] {message}", this);

        isWorldEntryInProgress = false;
        PublishWorldEntryProgress(
            new WorldEntryProgressInfo(
                "加载失败",
                message,
                0f,
                WorldEntryProgressState.Failed));
    }

    private void PublishWorldEntryProgress(WorldEntryProgressInfo progress)
    {
        Delegate[] listeners = WorldEntryProgressChanged?.GetInvocationList();
        if (listeners == null)
            return;

        foreach (Delegate listener in listeners)
        {
            try
            {
                ((Action<WorldEntryProgressInfo>)listener).Invoke(progress);
            }
            catch (Exception exception)
            {
                // 呈现层或观察者失败不能中断世界生命周期，但必须留下可测试的错误。
                Debug.LogException(exception, this);
            }
        }
    }

    private void ResetWorldEntryLifecycle()
    {
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (worldEntryCompletionCoroutine != null)
        {
            StopCoroutine(worldEntryCompletionCoroutine);
            worldEntryCompletionCoroutine = null;
        }

        isWorldEntryInProgress = false;
    }
}
