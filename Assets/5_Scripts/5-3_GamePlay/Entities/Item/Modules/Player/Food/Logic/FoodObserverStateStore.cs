using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>规则状态的通用读写工具；每条规则用自己的 StateKey 隔离数据。</summary>
public static class FoodObserverStateStore
{
    public const string SpoilageStateKey = "food.spoilage";
    public const string MeltingStateKey = "food.melting";
    public const string ConsumptionStateKey = "food.consumption";

    public static FoodMechanicStateData Find(ModData_FoodData data, string key)
    {
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
        return state?.Data != null && state.Data.TryGetValue(key, out string value)
            ? value ?? fallback
            : fallback;
    }

    public static float ReadFloat(FoodMechanicStateData state, string key, float fallback)
    {
        if (state == null || string.IsNullOrWhiteSpace(key))
            return fallback;
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

        state.RemoveRuntimeFloat(key);
        state.Data ??= new Dictionary<string, string>();
        state.Data[key] = value ?? string.Empty;
    }

    public static void WriteFloat(FoodMechanicStateData state, string key, float value)
    {
        if (state == null || string.IsNullOrWhiteSpace(key))
            return;

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
}
