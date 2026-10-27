#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static partial class GameplayTestNetworkBots
    {
        #region 假人通过正式消息请求拾取与提交租约
        // 夹具通过正式消息观察服务器库存，不冒充独立客户端或 Host 已验收。
        private static IEnumerator CapturePickup(Player local, JObject args, Action<JObject> completed)
        {
            BotWorld world = null;
            var ownedItems = new List<Item>();
            int itemGuid = 0;
            JObject result = null;
            try
            {
                world = new BotWorld(local);
                Vector3 origin = WorldLocalPresentation.ToLogical(local.transform.position);
                if (!GameManager.Instance.TryGetNearestLandSpawnPosition(origin + Vector3.right * 12f, out Vector3 remoteSpawn) ||
                    WorldTopologyRuntime.Distance(origin, remoteSpawn) < 6f)
                    throw new InvalidOperationException("拾取假人没有找到远离本地主角自动拾取范围的陆地。");
                world.PlaceSpawn(world.Prefix + "_拾取甲", remoteSpawn);
                world.PlaceSpawn(world.Prefix + "_拾取乙", remoteSpawn);
                yield return world.Join(21, world.Prefix + "_拾取甲");
                yield return world.Join(22, world.Prefix + "_拾取乙");
                NetworkWorldPlayer a = world.Core(21), b = world.Core(22);
                Item ownedItem = ItemMgr.Instance.InstantiateItem("Ore_Copper",
                    a.AuthoritativeLogicalPosition + new Vector3(.5f, 0f), Quaternion.identity, Vector3.one);
                ownedItems.Add(ownedItem);
                ownedItem.itemData.Stack.Amount = 10f;
                itemGuid = ownedItem.itemData.Guid;
                ownedItem.Load();
                yield return null;
                result = new JObject
                {
                    ["mode"] = "in-process-server-bots", ["itemGuid"] = itemGuid,
                    ["botA"] = PickupBotSample(a), ["botB"] = PickupBotSample(b),
                    ["sameCorePlayer"] = ReferenceEquals(a.CorePlayer, b.CorePlayer),
                    ["inventoryReceipt"] = "server-authority-verified-client-side-unverified",
                    ["initial"] = PickupItemSample(itemGuid)
                };

                NetworkItemPickupResponse grant = default;
                yield return RequestPickup(world.Transport, 21, itemGuid, value => grant = value);
                result["aGrant"] = PickupGrantSample(grant, itemGuid);
                NetworkItemPickupResponse denied = default;
                yield return RequestPickup(world.Transport, 22, itemGuid, value => denied = value);
                result["bDenied"] = new JObject { ["responseReceived"] = true, ["granted"] = denied.Granted };
                SubmitPickupCommit(world.Transport, 22, itemGuid, grant.ReservationToken, true, 3f, grant.RequestToken);
                yield return null;
                result["afterWrongOwner"] = PickupItemSample(itemGuid);
                uint wrong = grant.ReservationToken == uint.MaxValue ? 1u : grant.ReservationToken + 1u;
                SubmitPickupCommit(world.Transport, 21, itemGuid, wrong, true, 3f, grant.RequestToken);
                yield return null;
                result["afterWrongToken"] = PickupItemSample(itemGuid);
                int priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(21);
                SubmitPickupCommit(world.Transport, 21, itemGuid, grant.ReservationToken, false);
                NetworkItemPickupReceipt rejection = default;
                yield return WaitPickupReceipt(world.Transport, 21, priorReceipt, value => rejection = value);
                result["afterReject"] = PickupItemSample(itemGuid);
                SubmitPickupApplied(world.Transport, 21, rejection);

                NetworkItemPickupResponse regrant = default;
                yield return RequestPickup(world.Transport, 21, itemGuid, value => regrant = value);
                result["aRegrant"] = PickupGrantSample(regrant, itemGuid);
                Mod_ItemPicker picker = a.CorePlayer.GetComponentInChildren<Mod_ItemPicker>(true);
                picker.PickupAmountMultiplier = 1;
                if (!PlayerCarryCapacityUtility.TryGetSnapshot(a.CorePlayer, out PlayerCarryCapacitySnapshot capacity) ||
                    !ItemNetworkStateSerialization.TryDeserializeItemData(regrant.Payload, out ItemData source) || source.Stack.Volume <= 0f)
                    throw new InvalidOperationException("部分拾取夹具缺少真实背包容量或物品体积。");
                capacity.BagData.SetUnlimitedCarryCapacity(false);
                capacity.BagData.SetCarryCapacity(float.PositiveInfinity, capacity.CurrentVolume + source.Stack.Volume * 3f);
                float planned = picker.GetAcceptablePickupSourceAmount(source, 1);
                float beforeAmount = PickupInventoryAmount(a.CorePlayer, source.IDName);
                byte[] staleBaseline = ItemNetworkStateSerialization.Capture(a.CorePlayer, true);
                priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(21);
                SubmitPickupCommit(world.Transport, 21, itemGuid, regrant.ReservationToken, true, planned);
                NetworkItemPickupReceipt receipt = default;
                yield return WaitPickupReceipt(world.Transport, 21, priorReceipt, value => receipt = value);
                result["partial"] = PickupReceiptSample(receipt, a.CorePlayer, source.IDName, beforeAmount);
                result["partial"]["plannedSourceAmount"] = planned;
                result["afterPartial"] = PickupItemSample(itemGuid);
                priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(21);
                SubmitPickupCommit(world.Transport, 21, itemGuid, regrant.ReservationToken, true, planned);
                yield return WaitPickupReceipt(world.Transport, 21, priorReceipt, _ => { });
                result["afterDuplicate"] = PickupItemSample(itemGuid);
                result["duplicateInventoryDelta"] = PickupInventoryAmount(a.CorePlayer, source.IDName) - beforeAmount;
                SendPickupBaselineCommand(world, 21, a, staleBaseline);
                yield return null;
                result["pendingAckInventoryDelta"] = PickupInventoryAmount(a.CorePlayer, source.IDName) - beforeAmount;
                SubmitPickupApplied(world.Transport, 21, receipt);

                var invalid = new JArray();
                foreach (float amount in new[] { 0f, float.NaN, float.PositiveInfinity, 8f, .5f })
                {
                    NetworkItemPickupResponse invalidGrant = default;
                    yield return RequestPickup(world.Transport, 21, itemGuid, value => invalidGrant = value);
                    priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(21);
                    SubmitPickupCommit(world.Transport, 21, itemGuid, invalidGrant.ReservationToken, true, amount);
                    NetworkItemPickupReceipt invalidReceipt = default;
                    yield return WaitPickupReceipt(world.Transport, 21, priorReceipt, value => invalidReceipt = value);
                    JObject sample = PickupReceiptSample(invalidReceipt, a.CorePlayer, source.IDName, beforeAmount);
                    sample["world"] = PickupItemSample(itemGuid);
                    invalid.Add(sample);
                    SubmitPickupApplied(world.Transport, 21, invalidReceipt);
                }
                result["invalidAmounts"] = invalid;
                NetworkItemPickupResponse fullGrant = default;
                yield return RequestPickup(world.Transport, 21, itemGuid, value => fullGrant = value);
                picker = a.CorePlayer.GetComponentInChildren<Mod_ItemPicker>(true);
                var blocked = new List<Inventory_Data>();
                foreach (IInventory target in picker.AddTargetInventories)
                {
                    Inventory_Data data = target?.GetDefaultTargetInventory()?.Data;
                    if (data != null && !blocked.Contains(data)) { data.IsDepositBlocked = true; blocked.Add(data); }
                }
                priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(21);
                SubmitPickupCommit(world.Transport, 21, itemGuid, fullGrant.ReservationToken, true, 1f);
                NetworkItemPickupReceipt fullReceipt = default;
                yield return WaitPickupReceipt(world.Transport, 21, priorReceipt, value => fullReceipt = value);
                result["zeroCapacity"] = PickupReceiptSample(fullReceipt, a.CorePlayer, source.IDName, beforeAmount);
                result["zeroCapacity"]["world"] = PickupItemSample(itemGuid);
                foreach (Inventory_Data data in blocked) data.IsDepositBlocked = false;
                SubmitPickupApplied(world.Transport, 21, fullReceipt);

                NetworkItemPickupResponse finalGrant = default;
                yield return RequestPickup(world.Transport, 21, itemGuid, value => finalGrant = value);
                PlayerCarryCapacityUtility.TryGetSnapshot(a.CorePlayer, out capacity);
                capacity.BagData.SetUnlimitedCarryCapacity(true);
                priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(21);
                SubmitPickupCommit(world.Transport, 21, itemGuid, finalGrant.ReservationToken, true, 7f);
                NetworkItemPickupReceipt finalReceipt = default;
                yield return WaitPickupReceipt(world.Transport, 21, priorReceipt, value => finalReceipt = value);
                result["complete"] = PickupReceiptSample(finalReceipt, a.CorePlayer, source.IDName, beforeAmount);
                result["afterCommit"] = PickupItemSample(itemGuid);
                SubmitPickupApplied(world.Transport, 21, finalReceipt);

                Item timeoutItem = ItemMgr.Instance.InstantiateItem("Ore_Copper", a.AuthoritativeLogicalPosition + Vector3.right * .5f,
                    Quaternion.identity, Vector3.one);
                ownedItems.Add(timeoutItem); timeoutItem.Load();
                int timeoutGuid = timeoutItem.itemData.Guid;
                NetworkItemPickupResponse timeoutGrant = default;
                yield return RequestPickup(world.Transport, 22, timeoutGuid, value => timeoutGrant = value);
                priorReceipt = world.Transport.Count<NetworkItemPickupReceipt>(22);
                NetworkItemPickupReceipt expired = default;
                yield return WaitPickupReceipt(world.Transport, 22, priorReceipt, value => expired = value);
                result["timeout"] = PickupItemSample(timeoutGuid);
                result["timeout"]["accepted"] = expired.Accepted;
                SubmitPickupApplied(world.Transport, 22, expired);
                NetworkItemPickupResponse disconnectGrant = default;
                yield return RequestPickup(world.Transport, 22, timeoutGuid, value => disconnectGrant = value);
                world.Transport.ServerDisconnect(22);
                yield return Wait(() => PickupItemSample(timeoutGuid).Value<bool>("pickable"), "断线释放未提交拾取租约", 8d);
                result["disconnect"] = PickupItemSample(timeoutGuid);
            }
            finally
            {
                try
                {
                    foreach (Item owned in ownedItems)
                        if (owned?.itemData != null && ReferenceEquals(ItemMgr.GetInstance()?.GetItemByGuid(owned.itemData.Guid), owned))
                            ItemMgr.GetInstance().DespawnItem(owned, saveData: false);
                }
                finally { world?.Dispose(); }
            }
            yield return null;
            result["itemCleaned"] = ItemMgr.GetInstance()?.GetItemByGuid(itemGuid) == null;
            result["cleanup"] = world.CleanupSample();
            completed(result);
        }
        #endregion

        #region 原始回复与权威物品采样
        private static JObject PickupBotSample(NetworkWorldPlayer player) => new()
        {
            ["profileName"] = player.DisplayName, ["coreProfileName"] = player.CorePlayer?.ProfileName,
            ["netId"] = player.netId
        };

        private static JObject PickupItemSample(int guid)
        {
            Item item = ItemMgr.GetInstance()?.GetItemByGuid(guid);
            return new JObject
            {
                ["exists"] = item != null,
                ["loaded"] = item?.IsInitialized == true,
                ["pickable"] = item?.itemData?.Stack?.CanBePickedUp == true,
                ["amount"] = item?.itemData?.Stack?.Amount ?? 0f
            };
        }

        private static JObject PickupGrantSample(NetworkItemPickupResponse response, int guid)
        {
            bool decoded = ItemNetworkStateSerialization.TryDeserializeItemData(response.Payload, out ItemData payload);
            JObject sample = PickupItemSample(guid);
            sample["responseReceived"] = true; sample["granted"] = response.Granted;
            sample["token"] = response.ReservationToken; sample["payloadDecoded"] = decoded;
            sample["payloadGuid"] = decoded ? payload.Guid : 0;
            sample["payloadAmount"] = decoded ? payload.Stack?.Amount ?? 0f : 0f;
            return sample;
        }

        private static uint nextBotPickupRequest;

        private static float PickupInventoryAmount(Player player, string itemId)
        {
            float amount = 0f;
            var visited = new HashSet<Inventory_Data>();
            Mod_ItemPicker picker = player.GetComponentInChildren<Mod_ItemPicker>(true);
            foreach (IInventory target in picker.AddTargetInventories)
            {
                Inventory_Data data = target?.GetDefaultTargetInventory()?.Data;
                if (data?.itemSlots == null || !visited.Add(data)) continue;
                foreach (ItemSlot slot in data.itemSlots)
                    if (slot?.itemData?.IDName == itemId) amount += slot.Amount;
            }
            return amount;
        }

        private static JObject PickupReceiptSample(NetworkItemPickupReceipt receipt, Player player, string itemId, float before)
        {
            bool decoded = ItemNetworkStateSerialization.TryDeserializeItemData(receipt.InventoryPayload, out ItemData payload);
            return new JObject
            {
                ["accepted"] = receipt.Accepted, ["consumed"] = receipt.ConsumedSourceAmount,
                ["added"] = receipt.AddedAmount, ["remaining"] = receipt.RemainingSourceAmount,
                ["inventoryDelta"] = PickupInventoryAmount(player, itemId) - before,
                ["payloadDecoded"] = decoded, ["payloadGuid"] = decoded ? payload.Guid : 0,
                ["playerGuid"] = player.itemData.Guid
            };
        }

        private static uint BotRealm(int botId) => NetworkServer.connections.TryGetValue(botId, out var connection)
            ? connection.identity?.GetComponent<NetworkWorldPlayer>()?.RealmRevision ?? 0 : 0;

        private static void SubmitPickupCommit(GameplayTestBotTransport transport, int botId, int guid, uint token,
            bool accepted, float? requested = null, uint requestToken = 0)
        {
            foreach (NetworkItemPickupResponse grant in transport.Read<NetworkItemPickupResponse>(botId))
                if (grant.ReservationToken == token)
                {
                    if (requestToken == 0) requestToken = grant.RequestToken;
                    if (!requested.HasValue && ItemNetworkStateSerialization.TryDeserializeItemData(grant.Payload, out ItemData source))
                        requested = source.Stack.Amount;
                }
            if (!transport.Receive(botId, new NetworkItemPickupCommit
                { RealmRevision = BotRealm(botId), ItemGuid = guid, RequestToken = requestToken, ReservationToken = token,
                  RequestedSourceAmount = accepted ? requested ?? 0f : 0f }))
                throw new InvalidOperationException("假人拾取提交没有进入正式服务端传输入口。");
        }

        private static void SubmitPickupApplied(GameplayTestBotTransport transport, int botId, NetworkItemPickupReceipt receipt)
        {
            if (!transport.Receive(botId, new NetworkItemPickupApplied
                { RealmRevision = BotRealm(botId), ItemGuid = receipt.ItemGuid, RequestToken = receipt.RequestToken, ReservationToken = receipt.ReservationToken }))
                throw new InvalidOperationException("假人收货确认没有进入正式服务端传输入口。");
        }

        private static bool SendPickupBaselineCommand(BotWorld world, int botId, NetworkWorldPlayer actor, byte[] payload)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.WriteBytesAndSize(payload);
            writer.WriteVarUInt(actor.RealmRevision);
            const string command = "System.Void FlatWorld.Networking.Gameplay.NetworkWorldPlayer::CmdSubmitItemState(System.Byte[],System.UInt32)";
            return world.Transport.Receive(botId, new CommandMessage
            {
                netId = actor.netId, componentIndex = actor.ComponentIndex,
                functionHash = command.GetStableHashCode16(), payload = writer.ToArraySegment()
            });
        }

        private static IEnumerator WaitPickupReceipt(GameplayTestBotTransport transport, int botId, int previous,
            Action<NetworkItemPickupReceipt> received)
        {
            yield return Wait(() => transport.Count<NetworkItemPickupReceipt>(botId) > previous, "假人收到拾取权威库存回执", 10d);
            received(transport.Read<NetworkItemPickupReceipt>(botId)[previous]);
        }

        // 回复等待只以真实收包计数和真实时间为准，超时明确失败而不制造业务成功。
        private static IEnumerator RequestPickup(GameplayTestBotTransport transport, int botId, int guid,
            Action<NetworkItemPickupResponse> received)
        {
            int previous = transport.Count<NetworkItemPickupResponse>(botId);
            if (!transport.Receive(botId, new NetworkItemPickupRequest { RealmRevision = BotRealm(botId), ItemGuid = guid, RequestToken = ++nextBotPickupRequest }))
                throw new InvalidOperationException("假人拾取请求没有进入正式服务端传输入口。");
            double deadline = Time.realtimeSinceStartupAsDouble + 8d;
            while (transport.Count<NetworkItemPickupResponse>(botId) <= previous)
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("假人没有收到正式拾取响应：" + botId);
                yield return null;
            }
            received(transport.Read<NetworkItemPickupResponse>(botId)[previous]);
        }
        #endregion
    }
}
#endif
