using UnityEngine;

/// <summary>蜂群昼夜作息：日落归巢、巢内卸载睡眠、日出恢复同一成员实例身份。</summary>
public sealed partial class Mod_HiveColony
{
    #region 昼夜睡眠

    /// <summary>夜间让清醒成员返巢并推进睡眠消耗；白天恢复所有巢内成员。</summary>
    private void TickSleepCycle(float deltaTime)
    {
        bool dayTime = IsDayTime();
        if (dayTime)
        {
            WakeSleepingResidents(null);
            foreach (Item resident in residents.Values)
            {
                Mod_BeeBehavior bee = resident?.itemMods?.GetMod_ByID<Mod_BeeBehavior>(Mod_BeeBehavior.ModuleId);
                bee?.SetNightSleepRequested(false);
            }
            return;
        }

        AdvanceSleepingResidents(deltaTime);
        ItemMgr manager = itemManager;
        if (manager == null)
            return;
        for (int index = 0; index < state.Residents.Count; index++)
        {
            ResidentState member = state.Residents[index];
            if (member.SleepingInHive || !residents.TryGetValue(member.Guid, out Item resident) || resident == null)
                continue;

            Mod_BeeBehavior bee = resident.itemMods?.GetMod_ByID<Mod_BeeBehavior>(Mod_BeeBehavior.ModuleId);
            if (bee == null)
                continue;
            bee.SetNightSleepRequested(true);

            float arrivalRadius = bee.HomeArrivalRadius;
            if (WorldTopologyRuntime.SqrDistance(resident.transform.position, HomePosition) >
                arrivalRadius * arrivalRadius)
                continue;

            member.Bee = bee.CaptureState();
            member.SleepDrainPerSecond = Mathf.Max(0f, bee.SatietyDrainRate);
            member.SleepingInHive = true;
            residents.Remove(member.Guid);
            if (!resident.DestructionHandled)
                manager.DespawnItem(resident, saveData: false);
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
            if (!member.SleepingInHive || member.Bee == null)
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
        for (int index = 0; index < state.Residents.Count; index++)
        {
            ResidentState member = state.Residents[index];
            if (!member.SleepingInHive)
                continue;

            member.SleepingInHive = false;
            SpawnResident(index, member);
            if (!residents.TryGetValue(member.Guid, out Item resident) || resident == null)
                continue;
            Mod_BeeBehavior bee = resident.itemMods?.GetMod_ByID<Mod_BeeBehavior>(Mod_BeeBehavior.ModuleId);
            if (bee == null)
                continue;
            bee.SetNightSleepRequested(nightTime);
            if (defenseTarget != null)
                bee.ForceHiveDefenseTarget(defenseTarget);
        }
    }

    /// <summary>蜂巢受击专用唤醒入口。</summary>
    private void WakeSleepingResidentsForDefense(Item attacker) => WakeSleepingResidents(attacker);

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
