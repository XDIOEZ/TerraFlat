using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Inventory_ModuleData : ModuleData
{
    [ShowInInspector]
    [ReadOnly]
    public Dictionary<string, Inventory_Data> Data = new Dictionary<string, Inventory_Data>();

    public Vector3 PanleRectPosition = Vector3.zero;
    public string InventoryInitName = "";
    public bool BasePanelIsOpen = true;
}

/// <summary>
/// 复用 Inventory_ModuleData 作为独立制作/加工模块的库存存档载体。
/// 旧模块曾把 RawData 直接序列化为字符串列表；读取失败时保持预制体初始数据，保证旧存档仍可打开。
/// </summary>
public static class InventoryModuleDataPersistence
{
    /// <summary>尝试读取独立库存存档，旧格式或空数据返回 null。</summary>
    public static Inventory_ModuleData TryRead(Ex_ModData_MemoryPackable source)
    {
        if (source?.BitData == null || source.BitData.Length == 0)
            return null;

        try
        {
            Inventory_ModuleData data = source.GetData<Inventory_ModuleData>();
            return data?.Data != null ? data : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把独立库存存档写回模块数据。</summary>
    public static void Write(Ex_ModData_MemoryPackable target, Inventory_ModuleData data)
    {
        if (target == null || data == null)
            return;

        data.Data ??= new Dictionary<string, Inventory_Data>();
        target.WriteData(data);
    }

    /// <summary>从指定键恢复一个库存的数据引用。</summary>
    public static bool TryRestore(
        Inventory target,
        Inventory_ModuleData source,
        string key)
    {
        if (target == null || source?.Data == null || string.IsNullOrEmpty(key))
            return false;

        if (!source.Data.TryGetValue(key, out Inventory_Data savedData) || savedData == null)
            return false;

        target.Data = savedData;
        return true;
    }

    /// <summary>把一个库存当前数据写入指定键。</summary>
    public static void Capture(
        Inventory_ModuleData target,
        string key,
        Inventory source)
    {
        if (target == null || string.IsNullOrEmpty(key) || source?.Data == null)
            return;

        target.Data[key] = source.Data;
    }
}
