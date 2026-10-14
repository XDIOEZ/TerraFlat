#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static partial class GameplayTestNetworkBots
    {
        #region 假人通过正式消息请求拾取与提交租约
        // 地表合法提交只证明服务端移除掉落物，真实客户端的背包接收不在本夹具内伪造。
        private static IEnumerator CapturePickup(Player local, JObject args, Action<JObject> completed)
        {
            BotWorld world = null;
            Item ownedItem = null;
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
                ownedItem = ItemMgr.Instance.InstantiateItem("Knife_Flint",
                    a.AuthoritativeLogicalPosition + new Vector3(.5f, 0f, 0f), Quaternion.identity, Vector3.one);
                itemGuid = ownedItem.itemData.Guid;
                ownedItem.Load();
                yield return null;
                result = new JObject
                {
                    ["mode"] = "in-process-server-bots", ["itemGuid"] = itemGuid,
                    ["botA"] = PickupBotSample(a), ["botB"] = PickupBotSample(b),
                    ["sameCorePlayer"] = ReferenceEquals(a.CorePlayer, b.CorePlayer),
                    ["inventoryReceipt"] = "client-side-unverified", ["initial"] = PickupItemSample(itemGuid)
                };

                NetworkItemPickupResponse grant = default;
                yield return RequestPickup(world.Transport, 21, itemGuid, value => grant = value);
                result["aGrant"] = PickupGrantSample(grant, itemGuid);
                NetworkItemPickupResponse denied = default;
                yield return RequestPickup(world.Transport, 22, itemGuid, value => denied = value);
                result["bDenied"] = new JObject { ["responseReceived"] = true, ["granted"] = denied.Granted };

                SubmitPickupCommit(world.Transport, 22, itemGuid, grant.ReservationToken, true);
                yield return null;
                result["afterWrongOwner"] = PickupItemSample(itemGuid);
                uint wrongToken = grant.ReservationToken == uint.MaxValue ? 1u : grant.ReservationToken + 1u;
                SubmitPickupCommit(world.Transport, 21, itemGuid, wrongToken, true);
                yield return null;
                result["afterWrongToken"] = PickupItemSample(itemGuid);

                SubmitPickupCommit(world.Transport, 21, itemGuid, grant.ReservationToken, false);
                yield return null;
                result["afterReject"] = PickupItemSample(itemGuid);

                NetworkItemPickupResponse regrant = default;
                yield return RequestPickup(world.Transport, 21, itemGuid, value => regrant = value);
                result["aRegrant"] = PickupGrantSample(regrant, itemGuid);
                SubmitPickupCommit(world.Transport, 21, itemGuid, regrant.ReservationToken, true);
                yield return null;
                result["afterCommit"] = PickupItemSample(itemGuid);
            }
            finally
            {
                try
                {
                    Item current = itemGuid == 0 ? null : ItemMgr.GetInstance()?.GetItemByGuid(itemGuid);
                    if (current != null && ReferenceEquals(current, ownedItem))
                        ItemMgr.GetInstance().DespawnItem(current, saveData: false);
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

        private static void SubmitPickupCommit(GameplayTestBotTransport transport, int botId, int guid, uint token, bool accepted)
        {
            if (!transport.Receive(botId, new NetworkItemPickupCommit
                { ItemGuid = guid, ReservationToken = token, AcceptedIntoInventory = accepted }))
                throw new InvalidOperationException("假人拾取提交没有进入正式服务端传输入口。");
        }

        // 回复等待只以真实收包计数和真实时间为准，超时明确失败而不制造业务成功。
        private static IEnumerator RequestPickup(GameplayTestBotTransport transport, int botId, int guid,
            Action<NetworkItemPickupResponse> received)
        {
            int previous = transport.Count<NetworkItemPickupResponse>(botId);
            if (!transport.Receive(botId, new NetworkItemPickupRequest { ItemGuid = guid }))
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
