#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.InventoryFeedback;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>采样正式权威回执回放器，具体拒绝与数量断言由 Lua 持有。</summary>
    public static class GameplayTestQuickTransferFeedback
    {
        #region 操作登记与正式回放
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register() => GameplayTestRuntimeProbe.Register("inventory", "quick_transfer_receipts", Capture);

        private static JObject Capture(Player player, JObject args)
        {
            ItemData Item(float amount)
            {
                ItemData item = GameRes.ExistingInstance.CreateItemData("Twine");
                item.Stack.Amount = amount;
                return item;
            }
            Inventory source = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
                { new ItemSlot(0) { itemData = Item(3) } }, "lua.quick.receipts.source") };
            Inventory target = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
                { new ItemSlot(0) { itemData = Item(4) }, new ItemSlot(1) { itemData = Item(2) } }, "lua.quick.receipts.target") };
            var quick = new InventoryQuickTransferSignal(source, 0, target, -1, source.Data.GetItemSlot(0).itemData, 3);
            var receipts = new[] {
                new NetworkQuickTransferReceipt { TargetSlot = 0, TargetGuid = target.Data.GetItemSlot(0).itemData.Guid, Amount = 1 },
                new NetworkQuickTransferReceipt { TargetSlot = 1, TargetGuid = target.Data.GetItemSlot(1).itemData.Guid, Amount = 2 }
            };
            switch (args.Value<string>("mode"))
            {
                case "duplicate": receipts[1] = receipts[0]; break;
                case "invalid_slot": receipts[1].TargetSlot = -1; break;
                case "wrong_guid": receipts[1].TargetGuid ^= 1; break;
                case "over_amount": receipts[1].Amount = 3; break;
                case "non_finite": receipts[1].Amount = float.NaN; break;
                case "too_many": receipts = new NetworkQuickTransferReceipt[257]; break;
            }
            var events = new JArray();
            void Observe(InventoryQuickTransferSignal signal)
            {
                if (ReferenceEquals(signal.Source, source))
                    events.Add(new JObject { ["sourceIndex"] = signal.SourceIndex, ["targetIndex"] = signal.TargetIndex,
                        ["amount"] = signal.Amount, ["targetMatched"] = ReferenceEquals(signal.Target, target) });
            }
            InventoryQuickTransferFeedbackEvents.Committed += Observe;
            try
            {
                NetworkItemStateCoordinator.PublishQuickTransferReceipts(quick, receipts);
                return new JObject { ["events"] = events, ["sourceAmount"] = source.Data.GetItemSlot(0).itemData.Stack.Amount,
                    ["firstAmount"] = target.Data.GetItemSlot(0).itemData.Stack.Amount,
                    ["secondAmount"] = target.Data.GetItemSlot(1).itemData.Stack.Amount };
            }
            finally { InventoryQuickTransferFeedbackEvents.Committed -= Observe; }
        }
        #endregion
    }
}
#endif
