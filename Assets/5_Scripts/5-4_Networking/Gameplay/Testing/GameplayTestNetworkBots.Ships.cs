#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Structures;
using FlatWorld.Gameplay.Progress;
using System.Linq;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static partial class GameplayTestNetworkBots
    {
        #region 飞船正式服务端拒绝与连接身份采样
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterShipsRuntime() => RegisterShips();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterShipsEditor() => RegisterShips();
#endif
        private static void RegisterShips() => GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_ship_requests", CaptureShipRequests);

        // 假人发送真实 Mirror 消息，只采回复与权威世界；Lua 持有拒绝断言。
        private static IEnumerator CaptureShipRequests(Player local, JObject args, Action<JObject> completed)
        {
            IReadOnlyList<FixedStructureDefinition> templates = null;
            Exception error = null;
            yield return FixedStructureCatalog.LoadAsync(value => templates = value, value => error = value);
            if (error != null) throw error;
            FixedStructureDefinition template = templates?.FirstOrDefault(value => value.Kind == "ship");
            if (template == null) throw new InvalidOperationException("服务器没有测试飞船目录。");
            using var site = new GameplayTestSpaceScenario();
            yield return site.PrepareTemplateSite(template);
            using var world = new BotWorld(local);
            yield return world.Join(94, world.Prefix + "飞船甲");
            yield return world.Join(95, world.Prefix + "飞船乙");
            NetworkWorldPlayer actor = world.Core(94), witness = world.Core(95);
            actor.TryGetSpaceObserver(out SpaceNetworkObserver observer);
            uint netId = actor.netId;
            Player originalCore = actor.CorePlayer;
            Data_Player originalData = actor.CorePlayer.Data;
            int ships = world.Session.State.Ships.Count;
            var samples = new JArray();
            var request = new NetworkFixedShipRequest { Token = 1, WorldKey = observer.WorldKey,
                RealmRevision = unchecked(observer.RealmRevision + 1), TemplateId = "probe:missing-template" };
            world.Transport.Receive(94, request);
            yield return Wait(() => world.Transport.Read<NetworkFixedShipResponse>(94).Any(value => value.Token == 1), "旧世界生成飞船拒绝");
            AddReply("stale_realm", 1);
            request.Token = 2; request.RealmRevision = observer.RealmRevision;
            world.Transport.Receive(94, request);
            yield return Wait(() => world.Transport.Read<NetworkFixedShipResponse>(94).Any(value => value.Token == 2), "不存在的服务器飞船目录拒绝", 60d);
            AddReply("missing_template", 2);
            float next = Time.unscaledTime + .6f;
            yield return Wait(() => Time.unscaledTime >= next, "正式飞船操作冷却");
            request.Token = 3; request.Refill = true; request.TemplateId = string.Empty;
            world.Transport.Receive(94, request);
            yield return Wait(() => world.Transport.Read<NetworkFixedShipResponse>(94).Any(value => value.Token == 3), "无模板船时正式补给拒绝", 60d);
            AddReply("refill_without_ship", 3);
            world.Transport.Receive(94, new NetworkSpaceIntentRequest { Sequence = 1, Intent = new SpaceNetworkIntent
            { Kind = SpaceNetworkIntentKind.LaunchGroundShip, PieceId = int.MinValue.ToString(), RealmRevision = observer.RealmRevision } });
            yield return Wait(() => world.Transport.Read<NetworkSpaceIntentResponse>(94).Any(value => value.Sequence == 1), "不存在的控制台点火拒绝");
            NetworkSpaceIntentResponse launch = world.Transport.Read<NetworkSpaceIntentResponse>(94).Last(value => value.Sequence == 1);
            var result = new JObject
            {
                ["transport"] = "mirror-in-memory", ["samples"] = samples,
                ["launchAccepted"] = launch.Accepted, ["launchReason"] = launch.Reason,
                ["actorIdentityPreserved"] = actor.netId == netId && ReferenceEquals(actor.CorePlayer, originalCore) && ReferenceEquals(actor.CorePlayer.Data, originalData),
                ["independentActors"] = !ReferenceEquals(actor.CorePlayer, witness.CorePlayer) && !ReferenceEquals(actor.CorePlayer, local),
                ["shipCountUnchanged"] = world.Session.State.Ships.Count == ships,
                ["witnessReplies"] = world.Transport.Count<NetworkFixedShipResponse>(95) + world.Transport.Count<NetworkSpaceIntentResponse>(95)
            };
            // 正向流程同样走正式请求，不直接调用生成、补给或装配服务。
            using var shipFixture = new NetworkShipFixture(observer.WorldKey, template.Id, world.Session);
            GameplayTestRuntimeProbe.RegisterCleanup(shipFixture);
            next = Time.unscaledTime + .6f;
            yield return Wait(() => Time.unscaledTime >= next, "正向飞船操作冷却");
            request.Token = 4; request.Refill = false; request.TemplateId = template.Id;
            world.Transport.Receive(94, request);
            yield return Wait(() => world.Transport.Read<NetworkFixedShipResponse>(94).Any(value => value.Token == 4), "正式联机生成飞船", 60d);
            NetworkFixedShipResponse build = world.Transport.Read<NetworkFixedShipResponse>(94).Last(value => value.Token == 4);
            result["buildAccepted"] = build.Accepted; result["buildReason"] = build.Reason;
            if (!build.Accepted) throw new InvalidOperationException("正式联机生成失败：" + build.Reason);
            var entries = shipFixture.CreatedGround();
            result["createdBuildings"] = entries.Count;
            ShipGroundBuildingStore.Entry console = entries.FirstOrDefault(value => Mod_ShipPart.Read(value.Data)?.Kind == ShipPieceKind.Console);
            if (console?.Machine == null) throw new InvalidOperationException("联机生成的真实地面船缺少机器控制台。");
            bool consoleTeleportSent = SendBotTeleport(world, 94, actor, 1, console.Data.transform.position);
            string consoleTeleportImmediately = DescribeBotCommandAuthority(world, 94, actor, TeleportCommandName, "lastServerTeleportRequest");
            yield return Wait(() => ShipGroundAssembly.TryGetGroundConsole(actor.CorePlayer, console.Guid, out MachineEntity node) &&
                node.ElectricalPowerRatio >= .999f, "正式联机角色靠近并给控制台供电", 15d, () =>
                    $"已发包={consoleTeleportSent}，控制台={console.Data.transform.position}，电力比例={console.Machine.ElectricalPowerRatio}，" +
                    $"发送时：{consoleTeleportImmediately}；当前：" +
                    DescribeBotCommandAuthority(world, 94, actor, TeleportCommandName, "lastServerTeleportRequest"));
            actor.TryGetSpaceObserver(out observer);
            request.Token = 5; request.Refill = true; request.TemplateId = string.Empty;
            request.RealmRevision = observer.RealmRevision; request.WorldKey = observer.WorldKey;
            next = Time.unscaledTime + .6f;
            yield return Wait(() => Time.unscaledTime >= next, "正式补给冷却");
            world.Transport.Receive(94, request);
            yield return Wait(() => world.Transport.Read<NetworkFixedShipResponse>(94).Any(value => value.Token == 5), "正式联机补给飞船", 60d);
            NetworkFixedShipResponse refill = world.Transport.Read<NetworkFixedShipResponse>(94).Last(value => value.Token == 5);
            result["refillAccepted"] = refill.Accepted; result["refillReason"] = refill.Reason;
            world.Transport.Receive(94, new NetworkSpaceIntentRequest { Sequence = 2, Intent = new SpaceNetworkIntent
            { Kind = SpaceNetworkIntentKind.LaunchGroundShip, PieceId = console.Guid.ToString(), RealmRevision = observer.RealmRevision } });
            yield return Wait(() => world.Transport.Read<NetworkSpaceIntentResponse>(94).Any(value => value.Sequence == 2), "正式联机地面点火", 30d);
            NetworkSpaceIntentResponse started = world.Transport.Read<NetworkSpaceIntentResponse>(94).Last(value => value.Sequence == 2);
            result["positiveLaunchAccepted"] = started.Accepted; result["positiveLaunchReason"] = started.Reason;
            ShipState launched = world.Session.State.Ships.FirstOrDefault(value => value.Pieces.Any(piece => piece.PieceId == console.Guid.ToString()));
            result["authoritativeShipCreated"] = launched != null;
            result["flightIgnited"] = launched != null && world.Session.GetFlight(launched.ShipId)?.Ignited == true;
            result["groundOwnershipTransferred"] = !ShipGroundBuildingStore.Collect(observer.WorldKey).Any(value => entries.Any(own => own.Guid == value.Guid));
            result["identityAfterLaunch"] = actor.netId == netId && ReferenceEquals(actor.CorePlayer, originalCore) && ReferenceEquals(actor.CorePlayer.Data, originalData);
            shipFixture.Dispose();
            world.Dispose();
            yield return null;
            result["cleanup"] = world.CleanupSample();
            completed(result);

            void AddReply(string kind, uint token)
            {
                NetworkFixedShipResponse reply = world.Transport.Read<NetworkFixedShipResponse>(94).Last(value => value.Token == token);
                samples.Add(new JObject { ["kind"] = kind, ["accepted"] = reply.Accepted, ["reason"] = reply.Reason });
            }
        }
        private sealed class NetworkShipFixture : IDisposable
        {
            private readonly string world, template;
            private readonly SpaceSession session;
            private readonly HashSet<int> previous;
            private readonly HashSet<string> previousShips;
            private bool disposed;
            public NetworkShipFixture(string world, string template, SpaceSession session)
            {
                this.world = world; this.template = template; this.session = session;
                previous = new HashSet<int>(ShipGroundBuildingStore.Collect(world).Select(value => value.Guid));
                previousShips = new HashSet<string>(session.State.Ships.Select(value => value.ShipId));
            }
            public List<ShipGroundBuildingStore.Entry> CreatedGround() => ShipGroundBuildingStore.Collect(world)
                .Where(value => !previous.Contains(value.Guid) &&
                    ItemSpecialDataJsonStore.ReadRoot(value.Data.ItemSpecialData)[ShipFixedStructureBuilder.TemplateMarker]?["templateId"]?.Value<string>() == template).ToList();
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (ShipState ship in session.State.Ships.ToArray())
                    if (!previousShips.Contains(ship.ShipId) && ship.Pieces.Any(piece => int.TryParse(piece.PieceId, out int guid) && !previous.Contains(guid) &&
                        ItemSpecialDataJsonStore.ReadRoot(session.GetPieceData(piece)?.ItemSpecialData)[ShipFixedStructureBuilder.TemplateMarker]?["templateId"]?.Value<string>() == template))
                        foreach (ShipPieceState piece in ship.Pieces.ToArray()) session.RollbackPiece(ship, piece);
                foreach (ShipGroundBuildingStore.Entry entry in CreatedGround()) ShipGroundBuildingStore.RollbackCreated(entry);
            }
        }
        #endregion
    }
}
#endif
