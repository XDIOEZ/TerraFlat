using System;
using FlatWorld.Gameplay.Progress;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>机器冷快照的命名空间载荷；只保存数据，不序列化领域对象或 Unity 引用。</summary>
public static class MachinePersistence
{
    #region 快照编码
    public const string Namespace = "flatworld.machine";
    public const int Version = 1;
    private const int MaximumPayloadBytes = 8 * 1024 * 1024;

    public static bool Has(ItemData snapshot, string key)
        => ReadDocument(snapshot)?[key]?.Type == JTokenType.String;

    public static T Read<T>(ItemData snapshot, string key)
    {
        JObject document = ReadDocument(snapshot);
        string text = (string)document?[key];
        if (string.IsNullOrEmpty(text)) return default;
        if (text.Length > MaximumPayloadBytes * 4L / 3L + 4)
            throw new InvalidOperationException("机器状态载荷过大：" + key);
        byte[] bytes = Convert.FromBase64String(text);
        if (bytes.Length > MaximumPayloadBytes)
            throw new InvalidOperationException("机器状态载荷过大：" + key);
        return MemoryPackSerializer.Deserialize<T>(bytes);
    }

    public static void Write<T>(ItemData snapshot, string key, T value)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (string.IsNullOrWhiteSpace(key) || key == "version") throw new ArgumentException("机器载荷键无效。", nameof(key));
        JObject root = ItemSpecialDataJsonStore.ReadRoot(snapshot.ItemSpecialData);
        JObject document = ReadDocument(snapshot) ?? new JObject { ["version"] = Version };
        byte[] bytes = MemoryPackSerializer.Serialize(value);
        if (bytes.Length > MaximumPayloadBytes) throw new InvalidOperationException("机器状态载荷过大：" + key);
        document[key] = Convert.ToBase64String(bytes);
        root[Namespace] = document;
        snapshot.ItemSpecialData = root.ToString(Formatting.None);
    }

    public static T Clone<T>(T value)
        => MemoryPackSerializer.Deserialize<T>(MemoryPackSerializer.Serialize(value));

    public static bool HasCustomState(ItemData snapshot)
    {
        JObject root = ItemSpecialDataJsonStore.ReadRoot(snapshot?.ItemSpecialData);
        foreach (JProperty property in root.Properties())
        {
            if (property.Name != Namespace) return true;
            if (property.Value is not JObject state) return true;
            foreach (JProperty entry in state.Properties())
                if (entry.Name != "version" && entry.Name != "core") return true;
        }
        return false;
    }

    private static JObject ReadDocument(ItemData snapshot)
    {
        if (snapshot == null) return null;
        JObject root = ItemSpecialDataJsonStore.ReadRoot(snapshot.ItemSpecialData);
        if (root[Namespace] == null) return null;
        if (root[Namespace] is not JObject document || (int?)document["version"] != Version)
            throw new InvalidOperationException("机器快照版本无效，原始存档不会被修改。");
        return document;
    }
    #endregion
}
