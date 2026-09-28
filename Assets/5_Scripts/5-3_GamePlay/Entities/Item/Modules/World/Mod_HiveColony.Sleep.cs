using UnityEngine;

/// <summary>蜂群昼夜作息：日落归巢、巢内卸载睡眠、日出恢复同一成员实例身份。</summary>
public sealed partial class Mod_HiveColony
{
    #region 昼夜睡眠

    private double defenseUntil; // 蜂巢受击后短时间内不让防守成员重新睡回去。

    /// <summary>夜间让清醒成员返巢并推进睡眠消耗；白天恢复所有巢内成员。</summary>
    private void TickSleepCycle(float deltaTime)
    {
        bool dayTime = IsDayTime();
        if (dayTime)
        {
            WakeSleepingResidents(null);
            IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
            foreach (int guid in residents)
                backend?.TrySetHiveActorDirective(guid, false, 0f, default, false);
            return;
        }

        AdvanceSleepingResidents(deltaTime);
        if (TryGetWorldTime(out double now, out _) && now < defenseUntil)
            return;
        IAiEcologyBackend ecology = AiRuntimeBackendService.Ecology;
        for (int index = 0; index < state.Residents.Count; index++)
        {
            ResidentState member = state.Residents[index];
            if (member.SleepingInHive || !residents.Contains(member.Guid) || ecology == null ||
                !ecology.TryGetActor(member.Guid, out Vector3 position, out bool alive) || !alive)
                continue;
            ecology.TrySetHiveActorDirective(member.Guid, true, 0f, default, false);
            const float arrivalRadius = 0.45f;
            if (WorldTopologyRuntime.SqrDistance(position, HomePosition) > arrivalRadius * arrivalRadius)
                continue;

            if (ecology.TryGetHiveActorState(member.Guid, out AiHiveActorState live))
            {
                member.Bee.Satiety = live.Satiety;
                member.Bee.Anger = live.Anger;
                member.Bee.Angry = live.Angry;
                member.Bee.ReturningHome = live.ReturningHome;
            }
            member.SleepDrainPerSecond = 1f;
            member.SleepingInHive = true;
            residents.Remove(member.Guid);
            ecology.TryDespawnActor(member.Guid);
        }
    }

    /// <summary>睡眠中的蜜蜂不保留 GameObject，仅以正常消耗的一半推进饱食度。</summary>
    private void AdvanceSleepingResidents(float deltaTime)
    {
        float step = Mathf.Max(0f, deltaTime) * SleepingSatietyDrainMultiplier;
        if (step <= 0f)
            return;

        for (int index = 0; index < state.Residents.Count; index++)
        {
            ResidentState member = state.Residents[index];
            if (!member.SleepingInHive)
                continue;
            member.Bee.Satiety = Mathf.Max(
                0f,
                member.Bee.Satiety - Mathf.Max(0f, member.SleepDrainPerSecond) * step);
        }
    }

    /// <summary>日出或蜂巢受击时恢复巢内成员；护巢目标存在时立即覆盖普通睡眠行为。</summary>
    private void WakeSleepingResidents(Item defenseTarget)
    {
        bool nightTime = !IsDayTime();
        IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
        for (int index = 0; index < state.Residents.Count; index++)
        {
            ResidentState member = state.Residents[index];
            if (!member.SleepingInHive)
                continue;

            if (!SpawnResident(index, member))
                continue;
            member.SleepingInHive = false;
            backend?.TrySetHiveActorDirective(member.Guid, nightTime, defenseTarget != null ? 1f : 0f,
                defenseTarget != null ? (Vector2)defenseTarget.transform.position : default,
                defenseTarget != null);
        }
    }

    /// <summary>蜂巢受击专用唤醒入口。</summary>
    private void WakeSleepingResidentsForDefense(Item attacker)
    {
        if (TryGetWorldTime(out double now, out _))
            defenseUntil = now + 10d;
        WakeSleepingResidents(attacker);
    }

    /// <summary>当前场景六点到十八点视作活动时段；时钟不可用时保守保持清醒。</summary>
    private bool IsDayTime()
    {
        DayTimeSystem clock = DayTimeSystem.Instance;
        if (clock == null || !clock.TryGetResolvedTimeData(item.gameObject.scene.name, out _, out TimeData time) ||
            time == null)
            return true;

        float dayLength = Mathf.Max(1f, time.DayLength);
        float normalized = Mathf.Repeat(time.CurrentTime, dayLength) / dayLength;
        return normalized >= DayStartRatio && normalized <= DayEndRatio;
    }

    /// <summary>调试显示所需的巢内睡眠成员数量。</summary>
    private int CountSleepingResidents()
    {
        int count = 0;
        for (int index = 0; index < state.Residents.Count; index++)
            if (state.Residents[index].SleepingInHive)
                count++;
        return count;
    }

    #endregion
}
