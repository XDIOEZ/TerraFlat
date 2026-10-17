#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class GameplayTestBuffRuntimeProbe
{
    #region 正式来源 Buff 身份现场
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("buff", "source_presence", CaptureSourcePresence);
        GameplayTestRuntimeProbe.Register("buff", "bleeding_sources", CaptureBleedingSources);
        GameplayTestRuntimeProbe.Register("buff", "infection", CaptureInfection);
    }

    private sealed class SourceBuffScope : IDisposable
    {
        public const string DefinitionId = "core:night_vision";
        public readonly Mod_BuffManager Manager;
        public readonly string FirstSource = "test:buff_presence:" + Guid.NewGuid().ToString("N") + ":first";
        public readonly string SecondSource = "test:buff_presence:" + Guid.NewGuid().ToString("N") + ":second";
        public readonly int OriginalCount;
        private bool regularAttempted;
        private bool disposed;

        public SourceBuffScope(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            Manager = player.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager) ??
                throw new InvalidOperationException("测试主角没有正式 Buff 管理器。");
            if (Manager.HasBuff(DefinitionId)) throw new InvalidOperationException("来源身份现场需要先清除夜视状态。");
            OriginalCount = Manager.ActiveBuffs.Count;
        }

        public bool AddRegular()
        {
            regularAttempted = true;
            return Manager.AddBuff(DefinitionId);
        }

        public int SourceCount()
        {
            int count = 0;
            foreach (BuffInstance runtime in Manager.ActiveBuffs.Values)
                if (runtime != null && !string.IsNullOrEmpty(runtime.SourceKey) &&
                    Manager.ActiveBuffs.Comparer.Equals(runtime.DefinitionId, DefinitionId)) count++;
            return count;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Manager == null) return;
            // 自有来源逐一归还，某个移除回调失败也继续清理其余临时实例。
            try { Manager.RemoveSourceBuff(FirstSource); }
            finally
            {
                try { Manager.RemoveSourceBuff(SecondSource); }
                finally { if (regularAttempted) Manager.RemoveBuff(DefinitionId); }
            }
        }
    }

    private static JObject CaptureSourcePresence(Player player, JObject args)
    {
        var scope = new SourceBuffScope(player);
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        try
        {
            Mod_BuffManager manager = scope.Manager;
            var result = new JObject
            {
                ["initialAbsent"] = !manager.HasBuff(SourceBuffScope.DefinitionId),
                ["emptyAbsent"] = !manager.HasBuff(null) && !manager.HasBuff(string.Empty) && !manager.HasBuff(" ")
            };
            BuffInstance first = manager.EnsureSourceBuff(SourceBuffScope.DefinitionId, scope.FirstSource);
            result["firstPresent"] = manager.HasBuff(SourceBuffScope.DefinitionId);
            result["sourceCaseMatches"] = manager.HasBuff(SourceBuffScope.DefinitionId.ToUpperInvariant());
            result["sourceOnlyRegularKey"] = manager.ActiveBuffs.ContainsKey(SourceBuffScope.DefinitionId);
            result["firstCount"] = scope.SourceCount();
            result["sameSourceReused"] = ReferenceEquals(first,
                manager.EnsureSourceBuff(SourceBuffScope.DefinitionId, scope.FirstSource));

            BuffInstance second = manager.EnsureSourceBuff(SourceBuffScope.DefinitionId, scope.SecondSource);
            result["twoSourcesPresent"] = manager.HasBuff(SourceBuffScope.DefinitionId);
            result["twoSourcesDistinct"] = !ReferenceEquals(first, second);
            result["twoSourceCount"] = scope.SourceCount();
            manager.RemoveSourceBuff(scope.FirstSource);
            result["afterFirstPresent"] = manager.HasBuff(SourceBuffScope.DefinitionId);
            result["afterFirstCount"] = scope.SourceCount();
            manager.RemoveSourceBuff(scope.SecondSource);
            result["afterAllAbsent"] = !manager.HasBuff(SourceBuffScope.DefinitionId);
            result["afterAllCount"] = scope.SourceCount();

            result["regularAdded"] = scope.AddRegular();
            result["regularPresent"] = manager.HasBuff(SourceBuffScope.DefinitionId);
            result["regularCaseMatches"] = manager.HasBuff(SourceBuffScope.DefinitionId.ToUpperInvariant());
            result["regularKeyPresent"] = manager.ActiveBuffs.ContainsKey(SourceBuffScope.DefinitionId);
            manager.EnsureSourceBuff(SourceBuffScope.DefinitionId, scope.FirstSource);
            result["coexistingSourceCount"] = scope.SourceCount();
            manager.RemoveBuff(SourceBuffScope.DefinitionId);
            result["sourceAfterRegularRemoved"] = manager.HasBuff(SourceBuffScope.DefinitionId);
            result["regularKeyRemoved"] = !manager.ActiveBuffs.ContainsKey(SourceBuffScope.DefinitionId);
            manager.RemoveSourceBuff(scope.FirstSource);
            result["restoredAbsent"] = !manager.HasBuff(SourceBuffScope.DefinitionId);
            result["restoredCount"] = manager.ActiveBuffs.Count == scope.OriginalCount;
            return result;
        }
        finally { scope.Dispose(); }
    }
    #endregion

    #region 常规出血与来源实例独立续期
    private static JObject CaptureBleedingSources(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        // 使用已就绪的正式目录和独立未注册宿主，不修改主角原有创伤。
        var root = new GameObject("GameplayTest Independent Bleeding Sources") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        GameItem receiver = root.AddComponent<GameItem>();
        Mod_BuffManager manager = root.AddComponent<Mod_BuffManager>();
        manager.item = receiver;
        string low = BloodLossBuffIds.GetIdForTier(1), high = BloodLossBuffIds.GetIdForTier(3);
        string source = "test:bleeding:" + Guid.NewGuid().ToString("N");
        try
        {
            BuffInstance sourced = manager.EnsureSourceBuff(high, source);
            float sourceDuration = sourced.RemainingDurationSeconds;
            var result = new JObject
            {
                ["sourceOnlyPresent"] = manager.HasBuff(high),
                ["sourceOnlyRegularAbsent"] = !manager.TryGetBuff(high, out _),
                ["sourceOnlyLowerAccepted"] = manager.ApplyBleedingTier(1)
            };
            result["sourceOnlyLowerRegular"] = manager.TryGetBuff(low, out _);
            result["sourcePreservedAfterLower"] = ReferenceEquals(sourced, manager.EnsureSourceBuff(high, source)) &&
                sourced.RemainingDurationSeconds == sourceDuration;
            result["highRegularApplied"] = manager.ApplyBleedingTier(3);
            manager.TryGetBuff(high, out BuffInstance regular);
            result["regularSourceDistinct"] = regular != null && !ReferenceEquals(regular, sourced);
            result["lowRegularRemoved"] = !manager.TryGetBuff(low, out _);
            result["regularDurationSet"] = manager.TrySetBuffDuration(high, 1f);
            float beforeRefresh = regular?.RemainingDurationSeconds ?? 0f;
            result["lowerRenewsRegular"] = manager.ApplyBleedingTier(1);
            result["sameRegularAfterRenew"] = manager.TryGetBuff(high, out BuffInstance afterRenew) &&
                ReferenceEquals(afterRenew, regular);
            result["regularDurationIncreased"] = regular != null && regular.RemainingDurationSeconds > beforeRefresh;
            result["sourceDurationUnchanged"] = sourced.RemainingDurationSeconds == sourceDuration;
            manager.RemoveBuff(high);
            result["sourceAfterRegularRemoved"] = manager.HasBuff(high) && !manager.TryGetBuff(high, out _);
            manager.RemoveSourceBuff(source);
            result["allRemoved"] = !manager.HasBuff(high) && manager.ActiveBuffs.Count == 0;
            return result;
        }
        finally
        {
            try { manager.ClearAllBuffs(); }
            finally
            {
                try { receiver.PrepareForDespawn(saveData: false); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
    }
    #endregion

    #region 正式感染叠层与自然到期
    private static JObject CaptureInfection(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var scope = new GameplayTestInfectionFixture();
        Mod_BuffManager manager = scope.Manager;
        const string id = InfectionBuffIds.Infection;
        var result = new JObject { ["baseline"] = scope.Snapshot(), ["added"] = manager.AddBuff(id) };
        if (!manager.TryGetBuff(id, out BuffInstance infection))
            throw new InvalidOperationException("正式感染目录未能创建独立宿主状态。");
        result["one"] = scope.Snapshot(infection);
        float beforeDamage = scope.Health.Hp;
        manager.Tick(7f);
        result["beforeStack"] = scope.Snapshot(infection);
        result["secondAdded"] = manager.AddBuff(id);
        result["sameInstance"] = manager.TryGetBuff(id, out BuffInstance same) && ReferenceEquals(infection, same);
        result["two"] = scope.Snapshot(infection);
        manager.Tick(3f);
        result["secondTickLoss"] = beforeDamage - scope.Health.Hp;
        beforeDamage = scope.Health.Hp;
        result["thirdAdded"] = manager.AddBuff(id);
        result["three"] = scope.Snapshot(infection);
        manager.Tick(10f);
        result["thirdTickLoss"] = beforeDamage - scope.Health.Hp;
        manager.Tick(7f);
        result["fourthAdded"] = manager.AddBuff(id);
        result["overflowAdded"] = manager.AddBuff(id, int.MaxValue);
        result["capped"] = scope.Snapshot(infection);
        beforeDamage = scope.Health.Hp;
        manager.Tick(3f);
        result["cappedTickLoss"] = beforeDamage - scope.Health.Hp;
        manager.Tick(296f);
        result["beforeExpiry"] = scope.Snapshot(infection);
        result["presentBeforeExpiry"] = manager.HasBuff(id);
        manager.Tick(1f);
        result["absentAfterExpiry"] = !manager.HasBuff(id) && manager.ActiveBuffs.Count == 0;
        result["expiryCycleLoss"] = beforeDamage - scope.Health.Hp;
        result["expiredRestored"] = scope.Snapshot();

        string firstSource = "test:infection:" + Guid.NewGuid().ToString("N") + ":first";
        string secondSource = "test:infection:" + Guid.NewGuid().ToString("N") + ":second";
        BuffInstance first = manager.EnsureSourceBuff(id, firstSource);
        first.SetStackCount(2);
        BuffInstance second = manager.EnsureSourceBuff(id, secondSource);
        result["sourcesDistinct"] = !ReferenceEquals(first, second);
        result["sourceReuse"] = ReferenceEquals(first, manager.EnsureSourceBuff(id, firstSource));
        result["twoSources"] = scope.Snapshot();
        manager.RemoveSourceBuff(firstSource);
        result["oneSourcePresent"] = manager.HasBuff(id) && manager.ActiveBuffs.Count == 1;
        result["oneSourceLeft"] = scope.Snapshot(second);
        result["regularAddedAlongsideSource"] = manager.AddBuff(id);
        result["sourceAndRegular"] = scope.Snapshot();
        manager.RemoveBuff(id);
        result["sourceSurvivedRegularRemoval"] = manager.HasBuff(id) && manager.ActiveBuffs.Count == 1;
        result["afterRegularRemoval"] = scope.Snapshot(second);
        manager.RemoveSourceBuff(secondSource);
        manager.RemoveSourceBuff(secondSource);
        result["allSourcesRemoved"] = !manager.HasBuff(id) && manager.ActiveBuffs.Count == 0;
        result["sourcesRestored"] = scope.Snapshot();
        return result;
    }
    #endregion
}
#endif
