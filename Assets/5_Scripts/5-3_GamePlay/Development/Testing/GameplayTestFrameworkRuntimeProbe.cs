using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>用跨 Lua 的自有租约验证真实执行器顺序和统一收尾。</summary>
public static class GameplayTestFrameworkRuntimeProbe
{
    #region 操作登记与租约
    private static Lease pending;
    private sealed class Lease : IDisposable
    {
        public readonly string Token = Guid.NewGuid().ToString("N");
        public readonly string Profile;
        public readonly double Started = Time.realtimeSinceStartupAsDouble;
        public readonly int StartedFrame = Time.frameCount;
        public int Samples;
        public bool SyncCleaned;
        public bool AsyncCleaned;
        public Lease(Player player) { Profile = player.ProfileName; }
        public void Dispose() { SyncCleaned = true; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("testing", "begin_lease", Begin);
        GameplayTestRuntimeProbe.Register("testing", "sample_lease", Sample);
        GameplayTestRuntimeProbe.Register("testing", "previous_cleanup", PreviousCleanup);
    }
    private static System.Collections.IEnumerator Cleanup(Lease lease)
    {
        yield return null;
        lease.AsyncCleaned = true;
    }
    #endregion

    #region 实际命令队列与跨用例清理采样
    private static JObject Begin(Player player, JObject args)
    {
        if (pending != null && (!pending.SyncCleaned || !pending.AsyncCleaned))
            throw new InvalidOperationException("上轮测试租约尚未清理。");
        Lease lease = pending = new Lease(player);
        GameplayTestRuntimeProbe.RegisterCleanup(lease);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(() => Cleanup(lease));
        return new JObject { ["token"] = lease.Token, ["profile"] = lease.Profile,
            ["cleaned"] = lease.SyncCleaned, ["asyncCleaned"] = lease.AsyncCleaned, ["controlOwner"] = ControlOwner(player) };
    }
    private static string ControlOwner(Player player) => player.GetComponentInChildren<Mod_GameController>(true)?.ExternalGameplayControlOwnerName;
    private static JObject Sample(Player player, JObject args)
    {
        Lease lease = pending ?? throw new InvalidOperationException("没有准备队列测试租约。");
        return new JObject { ["token"] = lease.Token, ["sample"] = ++lease.Samples,
            ["elapsed"] = Time.realtimeSinceStartupAsDouble - lease.Started,
            ["frames"] = Time.frameCount - lease.StartedFrame, ["profilePreserved"] = player.ProfileName == lease.Profile,
            ["cleaned"] = lease.SyncCleaned, ["asyncCleaned"] = lease.AsyncCleaned, ["controlOwner"] = ControlOwner(player) };
    }
    private static JObject PreviousCleanup(Player player, JObject args)
    {
        Lease lease = pending ?? throw new InvalidOperationException("必须先执行队列租约前置 Lua。");
        var result = new JObject { ["token"] = lease.Token, ["samples"] = lease.Samples,
            ["cleaned"] = lease.SyncCleaned, ["asyncCleaned"] = lease.AsyncCleaned,
            ["profilePreserved"] = player.ProfileName == lease.Profile, ["controlOwner"] = ControlOwner(player) };
        if (lease.SyncCleaned && lease.AsyncCleaned) pending = null;
        return result;
    }
    #endregion
}
