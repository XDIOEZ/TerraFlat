
using MemoryPack;
using Newtonsoft.Json;
using System;

[System.Serializable]
[MemoryPackable]
public partial class Ex_ModData : ModuleData
{
    public string BitData;

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
    public byte[] BitData;

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
}
