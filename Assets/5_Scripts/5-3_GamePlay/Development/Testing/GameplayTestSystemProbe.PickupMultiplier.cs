using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 拾取倍率库存事务采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPickupMultiplierRules()
    {
        Register("inventory", "pickup_multiplier", CapturePickupMultiplier);
        Register("inventory", "pickup_lifecycle", CapturePickupPhysicsLeaseAndRequestRetry);
    }

    private sealed class PickupProbeInventory : IInventory
    {
        public Inventory Inventory;
        public Inventory GetDefaultTargetInventory() => Inventory;
    }

    /// <summary>禁用拾取器只连接独立冷库存，采样正式入包和剩余数量，不触碰当前玩家。</summary>
    private static JObject CapturePickupMultiplier(JObject args)
    {
        var root = new GameObject("[LuaPickupMultiplier]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            Mod_ItemPicker picker = root.AddComponent<Mod_ItemPicker>();
            picker.PickupAmountMultiplier = Flag(args, "enabled", true)
                ? (int)Number(args, "multiplier", 10) : 1;
            var inventories = new List<Inventory>();
            Inventory AddInventory(float capacity)
            {
                var inventory = new Inventory
                {
                    Data = new Inventory_Data(new List<ItemSlot> { new(0) { SlotMaxVolume = capacity } },
                        inventories.Count == 1 ? ModText.Bag : "independent pickup " + inventories.Count)
                };
                inventories.Add(inventory);
                picker.AddTargetInventories.Add(new PickupProbeInventory { Inventory = inventory });
                return inventory;
            }
            Inventory first = AddInventory((float)Number(args, "firstCapacity", 1000));
            if (args["secondCapacity"] != null)
                AddInventory((float)Number(args, "secondCapacity", 0));
            if (args["maxWeight"] != null || args["maxVolume"] != null)
            {
                Inventory_Data capacityData = inventories[inventories.Count - 1].Data;
                capacityData.Name = ModText.Bag;
                capacityData.SetCarryCapacity((float)Number(args, "maxWeight", 1000),
                    (float)Number(args, "maxVolume", 1000));
            }
            if (Flag(args, "duplicateTarget", false))
                picker.AddTargetInventories.Add(picker.AddTargetInventories[0]);

            var source = new Data_GeneralItem
            {
                IDName = "probe:pickup_multiplier",
                Durability = (float)Number(args, "durability", 1),
                MaxDurability = (float)Number(args, "maximum", 1),
                Stack = new ItemStack
                {
                    Amount = (float)Number(args, "amount", 3),
                    Stackable = Flag(args, "stackable", true),
                    Weight = (float)Number(args, "unitWeight", 1),
                    Volume = (float)Number(args, "unitVolume", 1),
                    CanBePickedUp = true
                }
            };
            first.Data.IsDepositBlocked = Flag(args, "blocked", false);
            var sourceDuringCommit = new JArray();
            var sourceMassChanges = new JArray();
            foreach (Inventory inventory in inventories)
                inventory.Data.Event_OnDataChanged += _ => sourceDuringCommit.Add(source.Stack.Amount);
            source.MassChanged += _ => sourceMassChanges.Add(source.Stack.Amount);
            float Count(Inventory inventory) => inventory.Data.GetItemSlot(0)?.itemData?.Stack?.Amount ?? 0f;
            float preflight = picker.GetAcceptablePickupSourceAmount(source);
            bool preflightUnchanged = sourceDuringCommit.Count == 0 && sourceMassChanges.Count == 0 &&
                source.Durability == (float)Number(args, "durability", 1) &&
                source.Stack.Amount == (float)Number(args, "amount", 3) && source.Stack.CanBePickedUp;
            bool accepted = picker.TryAcceptNetworkPickup(source, out float consumed, out float added,
                Flag(args, "allowPartial", true));
            var result = new JObject
            {
                ["appliedMultiplier"] = picker.PickupAmountMultiplier,
                ["accepted"] = accepted,
                ["firstAmount"] = Count(first),
                ["secondAmount"] = inventories.Count > 1 ? Count(inventories[1]) : 0f,
                ["firstDurability"] = first.Data.GetItemSlot(0)?.itemData?.Durability ?? 0f,
                ["secondDurability"] = inventories.Count > 1 ? inventories[1].Data.GetItemSlot(0)?.itemData?.Durability ?? 0f : 0f,
                ["remainingDurability"] = source.Durability,
                ["remaining"] = source.Stack.CanBePickedUp ? source.Stack.Amount : 0f,
                ["pickable"] = source.Stack.CanBePickedUp
            };
            result["preflight"] = preflight;
            result["preflightUnchanged"] = preflightUnchanged;
            result["sourceDuringCommit"] = sourceDuringCommit;
            result["sourceMassChanges"] = sourceMassChanges;
            result["consumed"] = consumed;
            result["added"] = added;
            if (Flag(args, "retry", false) && source.Stack.CanBePickedUp)
            {
                result["blockedRetry"] = picker.TryAcceptNetworkPickup(source);
                result["afterBlockedRetry"] = Count(first);
                first.Data.GetItemSlot(0).SlotMaxVolume = 1000f;
                result["retryAccepted"] = picker.TryAcceptNetworkPickup(source);
                result["afterRetry"] = Count(first);
                result["remainingAfterRetry"] = source.Stack.CanBePickedUp ? source.Stack.Amount : 0f;
            }
            return result;
        }
        finally { Object.DestroyImmediate(root); }
    }

    #endregion

    #region 拾取物理就绪与请求恢复采样
    // 临时物理世界只连接自有库存；网络替身只返回状态，不替正式拾取器维护重试。
    private static JObject CapturePickupPhysicsLeaseAndRequestRetry(JObject args)
    {
        using var space = WorldPhysicsScene2D.CreateIsolated("Pickup lifecycle " + System.Guid.NewGuid().ToString("N"));
        using var physics = new WorldPhysicsService2D(space.Scene.name, WorldPhysicsScene2D.Borrow(space.Scene));
        var root = new GameObject("[LuaPickupLifecycle]");
        var sourceRoot = new GameObject("[LuaPickupSource]");
        root.SetActive(false);
        sourceRoot.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, space.Scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sourceRoot, space.Scene);
        GameItem owner = root.AddComponent<GameItem>();
        GameItem source = sourceRoot.AddComponent<GameItem>();
        Mod_ItemPicker picker = null;
        var previousBridge = ItemNetworkStateSerialization.TryBeginNetworkPickup;
        try
        {
            owner.BindData(new Data_GeneralItem { IDName = "probe:pickup-owner", Stack = new ItemStack { CanBePickedUp = false } });
            source.BindData(new Data_GeneralItem
            {
                IDName = "probe:pickup-source", Guid = 749319,
                Stack = new ItemStack { Amount = 4f, Weight = 1f, Volume = 1f, CanBePickedUp = true }
            });
            BoxCollider2D sourceShape = sourceRoot.AddComponent<BoxCollider2D>();
            sourceShape.isTrigger = true;
            var sensor = new GameObject("Pickup sensor");
            sensor.transform.SetParent(root.transform, false);
            picker = sensor.AddComponent<Mod_ItemPicker>();
            picker.ModuleInit(owner, new Ex_ModData_MemoryPackable { Name = "probe:picker", ID = ModText.Picker, Enabled = true });
            var template = sensor.AddComponent<ItemPhysicsTemplate2D>();
            template.owningModule = picker;
            template.colliderType = nameof(BoxCollider2D);
            template.isTrigger = true;
            template.hasBody = true;
            template.bodyType = RigidbodyType2D.Kinematic;
            template.bodyGravityScale = 0f;
            var inventory = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0) }, ModText.Bag) };
            var target = root.AddComponent<Mod_Inventory>();
            target.InventoryInstances = new List<Inventory> { inventory };
            picker.AddTargetInventoryModules.Add(target);
            root.SetActive(true);
            sourceRoot.SetActive(true);
            picker.Load();
            var result = new JObject { ["missingBeforeLease"] = !picker.TryGetDroppedPickupBounds(out _) };
            physics.RegisterItem(owner);
            physics.RegisterItem(source);
            Physics2D.SyncTransforms();
            result["readyAfterLease"] = picker.TryGetDroppedPickupBounds(out _) && picker.CanReachDroppedPoint(Vector2.zero);
            result["registeredContactResolvesItem"] = GameplayPhysics2D.ResolveComponent<Item>(sourceShape) == source;

            int requests = 0;
            WorldItemPickupRequestStatus status = WorldItemPickupRequestStatus.Deferred;
            ItemNetworkStateSerialization.TryBeginNetworkPickup = (receiver, worldItem) =>
            {
                if (receiver != picker || worldItem != source)
                    return previousBridge?.Invoke(receiver, worldItem) ?? WorldItemPickupRequestStatus.NotHandled;
                requests++;
                return status;
            };
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Enter);
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["deferredContactRetries"] = requests == 2;
            status = WorldItemPickupRequestStatus.Pending;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["resumedWithoutLeaving"] = requests == 3;
            for (int i = 0; i < 10; i++) picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["pendingNotRepeated"] = requests == 3;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Exit);
            picker.CanPickUp = false;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["permissionBlocked"] = requests == 3;
            picker.CanPickUp = true;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["permissionRestored"] = requests == 4;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Exit);
            status = WorldItemPickupRequestStatus.CapacityRejected;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Enter);
            for (int i = 0; i < 10; i++) picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["fullCapacityNotRepeated"] = requests == 5;
            ulong massRevision = inventory.Data.MassRevision;
            inventory.Data.SetCarryCapacity(200f, 200f);
            status = WorldItemPickupRequestStatus.Pending;
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["capacityChangeRetries"] = requests == 6;
            inventory.Data.SetCarryCapacity(200f, 200f);
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["sameCapacityDoesNotRetry"] = requests == 6;
            inventory.Data.SetUnlimitedCarryCapacity(true);
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["unlimitedCapacityRetries"] = requests == 7;
            inventory.Data.ClearCarryCapacity();
            picker.ReceivePhysicsTrigger(sourceShape, WorldPhysicsTriggerPhase2D.Stay);
            result["clearedCapacityRetries"] = requests == 8;
            result["capacityDoesNotChangeMass"] = inventory.Data.MassRevision == massRevision;
            result["requestsDoNotMoveInventory"] = source.itemData.Stack.Amount == 4f &&
                source.itemData.Stack.CanBePickedUp && inventory.Data.GetItemSlot(0).itemData == null;

            picker.Unload();
            result["unloadedRangeRejected"] = !picker.TryGetDroppedPickupBounds(out _) && !picker.CanReachDroppedPoint(Vector2.zero);
            WorldPhysicsService2D.ReleaseItem(owner);
            picker.Load();
            result["returnedLeaseRejected"] = !picker.TryGetDroppedPickupBounds(out _);
            physics.RegisterItem(owner);
            Physics2D.SyncTransforms();
            result["rerentedRangeRestored"] = picker.TryGetDroppedPickupBounds(out _) && picker.CanReachDroppedPoint(Vector2.zero);
            return result;
        }
        finally
        {
            ItemNetworkStateSerialization.TryBeginNetworkPickup = previousBridge;
            picker?.Unload();
            WorldPhysicsService2D.ReleaseItem(source);
            WorldPhysicsService2D.ReleaseItem(owner);
            Object.DestroyImmediate(sourceRoot);
            Object.DestroyImmediate(root);
        }
    }
    #endregion
}
