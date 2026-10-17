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
        => Register("inventory", "pickup_multiplier", CapturePickupMultiplier);

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
                source.Stack.Amount == (float)Number(args, "amount", 3) && source.Stack.CanBePickedUp;
            bool accepted = picker.TryAcceptNetworkPickup(source, out float consumed, out float added,
                Flag(args, "allowPartial", true));
            var result = new JObject
            {
                ["appliedMultiplier"] = picker.PickupAmountMultiplier,
                ["accepted"] = accepted,
                ["firstAmount"] = Count(first),
                ["secondAmount"] = inventories.Count > 1 ? Count(inventories[1]) : 0f,
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
}
