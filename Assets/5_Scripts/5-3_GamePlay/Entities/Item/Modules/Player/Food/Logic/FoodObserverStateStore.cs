using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>规则状态的通用读写工具；每条规则用自己的 StateKey 隔离数据。</summary>
public static class FoodObserverStateStore
{
    public const string SpoilageStateKey = ModData_FoodData.SpoilageStateKey;
    public const string MeltingStateKey = ModData_FoodData.MeltingStateKey;
    public const string ConsumptionStateKey = "food.consumption";

    private static readonly string[] SpoilageFields =
    {
        "EnableSpoilage", "SpoilageElapsedSeconds", "SpoilageIntervalSeconds", "SpoilageTargetItemID",
        "DryingMaxMoisture", "DryingTargetItemID"
    };
    private static readonly string[] MeltingFields =
    {
        "EnableMelting", "MeltingElapsedSeconds", "MeltingIntervalSeconds"
    };

    public static FoodMechanicStateData Find(ModData_FoodData data, string key)
    {
        if (key == SpoilageStateKey) return data?.FindSpoilageState();
        if (key == MeltingStateKey) return data?.FindMeltingState();
        List<FoodMechanicStateData> states = data?.MechanicStates;
        if (states == null || key == null)
            return null;

        // 高频库存 Tick 禁止 List.Find 闭包；直接遍历不会为每次查找分配委托/闭包对象。
        for (int i = 0; i < states.Count; i++)
        {
            FoodMechanicStateData item = states[i];
            if (item != null && string.Equals(item.StateKey, key, StringComparison.Ordinal))
                return item;
        }

        return null;
    }

    public static FoodMechanicStateData GetOrCreate(ModData_FoodData data, string key)
    {
        if (data == null || string.IsNullOrWhiteSpace(key))
            return null;

        data.MechanicStates ??= new List<FoodMechanicStateData>();
        FoodMechanicStateData state = Find(data, key);
        if (state != null)
        {
            state.Data ??= new Dictionary<string, string>();
            return state;
        }

        state = new FoodMechanicStateData
        {
            StateKey = key,
            Data = new Dictionary<string, string>()
        };
        data.MechanicStates.Add(state);
        return state;
    }

    public static string ReadString(FoodMechanicStateData state, string key, string fallback = null)
    {
        if (state != null && TimerMatchesStateKey(state) && TryReadTimerString(state, key, out string timerValue))
            return timerValue ?? fallback;
        return state?.Data != null && state.Data.TryGetValue(key, out string value)
            ? value ?? fallback
            : fallback;
    }

    public static float ReadFloat(FoodMechanicStateData state, string key, float fallback)
    {
        if (state == null || string.IsNullOrWhiteSpace(key))
            return fallback;
        if (TimerMatchesStateKey(state) && TryReadTimerString(state, key, out string timerValue))
            return float.TryParse(timerValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed : fallback;
        if (state.TryGetRuntimeFloat(key, out float cached))
            return cached;

        string raw = state.Data != null && state.Data.TryGetValue(key, out string stored)
            ? stored
            : null;
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return fallback;

        if (Application.isPlaying)
            state.SetRuntimeFloat(key, value);
        return value;
    }

    public static bool ReadBool(FoodMechanicStateData state, string key, bool fallback)
    {
        return bool.TryParse(ReadString(state, key), out bool value) ? value : fallback;
    }

    public static void WriteString(FoodMechanicStateData state, string key, string value)
    {
        if (state == null || string.IsNullOrWhiteSpace(key))
            return;

        if (state.Timer.Initialized && IsTimerField(state, key))
            RestoreTimerFieldsToGenericData(state);

        state.RemoveRuntimeFloat(key);
        state.Data ??= new Dictionary<string, string>();
        state.Data[key] = value ?? string.Empty;
    }

    public static void WriteFloat(FoodMechanicStateData state, string key, float value)
    {
        if (state == null || string.IsNullOrWhiteSpace(key))
            return;

        if (state.Timer.Initialized && IsTimerField(state, key))
            RestoreTimerFieldsToGenericData(state);

        if (Application.isPlaying)
        {
            state.SetRuntimeFloat(key, value);
            return;
        }

        WriteString(state, key, value.ToString(CultureInfo.InvariantCulture));
    }

    public static void WriteBool(FoodMechanicStateData state, string key, bool value)
    {
        WriteString(state, key, value.ToString());
    }

    #region 强类型食物计时状态

    // 查询本身不改写配置，首次参与模拟时转成强类型状态。
    public static FoodTimerStateData ReadSpoilageTimer(FoodMechanicStateData state)
    {
        if (state != null && state.Timer.Initialized && state.Timer.IsSpoilage) return state.Timer;
        return new FoodTimerStateData
        {
            Initialized = true,
            IsSpoilage = true,
            Enabled = ReadBool(state, "EnableSpoilage", true),
            ElapsedSeconds = ReadFloat(state, "SpoilageElapsedSeconds", 0f),
            IntervalSeconds = ReadFloat(state, "SpoilageIntervalSeconds", 1800f),
            TargetItemID = ReadString(state, "SpoilageTargetItemID", "Meat_Rotten"),
            DryingMaxMoisture = ReadFloat(state, "DryingMaxMoisture", 0.2f),
            DryingTargetItemID = ReadString(state, "DryingTargetItemID")
        };
    }

    public static FoodTimerStateData ReadMeltingTimer(FoodMechanicStateData state)
    {
        if (state != null && state.Timer.Initialized && !state.Timer.IsSpoilage) return state.Timer;
        return new FoodTimerStateData
        {
            Initialized = true,
            Enabled = ReadBool(state, "EnableMelting", false),
            ElapsedSeconds = ReadFloat(state, "MeltingElapsedSeconds", 0f),
            IntervalSeconds = ReadFloat(state, "MeltingIntervalSeconds", 300f)
        };
    }

    public static void WriteSpoilageTimer(FoodMechanicStateData state, FoodTimerStateData timer)
    {
        if (state == null) return;
        bool initialized = state.Timer.Initialized && state.Timer.IsSpoilage;
        timer.Initialized = true;
        timer.IsSpoilage = true;
        state.Timer = timer;
        if (!initialized) RemoveGenericTimerFields(state, SpoilageFields);
    }

    public static void WriteMeltingTimer(FoodMechanicStateData state, FoodTimerStateData timer)
    {
        if (state == null) return;
        bool initialized = state.Timer.Initialized && !state.Timer.IsSpoilage;
        timer.Initialized = true;
        timer.IsSpoilage = false;
        state.Timer = timer;
        if (!initialized) RemoveGenericTimerFields(state, MeltingFields);
    }

    public static void InitializeSpoilageTimer(FoodMechanicStateData state)
    {
        if (state != null && (!state.Timer.Initialized || !state.Timer.IsSpoilage))
            WriteSpoilageTimer(state, ReadSpoilageTimer(state));
    }

    public static void InitializeMeltingTimer(FoodMechanicStateData state)
    {
        if (state != null && (!state.Timer.Initialized || state.Timer.IsSpoilage))
            WriteMeltingTimer(state, ReadMeltingTimer(state));
    }

    public static void WriteSpoilageElapsedSeconds(ModData_FoodData data, float elapsedSeconds)
    {
        FoodMechanicStateData state = data?.FindSpoilageState() ?? GetOrCreate(data, SpoilageStateKey);
        if (state == null) return;
        InitializeSpoilageTimer(state);
        state.Timer.ElapsedSeconds = elapsedSeconds;
    }

    public static void WriteMeltingElapsedSeconds(ModData_FoodData data, float elapsedSeconds)
    {
        FoodMechanicStateData state = data?.FindMeltingState() ?? GetOrCreate(data, MeltingStateKey);
        if (state == null) return;
        InitializeMeltingTimer(state);
        state.Timer.ElapsedSeconds = elapsedSeconds;
    }

    private static void RemoveGenericTimerFields(FoodMechanicStateData state, string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            state.Data?.Remove(fields[i]);
            state.RemoveRuntimeFloat(fields[i]);
        }
    }

    private static bool TimerMatchesStateKey(FoodMechanicStateData state) => state.Timer.Initialized &&
        (state.Timer.IsSpoilage ? state.StateKey == SpoilageStateKey : state.StateKey == MeltingStateKey);

    private static bool TryReadTimerString(FoodMechanicStateData state, string key, out string value)
    {
        if ((state.StateKey == SpoilageStateKey && key == "EnableSpoilage") ||
            (state.StateKey == MeltingStateKey && key == "EnableMelting")) value = state.Timer.Enabled.ToString();
        else if ((state.StateKey == SpoilageStateKey && key == "SpoilageElapsedSeconds") ||
                 (state.StateKey == MeltingStateKey && key == "MeltingElapsedSeconds"))
            value = state.Timer.ElapsedSeconds.ToString(CultureInfo.InvariantCulture);
        else if ((state.StateKey == SpoilageStateKey && key == "SpoilageIntervalSeconds") ||
                 (state.StateKey == MeltingStateKey && key == "MeltingIntervalSeconds"))
            value = state.Timer.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
        else if (state.StateKey == SpoilageStateKey && key == "DryingMaxMoisture")
            value = state.Timer.DryingMaxMoisture.ToString(CultureInfo.InvariantCulture);
        else if (state.StateKey == SpoilageStateKey && key == "SpoilageTargetItemID") value = state.Timer.TargetItemID;
        else if (state.StateKey == SpoilageStateKey && key == "DryingTargetItemID") value = state.Timer.DryingTargetItemID;
        else { value = null; return false; }
        return true;
    }

    private static bool IsTimerField(FoodMechanicStateData state, string key)
    {
        string[] fields = state.StateKey == SpoilageStateKey ? SpoilageFields :
            state.StateKey == MeltingStateKey ? MeltingFields : null;
        if (fields != null)
            for (int i = 0; i < fields.Length; i++)
                if (fields[i] == key) return true;
        return false;
    }

    // 通用字符串写入保留原有语义，下一次计时再转换回唯一的强类型状态。
    private static void RestoreTimerFieldsToGenericData(FoodMechanicStateData state)
    {
        FoodTimerStateData timer = state.Timer;
        state.Data ??= new Dictionary<string, string>();
        bool spoilage = timer.IsSpoilage;
        state.Data[spoilage ? "EnableSpoilage" : "EnableMelting"] = timer.Enabled.ToString();
        state.Data[spoilage ? "SpoilageElapsedSeconds" : "MeltingElapsedSeconds"] = timer.ElapsedSeconds.ToString(CultureInfo.InvariantCulture);
        state.Data[spoilage ? "SpoilageIntervalSeconds" : "MeltingIntervalSeconds"] = timer.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
        if (spoilage)
        {
            state.Data["SpoilageTargetItemID"] = timer.TargetItemID;
            state.Data["DryingMaxMoisture"] = timer.DryingMaxMoisture.ToString(CultureInfo.InvariantCulture);
            state.Data["DryingTargetItemID"] = timer.DryingTargetItemID;
        }
        state.Timer = default;
    }

    #endregion
}
