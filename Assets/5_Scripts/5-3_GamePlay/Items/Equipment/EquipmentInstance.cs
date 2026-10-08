using MemoryPack;


[System.Serializable]
[MemoryPackable]
[MemoryPackUnion(0, typeof(EquipmentInstance_Debug))]
[MemoryPackUnion(1, typeof(EquipmentInstance_Bag))]
[MemoryPackUnion(2, typeof(EquipmentInstance_Speed))]
[MemoryPackUnion(3, typeof(EquipmentInstance_Defense))]
[MemoryPackUnion(4, typeof(EquipmentInstance_WaterInsulation))]
[MemoryPackUnion(5, typeof(EquipmentInstance_ThermalInsulation))]
[MemoryPackUnion(6, typeof(EquipmentInstance_PressureProtection))]
[MemoryPackUnion(7, typeof(EquipmentInstance_SpacesuitSupport))]
public abstract partial class EquipmentInstance
{
    public string Name;
    [MemoryPackIgnore]
    public ItemData EquippedItem { get; private set; }
    public void BindEquippedItem(ItemData itemData) => EquippedItem = itemData;
    [MemoryPackIgnore]
    public virtual bool RequiresUpdate => false;
    public abstract void Equip(Item item);
    public abstract void Update();
    public virtual void Update(float deltaTime) => Update();
    public abstract void UnEquip(Item item);
}

/// <summary>装备只报告是否隔离环境供氧，外部能力不识别具体装备类型。</summary>
public interface IAmbientOxygenBlocker
{
    bool BlocksAmbientOxygen { get; }
}
