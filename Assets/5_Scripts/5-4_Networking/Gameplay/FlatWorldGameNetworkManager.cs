// AI-Context: FlatWorld Mirror 会话与场景生命周期总控；只在服务器创建权威对象并分配玩家身份。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using System.Linq;
using FlatWorld.Networking.MirrorAdapter;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 正式游戏联机管理器：先同步主机世界快照，客户端完成世界加载后再生成玩家。
    /// </summary>
    public sealed class FlatWorldGameNetworkManager : FlatWorldNetworkManager
    {
        private readonly Dictionary<int, string> pendingPlayerNames = new Dictionary<int, string>();
        private readonly Dictionary<int, NetworkProtocolHello> clientProtocolHellos = new Dictionary<int, NetworkProtocolHello>();
        private readonly HashSet<int> pendingPlayerSpawns = new HashSet<int>();
        private readonly Dictionary<int, NetworkConnectionToClient> worldReadyConnections = new();
        private readonly Dictionary<int, (NetworkConnectionToClient Connection, string World, int Transfer)> pendingBootstrapChunks = new();
        internal int ClientBootstrapTransfer { get; private set; }
        internal string ClientBootstrapWorld { get; private set; }
        private string localPlayerName = NewWorldCreationRequest.CreateRandomPlayerName();
        private string currentPlanetName;
        private Coroutine worldEnterCoroutine;
        private NetworkItemStateCoordinator itemStateCoordinator;
        private NetworkWeatherStateCoordinator weatherStateCoordinator;
        private NetworkSpaceStateCoordinator spaceStateCoordinator;
        private IncomingWorldSnapshot incomingWorldSnapshot;
        private int nextSnapshotTransferId;
        private bool ownsClientWorld;

        private sealed class IncomingWorldSnapshot
        {
            public NetworkWorldSnapshot Metadata;
            public int TransferId;
            public int CompressedBytes;
            public uint PayloadHash;
            public byte[][] Chunks;
            public int ReceivedChunks;
            public float LastReceivedTime;
        }
        private const float SnapshotReceiveIdleTimeoutSeconds = 30f;

        #region 单人私有会话与公开加入
        public bool IsPrivateSession { get; private set; } = true;
        public void PublishSession(ushort port = 7777)
        {
            if (!NetworkServer.active) throw new InvalidOperationException("本机房间尚未启动。");
            // 已经监听的 KCP 端口不能热改；直接提示重开公开，避免界面端口与真实监听不符。
            if (transport.ServerActive() && transport is kcp2k.KcpTransport currentKcp && currentKcp.Port != port)
                throw new InvalidOperationException("房间已在其它端口公开，请先关闭公开再用新端口发布。");
            if (!transport.ServerActive())
            {
                if (transport is kcp2k.KcpTransport kcp) kcp.Port = port;
                transport.ServerStart();
            }
            NetworkServer.maxConnections = maxConnections;
            NetworkServer.listen = true;
            IsPrivateSession = false;
        }
        public void MakeSessionPrivate()
        {
            IsPrivateSession = true;
            NetworkServer.listen = false;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values.ToArray())
                if (connection is not LocalConnectionToClient) connection.Disconnect();
            if (transport.ServerActive()) transport.ServerStop();
        }
        #endregion

        public string GameplayStatus { get; private set; } = "离线";
        public string CurrentPlanetName => currentPlanetName;
        public string LocalPlayerName => localPlayerName;
        public int LastSnapshotBytes { get; private set; }

        public event Action<string, object[]> GameplayStatusChanged;

        public void PrepareLocalPlayer(string playerName)
        {
            localPlayerName = SanitizePlayerName(playerName);
        }

        #region 复用当前世界的局域网会话

        public void PrepareHostWorld()
        {
            SaveDataMgr saveManager = SaveDataMgr.Instance;
            GameManager gameManager = GameManager.Instance;
            Player localPlayer = ItemMgr.Instance?.User_Player;
            if (gameManager == null || !gameManager.IsInGameWorld || !gameManager.IsGameplayReady ||
                saveManager?.SaveData == null || localPlayer == null || !localPlayer.IsInitialized ||
                DayTimeSystem.GetInstance() == null)
                throw new InvalidOperationException("请先进入世界，再从设置中公开到局域网。");

            currentPlanetName = ResolveCurrentPlanetName();
            if (!saveManager.SaveData.PlanetData_Dict.TryGetValue(currentPlanetName, out PlanetData planet) || planet == null)
                throw new InvalidOperationException("当前世界数据尚未就绪，无法公开到局域网。");

            // 公开会话只接管现有角色身份，不再创建第二份世界或改写生成参数。
            localPlayerName = localPlayer.ProfileName;

            // 快照和生成设置哈希必须使用同一份已冻结的生态配置。
            ChunkMgr.Instance?.EnsureActiveEcologyConfiguration();

            SetGameplayStatus("主机世界已准备：{0} / Seed {1}", currentPlanetName, saveManager.SaveData.Seed);
        }

        #endregion

        public override void OnStartServer()
        {
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(true);
            NetworkServer.RegisterHandler<NetworkProtocolHello>(OnServerProtocolHello, false);
            NetworkServer.RegisterHandler<NetworkJoinRequest>(OnServerJoinRequest, false);
            NetworkServer.RegisterHandler<NetworkWorldReady>(OnServerWorldReady, false);
            EnsureItemStateCoordinator().StartServerSide();
            EnsureWeatherStateCoordinator().StartServerSide();
            EnsureSpaceStateCoordinator().StartServerSide();
            base.OnStartServer();
            SetGameplayStatus("服务器已启动，等待玩家");
        }

        public override void OnStartClient()
        {
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(NetworkServer.active);
            NetworkClient.RegisterHandler<NetworkProtocolRejected>(OnClientProtocolRejected, false);
            NetworkClient.RegisterHandler<NetworkWorldSnapshotBegin>(OnClientWorldSnapshotBegin, false);
            NetworkClient.RegisterHandler<NetworkWorldSnapshotChunk>(OnClientWorldSnapshotChunk, false);
            EnsureItemStateCoordinator().StartClientSide();
            EnsureWeatherStateCoordinator().StartClientSide();
            EnsureSpaceStateCoordinator().StartClientSide();
            base.OnStartClient();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
            NetworkClient.Send(new NetworkProtocolHello
            {
                Version = NetworkGameplayProtocol.CurrentVersion,
                ModApiVersion = ModRuntimeManager.SupportedApiVersion,
                ModSetHash = modRuntime?.ModSetHash ?? string.Empty,
                ModSummary = modRuntime?.GetNetworkSummary() ?? string.Empty
            });
            NetworkClient.Send(new NetworkJoinRequest { PlayerName = localPlayerName });
            SetGameplayStatus("已连接，正在同步世界数据");
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient connection)
        {
            if (!CanSpawnJoinedPlayer(connection) || connection.identity != null ||
                !pendingPlayerSpawns.Add(connection.connectionId))
            {
                return;
            }

            StartCoroutine(SpawnPlayerOnLand(connection));
        }

        private IEnumerator SpawnPlayerOnLand(NetworkConnectionToClient requestedConnection)
        {
            int connectionId = requestedConnection.connectionId;
            int searchFrames = 0;
            try
            {
                while (NetworkServer.active &&
                       NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient connection) &&
                       ReferenceEquals(connection, requestedConnection) && CanSpawnJoinedPlayer(connection) && connection.identity == null)
                {
                    string playerName = pendingPlayerNames.TryGetValue(connectionId, out string pendingName)
                        ? pendingName
                        : $"Player_{connectionId}";
                    int playerIndex = NetworkServer.connections.Values.Count(activeConnection =>
                        activeConnection != null && activeConnection.identity != null);

                    SpaceSession space = SpaceSession.EnsureLoaded();
                    bool spaceSpawn = space.TryGetNetworkObserver(playerName, out SpaceNetworkObserver observer) && observer.InSpace;
                    Vector3 spawnPosition = spaceSpawn ? (Vector3)space.ProjectPosition(observer.Position) : default;
                    Player localPlayer = ItemMgr.Instance?.User_Player;
                    bool reuseLocalPlayer = connection is LocalConnectionToClient && localPlayer != null &&
                        localPlayer.ProfileName == playerName;
                    if (reuseLocalPlayer && !spaceSpawn)
                        spawnPosition = WorldTopologyRuntime.NormalizePosition(
                            ShipSurfacePresentation.GroundPosition(localPlayer.gameObject, localPlayer.transform.position));
                    if (spaceSpawn || reuseLocalPlayer || TryResolvePlayerLandSpawnPosition(playerName, playerIndex, out spawnPosition))
                    {
                        if (!spaceSpawn)
                        {
                            string worldKey = ResolveCurrentPlanetName();
                            space.SetNetworkPlayerRealm(playerName, false,
                                new SpaceSectorPosition(default, SpaceVector2.FromVector2(spawnPosition)), worldKey);
                            if (SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player savedPlayer))
                                savedPlayer.CurrentSceneName = worldKey;
                        }
                        GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                        playerObject.name = $"网络玩家_{playerName}";

                        NetworkWorldPlayer player = playerObject.GetComponent<NetworkWorldPlayer>();
                        if (player == null)
                            throw new MissingComponentException("联机玩家预制体缺少 NetworkWorldPlayer");

                        player.InitializeOnServer(playerName, playerIndex);
                        NetworkServer.AddPlayerForConnection(connection, playerObject);
                        if (!spaceSpawn && player.CorePlayer?.Data != null)
                            player.CorePlayer.Data.CurrentSceneName = ResolveCurrentPlanetName();
                        SetGameplayStatus(spaceSpawn ? "玩家已进入：{0}（太空恢复 {1}）" : "玩家已进入：{0}（陆地出生 {1}）", playerName, spawnPosition);
                        yield break;
                    }

                    searchFrames++;
                    if (searchFrames % 120 == 0)
                    {
                        Debug.LogWarning(
                            $"[联机出生] 正在等待陆地生成：player={playerName}, frames={searchFrames}",
                            this);
                    }

                    yield return null;
                }
            }
            finally
            {
                // 旧连接的出生协程不能清掉相同连接 ID 上新连接的出生预约。
                if (!NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient current) ||
                    ReferenceEquals(current, requestedConnection))
                    pendingPlayerSpawns.Remove(connectionId);
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            // 各子系统按连接引用清理；即使 ID 已复用也需释放旧连接独立的请求表。
            spaceStateCoordinator?.RemoveConnection(connection);
            // 连接 ID 复用时旧回调只能释放旧连接，不得清空新连接的握手和出生登记。
            if (!NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) ||
                ReferenceEquals(current, connection))
            {
                pendingPlayerNames.Remove(connection.connectionId);
                clientProtocolHellos.Remove(connection.connectionId);
                pendingPlayerSpawns.Remove(connection.connectionId);
                worldReadyConnections.Remove(connection.connectionId);
                pendingBootstrapChunks.Remove(connection.connectionId);
            }
            base.OnServerDisconnect(connection);
            SetGameplayStatus("玩家已断开（当前 {0} 人）", NetworkServer.connections.Count);
        }

        public override void OnStopClient()
        {
            if (worldEnterCoroutine != null)
                StopCoroutine(worldEnterCoroutine);
            worldEnterCoroutine = null;
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(NetworkServer.active);
            NetworkClient.UnregisterHandler<NetworkProtocolRejected>();
            NetworkClient.UnregisterHandler<NetworkWorldSnapshotBegin>();
            NetworkClient.UnregisterHandler<NetworkWorldSnapshotChunk>();
            incomingWorldSnapshot = null;
            ClientBootstrapTransfer = 0;
            ClientBootstrapWorld = null;
            if (itemStateCoordinator != null)
                itemStateCoordinator.StopClientSide();
            if (weatherStateCoordinator != null)
                weatherStateCoordinator.StopClientSide();
            spaceStateCoordinator?.StopClientSide();
            base.OnStopClient();
            SetGameplayStatus("客户端已停止");
            if (ownsClientWorld)
            {
                ownsClientWorld = false;
                GameManager gameManager = GameManager.ExistingInstance;
                if (gameManager != null && (gameManager.IsInGameWorld || gameManager.IsWorldEntryInProgress))
                    gameManager.StartCoroutine(gameManager.BackToHelloScene_Coroutine(null, saveCurrentGame: false));
            }
        }

        public override void OnStopServer()
        {
            IsPrivateSession = true;
            NetworkServer.listen = true;
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(!NetworkClient.active);
            NetworkServer.UnregisterHandler<NetworkProtocolHello>();
            NetworkServer.UnregisterHandler<NetworkJoinRequest>();
            NetworkServer.UnregisterHandler<NetworkWorldReady>();
            if (itemStateCoordinator != null)
                itemStateCoordinator.StopServerSide();
            if (weatherStateCoordinator != null)
                weatherStateCoordinator.StopServerSide();
            spaceStateCoordinator?.StopServerSide();
            pendingPlayerNames.Clear();
            clientProtocolHellos.Clear();
            pendingPlayerSpawns.Clear();
            worldReadyConnections.Clear();
            pendingBootstrapChunks.Clear();
            base.OnStopServer();
            SetGameplayStatus("服务器已停止");
        }

        private void OnServerProtocolHello(NetworkConnectionToClient connection, NetworkProtocolHello hello)
        {
            // Join 开始后锁定握手版本，避免同一连接中途替换出生协议。
            if (!IsCurrentAuthenticatedConnection(connection) || connection.identity != null ||
                pendingPlayerNames.ContainsKey(connection.connectionId)) return;
            if ((hello.ModSetHash?.Length ?? 0) > 128 || (hello.ModSummary?.Length ?? 0) > 4096)
            {
                connection.Disconnect();
                return;
            }
            clientProtocolHellos[connection.connectionId] = hello;
        }

        private void OnServerJoinRequest(NetworkConnectionToClient connection, NetworkJoinRequest request)
        {
            if (IsPrivateSession && connection is not LocalConnectionToClient)
            { connection.Disconnect(); return; }
            // 每个真实连接只允许启动一次世界快照；重复 Join 不得改名或重复压缩整张地图。
            if (!IsCurrentAuthenticatedConnection(connection) || connection.identity != null ||
                pendingPlayerNames.ContainsKey(connection.connectionId))
                return;
            if (GameManager.ExistingInstance?.IsInGameWorld != true || DayTimeSystem.GetInstance() == null)
            {
                connection.Disconnect();
                return;
            }

            if (!clientProtocolHellos.TryGetValue(connection.connectionId, out NetworkProtocolHello hello) ||
                hello.Version != NetworkGameplayProtocol.CurrentVersion)
            {
                connection.Send(new NetworkProtocolRejected
                {
                    ServerVersion = NetworkGameplayProtocol.CurrentVersion,
                    ClientVersion = hello.Version,
                    Reason = "联机脚本版本不一致，请在两个 Unity 编辑器中按 Ctrl+R 刷新后重试。"
                });
                StartCoroutine(DisconnectProtocolMismatchAfterSend(connection));
                return;
            }

            ModRuntimeManager serverMods = ModRuntimeManager.Instance;
            string serverHash = serverMods?.ModSetHash ?? string.Empty;
            if (hello.ModApiVersion != ModRuntimeManager.SupportedApiVersion ||
                !string.Equals(hello.ModSetHash, serverHash, StringComparison.OrdinalIgnoreCase))
            {
                string serverSummary = serverMods?.GetNetworkSummary() ?? "<未就绪>";
                connection.Send(new NetworkProtocolRejected
                {
                    ServerVersion = NetworkGameplayProtocol.CurrentVersion,
                    ClientVersion = hello.Version,
                    Reason = $"MOD 环境不一致。主机：{serverSummary}；客户端：{hello.ModSummary ?? "<空>"}"
                });
                StartCoroutine(DisconnectProtocolMismatchAfterSend(connection));
                return;
            }

            if (connection is LocalConnectionToClient)
            {
                // 本地主机沿用已加载的存档和玩家引用，不能把自己的快照反序列化覆盖回去。
                pendingPlayerNames[connection.connectionId] = localPlayerName;
                worldReadyConnections[connection.connectionId] = connection;
                OnServerAddPlayer(connection);
                return;
            }

            string playerName = MakeUniquePlayerName(SanitizePlayerName(request.PlayerName), connection.connectionId);

            ItemMgr.Instance.SavePlayer();
            SaveDataMgr.Instance.SaveData.DayTimeData = DayTimeSystem.GetInstance().GetSaveData();
            SpaceSession.Capture(SaveDataMgr.Instance?.SaveData);
            string planetName = ResolveCurrentPlanetName();
            PlanetData synchronizedPlanet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[planetName];
            NormalizeGenerationSettings(synchronizedPlanet);
            ChunkMgr.Instance?.EnsureActiveEcologyConfiguration();
            byte[] snapshot = CompressBootstrapSnapshot(SaveDataMgr.Instance.CaptureNetworkBootstrapSnapshot(playerName, planetName));
            LastSnapshotBytes = snapshot.Length;
            uint generationSettingsHash = NetworkMapGenerationProtocol.CalculateSettingsHash(
                SaveDataMgr.Instance.SaveData.Seed,
                synchronizedPlanet.Radius,
                synchronizedPlanet.SpatialDistanceScale,
                synchronizedPlanet.AutoGenerateMap,
                synchronizedPlanet.ChunkSize.x,
                synchronizedPlanet.ChunkSize.y,
                synchronizedPlanet.TopologyMode,
                ChunkMgr.Instance?.GetActiveGenerationFingerprint() ??
                synchronizedPlanet.Ecology?.Generation?.GenerationFingerprint ??
                synchronizedPlanet.Ecology?.ConfigurationFingerprint ?? 0UL);
            int transferId = unchecked(++nextSnapshotTransferId);
            if (transferId == 0)
                transferId = unchecked(++nextSnapshotTransferId);

            int chunkCount = Mathf.CeilToInt(snapshot.Length / (float)NetworkGameplayProtocol.SnapshotChunkBytes);
            // 快照准备成功后再占用 Join 名额，避免打包异常让本连接无法重试。
            pendingPlayerNames[connection.connectionId] = playerName;
            pendingBootstrapChunks[connection.connectionId] = (connection, planetName, transferId);
            connection.Send(new NetworkWorldSnapshotBegin
            {
                TransferId = transferId,
                PlanetName = planetName,
                Seed = SaveDataMgr.Instance.SaveData.Seed,
                GenerationProtocol = NetworkMapGenerationProtocol.CurrentVersion,
                PlanetRadius = synchronizedPlanet.Radius,
                SpatialDistanceScale = synchronizedPlanet.SpatialDistanceScale,
                AutoGenerateMap = synchronizedPlanet.AutoGenerateMap,
                ChunkSizeX = synchronizedPlanet.ChunkSize.x,
                ChunkSizeY = synchronizedPlanet.ChunkSize.y,
                TopologyMode = (int)synchronizedPlanet.TopologyMode,
                GenerationSettingsHash = generationSettingsHash,
                CompressedBytes = snapshot.Length,
                ChunkCount = chunkCount,
                PayloadHash = NetworkGameplayProtocol.CalculatePayloadHash(snapshot)
            });

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int offset = chunkIndex * NetworkGameplayProtocol.SnapshotChunkBytes;
                int count = Mathf.Min(NetworkGameplayProtocol.SnapshotChunkBytes, snapshot.Length - offset);
                byte[] chunk = new byte[count];
                Buffer.BlockCopy(snapshot, offset, chunk, 0, count);
                connection.Send(new NetworkWorldSnapshotChunk
                {
                    TransferId = transferId,
                    ChunkIndex = chunkIndex,
                    Data = chunk
                });
            }

            SetGameplayStatus("正在向 {0} 同步地图（{1:0.0} KB）", playerName, snapshot.Length / 1024f);
        }

        private IEnumerator DisconnectProtocolMismatchAfterSend(NetworkConnectionToClient connection)
        {
            // Mirror 的可靠消息按发送周期批量刷新，一帧后直接断开可能丢失拒绝原因。
            double sendDeadline = NetworkTime.localTime + NetworkServer.sendInterval;
            do { yield return null; }
            while (NetworkServer.active && NetworkTime.localTime < sendDeadline);
            // 给 NetworkLateUpdate 一次实际刷新机会，再断开原连接。
            yield return null;
            // 延迟拒绝只影响原连接，不得误踢复用了连接 ID 的新玩家。
            if (connection != null && NetworkServer.connections.TryGetValue(connection.connectionId, out var current) &&
                ReferenceEquals(current, connection)) connection.Disconnect();
        }

        private void OnClientProtocolRejected(NetworkProtocolRejected rejected)
        {
            string reason = string.IsNullOrWhiteSpace(rejected.Reason)
                ? $"联机协议不匹配：服务器={rejected.ServerVersion}，客户端={rejected.ClientVersion}"
                : rejected.Reason;
            if (string.IsNullOrWhiteSpace(rejected.Reason))
                SetGameplayStatus("联机协议不匹配：服务器={0}，客户端={1}", rejected.ServerVersion, rejected.ClientVersion);
            else
                SetGameplayStatus(reason);
            Debug.LogError($"[联机] {reason}", this);
        }

        private void OnClientWorldSnapshotBegin(NetworkWorldSnapshotBegin begin)
        {
            int expectedChunks = Mathf.CeilToInt(begin.CompressedBytes / (float)NetworkGameplayProtocol.SnapshotChunkBytes);
            if (begin.TransferId == 0 || begin.CompressedBytes <= 0 ||
                begin.CompressedBytes > NetworkGameplayProtocol.MaxSnapshotBytes ||
                begin.ChunkCount <= 0 || begin.ChunkCount != expectedChunks)
            {
                FailIncomingSnapshot("世界快照头无效：bytes={0}, chunks={1}, expected={2}",
                    begin.CompressedBytes, begin.ChunkCount, expectedChunks);
                return;
            }

            incomingWorldSnapshot = new IncomingWorldSnapshot
            {
                TransferId = begin.TransferId,
                CompressedBytes = begin.CompressedBytes,
                PayloadHash = begin.PayloadHash,
                Chunks = new byte[begin.ChunkCount][],
                LastReceivedTime = Time.unscaledTime,
                Metadata = new NetworkWorldSnapshot
                {
                    TransferId = begin.TransferId,
                    PlanetName = begin.PlanetName,
                    Seed = begin.Seed,
                    GenerationProtocol = begin.GenerationProtocol,
                    PlanetRadius = begin.PlanetRadius,
                    SpatialDistanceScale = begin.SpatialDistanceScale,
                    AutoGenerateMap = begin.AutoGenerateMap,
                    ChunkSizeX = begin.ChunkSizeX,
                    ChunkSizeY = begin.ChunkSizeY,
                    TopologyMode = begin.TopologyMode,
                    GenerationSettingsHash = begin.GenerationSettingsHash
                }
            };

            // 只监测分片接收是否停滞，不限制大世界正常持续传输的总用时。
            StartCoroutine(WatchIncomingWorldSnapshot(incomingWorldSnapshot));
            SetGameplayStatus("正在接收世界快照：{0:0.0} KB / {1} 片", begin.CompressedBytes / 1024f, begin.ChunkCount);
        }

        private IEnumerator WatchIncomingWorldSnapshot(IncomingWorldSnapshot transfer)
        {
            WaitForSecondsRealtime checkInterval = new WaitForSecondsRealtime(1f);
            while (NetworkClient.active && ReferenceEquals(incomingWorldSnapshot, transfer))
            {
                if (Time.unscaledTime - transfer.LastReceivedTime >= SnapshotReceiveIdleTimeoutSeconds)
                {
                    FailIncomingSnapshot("世界快照接收中断：超过30秒没有收到新分片，请重新加入。");
                    yield break;
                }
                yield return checkInterval;
            }
        }

        private void OnClientWorldSnapshotChunk(NetworkWorldSnapshotChunk chunk)
        {
            IncomingWorldSnapshot incoming = incomingWorldSnapshot;
            if (incoming == null || chunk.TransferId != incoming.TransferId)
                return;

            if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= incoming.Chunks.Length || chunk.Data == null)
            {
                FailIncomingSnapshot("世界快照分片无效：index={0}", chunk.ChunkIndex);
                return;
            }

            int offset = chunk.ChunkIndex * NetworkGameplayProtocol.SnapshotChunkBytes;
            int expectedBytes = Mathf.Min(
                NetworkGameplayProtocol.SnapshotChunkBytes,
                incoming.CompressedBytes - offset);
            if (chunk.Data.Length != expectedBytes)
            {
                FailIncomingSnapshot("世界快照分片长度错误：index={0}, actual={1}, expected={2}",
                    chunk.ChunkIndex, chunk.Data.Length, expectedBytes);
                return;
            }

            if (incoming.Chunks[chunk.ChunkIndex] != null)
                return;

            incoming.Chunks[chunk.ChunkIndex] = chunk.Data;
            incoming.LastReceivedTime = Time.unscaledTime;
            incoming.ReceivedChunks++;
            if (incoming.ReceivedChunks != incoming.Chunks.Length)
                return;

            byte[] compressedData = new byte[incoming.CompressedBytes];
            int writeOffset = 0;
            for (int i = 0; i < incoming.Chunks.Length; i++)
            {
                byte[] part = incoming.Chunks[i];
                Buffer.BlockCopy(part, 0, compressedData, writeOffset, part.Length);
                writeOffset += part.Length;
            }

            uint actualHash = NetworkGameplayProtocol.CalculatePayloadHash(compressedData);
            if (writeOffset != incoming.CompressedBytes || actualHash != incoming.PayloadHash ||
                compressedData.Length < 2 || compressedData[0] != 0x1F || compressedData[1] != 0x8B)
            {
                FailIncomingSnapshot("世界快照完整性校验失败：bytes={0}/{1}, hash={2:X8}/{3:X8}",
                    writeOffset, incoming.CompressedBytes, actualHash, incoming.PayloadHash);
                return;
            }

            NetworkWorldSnapshot completed = incoming.Metadata;
            completed.CompressedSaveData = compressedData;
            incomingWorldSnapshot = null;
            OnClientWorldSnapshot(completed);
        }

        private void FailIncomingSnapshot(string reasonTemplate, params object[] arguments)
        {
            incomingWorldSnapshot = null;
            SetGameplayStatus(reasonTemplate, arguments);
            string reason = arguments.Length == 0 ? reasonTemplate : string.Format(reasonTemplate, arguments);
            Debug.LogError($"[联机] {reason}", this);
            if (NetworkClient.active)
                NetworkClient.Disconnect();
        }

        private void OnClientWorldSnapshot(NetworkWorldSnapshot snapshot)
        {
            if (worldEnterCoroutine != null)
                StopCoroutine(worldEnterCoroutine);

            worldEnterCoroutine = StartCoroutine(EnterSynchronizedWorld(snapshot));
        }

        private IEnumerator EnterSynchronizedWorld(NetworkWorldSnapshot snapshot)
        {
            while (SaveDataMgr.Instance == null || GameManager.Instance == null || ChunkMgr.Instance == null)
                yield return null;

            if (GameManager.Instance.IsInGameWorld || GameManager.Instance.IsWorldEntryInProgress)
            {
                FailIncomingSnapshot("请先退出当前世界，再加入其他游戏。");
                yield break;
            }

            if (!TryApplySynchronizedSnapshot(snapshot))
                yield break;

            ClientBootstrapTransfer = snapshot.TransferId;
            ClientBootstrapWorld = snapshot.PlanetName;

            SaveDataMgr.Instance.CurrentContrrolPlayerName = localPlayerName;
            currentPlanetName = snapshot.PlanetName;
            LastSnapshotBytes = snapshot.CompressedSaveData?.Length ?? 0;
            SetGameplayStatus("地图同步完成，正在进入 {0}", currentPlanetName);

            bool sceneReady = false;
            bool accepted = GameManager.Instance.EnterWorldWithExternalPlayer(localPlayerName, currentPlanetName, () =>
            {
                if (!NetworkClient.active)
                    return;
                sceneReady = true;
                NetworkClient.Send(new NetworkWorldReady());
            });
            if (!accepted)
            {
                FailIncomingSnapshot("世界进入流程未能启动。");
                yield break;
            }
            ownsClientWorld = true;
            while (NetworkClient.active && GameManager.Instance.IsWorldEntryInProgress)
                yield return null;
            if (!sceneReady || !GameManager.Instance.IsInGameWorld)
            {
                FailIncomingSnapshot("世界加载失败，联机会话已断开。");
                yield break;
            }

            worldEnterCoroutine = null;
            SetGameplayStatus("世界已就绪：{0} / Seed {1}", currentPlanetName, snapshot.Seed);
        }

        private bool TryApplySynchronizedSnapshot(NetworkWorldSnapshot snapshot)
        {
            try
            {
                SaveDataMgr.Instance.ApplyNetworkBootstrapSnapshot(ExpandBootstrapSnapshot(snapshot.CompressedSaveData));
                RepairSynchronizedPlanet(snapshot);
                return true;
            }
            catch (Exception exception)
            {
                string reason = $"世界快照应用失败：{exception.Message}";
                worldEnterCoroutine = null;
                SetGameplayStatus("世界快照应用失败：{0}", exception.Message);
                Debug.LogError($"[联机] {reason}\n{exception}", this);
                if (NetworkClient.active)
                    NetworkClient.Disconnect();
                return false;
            }
        }

        #region 已认证连接的握手与出生资格
        private void OnServerWorldReady(NetworkConnectionToClient connection, NetworkWorldReady message)
        {
            if (!CanSpawnJoinedPlayer(connection, requireWorldReady: false)) return;
            worldReadyConnections[connection.connectionId] = connection;
            if (connection.identity == null)
                OnServerAddPlayer(connection);
        }

        private bool CanSpawnJoinedPlayer(NetworkConnectionToClient connection, bool requireWorldReady = true)
        {
            // 所有出生入口都要求当前连接完成正式版本、MOD 与入世检查。
            if (GameManager.ExistingInstance?.IsInGameWorld != true || DayTimeSystem.GetInstance() == null ||
                !IsCurrentAuthenticatedConnection(connection) ||
                !pendingPlayerNames.TryGetValue(connection.connectionId, out string name) || string.IsNullOrWhiteSpace(name) ||
                !clientProtocolHellos.TryGetValue(connection.connectionId, out NetworkProtocolHello hello)) return false;
            return (!requireWorldReady || worldReadyConnections.TryGetValue(connection.connectionId, out var ready) && ReferenceEquals(ready, connection)) &&
                hello.Version == NetworkGameplayProtocol.CurrentVersion &&
                hello.ModApiVersion == ModRuntimeManager.SupportedApiVersion &&
                string.Equals(hello.ModSetHash, ModRuntimeManager.Instance?.ModSetHash ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurrentAuthenticatedConnection(NetworkConnectionToClient connection) =>
            connection != null && connection.isAuthenticated &&
            NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) &&
            ReferenceEquals(current, connection);

        internal bool IsAuthorizedBootstrapChunkRequest(NetworkConnectionToClient connection, string worldKey, int transfer, Vector2Int origin)
        {
            if (transfer == 0 || !IsCurrentAuthenticatedConnection(connection) ||
                !pendingBootstrapChunks.TryGetValue(connection.connectionId, out var pending) ||
                !ReferenceEquals(connection, pending.Connection) || pending.Transfer != transfer || pending.World != worldKey ||
                !pendingPlayerNames.TryGetValue(connection.connectionId, out string playerName)) return false;
            int playerIndex = NetworkServer.connections.Values.Count(value => value?.identity != null);
            return TryResolvePlayerLandSpawnPosition(playerName, playerIndex, out Vector3 spawn) &&
                NetworkItemStateCoordinator.IsChunkInsideObserverWindow(worldKey, origin, spawn, 32);
        }

        private static byte[] CompressBootstrapSnapshot(byte[] raw)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        private static byte[] ExpandBootstrapSnapshot(byte[] compressed)
        {
            using var source = new MemoryStream(compressed, false);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + count > NetworkGameplayProtocol.MaxSnapshotBytes)
                    throw new InvalidDataException("世界资料解压后超过大小限制。");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        #endregion

        private void CaptureLoadedChunks()
        {
            if (ChunkMgr.Instance == null || SaveDataMgr.Instance?.Active_PlanetData == null)
                return;

            List<Chunk> chunks = ChunkMgr.Instance.Chunk_Dic_ByPos.Values.Where(chunk => chunk != null).ToList();
            PlanetData planet = SaveDataMgr.Instance.Active_PlanetData;
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i];
                chunk.SaveChunk();
                if (chunk.MapSave != null && !string.IsNullOrEmpty(chunk.MapSave.Name))
                    planet.MapData_Dict[chunk.MapSave.Name] = chunk.MapSave;
            }
        }

        /// <summary>
        /// Unity 的 Vector2Int 不依赖 MemoryPack 的内部布局跨实例传输；区块尺寸走显式字段，
        /// 地图坐标由稳定的字典键重建，避免反序列化后出现异常步长。
        /// </summary>
        private void RepairSynchronizedPlanet(NetworkWorldSnapshot snapshot)
        {
            if (snapshot.GenerationProtocol != NetworkMapGenerationProtocol.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"联机地图生成协议不匹配：主机={snapshot.GenerationProtocol}，客户端={NetworkMapGenerationProtocol.CurrentVersion}");
            }

            if (SaveDataMgr.Instance.SaveData?.PlanetData_Dict == null ||
                !SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(snapshot.PlanetName, out PlanetData planet) ||
                planet == null)
            {
                throw new InvalidOperationException($"快照中缺少星球数据：{snapshot.PlanetName}");
            }

            int chunkSizeX = PlanetData.IsValidChunkDimension(snapshot.ChunkSizeX)
                ? snapshot.ChunkSizeX : PlanetData.DefaultChunkDimension;
            int chunkSizeY = PlanetData.IsValidChunkDimension(snapshot.ChunkSizeY)
                ? snapshot.ChunkSizeY : PlanetData.DefaultChunkDimension;
            SaveDataMgr.Instance.SaveData.Seed = snapshot.Seed == 0 ? 1 : snapshot.Seed;
            SaveDataMgr.Instance.SaveData.SaveSeed = SaveDataMgr.Instance.SaveData.Seed.ToString();
            planet.Radius = Mathf.Max(1, snapshot.PlanetRadius);
            planet.SpatialDistanceScale = PlanetData.NormalizeSpatialDistanceScale(
                snapshot.SpatialDistanceScale);
            planet.AutoGenerateMap = snapshot.AutoGenerateMap;
            planet.ChunkSize = new Vector2Int(chunkSizeX, chunkSizeY);
            if (!System.Enum.IsDefined(typeof(WorldTopologyMode), snapshot.TopologyMode))
                throw new InvalidOperationException($"Invalid synchronized topology mode: {snapshot.TopologyMode}");
            planet.TopologyMode = (WorldTopologyMode)snapshot.TopologyMode;
            if (planet.TopologyMode == WorldTopologyMode.Wrapped &&
                !WorldTopologyBounds.TryCreate(planet, out _))
            {
                throw new InvalidOperationException("Synchronized wrapped-world bounds are invalid.");
            }

            uint localSettingsHash = NetworkMapGenerationProtocol.CalculateSettingsHash(
                SaveDataMgr.Instance.SaveData.Seed,
                planet.Radius,
                planet.SpatialDistanceScale,
                planet.AutoGenerateMap,
                planet.ChunkSize.x,
                planet.ChunkSize.y,
                planet.TopologyMode,
                ChunkMgr.Instance?.GetActiveGenerationFingerprint() ??
                planet.Ecology?.Generation?.GenerationFingerprint ??
                planet.Ecology?.ConfigurationFingerprint ?? 0UL);
            if (localSettingsHash != snapshot.GenerationSettingsHash)
            {
                throw new InvalidOperationException(
                    $"联机噪声参数校验失败：主机={snapshot.GenerationSettingsHash:X8}，客户端={localSettingsHash:X8}");
            }

            if (planet.MapData_Dict == null)
                planet.MapData_Dict = new Dictionary<string, MapSave>();

            Dictionary<string, MapSave> canonicalMaps = new Dictionary<string, MapSave>();
            foreach (KeyValuePair<string, MapSave> pair in planet.MapData_Dict)
            {
                if (pair.Value != null && TryParseChunkPosition(pair.Key, out Vector2Int mapPosition))
                {
                    if (WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds))
                        mapPosition = bounds.NormalizeChunkOrigin(mapPosition);
                    pair.Value.MapPosition = mapPosition;
                    pair.Value.Name = mapPosition.ToString();
                    canonicalMaps[pair.Value.Name] = pair.Value;
                }
                else
                {
                    canonicalMaps[pair.Key] = pair.Value;
                }
            }
            planet.MapData_Dict = canonicalMaps;

            Debug.Log(
                $"[联机地图] 空间距离倍率已同步：Seed={SaveDataMgr.Instance.SaveData.Seed}, " +
                $"SpatialScale={planet.SpatialDistanceScale}, Chunk={planet.ChunkSize.x}x{planet.ChunkSize.y}, " +
                $"Hash={localSettingsHash:X8}, 已有地图数={planet.MapData_Dict.Count}", this);
        }

        private static void NormalizeGenerationSettings(PlanetData planet)
        {
            if (planet == null)
                throw new ArgumentNullException(nameof(planet));

            planet.Radius = Mathf.Max(1, planet.Radius);
            planet.SpatialDistanceScale = PlanetData.NormalizeSpatialDistanceScale(
                planet.SpatialDistanceScale);
            planet.ChunkSize = PlanetData.NormalizeChunkSize(planet.ChunkSize);
            if (planet.TopologyMode != WorldTopologyMode.Infinite &&
                planet.TopologyMode != WorldTopologyMode.Wrapped)
            {
                planet.TopologyMode = WorldTopologyMode.Infinite;
            }
        }

        private static bool TryParseChunkPosition(string value, out Vector2Int position)
        {
            position = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().Trim('(', ')');
            string[] parts = normalized.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out int x) || !int.TryParse(parts[1].Trim(), out int y))
                return false;

            position = new Vector2Int(x, y);
            return true;
        }

        private string ResolveCurrentPlanetName()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsInGameWorld &&
                SaveDataMgr.Instance.SaveData.PlanetData_Dict.ContainsKey(SceneManager.GetActiveScene().name))
                currentPlanetName = SceneManager.GetActiveScene().name;

            if (string.IsNullOrWhiteSpace(currentPlanetName))
                currentPlanetName = SaveDataMgr.Instance.SaveData.PlanetData_Dict.Keys.FirstOrDefault();

            return string.IsNullOrWhiteSpace(currentPlanetName) ? "地球" : currentPlanetName;
        }

        private string MakeUniquePlayerName(string requestedName, int connectionId)
        {
            var used = new HashSet<string>(pendingPlayerNames.Values, StringComparer.Ordinal);
            // 远端不能占用现有角色档案；本地主机仍可接管自己的离线角色。
            if (ItemMgr.Instance != null)
                foreach (var entry in ItemMgr.Instance.Player_DIC)
                    if (entry.Value != null && (connectionId != 0 || !entry.Value.IsLocalProfile)) used.Add(entry.Key);
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection?.identity?.GetComponent<NetworkWorldPlayer>() is NetworkWorldPlayer player) used.Add(player.DisplayName);
            string candidate = requestedName;
            int suffix = 0;
            while (used.Contains(candidate)) candidate = $"{requestedName}_{connectionId}_{++suffix}";
            return candidate;
        }

        private static bool TryResolvePlayerLandSpawnPosition(
            string playerName,
            int playerIndex,
            out Vector3 spawnPosition)
        {
            spawnPosition = Vector3.zero;
            if (GameManager.Instance == null || ChunkMgr.Instance == null)
                return false;

            if (SaveDataMgr.Instance?.SaveData?.PlayerData_Dict != null &&
                SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData) &&
                playerData?.transform != null)
            {
                Vector3 savedPosition = playerData.transform.position;
                if (!float.IsNaN(savedPosition.x) && !float.IsInfinity(savedPosition.x) &&
                    !float.IsNaN(savedPosition.y) && !float.IsInfinity(savedPosition.y) &&
                    savedPosition.sqrMagnitude > 0.0001f)
                {
                    savedPosition.z = 0f;
                    if (GameManager.Instance.IsValidLandSpawnPosition(savedPosition))
                    {
                        spawnPosition = savedPosition;
                        return true;
                    }

                    return GameManager.Instance.TryGetNearestLandSpawnPosition(savedPosition, out spawnPosition);
                }
            }

            if (!GameManager.Instance.TryGetDefaultPlayerSpawnPosition(out Vector3 defaultLandPosition))
                return false;

            if (playerIndex <= 0)
            {
                spawnPosition = defaultLandPosition;
                return true;
            }

            Vector3 offsetPosition = defaultLandPosition + Vector3.right * (playerIndex * 2.5f);
            return GameManager.Instance.TryGetNearestLandSpawnPosition(offsetPosition, out spawnPosition);
        }

        private static string SanitizePlayerName(string playerName)
        {
            string value = string.IsNullOrWhiteSpace(playerName)
                ? NewWorldCreationRequest.CreateRandomPlayerName()
                : playerName.Trim();
            return value.Length <= 16 ? value : value.Substring(0, 16);
        }

        private NetworkItemStateCoordinator EnsureItemStateCoordinator()
        {
            if (itemStateCoordinator == null)
                itemStateCoordinator = GetComponent<NetworkItemStateCoordinator>();
            if (itemStateCoordinator == null)
                itemStateCoordinator = gameObject.AddComponent<NetworkItemStateCoordinator>();
            return itemStateCoordinator;
        }

        private NetworkWeatherStateCoordinator EnsureWeatherStateCoordinator()
        {
            if (weatherStateCoordinator == null)
                weatherStateCoordinator = GetComponent<NetworkWeatherStateCoordinator>();
            if (weatherStateCoordinator == null)
                weatherStateCoordinator = gameObject.AddComponent<NetworkWeatherStateCoordinator>();
            return weatherStateCoordinator;
        }

        #region 太空同步与观察者策略
        private NetworkSpaceStateCoordinator EnsureSpaceStateCoordinator()
        {
            if (spaceStateCoordinator == null)
                spaceStateCoordinator = GetComponent<NetworkSpaceStateCoordinator>() ?? gameObject.AddComponent<NetworkSpaceStateCoordinator>();
            if (GetComponent<NetworkSpaceInterestManagement>() == null)
                gameObject.AddComponent<NetworkSpaceInterestManagement>();
            return spaceStateCoordinator;
        }
        #endregion

        private void SetGameplayStatus(string statusTemplate, params object[] arguments)
        {
            // 保留原始模板供界面按当前语言格式化，日志仍记录实际数值。
            GameplayStatus = arguments.Length == 0 ? statusTemplate : string.Format(statusTemplate, arguments);
            GameplayStatusChanged?.Invoke(statusTemplate, arguments);
            Debug.Log($"[联机] {GameplayStatus}", this);
        }
    }
}
