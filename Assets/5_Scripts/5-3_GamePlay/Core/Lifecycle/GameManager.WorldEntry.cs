using System;
using System.Collections;
using UnityEngine;

public enum WorldEntryProgressState
{
    Running,
    Completed,
    Failed
}

public enum WorldEntryPresentationMode
{
    Standard,
    Dimension
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
    public WorldEntryPresentationMode PresentationMode { get; }
    public string TargetId { get; }

    public WorldEntryProgressInfo(
        string title,
        string status,
        float progress,
        WorldEntryProgressState state)
        : this(title, status, progress, state, WorldEntryPresentationMode.Standard, string.Empty)
    {
    }

    public WorldEntryProgressInfo(
        string title,
        string status,
        float progress,
        WorldEntryProgressState state,
        WorldEntryPresentationMode presentationMode,
        string targetId)
    {
        Title = title ?? string.Empty;
        Status = status ?? string.Empty;
        Progress = Mathf.Clamp01(progress);
        State = state;
        PresentationMode = presentationMode;
        TargetId = targetId ?? string.Empty;
    }
}

public partial class GameManager
{
    private bool isWorldEntryInProgress;
    private bool worldEntryCompletesOnPlayerReady;
    private WorldEntryPresentationMode worldEntryPresentationMode = WorldEntryPresentationMode.Standard;
    private string worldEntryTargetId = string.Empty;
    private Coroutine worldEntryCompletionCoroutine;
    private bool isRespawnLoadingPresentationActive;

    public bool IsWorldEntryInProgress => isWorldEntryInProgress;

    /// <summary>
    /// 世界进入生命周期的只读通知。核心流程不要求存在任何订阅者。
    /// </summary>
    public event Action<WorldEntryProgressInfo> WorldEntryProgressChanged;

    partial void InitializeWorldEntryPresentation();
    partial void DisposeWorldEntryPresentation();

    private bool BeginWorldEntry(string title, string status, float progress)
    {
        return BeginWorldEntry(
            title,
            status,
            progress,
            WorldEntryPresentationMode.Standard,
            string.Empty,
            true);
    }

    private bool BeginWorldEntry(
        string title,
        string status,
        float progress,
        WorldEntryPresentationMode presentationMode,
        string targetId,
        bool completeOnPlayerReady)
    {
        if (isWorldEntryInProgress)
        {
            Debug.LogWarning("[GameManager] 世界进入流程已在执行，忽略重复请求。");
            return false;
        }

        isWorldEntryInProgress = true;
        worldEntryCompletesOnPlayerReady = completeOnPlayerReady;
        worldEntryPresentationMode = presentationMode;
        worldEntryTargetId = targetId ?? string.Empty;
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        Event_PlayerEnterWorld += OnWorldEntryPlayerReady;
        PublishCurrentWorldEntryProgress(title, status, progress, WorldEntryProgressState.Running);
        return true;
    }

    private void ReportWorldEntryProgress(string title, string status, float progress)
    {
        if (!isWorldEntryInProgress)
            return;

        PublishCurrentWorldEntryProgress(title, status, progress, WorldEntryProgressState.Running);
    }

    /// <summary>复用世界加载页显示同场景重生的区块等待，不启动完整的世界进入生命周期。</summary>
    internal bool BeginRespawnLoadingPresentation()
    {
        if (isWorldEntryInProgress || isRespawnLoadingPresentationActive)
            return false;

        isRespawnLoadingPresentationActive = true;
        PublishWorldEntryProgress(new WorldEntryProgressInfo(
            "正在重生",
            "正在加载玩家周围区块…",
            0.05f,
            WorldEntryProgressState.Running));
        return true;
    }

    /// <summary>更新同场景重生的加载页进度。</summary>
    internal void ReportRespawnLoadingProgress(string status, float progress)
    {
        if (!isRespawnLoadingPresentationActive)
            return;

        PublishWorldEntryProgress(new WorldEntryProgressInfo(
            "正在重生",
            status,
            progress,
            WorldEntryProgressState.Running));
    }

    /// <summary>区块与碰撞准备完成后关闭同场景重生加载页。</summary>
    internal void CompleteRespawnLoadingPresentation()
    {
        if (!isRespawnLoadingPresentationActive)
            return;

        isRespawnLoadingPresentationActive = false;
        PublishWorldEntryProgress(new WorldEntryProgressInfo(
            "重生完成",
            "周围区域已经准备完毕。",
            1f,
            WorldEntryProgressState.Completed));
    }

    private void OnWorldEntryPlayerReady(Player player)
    {
        if (!isWorldEntryInProgress)
            return;

        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (!worldEntryCompletesOnPlayerReady)
            return;

        if (worldEntryCompletionCoroutine != null)
            StopCoroutine(worldEntryCompletionCoroutine);
        worldEntryCompletionCoroutine = StartCoroutine(CompleteWorldEntryCoroutine(player));
    }

    /// <summary>
    /// 等待玩家脚下区块与完整可见窗口完成表现绑定后，再结束标准世界加载页。
    /// 后台数据生成结束不代表 Tilemap、碰撞和实体表现已经可以安全展示。
    /// </summary>
    private IEnumerator CompleteWorldEntryCoroutine(Player player)
    {
        ReportWorldEntryProgress("正在进入世界", "正在加载玩家周围区域…", 0.78f);

        // 让本次 Event_PlayerEnterWorld 的其余订阅者先建立玩家区块窗口。
        yield return null;

        if (player == null)
        {
            worldEntryCompletionCoroutine = null;
            FailWorldEntry("玩家实例无效，无法准备可见地图区域。");
            yield break;
        }

        bool managerWarningLogged = false;
        float managerWarningAt = Time.realtimeSinceStartup + 12f;
        while (ChunkMgr.Instance == null)
        {
            if (!managerWarningLogged && Time.realtimeSinceStartup >= managerWarningAt)
            {
                managerWarningLogged = true;
                Debug.LogWarning("[GameManager] 进入存档后超过 12 秒仍找不到 ChunkMgr，继续保持加载页。", this);
            }

            yield return null;
        }

        ChunkMgr chunkManager = ChunkMgr.Instance;
        Vector3 playerPosition = player.transform.position;
        Mod_ChunkLoader chunkLoader = player.GetComponentInChildren<Mod_ChunkLoader>(true);
        if (chunkLoader != null)
        {
            // 与维度切换一致，使用玩家当前相机视距建立完整可见窗口。
            chunkLoader.RefreshChunksForCameraView();
        }
        else
        {
            Debug.LogWarning("[GameManager] 进入世界的玩家缺少 Mod_ChunkLoader，使用默认 3x3 区块窗口。", player);
            chunkManager.RefreshRuntimeWindow(
                playerPosition,
                2,
                3,
                includeLocalPresentation: true,
                prefetchDistance: 3);
        }

        bool centerWarningLogged = false;
        float centerWarningAt = Time.realtimeSinceStartup + 12f;
        float displayedProgress = 0.82f;
        while (!chunkManager.IsRuntimeEntityPresentationReady(playerPosition))
        {
            displayedProgress = MoveWorldEntryLoadingProgress(displayedProgress, 0.9f);
            ReportWorldEntryProgress("正在进入世界", "正在生成玩家所在区块…", displayedProgress);

            if (!centerWarningLogged && Time.realtimeSinceStartup >= centerWarningAt)
            {
                centerWarningLogged = true;
                Debug.LogWarning("[GameManager] 玩家脚下区块表现超过 12 秒，继续保持加载页。", chunkManager);
            }

            yield return null;
        }

        bool windowWarningLogged = false;
        float windowWarningAt = Time.realtimeSinceStartup + 12f;
        while (!chunkManager.AreRuntimeWindowPresentationsReady)
        {
            displayedProgress = MoveWorldEntryLoadingProgress(displayedProgress, 0.98f);
            ReportWorldEntryProgress("正在进入世界", "正在绘制可见地图区块…", displayedProgress);

            if (!windowWarningLogged && Time.realtimeSinceStartup >= windowWarningAt)
            {
                windowWarningLogged = true;
                Debug.LogWarning(
                    $"[GameManager] 可见区块窗口表现超过 12 秒，继续保持加载页：" +
                    $"待表现 {chunkManager.PendingRuntimeChunkPresentationCount}，" +
                    $"仍有后台生成 {chunkManager.HasPendingChunkDataLoads}。",
                    chunkManager);
            }

            yield return null;
        }

        // 碰撞体变更需要在加载页淡出前同步，并等待一次固定帧完成物理收尾。
        Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();

        worldEntryCompletionCoroutine = null;
        CompleteWorldEntry("加载完成", "世界已经准备完毕。");
    }

    /// <summary>平滑推进加载进度，但只由真实就绪条件触发最终完成。</summary>
    private static float MoveWorldEntryLoadingProgress(float current, float target)
    {
        return Mathf.MoveTowards(
            current,
            target,
            Mathf.Max(0.002f, Time.unscaledDeltaTime * 0.08f));
    }

    private void CompleteWorldEntry(string title, string status)
    {
        if (!isWorldEntryInProgress)
            return;

        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (worldEntryCompletionCoroutine != null)
        {
            StopCoroutine(worldEntryCompletionCoroutine);
            worldEntryCompletionCoroutine = null;
        }

        isWorldEntryInProgress = false;
        PublishCurrentWorldEntryProgress(title, status, 1f, WorldEntryProgressState.Completed);
        ResetCurrentWorldEntryContext();
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
        PublishCurrentWorldEntryProgress("加载失败", message, 0f, WorldEntryProgressState.Failed);
        ResetCurrentWorldEntryContext();
    }

    private void PublishCurrentWorldEntryProgress(
        string title,
        string status,
        float progress,
        WorldEntryProgressState state)
    {
        PublishWorldEntryProgress(
            new WorldEntryProgressInfo(
                title,
                status,
                progress,
                state,
                worldEntryPresentationMode,
                worldEntryTargetId));
    }

    private void ResetCurrentWorldEntryContext()
    {
        worldEntryCompletesOnPlayerReady = false;
        worldEntryPresentationMode = WorldEntryPresentationMode.Standard;
        worldEntryTargetId = string.Empty;
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
        isRespawnLoadingPresentationActive = false;
        ResetCurrentWorldEntryContext();
    }
}
