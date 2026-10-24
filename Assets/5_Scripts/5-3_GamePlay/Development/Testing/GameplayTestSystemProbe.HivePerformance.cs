using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

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

    #region 蜜蜂丢失目标后的警戒搜索

    // 固定采样正式搜索状态和目的地，不加载世界，也不向 Lua 开放反射能力。
    private static JObject EntityBeeTargetSearch(JObject args)
    {
        if (Application.isPlaying || ChunkMgr.ExistingInstance != null)
            throw new InvalidOperationException("蜜蜂搜索规则只允许在未加载世界的编辑态执行。");
        var root = new GameObject("[BeeTargetSearch]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(1031);
            var bee = root.AddComponent<Mod_BeeBehavior>();
            var target = root.AddComponent<GameItem>();
            var colony = root.AddComponent<Mod_HiveColony>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(Mod_BeeBehavior);
            T Read<T>(string name) => (T)type.GetField(name, flags).GetValue(bee);
            void Write(string name, object value) => type.GetField(name, flags).SetValue(bee, value);
            object Call(string name, params object[] values) => type.GetMethod(name, flags).Invoke(bee, values);
            var state = new Mod_BeeBehavior.BeeState { Anger = bee.AngerMaximum, Angry = true };
            var lastSeen = new Vector2(12f, 10f);
            Write("state", state);
            Write("colony", colony);
            Write("lockedTarget", target);
            Write("hiveDefenseTarget", target);
            Write("lastSeenPosition", lastSeen);
            Call("BeginLastPositionSearch");
            var result = new JObject
            {
                ["defenseBecameSearch"] = bee.HasActiveAngryPursuit && !bee.HasHiveDefenseTarget &&
                    ReferenceEquals(Read<Item>("lastSeenTarget"), target)
            };
            Vector2 travel = (Vector2)Call("AdvanceLastPositionSearch", new Vector2(10f, 10f), 2f);
            result["travelsToMemory"] = travel == lastSeen;
            result["remainingAfterTravel"] = Read<float>("lostTargetAngerRemaining");
            target.transform.position = new Vector3(55f, 55f);
            Vector2 first = (Vector2)Call("AdvanceLastPositionSearch", lastSeen, 1f);
            result["firstTravelDistance"] = Vector2.Distance(lastSeen, first);
            result["firstSearchRadius"] = WorldTopologyRuntime.ShortestDelta(lastSeen, first).magnitude;
            result["lockRadius"] = bee.AngryLockRadius;
            Vector2 retained = (Vector2)Call("AdvanceLastPositionSearch", lastSeen, .25f);
            result["waypointRetained"] = retained == first;
            Vector2 second = (Vector2)Call("AdvanceLastPositionSearch", first, .25f);
            result["nextTravelDistance"] = WorldTopologyRuntime.ShortestDelta(first, second).magnitude;
            result["memoryFrozen"] = Read<Vector2>("lastSeenPosition") == lastSeen;
            result["stillSearching"] = bee.HasActiveAngryPursuit && state.Angry;
            float remaining = Read<float>("lostTargetAngerRemaining");
            Call("AdvanceLastPositionSearch", first, -1f);
            result["negativeTimeIgnored"] = Read<float>("lostTargetAngerRemaining") == remaining;
            bee.SetNightSleepRequested(true);
            result["nightKeepsSearch"] = bee.HasActiveAngryPursuit &&
                Read<float>("lostTargetAngerRemaining") == remaining;
            result["debugState"] = (string)Call("GetBeeDebugState");
            Call("LockMovingTarget", target);
            result["reacquiredClearsSearch"] = ReferenceEquals(Read<Item>("lockedTarget"), target) &&
                !Read<bool>("searchingLastPosition") && !Read<bool>("hasSearchFlightTarget") &&
                !Read<bool>("reachedLastSeenPosition") && Read<float>("lostTargetAngerRemaining") == 0f &&
                Read<Vector2>("lastSeenPosition") == (Vector2)target.transform.position;
            Call("BeginLastPositionSearch");
            Call("AdvanceLastPositionSearch", lastSeen, bee.LostTargetAngerSeconds);
            result["timeoutClearsSearch"] = !bee.HasActiveAngryPursuit && !state.Angry && state.Anger == 0f &&
                !Read<bool>("hasSearchFlightTarget") && Read<Item>("lastSeenTarget") == null;
            state.Angry = true;
            Write("lockedTarget", target);
            Call("BeginLastPositionSearch");
            Call("AdvanceLastPositionSearch", (Vector2)target.transform.position, .1f);
            Call("ResetBeeCombat");
            result["resetClearsSearch"] = !bee.HasActiveAngryPursuit &&
                !Read<bool>("hasSearchFlightTarget") && !Read<bool>("reachedLastSeenPosition") &&
                Read<float>("lostTargetAngerRemaining") == 0f;
            return result;
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion
}
