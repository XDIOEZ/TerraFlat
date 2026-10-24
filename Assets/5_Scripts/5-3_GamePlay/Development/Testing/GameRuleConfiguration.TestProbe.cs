using System;
using Newtonsoft.Json.Linq;

public static partial class GameRuleConfiguration
{
    #region 隔离业务配置采样

    // 在同步探针内替换静态容器，结束后原样恢复，不通知或改写正在使用的配置来源。
    internal static JObject ProbeRulePipeline(JObject args)
    {
        GameRuleValues savedBase = baseValues;
        var savedModifiers = modifiers;
        GameRuleSnapshot savedCurrent = current;
        long savedOrder = nextOrder, savedVersion = Version;
        bool savedBuilding = building, savedNotifying = notifying;
        var savedChanged = Changed;
        var savedFailures = NotificationFailed;
        ResetRuntimeState();
        try
        {
            return ((string)args?["mode"] ?? "compose") switch
            {
                "compose" => ProbeRuleComposition(),
                "rollback" => ProbeRuleRollback(),
                "notification" => ProbeRuleNotification(),
                "session" => ProbeRuleSession(),
                _ => throw new ArgumentException("未知业务规则采样模式。")
            };
        }
        finally
        {
            baseValues = savedBase;
            modifiers = savedModifiers;
            current = savedCurrent;
            nextOrder = savedOrder;
            Version = savedVersion;
            building = savedBuilding;
            notifying = savedNotifying;
            Changed = savedChanged;
            NotificationFailed = savedFailures;
        }
    }

    private static JObject SampleRuleValues(GameRuleSnapshot snapshot) => new()
    {
        ["loot"] = snapshot.World.LootAmountMultiplier,
        ["fuel"] = snapshot.Production.FuelConsumptionMultiplier,
        ["growth"] = snapshot.Production.CropGrowthMultiplier,
        ["time"] = snapshot.World.TimeSpeedMultiplier,
        ["hunger"] = snapshot.PlayerSurvival.HungerDrainMultiplier,
        ["deathDrop"] = snapshot.PlayerDeath.DropAllCarriedItems
    };

    private static JObject ProbeRuleComposition()
    {
        var baseline = new GameRuleValues
        {
            LootAmountMultiplier = 2f, FuelConsumptionMultiplier = .5f, TimeSpeedMultiplier = 1.25f
        };
        SetBaseValues(baseline);
        baseline.LootAmountMultiplier = 999f;
        var first = new object();
        var second = new object();
        GameRuleValues escapedCandidate = null;
        Action<GameRuleValues> firstModifier = values =>
        {
            values.LootAmountMultiplier *= 1.5f;
            values.FuelConsumptionMultiplier *= 2f;
            values.TimeSpeedMultiplier *= 2f;
            values.CropGrowthMultiplier *= 2f;
            values.DropAllCarriedItems = true;
        };
        SetModifier(first, firstModifier, -20);
        SetModifier(second, values =>
        {
            escapedCandidate = values;
            values.LootAmountMultiplier *= 4f;
            values.FuelConsumptionMultiplier *= .25f;
            values.CropGrowthMultiplier = 6f;
            values.DropAllCarriedItems = false;
        }, 10);
        GameRuleSnapshot retained = Current;
        var composed = SampleRuleValues(retained);
        escapedCandidate.LootAmountMultiplier = 1234f;
        var immutable = SampleRuleValues(retained);
        SetModifier(first, firstModifier, -20);
        var replaced = SampleRuleValues(Current);
        RemoveModifier(second);
        var removedSecond = SampleRuleValues(Current);
        RemoveModifier(first);
        var original = SampleRuleValues(Current);
        long beforeMissing = Version;
        bool removedMissing = RemoveModifier(second);
        bool missingPreservedVersion = beforeMissing == Version;
        SetModifier(first, values => values.DropAllCarriedItems = true);
        SetModifier(second, values => values.DropAllCarriedItems = false);
        SetModifier(first, values => values.DropAllCarriedItems = true);
        bool stableOrder = Current.PlayerDeath.DropAllCarriedItems;
        ResetSession();
        return new JObject
        {
            ["composed"] = composed, ["immutable"] = immutable, ["replaced"] = replaced,
            ["removedSecond"] = removedSecond, ["original"] = original,
            ["reset"] = SampleRuleValues(Current), ["removedMissing"] = removedMissing,
            ["missingPreservedVersion"] = missingPreservedVersion, ["stableOrderDrop"] = stableOrder
        };
    }

    private static JObject ProbeRuleRollback()
    {
        var source = new object();
        var extra = new object();
        SetModifier(source, values => values.LootAmountMultiplier *= 2f);
        GameRuleSnapshot before = Current;
        long version = Version;
        string callbackFailure = null;
        try
        {
            SetModifier(source, values =>
            {
                values.LootAmountMultiplier = 999f;
                throw new InvalidOperationException("规则来源候选失败夹具");
            });
        }
        catch (InvalidOperationException exception) { callbackFailure = exception.GetType().Name; }
        bool callbackKeptSnapshot = ReferenceEquals(before, Current) && Version == version;
        SetModifier(extra, values => values.FuelConsumptionMultiplier *= 3f);
        var afterFailureRebuild = SampleRuleValues(Current);
        var invalidFailures = new JArray();
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            before = Current;
            version = Version;
            string failure = null;
            try { SetModifier(extra, values => values.FuelConsumptionMultiplier = invalid); }
            catch (ArgumentOutOfRangeException exception) { failure = exception.GetType().Name; }
            invalidFailures.Add(new JObject
            {
                ["failure"] = failure,
                ["preserved"] = ReferenceEquals(before, Current) && Version == version
            });
        }
        before = Current;
        version = Version;
        string reentryFailure = null;
        try { SetModifier(extra, values => RemoveModifier(source)); }
        catch (InvalidOperationException exception) { reentryFailure = exception.GetType().Name; }
        bool reentryPreserved = ReferenceEquals(before, Current) && Version == version;
        SetModifier(new object(), values => values.TimeSpeedMultiplier = 8f);
        return new JObject
        {
            ["callbackFailure"] = callbackFailure, ["callbackKeptSnapshot"] = callbackKeptSnapshot,
            ["afterFailureRebuild"] = afterFailureRebuild, ["invalid"] = invalidFailures,
            ["reentryFailure"] = reentryFailure, ["reentryPreserved"] = reentryPreserved,
            ["aboveFour"] = Current.World.TimeSpeedMultiplier
        };
    }

    private static JObject ProbeRuleNotification()
    {
        var failures = new JArray();
        int firstCalled = 0, lastCalled = 0;
        long notifiedVersion = -1;
        GameRuleSnapshot notifiedSnapshot = null;
        NotificationFailed += exception => failures.Add(exception.GetType().Name);
        Changed += _ =>
        {
            firstCalled++;
            throw new InvalidOperationException("规则监听异常夹具");
        };
        Changed += _ => SetModifier(new object(), values => values.LootAmountMultiplier = 999f);
        Changed += snapshot =>
        {
            lastCalled++;
            notifiedVersion = Version;
            notifiedSnapshot = snapshot;
        };
        long before = Version;
        string callerFailure = null;
        try { SetModifier(new object(), values => values.LootAmountMultiplier = 3f); }
        catch (Exception exception) { callerFailure = exception.GetType().Name; }
        return new JObject
        {
            ["failures"] = failures, ["firstCalled"] = firstCalled, ["lastCalled"] = lastCalled,
            ["versionDelta"] = Version - before, ["notifiedVersion"] = notifiedVersion,
            ["currentVersion"] = Version, ["callerFailure"] = callerFailure,
            ["sameSnapshot"] = ReferenceEquals(notifiedSnapshot, Current),
            ["loot"] = Current.World.LootAmountMultiplier
        };
    }

    private static JObject ProbeRuleSession()
    {
        int notifications = 0;
        Changed += _ => notifications++;
        var save = new GameSaveData { Difficulty = GameDifficultyId.Level20 };
        GameWorldRuleSession.Bind(save);
        var first = SampleRuleValues(Current);
        var outside = new object();
        SetModifier(outside, values =>
        {
            values.DropAllCarriedItems = false;
            values.CropGrowthMultiplier *= 2f;
        });
        save.Difficulty = GameDifficultyId.Level0;
        GameDifficultyService.PublishRules(save);
        var switched = SampleRuleValues(Current);
        bool reject = false;
        var rejectingSource = new object();
        SetModifier(rejectingSource, _ =>
        {
            if (reject) throw new InvalidOperationException("后续来源拒绝切换夹具");
        });
        reject = true;
        GameRuleSnapshot beforeFailure = Current;
        long beforeFailureVersion = Version;
        bool presetAccepted = GameDifficultyService.TrySetCurrent(save, GameDifficultyId.Level10, out string presetError);
        bool customAccepted = GameDifficultyService.TrySetCustom(save, new GameDifficultyRuleValues
        {
            HungerDrainMultiplier = 3f, DropAllCarriedItems = true
        }, out string customError);
        var rejected = new JObject
        {
            ["presetAccepted"] = presetAccepted, ["customAccepted"] = customAccepted,
            ["presetError"] = presetError, ["customError"] = customError,
            ["difficulty"] = (int)save.Difficulty, ["customHunger"] = save.CustomHungerDrainMultiplier,
            ["customDrop"] = save.CustomDifficultyDropAllCarriedItems,
            ["sameSnapshot"] = ReferenceEquals(beforeFailure, Current), ["sameVersion"] = beforeFailureVersion == Version
        };
        reject = false;
        RemoveModifier(rejectingSource);
        var next = new GameSaveData { Difficulty = GameDifficultyId.Custom };
        GameDifficultyCatalog.WriteCustomRules(next, new GameDifficultyRuleValues
        {
            HungerDrainMultiplier = 1.7f, CropGrowthMultiplier = 1.5f,
            FuelConsumptionMultiplier = .75f, TimeSpeedMultiplier = 2f, DropAllCarriedItems = true
        });
        int beforeBind = notifications;
        GameWorldRuleSession.Bind(next);
        int bindNotifications = notifications - beforeBind;
        var rebound = SampleRuleValues(Current);
        bool previousSourceRemoved = !RemoveModifier(outside);
        GameWorldRuleSession.Bind(null);
        return new JObject
        {
            ["first"] = first, ["switched"] = switched, ["rebound"] = rebound,
            ["bindNotifications"] = bindNotifications, ["previousSourceRemoved"] = previousSourceRemoved,
            ["ended"] = SampleRuleValues(Current), ["rejected"] = rejected
        };
    }

    #endregion
}
