using System;
using System.IO;
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
        GameplayTestRuntimeProbe.Register("testing", "contract_classification", CaptureContractClassification);
    }
    private static System.Collections.IEnumerator Cleanup(Lease lease)
    {
        yield return null;
        lease.AsyncCleaned = true;
    }
    #endregion

    #region 实际命令队列与跨用例清理采样
    private static JObject CaptureContractClassification(Player player, JObject args)
    {
        // 契约分类只描述覆盖边界，不改变案例启用状态或任何断言。
        var entry = new JObject { ["id"] = "classification", ["name"] = "classification", ["luaPath"] = "GameplayTests/classification.lua" };
        var suite = new JObject { ["schemaVersion"] = 1, ["cases"] = new JArray(entry) };
        string legacy = GameplayTestSuite.Parse(suite.ToString()).Cases[0].ContractKind;
        var kinds = new JArray();
        foreach (string kind in new[] { "behavior", "architecture", "configuration", "mixed" })
        {
            entry["contractKind"] = kind;
            kinds.Add(GameplayTestSuite.Parse(suite.ToString()).Cases[0].ContractKind);
        }
        entry["contractKind"] = "unknown";
        bool rejected = false;
        try { GameplayTestSuite.Parse(suite.ToString()); }
        catch (InvalidDataException) { rejected = true; }
        int noOracleRejected = 0;
        foreach (string source in new[] { "test.log('no oracle')", "test.wait(.01)", "test.check('world_ready')",
            "test.check('mechanical_plant_placed')", "test.check('mechanical_engine_fueled')" })
        {
            try { GameplayTestLuaRunner.ValidateScript(source, "@contract-no-oracle"); }
            catch (InvalidOperationException) { noOracleRejected++; }
        }
        bool resultCommandAccepted = true;
        try { GameplayTestLuaRunner.ValidateScript("test.move_to(1,1,1,.1)", "@contract-move-result"); }
        catch (InvalidOperationException) { resultCommandAccepted = false; }
        bool profilerOracleAccepted = true;
        try { GameplayTestLuaRunner.ValidateScript("test.profiler_start(1);test.profiler_stop(nil,function(data) assert(data) end)", "@contract-profiler-result"); }
        catch (InvalidOperationException) { profilerOracleAccepted = false; }
        return new JObject { ["legacyKind"] = legacy, ["kinds"] = kinds, ["unknownRejected"] = rejected,
            ["noOracleRejected"] = noOracleRejected, ["resultCommandAccepted"] = resultCommandAccepted,
            ["profilerOracleAccepted"] = profilerOracleAccepted };
    }

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
