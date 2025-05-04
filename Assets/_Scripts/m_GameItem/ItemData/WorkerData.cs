using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class WorkerData :ItemData
{
    [Header("存储插槽")]
    [ShowNonSerializedField]
    public Dictionary<string, Inventory_Data> Inventory_Data_Dict = new Dictionary<string, Inventory_Data>();

    public ItemValues ItemValues;

    public Hp Hp;
    public Defense Defense;

    public bool IsInstalled;
}