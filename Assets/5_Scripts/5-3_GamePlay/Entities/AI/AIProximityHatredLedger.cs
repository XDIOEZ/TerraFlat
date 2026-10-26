using System;
using System.Collections.Generic;

/// <summary>按目标身份独立累计连续驻留、受击仇恨和脱战衰减。</summary>
public sealed class AIProximityHatredLedger<TIdentity>
{
    #region 驻留与仇恨记录
    private sealed class TargetRecord
    {
        public float PresenceSeconds;
        public float Hatred;
        public bool Observed;
        public bool ProximityProvoked;
    }

    private readonly Dictionary<TIdentity, TargetRecord> records = new();
    private readonly List<TIdentity> expiredIdentities = new();
    private float presenceThresholdSeconds = 60f;
    private float initialHatred = 120f;
    private float hatredDecayPerSecond = 1f;
    private float hatredPerHit = 30f;
    private float updateDeltaSeconds;
    public int Count => records.Count;

    public void Configure(float thresholdSeconds, float startingHatred, float decayPerSecond, float perHit)
    {
        presenceThresholdSeconds = RequireFiniteNonNegative(thresholdSeconds, nameof(thresholdSeconds));
        initialHatred = RequireFiniteNonNegative(startingHatred, nameof(startingHatred));
        hatredDecayPerSecond = RequireFiniteNonNegative(decayPerSecond, nameof(decayPerSecond));
        hatredPerHit = RequireFiniteNonNegative(perHit, nameof(perHit));
    }

    public void BeginUpdate(float deltaSeconds)
    {
        updateDeltaSeconds = RequireFiniteNonNegative(deltaSeconds, nameof(deltaSeconds));
        foreach (TargetRecord record in records.Values)
        {
            record.Observed = false;
            record.Hatred = Math.Max(0f, record.Hatred - updateDeltaSeconds * hatredDecayPerSecond);
        }
    }

    public void ObserveNearbyPlayer(TIdentity identity)
    {
        TargetRecord record = GetOrCreateRecord(identity);
        if (record.Observed) return;
        record.Observed = true;
        record.PresenceSeconds += updateDeltaSeconds;
        // 同一次连续驻留只激怒一次，仇恨归零后必须离开再重新累计。
        if (!record.ProximityProvoked && record.PresenceSeconds > presenceThresholdSeconds)
        {
            record.ProximityProvoked = true;
            record.Hatred = Math.Max(record.Hatred, initialHatred);
        }
    }

    public void EndUpdate()
    {
        expiredIdentities.Clear();
        foreach (KeyValuePair<TIdentity, TargetRecord> pair in records)
        {
            TargetRecord record = pair.Value;
            if (record.Observed) continue;
            record.PresenceSeconds = 0f;
            record.ProximityProvoked = false;
            if (record.Hatred <= 0f) expiredIdentities.Add(pair.Key);
        }
        for (int index = 0; index < expiredIdentities.Count; index++)
            records.Remove(expiredIdentities[index]);
    }

    public void RecordHit(TIdentity identity)
    {
        TargetRecord record = GetOrCreateRecord(identity);
        record.Hatred = Math.Min(float.MaxValue, record.Hatred + hatredPerHit);
    }

    public bool Contains(TIdentity identity) => records.ContainsKey(identity);
    public float GetHatred(TIdentity identity) => records.TryGetValue(identity, out TargetRecord record) ? record.Hatred : 0f;
    public float GetContinuousPresenceSeconds(TIdentity identity) =>
        records.TryGetValue(identity, out TargetRecord record) ? record.PresenceSeconds : 0f;
    public bool Remove(TIdentity identity) => records.Remove(identity);
    public void Clear()
    {
        records.Clear();
        expiredIdentities.Clear();
        updateDeltaSeconds = 0f;
    }

    public bool TryGetHighestHatredTarget(out TIdentity identity) =>
        TryGetHighestHatredTarget(null, out identity);

    public bool TryGetHighestHatredTarget(Predicate<TIdentity> isEligible, out TIdentity identity)
    {
        identity = default;
        float highestHatred = 0f;
        foreach (KeyValuePair<TIdentity, TargetRecord> pair in records)
        {
            if (pair.Value.Hatred <= highestHatred || isEligible != null && !isEligible(pair.Key)) continue;
            identity = pair.Key;
            highestHatred = pair.Value.Hatred;
        }
        return highestHatred > 0f;
    }

    private TargetRecord GetOrCreateRecord(TIdentity identity)
    {
        if (!records.TryGetValue(identity, out TargetRecord record))
            records.Add(identity, record = new TargetRecord());
        return record;
    }

    private static float RequireFiniteNonNegative(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "时间和仇恨参数必须是非负有限数。");
        return value;
    }
    #endregion
}
