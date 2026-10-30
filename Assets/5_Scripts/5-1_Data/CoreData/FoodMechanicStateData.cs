using MemoryPack;
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 食物扩展机制的通用持久化负载。机制自行序列化 Payload，核心数据层不依赖具体玩法类型。
/// </summary>
[Serializable]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class FoodMechanicStateData
{
    #region 观察者持久化状态

    [MemoryPackOrder(0)] public string StateKey;
    [MemoryPackOrder(1)] public Dictionary<string, string> Data = new Dictionary<string, string>();
    [MemoryPackOrder(2)] public byte[] Payload;
    [MemoryPackOrder(3)] public FoodTimerStateData Timer;

    #endregion

    #region 其他观察者的运行时浮点状态

    [NonSerialized, MemoryPackIgnore]
    private Dictionary<string, float> runtimeFloats;

    public bool TryGetRuntimeFloat(string key, out float value)
    {
        if (runtimeFloats != null && key != null)
            return runtimeFloats.TryGetValue(key, out value);

        value = default;
        return false;
    }

    public void SetRuntimeFloat(string key, float value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        runtimeFloats ??= new Dictionary<string, float>(StringComparer.Ordinal);
        runtimeFloats[key] = value;
    }

    public void RemoveRuntimeFloat(string key)
    {
        if (runtimeFloats == null || key == null)
            return;

        runtimeFloats.Remove(key);
    }

    /// <summary>运行时浮点缓存只在真正持久化时格式化成字符串，避免高频 Tick 创建临时字符串。</summary>
    [MemoryPackOnSerializing]
    private void FlushRuntimeFloats()
    {
        if (runtimeFloats == null || runtimeFloats.Count == 0)
            return;

        Data ??= new Dictionary<string, string>();
        foreach (KeyValuePair<string, float> pair in runtimeFloats)
            Data[pair.Key] = pair.Value.ToString(CultureInfo.InvariantCulture);
    }

    #endregion
}

/// <summary>腐败或融化计时的强类型持久状态，其他观察者继续使用各自的负载。</summary>
[Serializable]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial struct FoodTimerStateData
{
    #region 食物计时数据

    [MemoryPackOrder(0)] public bool Initialized;
    [MemoryPackOrder(1)] public bool IsSpoilage;
    [MemoryPackOrder(2)] public bool Enabled;
    [MemoryPackOrder(3)] public float ElapsedSeconds;
    [MemoryPackOrder(4)] public float IntervalSeconds;
    [MemoryPackOrder(5)] public string TargetItemID;
    [MemoryPackOrder(6)] public float DryingMaxMoisture;
    [MemoryPackOrder(7)] public string DryingTargetItemID;

    #endregion
}
