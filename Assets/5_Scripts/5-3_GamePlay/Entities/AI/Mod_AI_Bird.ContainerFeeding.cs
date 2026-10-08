using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

public sealed partial class Mod_AI_Bird
{
    #region 背包种子取物
    public bool enableBackpackSeedTaking = true;
    [Range(0f, 1f)] public float backpackSeedChance = .15f;
    [Min(1f)] public float backpackSeedCooldown = 30f;
    [Min(.1f)] public float backpackSeedReach = 1.5f;
    [Range(0f, 1f)] public float backpackSeedHungerRate = .7f;
    private Inventory carriedSeedInventory;
    private IItemTransferPort carriedSeedPort;
    private uint carriedSeedGeneration;
    private void EnsureCarriedSeedPort()
    {
        if (carriedSeedPort != null && carriedSeedGeneration == RuntimeGeneration && carriedSeedPort.IsValid) return;
        carriedSeedGeneration = RuntimeGeneration;
        var data = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "bird-carried-seed");
        if (state.BackpackSeedSnapshot?.Length > 0)
        {
            var snapshot = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(state.BackpackSeedSnapshot);
            data.itemSlots[0].itemData = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, snapshot.CreateColdData());
        }
        carriedSeedInventory = new Inventory { Data = data, item = item };
        carriedSeedInventory.Data.SetUnlimitedSlots(false);
        uint generation = RuntimeGeneration;
        carriedSeedPort = new InventoryItemTransferPort(carriedSeedInventory, item, new ContainerPortConfiguration
        { Id = "carried-seed", Type = "core:inventory", Direction = ContainerPortDirection.Input, Access = ContainerAccessKind.Animal,
          ItemTags = new[] { "Seed" }, Reach = backpackSeedReach }, () => IsRuntimeLoaded && RuntimeGeneration == generation && IsAlive,
          changed: CaptureCarriedSeed);
    }
    private void CaptureCarriedSeed()
    {
        ItemData data = carriedSeedInventory?.Data?.itemSlots[0]?.itemData;
        state.BackpackSeedSnapshot = data != null ? MemoryPackSerializer.Serialize(ItemInstanceSnapshot.Capture(data)) : null;
        Save();
    }
    /// <summary>只在足够接近时尝试取一粒，玩家权限、标签和真实扣取仍由统一端口事务决定。</summary>
    private void TickBackpackSeedTaking(float seconds)
    {
        state.BackpackSeedCooldownSeconds = Mathf.Max(0f, state.BackpackSeedCooldownSeconds - seconds);
        if (!enableBackpackSeedTaking || permanentFlight || escapeRemaining > 0f || !GameNetwork.HasStateAuthority ||
            state.BackpackSeedCooldownSeconds > 0f || food?.Data?.nutrition == null ||
            food.Data.nutrition.GetFoodRate() >= backpackSeedHungerRate || ItemMgr.Instance == null) return;
        EnsureCarriedSeedPort();
        if (carriedSeedInventory.Data.itemSlots[0].itemData != null) return;
        foreach (Player player in ItemMgr.Instance.Player_DIC.Values)
        {
            if (player == null || player.DestructionHandled || player.gameObject.scene != item.gameObject.scene ||
                WorldTopologyRuntime.Distance(body.position, player.transform.position) > backpackSeedReach) continue;
            foreach (IContainerPort port in ContainerTransferService.Discover(player))
            {
                if (port is not IItemTransferPort output || (output.Configuration.Direction & ContainerPortDirection.Output) == 0 ||
                    (output.Configuration.Access & ContainerAccessKind.Animal) == 0 || output.PeekItem(tag: "Seed") == null) continue;
                state.BackpackSeedCooldownSeconds = Mathf.Max(1f, backpackSeedCooldown);
                if (UnityEngine.Random.value >= Mathf.Clamp01(backpackSeedChance)) return;
                var result = ContainerTransferService.TransferItems(output, carriedSeedPort,
                    new ContainerTransferContext(item, ContainerAccessKind.Animal, "bird-backpack-seed"), 1, tag: "Seed");
                if (result.Success) StartEscape(player);
                return;
            }
        }
    }
    private void DropCarriedBackpackSeed()
    {
        if (!GameNetwork.HasStateAuthority || state.BackpackSeedSnapshot == null || state.BackpackSeedSnapshot.Length == 0) return;
        var snapshot = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(state.BackpackSeedSnapshot);
        ItemData data = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, snapshot.CreateColdData());
        DroppedItemHandle dropped = DroppedItemService.Spawn(data, body.position, duration: 0f);
        if (!dropped.IsValid) return;
        state.BackpackSeedSnapshot = null;
        carriedSeedInventory = null; carriedSeedPort = null;
        Save();
    }
    #endregion
}
