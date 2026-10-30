using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>火种通过普通使用意图点燃地格，成功后只扣当前库存的一枚。</summary>
public sealed class Mod_WorldFireIgnition : Module
{
    #region 组合点火能力
    public const string ModuleId = "Item.WorldFireIgnition";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData_MemoryPackable State = new() { ModuleId = ModuleId };
    public override ModuleData _Data { get => State; set => State = (Ex_ModData_MemoryPackable)value; }
    public float Reach = 2f;
    protected override void OnLoad() => item.OnAct += Act;
    protected override void OnUnload() { if (item != null) item.OnAct -= Act; }
    protected override void OnSave() { }

    public override void Act()
    {
        Item actor = item?.Owner;
        if (!GameNetwork.HasStateAuthority || actor == null || !item.InHand ||
            !InventoryContextResolver.TryResolveContainingInventory(actor, item.itemData, out Inventory inventory)) return;
        var slot = inventory.Data.itemSlots.Find(candidate => candidate.itemData?.Guid == item.itemData.Guid);
        if (slot?.itemData?.Stack == null || slot.itemData.Stack.Amount < 1f) return;
        Mod_GameController controller = actor.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller == null) return;
        Vector2 target = WorldLocalPresentation.ToLogical(controller.GetAimWorldPosition(Reach));
        Vector2 origin = WorldLocalPresentation.ToLogical(actor.transform.position);
        if (WorldTopologyRuntime.ShortestDelta(origin, target).sqrMagnitude > Reach * Reach) return;
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(actor);
        if (!WorldFireSystem.TryIgniteWorldFireCell(worldKey, target)) return;
        if (!inventory.Data.TryConsumeFromSlot(slot, 1, out _)) WorldFireSystem.ExtinguishWorldFireCell(worldKey, target);
    }
    #endregion
}
