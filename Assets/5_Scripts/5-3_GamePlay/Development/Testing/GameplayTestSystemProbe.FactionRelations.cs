using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 阵营关系查询规则

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterFactionRelationRules() =>
        Register("combat", "faction_relation_lookup", SampleFactionRelationLookup);

    private static JObject SampleFactionRelationLookup(JObject args)
    {
        var result = new JObject();
        var candidate = new ResourceReloadContext();
        FactionRelationService.ConfigureResourceReload(candidate);
        candidate.InCandidate(() =>
        {
            const string owner = "test:faction_lookup";
            const string alpha = "test:alpha";
            const string beta = "test:beta";
            result["sameFactionFriendly"] = FactionRelationService.GetRelation("TEST:ALPHA", alpha) == FactionRelation.Friendly;
            result["differentFactionHostile"] = FactionRelationService.GetRelation(alpha, beta) == FactionRelation.Hostile;
            result["neutralFactionNeutral"] = FactionRelationService.GetRelation(" NEUTRAL ", beta) == FactionRelation.Neutral;
            result["emptyFactionHostile"] = FactionRelationService.GetRelation("", beta) == FactionRelation.Hostile;

            var allocationControl = new object[32];
            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < allocationControl.Length; i++) allocationControl[i] = new byte[64];
            result["controlAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            int controlSize = 0;
            for (int i = 0; i < allocationControl.Length; i++) controlSize += ((byte[])allocationControl[i]).Length;
            result["controlSize"] = controlSize;
            result["allocationCounterAvailable"] = (long)result["controlAllocatedBytes"] > 0;

            for (int i = 0; i < 16; i++) FactionRelationService.GetRelation(alpha, beta);
            long start = GC.GetAllocatedBytesForCurrentThread();
            int defaultSum = 0;
            for (int i = 0; i < 256; i++) defaultSum += (int)FactionRelationService.GetRelation(alpha, beta);
            result["defaultAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - start;
            result["defaultSum"] = defaultSum;

            FactionRelationService.RegisterExternalRelation(owner, alpha, beta, FactionRelation.Neutral);
            result["registeredBothDirections"] = FactionRelationService.GetRelation(alpha, beta) == FactionRelation.Neutral &&
                FactionRelationService.GetRelation("TEST:BETA", "Test:Alpha") == FactionRelation.Neutral;
            bool ownerConflictRejected = false;
            try { FactionRelationService.RegisterExternalRelation("test:another", beta, alpha, FactionRelation.Hostile); }
            catch (InvalidOperationException) { ownerConflictRejected = true; }
            result["ownerConflictRejected"] = ownerConflictRejected;

            for (int i = 0; i < 16; i++) FactionRelationService.GetRelation("TEST:BETA", alpha);
            start = GC.GetAllocatedBytesForCurrentThread();
            int registeredSum = 0;
            for (int i = 0; i < 256; i++) registeredSum += (int)FactionRelationService.GetRelation("TEST:BETA", alpha);
            result["registeredAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - start;
            result["registeredSum"] = registeredSum;

            FactionRelationService.RegisterExternalRelation(owner, "neutral", alpha, FactionRelation.Hostile);
            result["neutralOverride"] = FactionRelationService.GetRelation(alpha, "NEUTRAL") == FactionRelation.Hostile;
            FactionRelationService.RegisterExternalRelation(owner, beta, alpha, FactionRelation.Friendly);
            result["ownerReplacement"] = FactionRelationService.GetRelation(alpha, beta) == FactionRelation.Friendly;
            FactionRelationService.ClearExternalRelations(owner);
            result["ownerCleared"] = FactionRelationService.GetRelation(alpha, beta) == FactionRelation.Hostile &&
                FactionRelationService.GetRelation(alpha, "neutral") == FactionRelation.Neutral;
        });
        result["candidateIsolated"] = FactionRelationService.GetRelation("test:alpha", "test:beta") == FactionRelation.Hostile;
        return result;
    }

    #endregion
}
