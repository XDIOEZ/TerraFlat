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
        private static void RegisterShips()
        {
            GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_ship_requests", CaptureShipRequests);
            GameplayTestRuntimeProbe.Register("networking", "ship_streaming_boundary_prepare", PrepareShipStreamingBoundary);
            GameplayTestRuntimeProbe.RegisterAsync("networking", "ship_streaming_boundary_sample", CaptureShipStreamingBoundary);
        }

        // 假人发送真实 Mirror 消息，只采回复与权威世界；Lua 持有拒绝断言。
        private static IEnumerator CaptureShipRequests(Player local, JObject args, Action<JObject> completed)
        {
            var streamingWindows = new JArray { CaptureLocalStreamingWindow("before", local) };
            IReadOnlyList<FixedStructureDefinition> templates = null;
            Exception error = null;
            yield return FixedStructureCatalog.LoadAsync(value => templates = value, value => error = value);
            if (error != null) throw error;
            FixedStructureDefinition template = templates?.FirstOrDefault(value => value.Kind == "ship");
            if (template == null) throw new InvalidOperationException("服务器没有测试飞船目录。");
            using var site = new GameplayTestSpaceScenario();
            yield return site.PrepareTemplateSite(template);
            streamingWindows.Add(CaptureLocalStreamingWindow("site-prepared", local));
            using var world = new BotWorld(local);
            yield return world.Join(94, world.Prefix + "飞船甲");
            yield return world.Join(95, world.Prefix + "飞船乙");
            streamingWindows.Add(CaptureLocalStreamingWindow("bots-joined", local));
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
            streamingWindows.Add(CaptureLocalStreamingWindow("bot-at-console", local));
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
            streamingWindows.Add(CaptureLocalStreamingWindow("ship-launched", local));
            shipFixture.Dispose();
            world.Dispose();
            yield return null;
            result["cleanup"] = world.CleanupSample();
            streamingWindows.Add(CaptureLocalStreamingWindow("after-cleanup", local));
            result["streamingWindows"] = streamingWindows;
            completed(result);

            void AddReply(string kind, uint token)
            {
                NetworkFixedShipResponse reply = world.Transport.Read<NetworkFixedShipResponse>(94).Last(value => value.Token == token);
                samples.Add(new JObject { ["kind"] = kind, ["accepted"] = reply.Accepted, ["reason"] = reply.Reason });
            }
        }

        // 比较本地玩家流送目标与云雾采用的实际交接窗口，定位测试前后的画面残留。
        private static JObject CaptureLocalStreamingWindow(string stage, Player local)
        {
            Mod_ChunkLoader loader = local.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
            Mod_Cam cameraModule = local.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);
            Camera camera = cameraModule?.ControllerCamera;
            ChunkMgr manager = ChunkMgr.ExistingInstance;
            Rect active = default, presentation = default, targetActive = default, targetPresentation = default;
            bool presented = loader != null && loader.TryGetLoadingWindowBounds(out active, out presentation);
            bool target = manager != null && manager.TryGetRuntimeWindowBounds(local.transform.position,
                out targetActive, out targetPresentation);
            Vector2 position = local.transform.position;
            return new JObject
            {
                ["stage"] = stage, ["scene"] = local.gameObject.scene.name,
                ["worldKey"] = GameplayWorldRegistry.GetItemWorldKey(local),
                ["executionWorldKey"] = GameplayWorldRegistry.ExecutionWorldKey,
                ["position"] = new JObject { ["x"] = position.x, ["y"] = position.y },
                ["anchor"] = new JObject { ["x"] = WorldLocalPresentation.AnchorPosition.x, ["y"] = WorldLocalPresentation.AnchorPosition.y },
                ["camera"] = camera != null ? new JObject { ["x"] = camera.transform.position.x,
                    ["y"] = camera.transform.position.y, ["size"] = camera.orthographicSize } : null,
                ["loadDistance"] = loader != null ? new JObject { ["x"] = loader.CurrentLoadChunkDistanceXY.x,
                    ["y"] = loader.CurrentLoadChunkDistanceXY.y } : null,
                ["presentationDistance"] = loader != null ? new JObject { ["x"] = loader.CurrentPresentationChunkDistanceXY.x,
                    ["y"] = loader.CurrentPresentationChunkDistanceXY.y } : null,
                ["hasPresentedBounds"] = presented, ["active"] = presented ? WindowRect(active) : null,
                ["presentation"] = presented ? WindowRect(presentation) : null,
                ["hasTargetBounds"] = target, ["targetActive"] = target ? WindowRect(targetActive) : null,
                ["targetPresentation"] = target ? WindowRect(targetPresentation) : null,
                ["playerInActive"] = presented && active.Contains(position),
                ["cameraInActive"] = presented && camera != null && active.Contains(camera.transform.position)
            };
        }

        private static JObject WindowRect(Rect value) => new()
        { ["xMin"] = value.xMin, ["yMin"] = value.yMin, ["xMax"] = value.xMax, ["yMax"] = value.yMax };

        private static ShipStreamingBoundaryFixture streamingBoundary;

        // 使用正式传送与 Lua 自带移动输入复现玩家跨区块后的窗口交接。
        private static JObject PrepareShipStreamingBoundary(Player player, JObject args)
        {
            if (streamingBoundary != null) throw new InvalidOperationException("区块边界现场尚未释放。");
            var fixture = new ShipStreamingBoundaryFixture(player);
            streamingBoundary = fixture;
            GameplayTestRuntimeProbe.RegisterCleanup(fixture);
            fixture.Prepare();
            return new JObject { ["prepared"] = true, ["boundaryX"] = ShipStreamingBoundaryFixture.BoundaryX };
        }

        private static IEnumerator CaptureShipStreamingBoundary(Player player, JObject args, Action<JObject> completed)
        {
            ShipStreamingBoundaryFixture fixture = streamingBoundary ??
                throw new InvalidOperationException("区块边界现场未准备。");
            string stage = args.Value<string>("stage");
            if (stage != "before" && stage != "after") throw new ArgumentException("区块边界阶段无效。");
            yield return new WaitForFixedUpdate();
            yield return Wait(() => fixture.TargetAndPresentedWindowsMatchPlayer(), "本地玩家跨块后真实流送窗口交接", 8d,
                () => fixture.Sample(stage).ToString(Newtonsoft.Json.Formatting.None));
            JObject result = fixture.Sample(stage);
            if (stage == "after")
            {
                fixture.Dispose();
                result["restored"] = fixture.Restored;
            }
            completed(result);
        }

        private sealed class ShipStreamingBoundaryFixture : IDisposable
        {
            internal const float BoundaryX = 176f;
            private readonly Player player;
            private readonly Mod_PlayerTraits traits;
            private readonly Mod_Mover mover;
            private readonly Vector3 originalPosition;
            private readonly IDisposable windSuppression;
            private bool disposed;
            internal bool Restored { get; private set; }

            internal ShipStreamingBoundaryFixture(Player player)
            {
                this.player = player;
                traits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId)
                    ?? throw new InvalidOperationException("区块边界现场缺少正式传送入口。");
                mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)
                    ?? throw new InvalidOperationException("区块边界现场缺少正式移动模块。");
                originalPosition = player.transform.position;
                windSuppression = mover.SuppressWindMotion();
            }

            internal void Prepare()
            {
                ChunkMgr manager = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("区块管理器未就绪。");
                Vector2 left = new(BoundaryX - .5f, 48.5f), right = new(BoundaryX + .5f, 48.5f);
                if (manager.ResolveRuntimeChunkOrigin(left) == manager.ResolveRuntimeChunkOrigin(right) ||
                    !traits.TryTeleportToWorldPosition(right))
                    throw new InvalidOperationException("无法通过正式地表传送准备区块边界。");
            }

            private bool TryWindowBounds(out Rect expected, out Rect target, out Rect presented)
            {
                expected = target = presented = default;
                ChunkMgr manager = ChunkMgr.ExistingInstance;
                Mod_ChunkLoader loader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
                if (manager?.ActiveGenerationProfile == null || loader == null || mover.rb == null ||
                    !manager.TryGetRuntimeWindowBounds(mover.rb.position, out target, out _) ||
                    !loader.TryGetLoadingWindowBounds(out presented, out _)) return false;
                var profile = manager.ActiveGenerationProfile;
                Vector2Int origin = manager.ResolveRuntimeChunkOrigin(mover.rb.position);
                expected = ChunkMgr.ProjectRuntimeWindowBounds(ChunkMgr.CalculateRuntimeWindowBounds(origin,
                    new Vector2Int(profile.Width, profile.Height), loader.CurrentLoadChunkDistanceXY), mover.rb.position);
                return true;
            }

            private static bool SameRect(Rect left, Rect right) =>
                Vector2.Distance(left.min, right.min) < .01f && Vector2.Distance(left.max, right.max) < .01f;

            internal bool TargetAndPresentedWindowsMatchPlayer() =>
                TryWindowBounds(out Rect expected, out Rect target, out Rect presented) &&
                SameRect(expected, target) && SameRect(target, presented);

            internal JObject Sample(string stage)
            {
                bool hasBounds = TryWindowBounds(out Rect expected, out Rect target, out Rect presented);
                Vector2 position = mover.rb.position;
                return new JObject
                {
                    ["stage"] = stage, ["boundaryX"] = BoundaryX,
                    ["position"] = new JObject { ["x"] = position.x, ["y"] = position.y },
                    ["crossed"] = stage == "before" ? position.x < BoundaryX : position.x > BoundaryX,
                    ["targetMatchesPlayerChunk"] = hasBounds && SameRect(expected, target),
                    ["presentedMatchesTarget"] = hasBounds && SameRect(target, presented),
                    ["playerInActive"] = hasBounds && presented.Contains(position),
                    ["expected"] = hasBounds ? WindowRect(expected) : null,
                    ["target"] = hasBounds ? WindowRect(target) : null,
                    ["presented"] = hasBounds ? WindowRect(presented) : null,
                    ["window"] = CaptureLocalStreamingWindow(stage, player)
                };
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    Restored = player != null && traits != null && traits.TryTeleportToWorldPosition(originalPosition) &&
                        Vector2.Distance(player.transform.position, originalPosition) < .01f;
                    if (!Restored) throw new InvalidOperationException("区块边界现场未能恢复原位置。");
                }
                finally
                {
                    windSuppression.Dispose();
                    if (ReferenceEquals(streamingBoundary, this)) streamingBoundary = null;
                }
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
