#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static partial class GameplayTestNetworkBots
    {
        #region 联机假人真实动作与现场保留
        // 对应 Mirror Weaver 的正式 CmdSubmitMotion 签名，假人从独立连接发包而非直接改坐标。
        private const string MotionCommandName =
            "System.Void FlatWorld.Networking.Gameplay.NetworkWorldPlayer::CmdSubmitMotion(UnityEngine.Vector3,UnityEngine.Vector2,System.Boolean,FlatWorld.Networking.Gameplay.NetworkPlayerVisualState)";

        private static BotActionScene activeActionScene;

        private sealed class BotActionScene : IDisposable
        {
            public readonly BotWorld World;
            public readonly List<int> OwnedItemGuids = new();
            private bool disposed;

            public BotActionScene(BotWorld world)
            {
                World = world;
                GameplayTestRuntimeProbe.RegisterCleanup(this);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (int guid in OwnedItemGuids)
                {
                    Item item = ItemMgr.GetInstance()?.GetItemByGuid(guid);
                    if (item != null) ItemMgr.GetInstance().DespawnItem(item, saveData: false);
                }
                World.Dispose();
                if (ReferenceEquals(activeActionScene, this)) activeActionScene = null;
            }
        }

        private static IEnumerator CaptureActions(Player local, JObject args, Action<JObject> completed)
        {
            if (activeActionScene != null)
                throw new InvalidOperationException("上一份联机假人动作现场尚未清理。");

            var world = new BotWorld(local);
            var scene = new BotActionScene(world);
            activeActionScene = scene;
            Vector3 localStart = WorldLocalPresentation.ToLogical(local.transform.position);
            if (!GameManager.Instance.TryGetNearestLandSpawnPosition(localStart + Vector3.right * 8f, out Vector3 botSpawn) ||
                WorldTopologyRuntime.Distance(localStart, botSpawn) < 5f)
                throw new InvalidOperationException("无法在不干扰本地玩家的区域出生联机假人。");
            world.PlaceSpawn(world.Prefix + "行动甲", botSpawn);
            world.PlaceSpawn(world.Prefix + "行动乙", botSpawn + Vector3.up * 3f);
            yield return world.Join(81, world.Prefix + "行动甲");
            yield return world.Join(82, world.Prefix + "行动乙");
            NetworkWorldPlayer actor = world.Core(81), witness = world.Core(82);
            // 普通远端副本刻意不完整加载模块；行动夹具按正式服务器战斗权威入口启用实际模块。
            ItemMgr.Instance.InitializeNetworkAuthorityPlayer(actor.CorePlayer);
            yield return Wait(() => actor.CorePlayer?.IsInitialized == true, "假人服务器权威角色加载");

            Vector3 start = actor.AuthoritativeLogicalPosition;
            var motionSamples = new JArray();
            for (int step = 1; step <= 6; step++)
            {
                Vector3 next = WorldTopologyRuntime.NormalizePosition(start + Vector3.right * (.4f * step));
                if (!SendBotMotion(world, 81, actor, next, Vector2.right * 3f))
                    throw new InvalidOperationException("假人正式移动命令没有进入 Mirror 服务端。");
                yield return null;
                motionSamples.Add(new JObject
                {
                    ["x"] = actor.AuthoritativeLogicalPosition.x,
                    ["y"] = actor.AuthoritativeLogicalPosition.y
                });
            }
            if (!SendBotMotion(world, 81, actor, actor.AuthoritativeLogicalPosition, Vector2.zero))
                throw new InvalidOperationException("假人停止移动命令发送失败。");
            yield return null;
            Vector3 finish = actor.AuthoritativeLogicalPosition;
            Vector3 body = WorldLocalPresentation.ToLogical(actor.CorePlayer.transform.position);
            var result = new JObject
            {
                ["scope"] = "live-isolated-world", ["transport"] = "mirror-in-memory",
                ["botA"] = PickupBotSample(actor), ["botB"] = PickupBotSample(witness),
                ["movement"] = new JObject
                {
                    ["fromX"] = start.x, ["toX"] = finish.x, ["bodyX"] = body.x,
                    ["bodyY"] = body.y, ["authoritativeY"] = finish.y,
                    ["commands"] = motionSamples, ["coreInitialized"] = actor.CorePlayer.IsInitialized
                }
            };

            // 真实背包装备武器并触发武器模块的攻击窗口，不以发送攻击日志冒充挥击。
            Mod_HotBar hotbar = actor.CorePlayer.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            if (hotbar?.Data?.itemSlots == null) throw new InvalidOperationException("假人缺少真实快捷栏。");
            int weaponSlot = FindEmptyHotbarSlot(hotbar);
            if (!GameRes.ExistingInstance.TryGetItemDefinition("Knife_Flint", out RuntimeItemDefinition knifeDefinition))
                throw new InvalidOperationException("缺少 Knife_Flint 武器定义。");
            ItemData knife = knifeDefinition.CreateItemData();
            knife.Guid = ItemMgr.Instance.GenerateGuid();
            knife.Stack.Amount = 1;
            hotbar.Data.SetOne_ItemData(weaponSlot, knife);
            if (!hotbar.TrySelectSlot(weaponSlot)) throw new InvalidOperationException("假人无法选择真实武器槽。");
            yield return Wait(() => hotbar.CurentSelectItem != null, "假人武器实体生成", 5d);
            Item held = hotbar.CurentSelectItem;
            Mod_Weapon_AnimationAction weapon = held.GetComponentInChildren<Mod_Weapon_AnimationAction>(true);
            Mod_Damage damage = held.GetComponentInChildren<Mod_Damage>(true);
            if (weapon == null || damage == null) throw new InvalidOperationException("假人武器缺少正式武器动作或伤害模块。");
            bool attacking = weapon.TryRequestAttack(false);
            // 由正式武器播放挥击，命中窗口复用攻击事件实际使用的伤害模块入口。
            if (attacking) damage.StartAttack();
            bool activeDamage = damage.IsDamageEnabled();
            yield return null;
            damage.StopAttack();
            result["attack"] = new JObject
            {
                ["weapon"] = held.itemData.IDName, ["started"] = attacking,
                ["damageWindowOpened"] = activeDamage, ["damageWindowClosed"] = !damage.IsDamageEnabled()
            };

            // 掉落物先由正式拾取租约锁定，再将真实回复载荷放入该假人的测试背包。
            Item loot = ItemMgr.Instance.InstantiateItem("Ore_Copper", finish + new Vector3(.5f, 0f),
                Quaternion.identity, Vector3.one);
            scene.OwnedItemGuids.Add(loot.itemData.Guid);
            int lootGuid = loot.itemData.Guid;
            loot.Load();
            yield return null;
            JObject beforePickup = PickupItemSample(lootGuid);
            NetworkItemPickupResponse grant = default;
            yield return RequestPickup(world.Transport, 81, lootGuid, response => grant = response);
            if (!grant.Granted || !ItemNetworkStateSerialization.TryDeserializeItemData(grant.Payload, out ItemData received))
                throw new InvalidOperationException("假人正式拾取未授予可解码的物品载荷。");
            int inventorySlot = FindEmptyHotbarSlot(hotbar);
            hotbar.Data.SetOne_ItemData(inventorySlot, received);
            ItemSlot slot = hotbar.Data.GetItemSlot(inventorySlot);
            bool inventoryReceived = slot?.itemData?.IDName == received.IDName && slot.Amount == received.Stack.Amount;
            if (!inventoryReceived) throw new InvalidOperationException("假人测试背包未接收真实拾取物品。");
            SubmitPickupCommit(world.Transport, 81, lootGuid, grant.ReservationToken, true);
            yield return null;
            result["pickup"] = new JObject
            {
                ["worldItemBefore"] = beforePickup["exists"],
                ["responseGranted"] = grant.Granted, ["receivedItem"] = received.IDName,
                ["inventoryReceived"] = inventoryReceived,
                ["worldItemRemoved"] = !PickupItemSample(lootGuid).Value<bool>("exists"),
                ["realClientInventoryVerified"] = false
            };

            Mod_DiscardItem discard = actor.CorePlayer.GetComponentInChildren<Mod_DiscardItem>(true);
            if (discard == null) throw new InvalidOperationException("假人没有正式丢弃模块。");
            HashSet<int> priorGuids = new(ItemMgr.Instance.WorldRunTimeItems.Values
                .Where(item => item?.itemData != null).Select(item => item.itemData.Guid));
            discard.DropItemByCountAtWorldPosition(slot, 1, finish + Vector3.right);
            Item thrown = null;
            yield return Wait(() =>
            {
                thrown = ItemMgr.Instance.WorldRunTimeItems.Values.FirstOrDefault(item => item != null &&
                    item.itemData?.IDName == "Ore_Copper" && !priorGuids.Contains(item.itemData.Guid));
                return thrown != null;
            }, "正式丢弃物落入真实世界", 5d);
            scene.OwnedItemGuids.Add(thrown.itemData.Guid);
            result["drop"] = new JObject
            {
                ["slotCleared"] = slot.itemData == null || slot.Amount == 0,
                ["worldItemExists"] = thrown.IsInitialized,
                ["itemId"] = thrown.itemData.IDName,
                ["nearBot"] = WorldTopologyRuntime.Distance(actor.AuthoritativeLogicalPosition,
                    WorldLocalPresentation.ToLogical(thrown.transform.position)) < 4f
            };
            result["localUnmoved"] = WorldTopologyRuntime.Distance(localStart,
                WorldLocalPresentation.ToLogical(local.transform.position)) < .25f;
            completed(result);
        }

        private static IEnumerator CaptureActionsCleanup(Player local, JObject args, Action<JObject> completed)
        {
            BotActionScene scene = activeActionScene ?? throw new InvalidOperationException("没有可回收的假人动作现场。");
            BotWorld world = scene.World;
            scene.Dispose();
            yield return null;
            completed(new JObject { ["cleanup"] = world.CleanupSample() });
        }

        private static int FindEmptyHotbarSlot(Mod_HotBar hotbar)
        {
            for (int index = 0; index < hotbar.MaxIndex; index++)
                if (hotbar.Data.GetItemSlot(index)?.itemData == null) return index;
            throw new InvalidOperationException("联机假人快捷栏没有空余的测试槽位。");
        }

        private static bool SendBotMotion(BotWorld world, int botId, NetworkWorldPlayer actor,
            Vector3 target, Vector2 velocity)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.WriteVector3(target);
            writer.WriteVector2(velocity);
            writer.WriteBool(false);
            writer.Write(new NetworkPlayerVisualState { IsMoving = velocity.sqrMagnitude > 0f });
            return world.Transport.Receive(botId, new CommandMessage
            {
                netId = actor.netId, componentIndex = actor.ComponentIndex,
                functionHash = MotionCommandName.GetStableHashCode16(), payload = writer.ToArraySegment()
            });
        }
        #endregion
    }
}
#endif
