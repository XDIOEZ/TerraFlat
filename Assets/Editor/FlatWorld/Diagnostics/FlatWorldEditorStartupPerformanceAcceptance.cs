using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>普通启动场景直接验收完整资源加载，不启用 Lua 缓存或 Player 构建。</summary>
public static class FlatWorldEditorStartupPerformanceAcceptance
{
    #region 编辑器完整资源计时

    private const string SessionKey = "FlatWorld.EditorStartupAcceptance";
    private const string ScenePath = "Assets/3_Scenes/GameStartScene.unity";
    private static long resourceStarted;
    public static string ReportPath => SessionState.GetString(SessionKey + ".report", string.Empty);

    [InitializeOnLoadMethod]
    private static void RestoreAcceptanceAfterDomainReload()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        GameRes.StartupResourcesReady -= RequestCompleteWorldResources;
        GameRes.StartupResourcesReady += RequestCompleteWorldResources;
        EditorApplication.update -= ObserveCompleteResourceReadiness;
        EditorApplication.update += ObserveCompleteResourceReadiness;
        EditorApplication.playModeStateChanged -= CancelAcceptanceWhenPlayModeExits;
        EditorApplication.playModeStateChanged += CancelAcceptanceWhenPlayModeExits;
    }

    [MenuItem("FlatWorld/测试/普通 Editor 启动十秒验收")]
    public static string BeginNormalEditorStartupAcceptance()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("当前正在运行，请先结束自己的游玩再开始启动验收。");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty) throw new InvalidOperationException("请先保存当前场景，再开始启动验收。");
            EditorSceneManager.OpenScene(ScenePath);
        }
        string path = "Library/FlatWorldEditorStartupAcceptance/" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json";
        SessionState.SetString(SessionKey + ".report", path);
        SessionState.SetBool(SessionKey, true);
        resourceStarted = 0;
        try
        {
            RestoreAcceptanceAfterDomainReload();
            EditorApplication.EnterPlaymode();
        }
        catch { ReleaseAcceptanceSubscriptions(); throw; }
        return path;
    }

    private static void RequestCompleteWorldResources(GameRes resources)
    {
        // 从真实菜单资源耗时反推起点，入世请求在同一通知中排队，计时包含两个阶段之间的等待。
        resourceStarted = Stopwatch.GetTimestamp() - (long)(resources.LastStartupLoadMilliseconds * Stopwatch.Frequency / 1000d);
        if (!resources.RequestWorldResources())
            FinishAcceptance(resources, "菜单就绪后没有接受世界资源请求。");
    }

    private static void CancelAcceptanceWhenPlayModeExits(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        ReleaseAcceptanceSubscriptions();
    }

    private static void ReleaseAcceptanceSubscriptions()
    {
        SessionState.SetBool(SessionKey, false);
        resourceStarted = 0;
        GameRes.StartupResourcesReady -= RequestCompleteWorldResources;
        EditorApplication.update -= ObserveCompleteResourceReadiness;
        EditorApplication.playModeStateChanged -= CancelAcceptanceWhenPlayModeExits;
    }

    private static void ObserveCompleteResourceReadiness()
    {
        if (!EditorApplication.isPlaying) return;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null) return;
        if (resources.LoadState == ResourceLoadState.Failed)
            FinishAcceptance(resources, resources.LastLoadError);
        else if (resources.isLoadFinish && resourceStarted != 0)
            FinishAcceptance(resources, null);
    }

    private static void FinishAcceptance(GameRes resources, string failure)
    {
        var result = new AcceptanceResult
        {
            milliseconds = resourceStarted == 0 ? 0 : (Stopwatch.GetTimestamp() - resourceStarted) * 1000d / Stopwatch.Frequency,
            menuMilliseconds = resources.LastStartupLoadMilliseconds,
            worldMilliseconds = resources.LastResourceLoadMilliseconds,
            ready = resources.isLoadFinish,
            itemCount = resources.ItemDefinitions.Count,
            actorCount = resources.ActorDefinitions.Count,
            tileCount = resources.TileBlockDict.Count,
            recipeCount = resources.recipeById.Count,
            luaCache = GameplayTestResourceCache.Status,
            error = failure
        };
        result.passed = failure == null && result.ready && result.luaCache == "disabled" && result.milliseconds < 10000 &&
            result.itemCount > 0 && result.actorCount > 0 && result.tileCount > 0 && result.recipeCount > 0;
        ReleaseAcceptanceSubscriptions();
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(result, true));
        string message = $"[Editor启动验收] 完整资源 {result.milliseconds:F1} ms，菜单 {result.menuMilliseconds:F1} ms，" +
            $"物品/Actor {result.itemCount}/{result.actorCount}，地块 {result.tileCount}，配方 {result.recipeCount}，Lua缓存 {result.luaCache}。";
        if (result.passed) UnityEngine.Debug.Log(message);
        else UnityEngine.Debug.LogWarning(message + failure);
    }

    [Serializable]
    private sealed class AcceptanceResult
    {
        public bool passed, ready;
        public double milliseconds, menuMilliseconds, worldMilliseconds;
        public int itemCount, actorCount, tileCount, recipeCount;
        public string luaCache, error;
    }

    #endregion
}
