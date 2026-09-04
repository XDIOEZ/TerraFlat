using MemoryPack;


[System.Serializable]
[MemoryPackable]
[MemoryPackUnion(0, typeof(EquipmentInstance_Debug))]
[MemoryPackUnion(1, typeof(EquipmentInstance_Bag))]
[MemoryPackUnion(2, typeof(EquipmentInstance_Speed))]
[MemoryPackUnion(3, typeof(EquipmentInstance_Defense))]
[MemoryPackUnion(4, typeof(EquipmentInstance_WaterInsulation))]
public abstract partial class EquipmentInstance
{
    public string Name;
    public abstract void Equip(Item item);
    public abstract void Update();
    public abstract void UnEquip(Item item);
}
