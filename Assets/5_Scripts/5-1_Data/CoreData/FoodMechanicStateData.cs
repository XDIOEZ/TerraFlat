using MemoryPack;
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 食物扩展机制的通用持久化负载。机制自行序列化 Payload，核心数据层不依赖具体玩法类型。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class FoodMechanicStateData
{
    public string StateKey;
    public Dictionary<string, string> Data = new Dictionary<string, string>();
    public byte[] Payload;

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
}
