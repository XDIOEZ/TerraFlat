
using MemoryPack;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System;

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
        public override byte[] Serialize() => MemoryPackSerializer.Serialize(Value);
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
            Value = payload == null || payload.Length == 0 ? default : MemoryPackSerializer.Deserialize<T>(payload)
        };
        runtimeState = created;
        return created;
    }

    #endregion

    public T GetData<T>()
    {
        if (BitData == null || BitData.Length == 0) return default;
        return MemoryPackSerializer.Deserialize<T>(BitData);
    }

    public void ReadData<T>(ref T setData)
    {
        if (BitData == null || BitData.Length == 0) return;
        setData = MemoryPackSerializer.Deserialize<T>(BitData);
    }

    public void WriteData<T>(T bitData)
    {
        BitData = MemoryPackSerializer.Serialize(bitData);
    }
    public void OutData<T>(out T value)
    {
        if (BitData == null || BitData.Length == 0)
        {
            value = default;
            return;
        }
        value = MemoryPackSerializer.Deserialize<T>(BitData);
    }

}
