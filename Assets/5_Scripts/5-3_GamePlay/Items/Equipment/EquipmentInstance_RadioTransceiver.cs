using MemoryPack;
using UnityEngine;

/// <summary>穿戴时由装备生命周期持有对讲机端点。</summary>
[System.Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EquipmentInstance_RadioTransceiver : EquipmentInstance, IEquipmentConfiguration
{
    #region 装备与配置
    [MemoryPackIgnore] private Item owner;
    public override bool RequiresUpdate => true;
    [MemoryPackIgnore] public bool CanConfigure => owner != null && EquippedItem != null &&
        SpacesuitSystem.IsActuallyEquipped(owner, EquippedItem);

    public override void Equip(Item item)
    {
        owner = item;
        RadioTransceiverSystem.BindRuntime(EquippedItem, owner);
    }
    public override void Update() => Update(Time.deltaTime);
    public override void Update(float deltaTime)
    {
        if (CanConfigure) RadioTransceiverSystem.Advance(EquippedItem, owner, deltaTime);
    }
    public override void UnEquip(Item item)
    {
        RadioTransceiverPanelSession.CloseFor(EquippedItem);
        RadioTransceiverSystem.Unbind(EquippedItem, owner);
        owner = null;
        BindEquippedItem(null);
    }
    public void OpenConfiguration()
    {
        if (CanConfigure) RadioTransceiverPanelSession.Show(EquippedItem, owner);
    }
    #endregion
}
