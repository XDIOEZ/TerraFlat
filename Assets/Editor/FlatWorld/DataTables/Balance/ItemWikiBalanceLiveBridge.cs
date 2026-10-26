using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlatWorld.Networking;
using UnityEditor;
using UnityEngine;

/// <summary>ItemWiki 保存后的 Unity 编辑器桥接：只观察 Library 请求，不复制网页保存和数值算法。</summary>
[InitializeOnLoad]
internal static class ItemWikiBalanceLiveBridge
{
    #region 状态与消息

    [Serializable]
    private sealed class BalanceRequest
    {
        public string requestId;
        public string itemId;
        public string fieldPath;
        public float value;
    }

    [Serializable]
    private sealed class BalanceStatus
    {
        public string requestId;
        public string state;
        public string reason;
        public int appliedCount;
        public bool reloadStarted;
    }

    private static readonly string BalanceRoot = Path.Combine(
        Directory.GetParent(Application.dataPath).FullName, "Library", "FlatWorldItemWiki", "balance-live");
    private static readonly string PendingDirectory = Path.Combine(BalanceRoot, "pending");
    private static readonly string StatusDirectory = Path.Combine(BalanceRoot, "statuses");
    private static readonly List<BalanceStatus> WaitingForReload = new();
    private static double nextPoll;
    private static double nextReloadAt;
    private static int reloadAttempts;

    static ItemWikiBalanceLiveBridge()
    {
        EditorApplication.update += ProcessBalanceRequests;
    }

    #endregion

    #region 实时更新

    /// <summary>以低频轮询读事件；文件通过服务端原子创建，处理时不触碰 Unity 游戏时钟。</summary>
    private static void ProcessBalanceRequests()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.3;
        try
        {
            if (Directory.Exists(PendingDirectory))
            {
                foreach (string path in Directory.GetFiles(PendingDirectory, "*.json"))
                    ProcessOneRequest(path);
            }
            if (WaitingForReload.Count > 0 && EditorApplication.timeSinceStartup >= nextReloadAt)
                TryCompleteReload();
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("[ItemWiki 调参桥接] " + exception.Message);
        }
    }

    /// <summary>先做明确可安全更新的活实例原位修改，再合并后续 JSON 目录热重载。</summary>
    private static void ProcessOneRequest(string path)
    {
        BalanceRequest request;
        try
        {
            request = JsonUtility.FromJson<BalanceRequest>(File.ReadAllText(path));
            if (request == null || string.IsNullOrWhiteSpace(request.requestId) ||
                string.IsNullOrWhiteSpace(request.itemId) || string.IsNullOrWhiteSpace(request.fieldPath) ||
                float.IsNaN(request.value) || float.IsInfinity(request.value))
                throw new InvalidDataException("收到无效的数值调整请求");
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning($"[ItemWiki 调参桥接] 忽略无效请求 {Path.GetFileName(path)}: {exception.Message}");
            File.Delete(path);
            return;
        }

        BalanceStatus status = new() { requestId = request.requestId };
        if (!EditorApplication.isPlaying)
        {
            status.state = "blocked";
            status.reason = "Unity 当前不是 Play Mode；下次运行会从新 JSON 加载";
            WriteStatus(status);
        }
        else if (GameNetwork.IsOnline)
        {
            status.state = "blocked";
            status.reason = "当前是联机会话，禁止单边覆盖服务端物品数值";
            WriteStatus(status);
        }
        else
        {
            var entry = new BalanceTuningRecord
            {
                DefinitionId = request.itemId,
                FieldPath = request.fieldPath,
                Value = request.value,
                ValueKind = BalanceTuningValueKind.Number,
                SourceKind = BalanceTuningSourceKind.JsonDefinition
            };
            status.appliedCount = BalanceTuningLivePreview.ApplyNow(entry);
            WaitingForReload.Add(status);
            nextReloadAt = EditorApplication.timeSinceStartup + 0.85;
            reloadAttempts = 0;
        }
        File.Delete(path);
    }

    /// <summary>多次快速点击只发起一次资源会话热重载；失败时限制重试，不阻塞 Editor。</summary>
    private static void TryCompleteReload()
    {
        bool started = false;
        GameRes resources = GameRes.ExistingInstance;
        if (EditorApplication.isPlaying && !GameNetwork.IsOnline && resources != null &&
            resources.isLoadFinish)
        {
            started = resources.RequestResourceReload();
        }
        if (!started && EditorApplication.isPlaying && !GameNetwork.IsOnline && reloadAttempts++ < 6)
        {
            nextReloadAt = EditorApplication.timeSinceStartup + 1d;
            return;
        }
        foreach (BalanceStatus status in WaitingForReload)
        {
            status.state = "applied";
            status.reloadStarted = started;
            if (!started)
                status.reason = "当前资源会话未接受热重载；已加载实例按能力直接更新，后续实例需重新加载资源";
            WriteStatus(status);
        }
        WaitingForReload.Clear();
        reloadAttempts = 0;
    }

    /// <summary>状态文件只供本机 ItemWiki 查询，公开服务永远不向浏览器返回。</summary>
    private static void WriteStatus(BalanceStatus status)
    {
        Directory.CreateDirectory(StatusDirectory);
        string target = Path.Combine(StatusDirectory, status.requestId + ".json");
        string temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(status));
        if (File.Exists(target)) File.Delete(target);
        File.Move(temporary, target);
    }

    #endregion

    #region 菜单

    /// <summary>不再保留独立调参窗口，编辑入口统一是 ItemWiki 物品详情页。</summary>
    [MenuItem("FlatWorld/物品百科/打开 ItemWiki")]
    private static void OpenItemWiki()
    {
        string wikiFolder = Path.Combine(Application.dataPath, "StreamingAssets", "ItemWiki");
        string starter = Path.Combine(wikiFolder, "打开物品Wiki.cmd");
        if (!File.Exists(starter))
        {
            EditorUtility.DisplayDialog("物品百科", "无法找到 ItemWiki 启动脚本。", "确定");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(starter)
            {
                UseShellExecute = true,
                WorkingDirectory = wikiFolder
            });
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("[物品百科] 无法打开 ItemWiki：" + exception.Message);
        }
    }

    #endregion
}
