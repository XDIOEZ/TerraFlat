using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 蜂巢分帧出巢规则

    // 只采正式成员意图、帧配额与完成提交，不实例化蜜蜂或触碰正在玩的世界。
    private static JObject EntityHiveResidentSchedule(JObject args)
    {
        var colony = new Mod_HiveColony.ColonyState();
        int[] generated = { 0, 101, 101, 102, 103, 104 };
        int generatedCount = 0;
        int Generate() => generated[generatedCount++];
        Mod_HiveColony.PrepareInitialResidentIntents(colony, 3, 6, Generate);
        var intended = new JArray();
        foreach (Mod_HiveColony.ResidentState member in colony.Residents)
            intended.Add(new JObject { ["guid"] = member.Guid, ["pending"] = member.NeedsInitialState });
        Mod_HiveColony.PrepareInitialResidentIntents(colony, 3, 9, Generate);
        int preparedCount = colony.Residents.Count;
        int originalHoney = colony.Honey;
        int lastFrame = -1;
        var admissions = new JArray
        {
            Mod_HiveColony.TryReserveResidentSpawnFrame(ref lastFrame, 10, false),
            Mod_HiveColony.TryReserveResidentSpawnFrame(ref lastFrame, 10, false),
            Mod_HiveColony.TryReserveResidentSpawnFrame(ref lastFrame, 10, true),
            Mod_HiveColony.TryReserveResidentSpawnFrame(ref lastFrame, 10, true),
            Mod_HiveColony.TryReserveResidentSpawnFrame(ref lastFrame, 10, false),
            Mod_HiveColony.TryReserveResidentSpawnFrame(ref lastFrame, 11, false)
        };
        // 未成功创建就不调用完成提交，保存和重载必须继续保留同一成员意图。
        colony.Residents[0].SleepingInHive = true;
        var resumed = JsonConvert.DeserializeObject<Mod_HiveColony.ColonyState>(JsonConvert.SerializeObject(colony));
        Mod_HiveColony.PrepareInitialResidentIntents(resumed, 3, 10, Generate);
        bool pendingAfterReload = resumed.Residents[0].NeedsInitialState;
        bool sleepingAfterReload = resumed.Residents[0].SleepingInHive;
        int firstGuidAfterReload = resumed.Residents[0].Guid;
        int resumedCount = resumed.Residents.Count;
        int retryFrame = -1;
        bool failedAttemptAdmitted = Mod_HiveColony.TryReserveResidentSpawnFrame(ref retryFrame, 20, false);
        bool sameFrameRetry = Mod_HiveColony.TryReserveResidentSpawnFrame(ref retryFrame, 20, false);
        bool nextFrameRetry = Mod_HiveColony.TryReserveResidentSpawnFrame(ref retryFrame, 21, false);
        Mod_HiveColony.CompleteResidentSpawn(resumed.Residents[0], new Mod_BeeBehavior.BeeState { Satiety = 321f });
        var completed = JsonConvert.DeserializeObject<Mod_HiveColony.ColonyState>(JsonConvert.SerializeObject(resumed));
        bool completedPending = completed.Residents[0].NeedsInitialState;
        bool completedSleeping = completed.Residents[0].SleepingInHive;
        float completedSatiety = completed.Residents[0].Bee.Satiety;
        bool secondStillPending = completed.Residents[1].NeedsInitialState;
        completed.Residents.RemoveAt(0);
        Mod_HiveColony.PrepareInitialResidentIntents(completed, 3, 10, Generate);
        var survivors = new JArray();
        foreach (Mod_HiveColony.ResidentState member in completed.Residents) survivors.Add(member.Guid);
        return new JObject
        {
            ["intended"] = intended, ["preparedCount"] = preparedCount, ["generatedCount"] = generatedCount,
            ["originalHoney"] = originalHoney, ["admissions"] = admissions,
            ["pendingAfterReload"] = pendingAfterReload, ["sleepingAfterReload"] = sleepingAfterReload,
            ["firstGuidAfterReload"] = firstGuidAfterReload, ["resumedCount"] = resumedCount,
            ["failedAttemptAdmitted"] = failedAttemptAdmitted, ["sameFrameRetry"] = sameFrameRetry,
            ["nextFrameRetry"] = nextFrameRetry, ["completedPending"] = completedPending,
            ["completedSleeping"] = completedSleeping, ["completedSatiety"] = completedSatiety,
            ["secondStillPending"] = secondStillPending, ["survivors"] = survivors,
            ["remainingCount"] = completed.Residents.Count
        };
    }

    #endregion
}
