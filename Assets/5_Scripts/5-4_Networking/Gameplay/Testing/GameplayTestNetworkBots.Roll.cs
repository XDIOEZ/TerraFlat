#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Reflection;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static partial class GameplayTestNetworkBots
    {
        #region 联机翻滚正式命令夹具
        private const string RollCommandName =
            "System.Void FlatWorld.Networking.Gameplay.NetworkWorldPlayer::CmdRequestRoll(System.UInt32,UnityEngine.Vector2,System.Byte[],System.UInt32)";
        private const string RollStaminaAckCommandName =
            "System.Void FlatWorld.Networking.Gameplay.NetworkWorldPlayer::CmdAcknowledgeRollStamina(System.UInt32,System.Single)";
        private const string ItemStateCommandName =
            "System.Void FlatWorld.Networking.Gameplay.NetworkWorldPlayer::CmdSubmitItemState(System.Byte[],System.UInt32)";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRollRuntime() => RegisterRoll();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterRollEditor() => RegisterRoll();
#endif
        private static void RegisterRoll() => GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_roll", CaptureRoll);

        private static bool SendRollCommand(BotWorld world, NetworkWorldPlayer actor, int botId,
            uint request, Vector2 direction, byte[] baseline, uint? realm = null)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.WriteVarUInt(request);
            writer.WriteVector2(direction);
            writer.WriteBytesAndSize(baseline);
            writer.WriteVarUInt(realm ?? actor.RealmRevision);
            return SendRollCommandBytes(world, actor, botId, RollCommandName, writer);
        }

        private static bool SendRollStaminaAck(BotWorld world, NetworkWorldPlayer actor, int botId, uint request, float remaining)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.WriteVarUInt(request);
            writer.WriteFloat(remaining);
            return SendRollCommandBytes(world, actor, botId, RollStaminaAckCommandName, writer);
        }

        private static bool SendRollItemState(BotWorld world, NetworkWorldPlayer actor, int botId, byte[] payload)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.WriteBytesAndSize(payload);
            writer.WriteVarUInt(actor.RealmRevision);
            return SendRollCommandBytes(world, actor, botId, ItemStateCommandName, writer);
        }

        private static bool SendRollCommandBytes(BotWorld world, NetworkWorldPlayer actor, int botId,
            string command, NetworkWriterPooled writer) => world.Transport.Receive(botId, new CommandMessage
            {
                netId = actor.netId, componentIndex = actor.ComponentIndex,
                functionHash = command.GetStableHashCode16(), payload = writer.ToArraySegment()
            });

        private static IEnumerator CaptureRoll(Player local, JObject args, Action<JObject> completed)
        {
            const int botId = 91;
            using var world = new BotWorld(local);
            yield return world.Join(botId, world.Prefix + "翻滚");
            NetworkWorldPlayer actor = world.Core(botId);
            Player player = actor.CorePlayer;
            Mod_Roll roll = player.itemMods.GetMod_ByID<Mod_Roll>(Mod_Roll.ModuleId) ??
                throw new InvalidOperationException("正式网络玩家缺少翻滚模块。");
            Mod_Stamina stamina = player.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
            Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            stamina.modData.ReadData(ref stamina.Data);
            health.ApplyRemoteNetworkData(player, health._Data);
            stamina.CurrentValue = stamina.MaxValue;
            stamina.modData.WriteData(stamina.Data);
            byte[] beforeRoll = ItemNetworkStateSerialization.Capture(player, true);
            bool baselineReadable = ItemNetworkStateSerialization.TryDeserializeItemData(beforeRoll, out ItemData baselineData);
            bool baselineIdentity = baselineData is Data_Player && baselineData.Guid == player.itemData.Guid &&
                baselineData.IDName == player.itemData.IDName;
            float before = stamina.CurrentValue, hpBefore = health.Hp;
            RigidbodyType2D originalBodyType = body.bodyType;
            bool originalSimulation = body.simulated, originalMoveLock = mover.IsLock;
            var result = new JObject { ["transport"] = "mirror-in-memory", ["scope"] = "live-isolated-world",
                ["beforeStamina"] = before, ["cost"] = roll.StaminaCost, ["moduleInitiallyLoaded"] = roll.IsRuntimeLoaded };

            SendRollCommand(world, actor, botId, 1, Vector2.right, beforeRoll, unchecked(actor.RealmRevision - 1));
            yield return null;
            result["oldRealmRejected"] = !roll.IsRolling && stamina.CurrentValue == before &&
                !health.IsTemporarilyInvulnerable;

            bool rollSent = SendRollCommand(world, actor, botId, 1, Vector2.right, beforeRoll);
            bool canStartImmediately = (bool)typeof(Mod_Roll).GetMethod("CanStartRoll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(roll, null);
            string rollImmediately = DescribeBotCommandAuthority(world, botId, actor, RollCommandName, "lastServerRollRequestId");
            yield return Wait(() => roll.IsRolling, "正式请求启动服务器翻滚", 2d, () =>
                $"已发包={rollSent}，发送时允许启动={canStartImmediately}，启用={roll.Enabled}，组件活动={roll.isActiveAndEnabled}，" +
                $"模块加载={roll.IsRuntimeLoaded}，体力={stamina.CurrentValue}/{stamina.MaxValue}，生命={health.Hp}，" +
                $"地表移动可用={mover.IsSurfaceMotionContextAvailable}，基线可读={baselineReadable}，基线身份匹配={baselineIdentity}，" +
                $"基线类型={baselineData?.GetType().Name}，基线物品={baselineData?.IDName}，角色物品={player.itemData.IDName}，" +
                $"发送时：{rollImmediately}；当前：" +
                DescribeBotCommandAuthority(world, botId, actor, RollCommandName, "lastServerRollRequestId"));
            result["started"] = roll.IsRolling;
            result["invulnerable"] = health.IsTemporarilyInvulnerable;
            result["paidStamina"] = stamina.CurrentValue;
            result["firstSequence"] = roll.Sequence;
            result["hurtDuringRoll"] = health.ForceHurt(1f);
            result["hpDuringRoll"] = health.Hp;
            result["hpBefore"] = hpBefore;
            float paid = stamina.CurrentValue;
            uint sequence = roll.Sequence;

            // 重复请求和扣费前的旧整包都经过正式 Mirror 收发与服务器处理器。
            SendRollCommand(world, actor, botId, 1, Vector2.left, beforeRoll);
            SendRollItemState(world, actor, botId, beforeRoll);
            result["staminaAfterStaleSnapshot"] = stamina.modData.GetData<Mod_Stamina.StaminaData>().CurrentStamina;
            result["sequenceAfterDuplicate"] = roll.Sequence;
            result["directionAfterDuplicateX"] = roll.Direction.x;
            SendRollStaminaAck(world, actor, botId, 1, paid);
            yield return Wait(() => !roll.IsRolling, "服务器翻滚结束撤销无敌", 2d);
            result["invulnerableAfterEnd"] = health.IsTemporarilyInvulnerable;
            result["cooldownAfterEnd"] = roll.CooldownRemaining;
            result["physicalOwnershipUnchanged"] = mover.IsLock == originalMoveLock && body.bodyType == originalBodyType &&
                body.simulated == originalSimulation;
            byte[] paidBaseline = ItemNetworkStateSerialization.Capture(player, true);
            SendRollCommand(world, actor, botId, 2, new Vector2(float.NaN, 0f), paidBaseline);
            result["nonFiniteRejected"] = !roll.IsRolling && roll.Sequence == sequence;
            stamina.CurrentValue = 0f;
            stamina.modData.WriteData(stamina.Data);
            byte[] emptyBaseline = ItemNetworkStateSerialization.Capture(player, true);
            SendRollCommand(world, actor, botId, 3, Vector2.left, emptyBaseline);
            result["emptyStaminaRejected"] = !roll.IsRolling && !health.IsTemporarilyInvulnerable && stamina.CurrentValue == 0f;
            stamina.CurrentValue = paid;
            stamina.modData.WriteData(stamina.Data);
            byte[] nextBaseline = ItemNetworkStateSerialization.Capture(player, true);
            SendRollCommand(world, actor, botId, 4, Vector2.left, nextBaseline);
            yield return Wait(() => roll.IsRolling, "上一轮结束后立即开始第二轮合法翻滚", 2d);
            result["secondStarted"] = roll.IsRolling && roll.Sequence != sequence && roll.Direction.x < 0f;
            result["secondPaidStamina"] = stamina.CurrentValue;
            float secondPaid = stamina.CurrentValue;
            // 暂时解绑动作组件只取消本轮动作，扣费交接仍属于同一网络身份。
            actor.enabled = false;
            actor.enabled = true;
            yield return null;
            // 扣费后正常恢复可能跨帧推进，记录旧包发出前的真实权威体力。
            result["rebindBeforeStaleSnapshot"] = stamina.CurrentValue;
            SendRollItemState(world, actor, botId, nextBaseline);
            result["rebindProtectedStamina"] = stamina.modData.GetData<Mod_Stamina.StaminaData>().CurrentStamina;
            result["rebindRuntimeStamina"] = stamina.CurrentValue;
            result["rebindInvulnerabilityCleared"] = !health.IsTemporarilyInvulnerable;
            SendRollStaminaAck(world, actor, botId, 4, secondPaid);
            world.Transport.ServerDisconnect(botId);
            yield return null;
            result["disconnectedRuntimeRemoved"] = !ItemMgr.Instance.Player_DIC.ContainsKey(actor.DisplayName);
            world.Dispose();
            result["cleanup"] = world.CleanupSample();
            completed(result);
        }
        #endregion
    }
}
#endif
