using System;
using UnityEngine;

/// <summary>电池只记录储能与回充意图，不关心供电来源和工作种类。</summary>
[Serializable]
public sealed class ServitorBatteryState
{
    #region 电量与回充滞回
    public float StoredJoules = 10000f;
    public bool RechargeRequested;
    public float Fraction(float capacity) => Mathf.Clamp01(StoredJoules / capacity);

    public void Consume(float joules, float capacity, float lowFraction)
    {
        StoredJoules = Mathf.Clamp(StoredJoules - Mathf.Max(0f, joules), 0f, capacity);
        if (StoredJoules <= capacity * lowFraction) RechargeRequested = true;
    }

    public float RequestChargingWatts(float seconds, float capacity)
        => seconds > 0f && RechargeRequested
            ? Mathf.Min(capacity * .01f, Mathf.Max(0f, capacity - StoredJoules) / seconds) : 0f;

    public void ReceiveEnergy(float joules, float capacity)
    {
        StoredJoules = Mathf.Clamp(StoredJoules + Mathf.Max(0f, joules), 0f, capacity);
        if (StoredJoules >= capacity - .001f) { StoredJoules = capacity; RechargeRequested = false; }
    }
    #endregion
}
