#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
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
            writer.WriteUInt(request);
            writer.WriteVector3(target);
            writer.WriteUInt(realm ?? actor.RealmRevision);
            return world.Transport.Receive(botId, new CommandMessage
            {
                netId = actor.netId, componentIndex = actor.ComponentIndex,
                functionHash = TeleportCommandName.GetStableHashCode16(), payload = writer.ToArraySegment()
            });
        }

        private static JObject TeleportSample(NetworkWorldPlayer actor)
        {
            Vector3 position = actor.AuthoritativeLogicalPosition;
            Vector3 body = WorldLocalPresentation.ToLogical(actor.CorePlayer.transform.position);
            Vector3 saved = actor.CorePlayer.Data.transform.position;
            SpaceSession.Current.TryGetNetworkObserver(actor.DisplayName, out SpaceNetworkObserver observer);
            return new JObject
            {
                ["x"] = position.x, ["y"] = position.y, ["bodyX"] = body.x, ["bodyY"] = body.y,
                ["savedX"] = saved.x, ["savedY"] = saved.y,
                ["observerX"] = observer.Position.LocalMeters.X, ["observerY"] = observer.Position.LocalMeters.Y,
                ["revision"] = observer.RealmRevision, ["shipId"] = observer.ShipId,
                ["velocity"] = actor.CorePlayer.GetComponent<Rigidbody2D>().velocity.magnitude
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
            SendBotTeleport(world, 92, actor, 1, destination);
            yield return Wait(() => WorldTopologyRuntime.ShortestDelta(actor.AuthoritativeLogicalPosition, destination).sqrMagnitude < .00001f,
                "GM 传送更新权威坐标");
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
            yield return Wait(() => WorldTopologyRuntime.ShortestDelta(actor.AuthoritativeLogicalPosition, next).sqrMagnitude < .00001f &&
                SpaceSession.Current.TryGetNetworkObserver(actor.DisplayName, out SpaceNetworkObserver moved) &&
                WorldTopologyRuntime.ShortestDelta(moved.Position.LocalMeters.ToVector2(), next).sqrMagnitude < .00001f,
                "传送后的普通移动沿新落点继续");
            result["nextX"] = next.x;
            result["nextY"] = next.y;
            result["afterMove"] = TeleportSample(actor);
            world.Dispose();
            yield return null;
            result["cleanup"] = world.CleanupSample();
            completed(result);
        }

        #endregion
    }
}
#endif
