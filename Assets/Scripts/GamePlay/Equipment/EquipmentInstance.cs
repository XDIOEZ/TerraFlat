using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;


[System.Serializable]
[MemoryPackable]
[MemoryPackUnion(0, typeof(EquipmentInstance_Bag))]
public abstract partial class EquipmentInstance
{
    public string Name;
    public abstract void Equip();
    public abstract void Update();
    public abstract void UnEquip();
}
