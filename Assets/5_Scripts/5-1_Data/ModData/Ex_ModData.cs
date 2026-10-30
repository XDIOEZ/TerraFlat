
using MemoryPack;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

[System.Serializable]
[MemoryPackable]
public partial class Ex_ModData : ModuleData
{
    [ReadOnly]
    [UnityEngine.SerializeField, UnityEngine.Serialization.FormerlySerializedAs("BitData"), MemoryPackIgnore, Newtonsoft.Json.JsonIgnore]
    private string bitData;
    public string BitData { get => bitData; set { if (bitData == value) return; bitData = value; NotifyPayloadChanged(); } }

    public T GetData<T>()
    {
        if (string.IsNullOrEmpty(BitData)) return default;
        return JsonConvert.DeserializeObject<T>(BitData);
    }

    public void ReadData<T>(ref T setData)
    {
        if (string.IsNullOrEmpty(BitData)) return;
        setData = JsonConvert.DeserializeObject<T>(BitData);
    }


    public void WriteData<T>(T bitData)
    {
        BitData = JsonConvert.SerializeObject(bitData);
    }
}

[Serializable]
[MemoryPackable]
public partial class Ex_ModData_MemoryPackable : ModuleData
{
    #region 强类型运行状态与按需持久化

    private abstract class RuntimeState
    {
        public abstract byte[] Serialize();
    }

    private sealed class RuntimeState<T> : RuntimeState where T : struct
    {
        public T Value;
        public override byte[] Serialize() => ModuleBinaryPayloadCodecs.Serialize(Value);
    }

    [NonSerialized, MemoryPackIgnore, JsonIgnore]
    private RuntimeState runtimeState;
    [NonSerialized, MemoryPackIgnore, JsonIgnore]
    private bool runtimeStateChanged;

    [ReadOnly]
    [UnityEngine.SerializeField, UnityEngine.Serialization.FormerlySerializedAs("BitData"), MemoryPackIgnore, Newtonsoft.Json.JsonIgnore]
    private byte[] bitData;
    public byte[] BitData
    {
        get
        {
            // 高频计算只更新强类型状态，快照或保存读取载荷时才编码。
            if (runtimeStateChanged)
            {
                bitData = runtimeState.Serialize();
                runtimeStateChanged = false;
            }
            return bitData;
        }
        set
        {
            if (ReferenceEquals(bitData, value) && runtimeState == null) return;
            bitData = value;
            runtimeState = null;
            runtimeStateChanged = false;
            NotifyPayloadChanged();
        }
    }

    public T ReadRuntimeState<T>() where T : struct => GetOrCreateRuntimeState<T>().Value;

    public void WriteRuntimeState<T>(T value) where T : struct
    {
        GetOrCreateRuntimeState<T>().Value = value;
        runtimeStateChanged = true;
        NotifyPayloadChanged();
    }

    private RuntimeState<T> GetOrCreateRuntimeState<T>() where T : struct
    {
        if (runtimeState is RuntimeState<T> typed) return typed;
        byte[] payload = BitData;
        var created = new RuntimeState<T>
        {
            Value = payload == null || payload.Length == 0 ? default : ModuleBinaryPayloadCodecs.Deserialize<T>(payload)
        };
        runtimeState = created;
        return created;
    }

    #endregion

    public T GetData<T>()
    {
        if (BitData == null || BitData.Length == 0) return default;
        return ModuleBinaryPayloadCodecs.Deserialize<T>(BitData);
    }

    public void ReadData<T>(ref T setData)
    {
        if (BitData == null || BitData.Length == 0) return;
        setData = ModuleBinaryPayloadCodecs.Deserialize<T>(BitData);
    }

    public void WriteData<T>(T bitData)
    {
        BitData = ModuleBinaryPayloadCodecs.Serialize(bitData);
    }
    public void OutData<T>(out T value)
    {
        if (BitData == null || BitData.Length == 0)
        {
            value = default;
            return;
        }
        value = ModuleBinaryPayloadCodecs.Deserialize<T>(BitData);
    }

}

public interface IModuleBinaryPayloadCodec<T>
{
    string SchemaId { get; }
    int CurrentVersion { get; }
    byte[] Serialize(T value);
    T Deserialize(int version, byte[] payload);
}

public static class ModuleBinaryPayloadCodecs
{
    #region 运行结构的显式存档编码
    private static class CodecSlot<T>
    {
        public static IModuleBinaryPayloadCodec<T> Codec;
    }

    public static IDisposable Register<T>(IModuleBinaryPayloadCodec<T> codec)
    {
        if (codec == null || string.IsNullOrWhiteSpace(codec.SchemaId) || codec.CurrentVersion < 1)
            throw new ArgumentException("模块载荷编解码器声明无效。", nameof(codec));
        if (Interlocked.CompareExchange(ref CodecSlot<T>.Codec, codec, null) != null)
            throw new InvalidOperationException($"模块载荷编解码器重复：{typeof(T).FullName}");
        return new CodecRegistration<T>(codec);
    }

    public static byte[] Serialize<T>(T value)
    {
        var codec = CodecSlot<T>.Codec;
        if (codec != null)
            return ItemSnapshotSerialization.SerializePayload(new ModuleBinaryPayloadEnvelope
            { SchemaId = codec.SchemaId, SchemaVersion = codec.CurrentVersion, Payload = codec.Serialize(value) });
        RequireSafeRuntimePayloadType<T>();
        return MemoryPackSerializer.Serialize(value);
    }

    public static T Deserialize<T>(byte[] payload)
    {
        var codec = CodecSlot<T>.Codec;
        if (codec != null)
        {
            var envelope = MemoryPackSerializer.Deserialize<ModuleBinaryPayloadEnvelope>(payload);
            if (envelope == null || envelope.SchemaId != codec.SchemaId || envelope.SchemaVersion < 1 ||
                envelope.SchemaVersion > codec.CurrentVersion || envelope.Payload == null)
                throw new InvalidOperationException($"不支持的模块存档载荷：{typeof(T).FullName}");
            return codec.Deserialize(envelope.SchemaVersion, envelope.Payload);
        }
        RequireSafeRuntimePayloadType<T>();
        return MemoryPackSerializer.Deserialize<T>(payload);
    }

    // 裸结构体会按内存布局写盘，必须由模块提供稳定字段编解码器。
    private static void RequireSafeRuntimePayloadType<T>()
    {
        if (typeof(T).IsValueType && !RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            throw new InvalidOperationException($"模块值类型缺少稳定的存档编解码器：{typeof(T).FullName}");
    }

    private sealed class CodecRegistration<T> : IDisposable
    {
        private readonly IModuleBinaryPayloadCodec<T> codec;
        public CodecRegistration(IModuleBinaryPayloadCodec<T> codec) => this.codec = codec;
        public void Dispose() => Interlocked.CompareExchange(ref CodecSlot<T>.Codec, null, codec);
    }
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class ModuleBinaryPayloadEnvelope
{
    [MemoryPackOrder(0)] public string SchemaId;
    [MemoryPackOrder(1)] public int SchemaVersion;
    [MemoryPackOrder(2)] public byte[] Payload;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ModuleGridPositionSaveSnapshot
{
    [MemoryPackOrder(0)] public int X;
    [MemoryPackOrder(1)] public int Y;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ModuleVector2SaveSnapshot
{
    #region 二维浮点坐标
    [MemoryPackOrder(0)] public float X;
    [MemoryPackOrder(1)] public float Y;

    public static System.Collections.Generic.Dictionary<string, ModuleVector2SaveSnapshot> CaptureDictionary(
        System.Collections.Generic.Dictionary<string, UnityEngine.Vector2> values)
    {
        if (values == null) return null;
        var result = new System.Collections.Generic.Dictionary<string, ModuleVector2SaveSnapshot>(values.Count);
        foreach (var pair in values) result.Add(pair.Key, new ModuleVector2SaveSnapshot { X = pair.Value.x, Y = pair.Value.y });
        return result;
    }

    public static System.Collections.Generic.Dictionary<string, UnityEngine.Vector2> RestoreDictionary(
        System.Collections.Generic.Dictionary<string, ModuleVector2SaveSnapshot> values)
    {
        if (values == null) return null;
        var result = new System.Collections.Generic.Dictionary<string, UnityEngine.Vector2>(values.Count);
        foreach (var pair in values)
            result.Add(pair.Key, pair.Value == null ? UnityEngine.Vector2.zero : new UnityEngine.Vector2(pair.Value.X, pair.Value.Y));
        return result;
    }
    #endregion
}
