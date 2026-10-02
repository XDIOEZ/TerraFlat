using System;
using FlatWorld.Networking;
using UnityEngine;

public sealed partial class Mod_AI_Bird
{
    #region 产蛋配置
    public bool enableEggLaying;
    public string eggItemId = string.Empty;
    [Min(2f)] public float layEggMinimumIntervalDays = 2f;
    [Min(0f)] public float layEggRandomDelayDays = 1f;
    #endregion

    #region 权威产蛋
    private void InitializeEggLaying()
    {
        state.EggLaying ??= new AnimalEggLayingSchedule();
        if (!GameNetwork.HasStateAuthority || !enableEggLaying || state.EggLaying.Initialized)
            return;
        if (TryGetEggGameDay(out double currentDay))
            BeginNextEggCycle(currentDay);
    }

    private void TickEggLaying()
    {
        if (!GameNetwork.HasStateAuthority || !enableEggLaying || !IsAlive ||
            string.IsNullOrWhiteSpace(eggItemId) || !TryGetEggGameDay(out double currentDay))
            return;

        // 世界时钟晚于动物装配时，从首次可用时刻开始，而不是生成后立即产蛋。
        if (!state.EggLaying.Initialized)
        {
            BeginNextEggCycle(currentDay);
            return;
        }
        if (state.Phase != BirdFlightPhase.Ground || escapeRemaining > 0f ||
            !state.EggLaying.IsDue(currentDay, layEggMinimumIntervalDays))
            return;
        if (TryGetNearbyThreat(fleeTriggerDistance, out _) || !CanLand(body.position))
            return;

        // 单机与联机都复用世界掉落入口；只有成功生成这一颗后才开始新的完整冷却。
        DroppedItemService.SpawnLoot(eggItemId, body.position, amount: 1f,
            radius: 0f, duration: 0f, destination: body.position);
        BeginNextEggCycle(currentDay);
    }

    private void BeginNextEggCycle(double currentDay)
    {
        state.EggLaying.BeginCycle(currentDay, layEggMinimumIntervalDays,
            layEggRandomDelayDays, UnityEngine.Random.value);
    }

    private bool TryGetEggGameDay(out double currentDay)
    {
        currentDay = 0d;
        DayTimeSystem clock = DayTimeSystem.GetInstance();
        if (clock == null ||
            !clock.TryGetResolvedTimeData(item.gameObject.scene.name, out _, out TimeData time) ||
            time.DayLength <= 0f || float.IsNaN(time.DayLength) || float.IsInfinity(time.DayLength) ||
            float.IsNaN(time.CurrentTime) || float.IsInfinity(time.CurrentTime))
            return false;

        currentDay = Math.Max(0, time.TotalDays) +
            (double)Mathf.Repeat(time.CurrentTime, time.DayLength) / time.DayLength;
        return true;
    }
    #endregion
}
