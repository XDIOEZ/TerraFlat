#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Spaceflight;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>隔离世界中的联机假人通过正式 Mirror 消息入口采样，业务断言由 Lua 持有。</summary>
    public static partial class GameplayTestNetworkBots
    {
        #region Lua 操作登记与有界等待
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register()
        {
            GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_session", CaptureSession);
            GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_rejection", CaptureRejection);
            GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_input", CaptureInput);
            GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_pickup", CapturePickup);
            GameplayTestRuntimeProbe.RegisterAsync("networking", "bots_reconnect", CaptureReconnect);
        }

        private static IEnumerator Wait(Func<bool> ready, string label, double seconds = 12d)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!ready())
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("假人联机等待超时：" + label);
                yield return null;
            }
        }
        #endregion

        #region 独立玩家、正式入世快照与重复就绪
        private static IEnumerator CaptureSession(Player local, JObject args, Action<JObject> completed)
        {
            int count = args.Value<int?>("count") ?? 4;
            if (count < 2 || count > 8) throw new ArgumentOutOfRangeException(nameof(args), "假人数量限 2～8。");
            using var world = new BotWorld(local);
            var result = new JObject { ["transport"] = "mirror-in-memory", ["requestedCount"] = count };
            var players = new JArray();
            for (int i = 0; i < count; i++)
            {
                int id = 31 + i;
                yield return world.Join(id, world.Prefix + "same", i == 0);
                NetworkWorldPlayer bot = world.Core(id);
                uint netId = bot.netId;
                world.Transport.Receive(id, new NetworkWorldReady());
                yield return null;
                JObject sample = BotSample(bot, local);
                sample["connectionId"] = id;
                sample["sameAfterReady"] = world.Core(id) == bot && world.Core(id).netId == netId;
                sample["spawnedBeforeReady"] = world.SpawnedBeforeReady(id);
                sample["snapshot"] = SnapshotSample(world.Transport, id);
                players.Add(sample);
            }
            result["players"] = players;
            result["replayedHandshake"] = world.ReplayedHandshake;
            result["independentData"] = players.Count == count &&
                Enumerable.Range(31, count).Select(id => world.Core(id).CorePlayer.Data).Distinct().Count() == count;
            var inventories = Enumerable.Range(31, count).Select(id => world.Core(id).CorePlayer.Data.ModuleDataDic.Values
                .OfType<Inventory_ModuleData>().ToArray()).ToArray();
            Inventory_ModuleData[] native = inventories.SelectMany(value => value).ToArray();
            result["nativeInventoryModuleCounts"] = new JArray(inventories.Select(value => value.Length));
            // 正式库存由模块状态持有，禁止用已不承载运行库存的旧玩家字典作独立性证据。
            result["independentInventories"] = inventories.All(value => value.Length > 0) &&
                native.All(value => value.Data != null) && native.Distinct().Count() == native.Length &&
                native.Select(value => value.Data).Distinct().Count() == native.Length;
            // 实际主角使用长稳定 ID，另准备短名角色来确保覆盖真实的档案名冲突。
            string occupiedProfile = world.Prefix + "occupied";
            world.PlaceSpawn(occupiedProfile, local.Data.transform.position);
            Player occupied = ItemMgr.Instance.LoadNetworkPlayer(occupiedProfile, 0,
                WorldLocalPresentation.ProjectPosition(local.Data.transform.position), false);
            // 这个自有短名占位角色只验证身份，不参与物理拾取。
            foreach (Collider2D collider in occupied.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            yield return world.Join(89, occupiedProfile);
            result["occupiedNameCollision"] = BotSample(world.Core(89), local);
            result["occupiedCoreReused"] = ReferenceEquals(world.Core(89).CorePlayer, occupied);
            result["occupiedProfile"] = occupiedProfile;
            ItemMgr.Instance.ReleaseNetworkPlayer(occupied, persistData: false);
            yield return world.Join(90, local.ProfileName);
            result["localNameCollision"] = BotSample(world.Core(90), local);
            result["liveConnections"] = NetworkServer.connections.Count;
            world.Dispose();
            yield return null;
            result["cleanup"] = world.CleanupSample();
            completed(result);
        }

        private static JObject BotSample(NetworkWorldPlayer bot, Player local) => new()
        {
            ["profile"] = bot.DisplayName, ["netId"] = bot.netId,
            ["guid"] = bot.CorePlayer.itemData.Guid,
            ["profileBound"] = bot.CorePlayer.ProfileName == bot.DisplayName,
            ["isLocalProfile"] = bot.CorePlayer.IsLocalProfile,
            ["sameAsLocal"] = ReferenceEquals(bot.CorePlayer, local),
            ["ready"] = bot.connectionToClient.isReady,
            ["authenticated"] = bot.connectionToClient.isAuthenticated,
            ["indexed"] = ReferenceEquals(ItemMgr.Instance.Player_DIC[bot.DisplayName], bot.CorePlayer)
        };

        private static JObject SnapshotSample(GameplayTestBotTransport transport, int id)
        {
            NetworkWorldSnapshotBegin begin = transport.Read<NetworkWorldSnapshotBegin>(id).Last();
            var chunks = transport.Read<NetworkWorldSnapshotChunk>(id).Where(value => value.TransferId == begin.TransferId).ToArray();
            byte[] payload = chunks.OrderBy(value => value.ChunkIndex).SelectMany(value => value.Data).ToArray();
            return new JObject
            {
                ["begins"] = transport.Count<NetworkWorldSnapshotBegin>(id),
                ["declaredChunks"] = begin.ChunkCount, ["receivedChunks"] = chunks.Length,
                ["uniqueIndices"] = chunks.Select(value => value.ChunkIndex).Distinct().Count(),
                ["declaredBytes"] = begin.CompressedBytes, ["receivedBytes"] = payload.Length,
                ["declaredHash"] = begin.PayloadHash, ["actualHash"] = NetworkGameplayProtocol.CalculatePayloadHash(payload),
                ["gzip"] = payload.Length > 2 && payload[0] == 0x1f && payload[1] == 0x8b,
                ["seed"] = begin.Seed, ["expectedSeed"] = SaveDataMgr.Instance.SaveData.Seed,
                ["generationProtocol"] = begin.GenerationProtocol, ["expectedGenerationProtocol"] = NetworkMapGenerationProtocol.CurrentVersion
            };
        }
        #endregion

        #region 未入世、版本和 MOD 不符拒绝
        private static IEnumerator CaptureRejection(Player local, JObject args, Action<JObject> completed)
        {
            using var world = new BotWorld(local);
            var samples = new JArray();
            world.Transport.ConnectBot(51);
            world.Transport.Receive(51, new NetworkWorldReady());
            world.Transport.Receive(51, new AddPlayerMessage());
            yield return null;
            samples.Add(new JObject { ["kind"] = "no_join", ["spawned"] = world.Core(51) != null,
                ["snapshots"] = world.Transport.Count<NetworkWorldSnapshotBegin>(51) });
            for (int i = 0; i < 3; i++)
            {
                int id = 52 + i;
                world.Transport.ConnectBot(id);
                NetworkProtocolHello hello = Hello();
                if (i == 0) hello.Version--;
                else if (i == 1) hello.ModApiVersion--;
                else hello.ModSetHash = "lua-bot-mismatched-mods";
                world.Transport.Receive(id, hello);
                world.Transport.Receive(id, new NetworkJoinRequest { PlayerName = world.Prefix + id });
                world.Transport.Receive(id, new NetworkWorldReady());
                world.Transport.Receive(id, new AddPlayerMessage());
                yield return Wait(() => !NetworkServer.connections.ContainsKey(id), "拒绝连接断开");
                var replies = world.Transport.Read<NetworkProtocolRejected>(id);
                samples.Add(new JObject { ["kind"] = i == 0 ? "version" : i == 1 ? "mod_api" : "mod_set",
                    ["connected"] = world.Transport.IsBotConnected(id), ["spawned"] = world.Core(id) != null,
                    ["rejections"] = replies.Count, ["reason"] = replies.Count > 0 ? replies[0].Reason : "",
                    ["snapshots"] = world.Transport.Count<NetworkWorldSnapshotBegin>(id) });
            }
            // Hello 不能代替 Join；就绪请求也不能自行通过版本检查。
            world.Transport.ConnectBot(55);
            world.Transport.Receive(55, Hello());
            world.Transport.Receive(55, new NetworkWorldReady());
            yield return null;
            samples.Add(new JObject { ["kind"] = "hello_only", ["spawned"] = world.Core(55) != null,
                ["snapshots"] = world.Transport.Count<NetworkWorldSnapshotBegin>(55) });
            // 旧连接的延迟拒绝回调不得踢掉复用同一连接 ID 的新合法连接。
            world.Transport.ConnectBot(56);
            NetworkProtocolHello invalid = Hello();
            invalid.Version--;
            world.Transport.Receive(56, invalid);
            world.Transport.Receive(56, new NetworkJoinRequest { PlayerName = world.Prefix + "stale" });
            world.Transport.ServerDisconnect(56);
            world.Transport.ConnectBot(56);
            world.Transport.Receive(56, Hello());
            world.Transport.Receive(56, new NetworkJoinRequest { PlayerName = world.Prefix + "fresh" });
            yield return Wait(() => world.Transport.Count<NetworkWorldSnapshotBegin>(56) == 1, "复用连接正常获取世界快照");
            yield return null;
            samples.Add(new JObject { ["kind"] = "stale_rejection", ["connected"] = world.Transport.IsBotConnected(56),
                ["spawned"] = world.Core(56) != null, ["snapshots"] = world.Transport.Count<NetworkWorldSnapshotBegin>(56) });
            world.Dispose();
            yield return null;
            completed(new JObject { ["samples"] = samples, ["cleanup"] = world.CleanupSample() });
        }
        #endregion

        #region 连接绑定输入、乱序拒绝、超时与断线
        private static IEnumerator CaptureInput(Player local, JObject args, Action<JObject> completed)
        {
            using var world = new BotWorld(local);
            yield return world.Join(61, world.Prefix + "A");
            yield return world.Join(62, world.Prefix + "B");
            string a = world.Core(61).DisplayName, b = world.Core(62).DisplayName;
            var result = new JObject();
            void Send(int id, uint sequence, float x, float y = 0f) => world.Transport.Receive(id,
                new NetworkSpaceInput { Sequence = sequence, Input = new SpacePassengerInput { MoveX = x, MoveY = y } }, Channels.Unreliable);
            Send(61, 10, .6f, .8f);
            result["firstA"] = InputSample(world.Session, a);
            result["beforeB"] = InputSample(world.Session, b);
            Send(61, 10, -.4f); result["duplicateA"] = InputSample(world.Session, a);
            Send(61, 9, -.4f); result["staleA"] = InputSample(world.Session, a);
            Send(61, 0, -.4f); result["zeroA"] = InputSample(world.Session, a);
            Send(61, 11, float.NaN); result["nanA"] = InputSample(world.Session, a);
            Send(61, 11, 2f); result["oversizedA"] = InputSample(world.Session, a);
            Send(61, 11, -.5f); result["updatedA"] = InputSample(world.Session, a);
            NetworkServer.connections[61].isReady = false;
            Send(61, 12, .25f); result["notReadyA"] = InputSample(world.Session, a);
            NetworkServer.connections[61].isReady = true;
            Send(61, 12, .25f); result["readyA"] = InputSample(world.Session, a);
            Send(62, 1, -.75f); result["independentB"] = InputSample(world.Session, b);
            result["aAfterB"] = InputSample(world.Session, a);

            world.Transport.Receive(61, new NetworkSpaceIntentRequest { Sequence = 1,
                Intent = new SpaceNetworkIntent { Kind = (SpaceNetworkIntentKind)999 } });
            world.Transport.Receive(61, new NetworkSpaceIntentRequest { Sequence = 1,
                Intent = new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.ToggleDoor, PieceId = "missing" } });
            world.Transport.Receive(62, new NetworkSpaceIntentRequest { Sequence = 1,
                Intent = new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.ToggleDoor, PieceId = "missing" } });
            yield return Wait(() => world.Transport.Count<NetworkSpaceIntentResponse>(61) > 0 &&
                world.Transport.Count<NetworkSpaceIntentResponse>(62) > 0, "各连接独立意图回复");
            result["intentA"] = new JArray(world.Transport.Read<NetworkSpaceIntentResponse>(61).Select(IntentSample));
            result["intentB"] = new JArray(world.Transport.Read<NetworkSpaceIntentResponse>(62).Select(IntentSample));
            // 等待正式 Update 清零最后输入，以状态为条件而非固定延时冒充结果。
            yield return Wait(() => world.Session.TryGetNetworkPassengerInput(a, out var input) && input.MoveX == 0f && input.MoveY == 0f,
                "失联输入超时清零");
            result["timeoutA"] = InputSample(world.Session, a);
            Send(62, 2, -.5f);
            NetworkWorldPlayer old = world.Core(61);
            uint oldId = old.netId;
            world.Transport.ServerDisconnect(61);
            result["disconnectedA"] = InputSample(world.Session, a);
            result["oldSpawnPresent"] = NetworkServer.spawned.ContainsKey(oldId);
            result["oldRuntimePresent"] = ItemMgr.Instance.Player_DIC.ContainsKey(a);
            result["bAfterDisconnect"] = InputSample(world.Session, b);
            result["savedProfileRetained"] = SaveDataMgr.Instance.SaveData.PlayerData_Dict.ContainsKey(a);
            world.Dispose();
            yield return null;
            result["cleanup"] = world.CleanupSample();
            completed(result);
        }

        private static JObject InputSample(SpaceSession session, string profile)
        {
            bool present = session.TryGetNetworkPassengerInput(profile, out SpacePassengerInput input);
            return new JObject { ["present"] = present, ["x"] = input.MoveX, ["y"] = input.MoveY,
                ["turn"] = input.Turn, ["thrust"] = input.Thrust };
        }
        private static JObject IntentSample(NetworkSpaceIntentResponse reply) => new()
        { ["sequence"] = reply.Sequence, ["accepted"] = reply.Accepted, ["reason"] = reply.Reason ?? "" };
        #endregion

        #region 同档案重连与连接 ID 复用
        private static IEnumerator CaptureReconnect(Player local, JObject args, Action<JObject> completed)
        {
            using var world = new BotWorld(local);
            var rounds = new JArray();
            string profile = world.Prefix + "reconnect";
            Data_Player previousData = null;
            uint previousId = 0;
            for (int i = 0; i < 3; i++)
            {
                yield return world.Join(71, profile);
                NetworkWorldPlayer bot = world.Core(71);
                var sample = new JObject { ["profile"] = bot.DisplayName, ["netId"] = bot.netId,
                    ["sameSavedData"] = i == 0 || ReferenceEquals(previousData, bot.CorePlayer.Data),
                    ["oldSpawnPresent"] = previousId != 0 && NetworkServer.spawned.ContainsKey(previousId),
                    ["connections"] = NetworkServer.connections.Count };
                previousData = bot.CorePlayer.Data;
                previousId = bot.netId;
                world.Transport.Receive(71, new NetworkSpaceInput { Sequence = 1, Input = new SpacePassengerInput { MoveX = .5f } });
                sample["freshSequence"] = InputSample(world.Session, profile);
                world.Transport.ServerDisconnect(71);
                sample["runtimePresentAfterDisconnect"] = ItemMgr.Instance.Player_DIC.ContainsKey(profile);
                sample["inputAfterDisconnect"] = InputSample(world.Session, profile);
                sample["savedAfterDisconnect"] = SaveDataMgr.Instance.SaveData.PlayerData_Dict.ContainsKey(profile);
                rounds.Add(sample);
                yield return null;
            }
            world.Dispose();
            yield return null;
            completed(new JObject { ["rounds"] = rounds, ["cleanup"] = world.CleanupSample() });
        }
        #endregion

        #region 隔离服务器租约与正式入口
        private static NetworkProtocolHello Hello() => new()
        { Version = NetworkGameplayProtocol.CurrentVersion, ModApiVersion = ModRuntimeManager.SupportedApiVersion,
            ModSetHash = ModRuntimeManager.Instance?.ModSetHash ?? "", ModSummary = ModRuntimeManager.Instance?.GetNetworkSummary() ?? "" };

        private sealed class BotWorld : IDisposable
        {
            public readonly GameplayTestBotTransport Transport;
            public readonly FlatWorldGameNetworkManager Manager;
            public readonly SpaceSession Session;
            public readonly string Prefix = "B_" + Guid.NewGuid().ToString("N").Substring(0, 4) + "_";
            private readonly Player local;
            private readonly Data_Player localData;
            private readonly Dictionary<string, Data_Player> originalProfiles;
            private readonly HashSet<string> ownedProfiles = new(StringComparer.Ordinal);
            private readonly Dictionary<int, bool> prematureSpawns = new();
            private readonly HashSet<Component> originalComponents;
            private readonly Mirror.Transport originalTransport, originalActive;
            private readonly InterestManagementBase originalAoi, originalClientAoi;
            private readonly string originalOnline, originalOffline;
            private readonly int originalMax, originalSendRate;
            private readonly Scene initialScene;
            private readonly bool ownsManager;
            private bool disposed;
            public JObject ReplayedHandshake { get; private set; }

            public BotWorld(Player player)
            {
                GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
                if (NetworkServer.active || NetworkClient.active || GameNetwork.IsOnline)
                    throw new InvalidOperationException("假人测试只接管隔离世界的离线会话。");
                local = player; localData = player.Data;
                initialScene = SceneManager.GetActiveScene();
                originalProfiles = new Dictionary<string, Data_Player>(SaveDataMgr.Instance.SaveData.PlayerData_Dict);
                originalActive = Mirror.Transport.active;
                originalAoi = NetworkServer.aoi; originalClientAoi = NetworkClient.aoi;
                Manager = NetworkManager.singleton as FlatWorldGameNetworkManager;
                if (NetworkManager.singleton != null && Manager == null)
                    throw new InvalidOperationException("存在非 FlatWorld 的联机管理器，拒绝替换。");
                if (Manager == null)
                {
                    ownsManager = true;
                    var root = new GameObject("Lua假人隔离联机管理器");
                    Manager = root.AddComponent<FlatWorldGameNetworkManager>();
                    Manager.playerPrefab = Resources.Load<GameObject>("Networking/FlatWorldNetworkPlayer");
                }
                originalComponents = new HashSet<Component>(Manager.GetComponents<Component>());
                originalTransport = Manager.transport; originalOnline = Manager.onlineScene; originalOffline = Manager.offlineScene;
                originalMax = Manager.maxConnections; originalSendRate = Manager.sendRate;
                Transport = Manager.gameObject.AddComponent<GameplayTestBotTransport>();
                try
                {
                    GameplayTestRuntimeProbe.RegisterCleanup(this);
                    Session = SpaceSession.EnsureLoaded();
                    Manager.transport = Transport;
                    // Mirror 已有 singleton 时跳过传输重绑，必须显式切到本轮内存传输。
                    Mirror.Transport.active = Transport;
                    Manager.onlineScene = ""; Manager.offlineScene = ""; Manager.maxConnections = 16;
                    NetworkStartResult start = Manager.StartServer(Manager.GetComponent<kcp2k.KcpTransport>().Port);
                    if (!start.Success) throw new InvalidOperationException("隔离服务器未启动：" + start.Error);
                }
                catch { Dispose(); throw; }
            }

            public NetworkWorldPlayer Core(int id) => NetworkServer.connections.TryGetValue(id, out var connection)
                ? connection.identity?.GetComponent<NetworkWorldPlayer>() : null;
            public bool SpawnedBeforeReady(int id) => prematureSpawns[id];

            public void PlaceSpawn(string profile, Vector3 logicalPosition)
            {
                SeedProfile(profile);
                if (!ownedProfiles.Contains(profile)) throw new InvalidOperationException("不能移动原有玩家档案。");
                SaveDataMgr.Instance.SaveData.PlayerData_Dict[profile].transform.position = logicalPosition;
            }

            public IEnumerator Join(int id, string requested, bool replayHandshake = false)
            {
                // 固定合法陆地只用于出生前置，实际名字由正式服务器分配。
                SeedProfile(requested);
                SeedProfile(requested + "_" + id + "_1");
                Transport.ConnectBot(id);
                Transport.Receive(id, Hello());
                Transport.Receive(id, new NetworkJoinRequest { PlayerName = requested });
                yield return Wait(() => Transport.Count<NetworkWorldSnapshotBegin>(id) > 0 &&
                    Transport.Read<NetworkWorldSnapshotChunk>(id).Count == Transport.Read<NetworkWorldSnapshotBegin>(id).Last().ChunkCount,
                    "完整入世快照");
                if (replayHandshake)
                {
                    int before = Transport.Count<NetworkWorldSnapshotBegin>(id);
                    // 在出生前重发 Join 和错误版本 Hello，验证协议锁定且不重复压缩世界。
                    Transport.Receive(id, new NetworkJoinRequest { PlayerName = requested + "_changed" });
                    NetworkProtocolHello changed = Hello();
                    changed.Version--;
                    Transport.Receive(id, changed);
                    ReplayedHandshake = new JObject
                    {
                        ["snapshotsBefore"] = before,
                        ["snapshotsAfter"] = Transport.Count<NetworkWorldSnapshotBegin>(id),
                        ["notSpawnedBeforeReady"] = Core(id) == null
                    };
                }
                Transport.Receive(id, new AddPlayerMessage());
                yield return null;
                prematureSpawns[id] = Core(id) != null;
                Transport.Receive(id, new NetworkWorldReady());
                yield return Wait(() => Core(id)?.CorePlayer != null, "正式玩家出生");
                if (replayHandshake)
                    ReplayedHandshake["originalProfilePreserved"] = Core(id).DisplayName == requested;
                // 假人通过明确消息拾取，关闭自有自动拾取触发器以免与测试请求竞争。
                foreach (Mod_ItemPicker picker in Core(id).CorePlayer.GetComponentsInChildren<Mod_ItemPicker>(true))
                    foreach (Collider2D trigger in picker.GetComponentsInChildren<Collider2D>(true)) trigger.enabled = false;
                string profile = Core(id).DisplayName;
                if (!originalProfiles.ContainsKey(profile)) ownedProfiles.Add(profile);
            }

            private void SeedProfile(string profile)
            {
                if (SaveDataMgr.Instance.SaveData.PlayerData_Dict.ContainsKey(profile)) return;
                Data_Player data = (Data_Player)GameRes.Instance.GetPrefab("Player").GetComponent<Player>().Get_NewItemData();
                data.Guid = ItemMgr.Instance.GenerateGuid();
                data.Name_User = profile; data.CurrentSceneName = initialScene.name;
                data.transform.position = WorldTopologyRuntime.NormalizePosition(WorldLocalPresentation.ToLogical(local.transform.position));
                data.transform.rotation = Quaternion.identity; data.transform.scale = Vector3.one;
                SaveDataMgr.Instance.SaveData.PlayerData_Dict.Add(profile, data);
                ownedProfiles.Add(profile);
            }

            public JObject CleanupSample() => new()
            {
                ["serverActive"] = NetworkServer.active, ["clientActive"] = NetworkClient.active,
                ["connections"] = NetworkServer.connections.Count, ["spawned"] = NetworkServer.spawned.Count,
                ["localUnchanged"] = ReferenceEquals(ItemMgr.Instance.User_Player, local) && ReferenceEquals(local.Data, localData),
                ["sceneUnchanged"] = SceneManager.GetActiveScene() == initialScene,
                ["transportRestored"] = Mirror.Transport.active == originalActive && (ownsManager || Manager.transport == originalTransport),
                ["profilesRestored"] = SaveDataMgr.Instance.SaveData.PlayerData_Dict.Count == originalProfiles.Count &&
                    originalProfiles.All(value => SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(value.Key, out var data) && ReferenceEquals(value.Value, data)),
                ["botRuntimeLeft"] = ownedProfiles.Count(value => ItemMgr.Instance.Player_DIC.ContainsKey(value)),
                ["botPassengersLeft"] = Session.State.Passengers.Count(value => ownedProfiles.Contains(value.ProfileId))
            };

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (NetworkServer.active) Manager.StopServer();
                }
                finally
                {
                    foreach (string profile in ownedProfiles)
                    {
                        if (ItemMgr.Instance.Player_DIC.TryGetValue(profile, out Player bot) && bot != null)
                            ItemMgr.Instance.ReleaseNetworkPlayer(bot, persistData: false);
                        Session?.RemoveNetworkPassengerInput(profile);
                        Session?.State?.Passengers.RemoveAll(value => value.ProfileId == profile);
                        SaveDataMgr.Instance.SaveData.PlayerData_Dict.Remove(profile);
                    }
                    Manager.transport = originalTransport; Manager.onlineScene = originalOnline; Manager.offlineScene = originalOffline;
                    Manager.maxConnections = originalMax; Manager.sendRate = originalSendRate;
                    NetworkServer.aoi = originalAoi; NetworkClient.aoi = originalClientAoi;
                    foreach (Component component in Manager.GetComponents<Component>())
                        if (component != null && !originalComponents.Contains(component)) UnityEngine.Object.Destroy(component);
                    if (ownsManager) UnityEngine.Object.Destroy(Manager.gameObject);
                    Mirror.Transport.active = originalActive;
                }
            }
        }
        #endregion
    }
}
#endif
