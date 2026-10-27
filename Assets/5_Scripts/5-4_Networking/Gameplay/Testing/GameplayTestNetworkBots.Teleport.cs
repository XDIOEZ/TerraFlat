#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Reflection;
using FlatWorld.Spaceflight;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static partial class GameplayTestNetworkBots
    {
        #region GM 传送正式命令夹具

        private const string TeleportCommandName =
            "System.Void FlatWorld.Networking.Gameplay.NetworkWorldPlayer::CmdRequestGmTeleport(System.UInt32,UnityEngine.Vector3,System.UInt32)";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterTeleportRuntime() => RegisterTeleport();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterTeleportEditor() => RegisterTeleport();
#endif
        private static void RegisterTeleport() => GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_teleport", CaptureTeleport);

        private static bool SendBotTeleport(BotWorld world, int botId, NetworkWorldPlayer actor, uint request, Vector3 target, uint? realm = null)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            // 与 Mirror Weaver 的变长整型编码保持一致，避免后续坐标错位。
            writer.WriteVarUInt(request);
            writer.WriteVector3(target);
            writer.WriteVarUInt(realm ?? actor.RealmRevision);
            return world.Transport.Receive(botId, new CommandMessage
            {
                netId = actor.netId, componentIndex = actor.ComponentIndex,
                functionHash = TeleportCommandName.GetStableHashCode16(), payload = writer.ToArraySegment()
            });
        }

        // 超时时保留连接和世界门禁的真实状态，便于区分发包与服务端拒绝。
        private static string DescribeBotCommandAuthority(BotWorld world, int botId, NetworkWorldPlayer actor,
            string command, string serverSequenceField)
        {
            bool observed = actor.TryGetSpaceObserver(out SpaceNetworkObserver observer);
            NetworkServer.connections.TryGetValue(botId, out NetworkConnectionToClient connection);
            object sequence = typeof(NetworkWorldPlayer).GetField(serverSequenceField,
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor);
            var handler = Mirror.RemoteCalls.RemoteProcedureCalls.GetDelegate(command.GetStableHashCode16());
            string worldKey = actor.CorePlayer?.Data?.CurrentSceneName;
            string transferReason = null;
            bool transferable = world.Session != null && world.Session.CanTransferToNetworkSurface(worldKey, out transferReason);
            SpacePassengerState passenger = world.Session?.State?.Passengers.Find(value => value.ProfileId == actor.DisplayName);
            string passengerState = passenger == null ? "不存在" :
                $"支撑={passenger.Supported}，已落地={passenger.LandingResolved}，自由坐标={passenger.FreeMotion.PositionMeters.X},{passenger.FreeMotion.PositionMeters.Y}";
            return $"连接={botId}，已连接={world.Transport.IsBotConnected(botId)}，就绪={connection?.isReady}，" +
                $"已认证={connection?.isAuthenticated}，身份匹配={connection?.identity == actor.netIdentity}，" +
                $"命令处理器={handler?.Method.Name}，服务端={actor.isServer}，玩家世界={worldKey}，" +
                $"玩家场景={actor.CorePlayer?.gameObject.scene.name}，观察者存在={observed}，" +
                $"观察世界={observer.WorldKey}，观察太空={observer.InSpace}，观察代际={observer.RealmRevision}，" +
                $"角色太空={actor.IsInSpace}，角色代际={actor.RealmRevision}，迁移中={actor.IsWorldTransferPending}，" +
                $"地表可用={transferable}，地表原因={transferReason}，已处理序号={sequence}，" +
                $"权威位置={actor.AuthoritativeLogicalPosition}，观察坐标={observer.Position.LocalMeters.X},{observer.Position.LocalMeters.Y}，" +
                $"角色物理坐标={actor.CorePlayer?.transform.position}，存档坐标={actor.CorePlayer?.Data?.transform.position}，" +
                $"乘员={passengerState}，会话一致={ReferenceEquals(world.Session, SpaceSession.Current)}，" +
                $"网络在线={FlatWorld.Networking.GameNetwork.IsOnline}，角色={FlatWorld.Networking.GameNetwork.Session.Role}，" +
                $"游戏就绪={GameManager.ExistingInstance?.IsGameplayReady}";
        }

        private static JObject TeleportSample(NetworkWorldPlayer actor)
        {
            Vector3 position = actor.AuthoritativeLogicalPosition;
            Vector3 body = WorldLocalPresentation.ToLogical(actor.CorePlayer.transform.position);
            Rigidbody2D rigidbody = actor.CorePlayer.GetComponent<Rigidbody2D>();
            Vector2 physics = WorldLocalPresentation.ToLogical(rigidbody.position);
            Vector3 saved = actor.CorePlayer.Data.transform.position;
            SpaceSession.Current.TryGetNetworkObserver(actor.DisplayName, out SpaceNetworkObserver observer);
            return new JObject
            {
                ["x"] = position.x, ["y"] = position.y, ["bodyX"] = body.x, ["bodyY"] = body.y,
                ["physicsX"] = physics.x, ["physicsY"] = physics.y,
                ["savedX"] = saved.x, ["savedY"] = saved.y,
                ["observerX"] = observer.Position.LocalMeters.X, ["observerY"] = observer.Position.LocalMeters.Y,
                ["revision"] = observer.RealmRevision, ["shipId"] = observer.ShipId,
                ["velocity"] = rigidbody.velocity.magnitude
            };
        }

        private static IEnumerator CaptureTeleport(Player local, JObject args, Action<JObject> completed)
        {
            using var world = new BotWorld(local);
            yield return world.Join(92, world.Prefix + "传送甲");
            yield return world.Join(93, world.Prefix + "传送乙");
            NetworkWorldPlayer actor = world.Core(92), witness = world.Core(93);
            Vector3 start = actor.AuthoritativeLogicalPosition;
            uint oldRealm = actor.RealmRevision;
            Vector3 destination = WorldTopologyRuntime.NormalizePosition(start + Vector3.right * 48f);
            var result = new JObject
            {
                ["scope"] = "live-isolated-world", ["transport"] = "mirror-in-memory",
                ["start"] = TeleportSample(actor), ["witnessBefore"] = TeleportSample(witness),
                ["targetX"] = destination.x, ["targetY"] = destination.y
            };

            // 普通移动和 GM 请求分别走正式 Mirror 命令，不能放宽普通位移校验。
            SendBotMotion(world, 92, actor, destination, Vector2.zero);
            yield return null;
            result["ordinaryMove"] = TeleportSample(actor);
            result["ordinaryStep"] = WorldTopologyRuntime.ShortestDelta(start, actor.AuthoritativeLogicalPosition).magnitude;
            bool teleportSent = SendBotTeleport(world, 92, actor, 1, destination);
            string teleportImmediately = DescribeBotCommandAuthority(world, 92, actor, TeleportCommandName, "lastServerTeleportRequest");
            yield return Wait(() => WorldTopologyRuntime.ShortestDelta(actor.AuthoritativeLogicalPosition, destination).sqrMagnitude < .00001f,
                "GM 传送更新权威坐标", 12d, () => $"已发包={teleportSent}，目标={destination}；发送时：{teleportImmediately}；当前：" +
                    DescribeBotCommandAuthority(world, 92, actor, TeleportCommandName, "lastServerTeleportRequest"));
            result["teleported"] = TeleportSample(actor);

            Vector3 other = WorldTopologyRuntime.NormalizePosition(destination + Vector3.up * 12f);
            result["otherX"] = other.x;
            result["otherY"] = other.y;
            // 较早世界的合法序号也必须拒绝，且不能消耗新世界的同一序号。
            SendBotTeleport(world, 92, actor, 2, other, oldRealm);
            SendBotTeleport(world, 92, actor, 1, other);
            SendBotTeleport(world, 92, actor, 0, other);
            SendBotTeleport(world, 92, actor, 2, new Vector3(float.NaN, 0f));
            SendBotTeleport(world, 92, actor, 2, new Vector3(float.PositiveInfinity, 0f));
            SendBotTeleport(world, 93, actor, 2, other);
            yield return null;
            result["afterRejected"] = TeleportSample(actor);
            result["witnessAfter"] = TeleportSample(witness);

            SendBotTeleport(world, 92, actor, 2, other);
            yield return Wait(() => WorldTopologyRuntime.ShortestDelta(actor.AuthoritativeLogicalPosition, other).sqrMagnitude < .00001f,
                "旧世界请求拒绝后新世界同序号可以传送");
            result["afterFresh"] = TeleportSample(actor);

            Vector3 next = WorldTopologyRuntime.NormalizePosition(other + Vector3.right * .25f);
            SendBotMotion(world, 92, actor, next, Vector2.zero);
            // 真正的物理同步完成后再比较角色位置，避免把同帧 Transform 延迟误判为移动失败。
            yield return Wait(() => WorldTopologyRuntime.ShortestDelta(actor.AuthoritativeLogicalPosition, next).sqrMagnitude < .00001f &&
                SpaceSession.Current.TryGetNetworkObserver(actor.DisplayName, out SpaceNetworkObserver moved) &&
                WorldTopologyRuntime.ShortestDelta(moved.Position.LocalMeters.ToVector2(), next).sqrMagnitude < .00001f &&
                WorldTopologyRuntime.ShortestDelta(WorldLocalPresentation.ToLogical(actor.CorePlayer.transform.position), next).sqrMagnitude < .00001f &&
                WorldTopologyRuntime.ShortestDelta(WorldLocalPresentation.ToLogical(actor.CorePlayer.GetComponent<Rigidbody2D>().position), next).sqrMagnitude < .00001f,
                "传送后的普通移动沿新落点继续");
            result["nextX"] = next.x;
            result["nextY"] = next.y;
            result["afterMove"] = TeleportSample(actor);
            result["visualOwnership"] = CaptureTeleportVisualOwnership(actor);
            result["respawnReady"] = CaptureRespawnReady(world, actor, next);
            Vector3 afterRespawn = WorldTopologyRuntime.NormalizePosition(next + Vector3.up * .5f);
            SendBotTeleport(world, 92, actor, 3, afterRespawn);
            yield return Wait(() => WorldTopologyRuntime.ShortestDelta(actor.AuthoritativeLogicalPosition, afterRespawn).sqrMagnitude < .00001f,
                "重生就绪确认后的 GM 传送");
            result["respawnTeleportX"] = afterRespawn.x;
            result["respawnTeleportY"] = afterRespawn.y;
            result["afterRespawnTeleport"] = TeleportSample(actor);
            world.Dispose();
            yield return null;
            result["cleanup"] = world.CleanupSample();
            completed(result);
        }

        #endregion

        #region 重生就绪与动画归属采样

        private static JObject CaptureTeleportVisualOwnership(NetworkWorldPlayer actor)
        {
            Mod_Mover mover = actor.CorePlayer.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            Mod_AnimatorController animation = actor.CorePlayer.itemMods.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);
            PropertyInfo ownership = typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.isOwned));
            MethodInfo apply = typeof(NetworkWorldPlayer).GetMethod("ApplyRemoteVisualState", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mover == null || animation == null || !animation.IsSingleSpriteMode || ownership == null || apply == null)
                throw new InvalidOperationException("动画归属夹具缺少正式单图动画、移动或网络入口。");
            bool wasOwned = actor.isOwned, wasMoving = mover.IsMoving;
            float Sample(bool owned, bool moving, bool replicatedMoving)
            {
                // 同步采样内只切换自有假人的归属，观察正式动画入口实际产生的身体转角。
                ownership.SetValue(actor.netIdentity, owned);
                mover.IsMoving = moving;
                animation.SetNetworkPresentation(false, false);
                animation.ResetPresentation();
                apply.Invoke(actor, new object[] { new NetworkPlayerVisualState { IsMoving = replicatedMoving } });
                animation.AdvancePresentation(.05f);
                return Mathf.Abs(Mathf.DeltaAngle(0f, animation.spriteVisualRoot.parent.localEulerAngles.z));
            }
            try
            {
                return new JObject
                {
                    ["ownedMoving"] = Sample(true, true, false),
                    ["ownedIdle"] = Sample(true, false, true),
                    ["remoteMoving"] = Sample(false, false, true),
                    ["remoteIdle"] = Sample(false, true, false)
                };
            }
            finally
            {
                ownership.SetValue(actor.netIdentity, wasOwned);
                mover.IsMoving = wasMoving;
                animation.SetNetworkPresentation(true, false);
                animation.ResetPresentation();
            }
        }

        private static JObject CaptureRespawnReady(BotWorld world, NetworkWorldPlayer actor, Vector3 position)
        {
            Player player = actor.CorePlayer;
            Mod_PlayerDeathState death = player.itemMods.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
            Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            Mod_GameController controller = player.GetComponent<Mod_GameController>();
            if (death?.IsRuntimeLoaded != true || health == null || mover == null || controller == null)
                throw new InvalidOperationException("重生就绪夹具缺少正式玩家模块。");
            death.SetDropAllCarriedItems(false);
            health.Hp = 0f;
            health.ResolveNetworkAuthoritativeDeath("test:respawn_ready");
            var result = new JObject
            {
                ["deadLocked"] = death.IsInDyingState && controller.IsGameplayInputLocked && !mover.enabled,
                ["began"] = actor.TryBeginWorldTransfer()
            };
            uint previousRealm = actor.RealmRevision;
            string worldKey = player.Data.CurrentSceneName;
            world.Session.ResetPassengerForRespawn(player);
            death.CompleteNetworkRespawn();
            result["committed"] = world.Session.CompleteNetworkWorldTransfer(player, worldKey, position);
            world.Session.TryGetNetworkObserver(actor.DisplayName, out SpaceNetworkObserver observer);
            result["authorityWasStale"] = actor.RealmRevision != observer.RealmRevision;
            // 重放主机同帧就绪早于下一轮 Update 的正式调用顺序。
            actor.AcknowledgeWorldTransfer(worldKey, observer.RealmRevision);
            result["pendingAfterReady"] = actor.IsWorldTransferPending;
            result["revisionAfterReady"] = actor.RealmRevision;
            result["expectedRevision"] = observer.RealmRevision;
            result["aliveUnlocked"] = health.Hp > 0f && !death.IsInDyingState && mover.enabled && !controller.IsGameplayInputLocked;

            actor.RefreshSpaceAuthority();
            actor.AcknowledgeWorldTransfer(worldKey, observer.RealmRevision);
            result["beganAgain"] = actor.TryBeginWorldTransfer();
            result["committedAgain"] = world.Session.CompleteNetworkWorldTransfer(player, worldKey, position);
            world.Session.TryGetNetworkObserver(actor.DisplayName, out observer);
            actor.AcknowledgeWorldTransfer(worldKey + ":wrong", observer.RealmRevision);
            result["wrongWorldHeld"] = actor.IsWorldTransferPending;
            SpacePassengerState passenger = world.Session.State.Passengers.Find(value => value.ProfileId == player.ProfileName);
            world.Session.State.Passengers.Remove(passenger);
            try
            {
                actor.AcknowledgeWorldTransfer(worldKey, observer.RealmRevision);
                result["missingObserverHeld"] = actor.IsWorldTransferPending;
            }
            finally { world.Session.State.Passengers.Add(passenger); }
            actor.AcknowledgeWorldTransfer(worldKey, previousRealm);
            result["staleRealmHeld"] = actor.IsWorldTransferPending;
            actor.AcknowledgeWorldTransfer(worldKey, unchecked(observer.RealmRevision + 1));
            result["futureRealmHeld"] = actor.IsWorldTransferPending;
            actor.AcknowledgeWorldTransfer(worldKey, observer.RealmRevision);
            actor.AcknowledgeWorldTransfer(worldKey, observer.RealmRevision);
            result["pendingAfterRepeatedReady"] = actor.IsWorldTransferPending;
            result["rollbackBegan"] = actor.TryBeginWorldTransfer();
            actor.FinishWorldTransfer(false);
            result["pendingAfterRollback"] = actor.IsWorldTransferPending;
            return result;
        }

        #endregion
    }
}
#endif
