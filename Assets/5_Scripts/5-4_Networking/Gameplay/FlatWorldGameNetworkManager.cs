// AI-Context: FlatWorld Mirror 会话与场景生命周期总控；只在服务器创建权威对象并分配玩家身份。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Networking.MirrorAdapter;
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
        private string localPlayerName = "玩家";
        private string currentPlanetName;
        private Coroutine worldEnterCoroutine;
        private NetworkItemStateCoordinator itemStateCoordinator;
        private NetworkWeatherStateCoordinator weatherStateCoordinator;
        private IncomingWorldSnapshot incomingWorldSnapshot;
        private int nextSnapshotTransferId;

        private sealed class IncomingWorldSnapshot
        {
            public NetworkWorldSnapshot Metadata;
            public int TransferId;
            public int CompressedBytes;
            public uint PayloadHash;
            public byte[][] Chunks;
            public int ReceivedChunks;
        }

        public string GameplayStatus { get; private set; } = "离线";
        public string CurrentPlanetName => currentPlanetName;
        public int LastSnapshotBytes { get; private set; }

        public event Action<string> GameplayStatusChanged;

        public void PrepareLocalPlayer(string playerName)
        {
            localPlayerName = SanitizePlayerName(playerName);
        }

        public void PrepareHostWorld()
        {
            SaveDataMgr saveManager = SaveDataMgr.Instance;
            GameManager gameManager = GameManager.Instance;
            if (saveManager == null || gameManager == null)
                throw new InvalidOperationException("核心游戏管理器尚未初始化");

            if (saveManager.SaveData == null)
                saveManager.SaveData = new GameSaveData();

            GameSaveData saveData = saveManager.SaveData;
            if (saveData.Seed == 0)
            {
                int seed = Environment.TickCount;
                if (seed == 0)
                    seed = 1;

                saveData.saveName = "NetworkWorld";
                saveData.SaveSeed = seed.ToString();
                saveData.Seed = seed;
            }

            if (saveData.PlanetData_Dict == null)
                saveData.PlanetData_Dict = new Dictionary<string, PlanetData>();

            PlanetData planet = saveData.PlanetData_Dict.Values.FirstOrDefault(item => item != null);
            if (planet == null)
            {
                PlanetData readyPlanet = gameManager.ReadyPlanetData ?? new PlanetData();
                if (string.IsNullOrWhiteSpace(readyPlanet.Name))
                    readyPlanet.Name = "地球";

                gameManager.SetNewPlanetData(readyPlanet, gameManager.ReadyTimeData);
                planet = saveData.PlanetData_Dict[readyPlanet.Name];
            }

            currentPlanetName = planet.Name;
            if (string.IsNullOrWhiteSpace(currentPlanetName))
            {
                currentPlanetName = "地球";
                planet.Name = currentPlanetName;
                saveData.PlanetData_Dict[currentPlanetName] = planet;
            }

            NormalizeGenerationSettings(planet);

            SetGameplayStatus($"主机世界已准备：{currentPlanetName} / Seed {saveData.Seed}");
        }

        public override void OnStartServer()
        {
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(true);
            NetworkServer.RegisterHandler<NetworkProtocolHello>(OnServerProtocolHello, false);
            NetworkServer.RegisterHandler<NetworkJoinRequest>(OnServerJoinRequest, false);
            NetworkServer.RegisterHandler<NetworkWorldReady>(OnServerWorldReady, false);
            EnsureItemStateCoordinator().StartServerSide();
            EnsureWeatherStateCoordinator().StartServerSide();
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
            if (connection == null || connection.identity != null ||
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
                       connection != null && connection.identity == null)
                {
                    string playerName = pendingPlayerNames.TryGetValue(connectionId, out string pendingName)
                        ? pendingName
                        : $"玩家_{connectionId}";
                    int playerIndex = NetworkServer.connections.Values.Count(activeConnection =>
                        activeConnection != null && activeConnection.identity != null);

                    if (TryResolvePlayerLandSpawnPosition(playerName, playerIndex, out Vector3 spawnPosition))
                    {
                        GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
                        playerObject.name = $"网络玩家_{playerName}";

                        NetworkWorldPlayer player = playerObject.GetComponent<NetworkWorldPlayer>();
                        if (player == null)
                            throw new MissingComponentException("联机玩家预制体缺少 NetworkWorldPlayer");

                        player.InitializeOnServer(playerName, playerIndex);
                        NetworkServer.AddPlayerForConnection(connection, playerObject);
                        SetGameplayStatus($"玩家已进入：{playerName}（陆地出生 {spawnPosition}）");
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
                pendingPlayerSpawns.Remove(connectionId);
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            pendingPlayerNames.Remove(connection.connectionId);
            clientProtocolHellos.Remove(connection.connectionId);
            pendingPlayerSpawns.Remove(connection.connectionId);
            base.OnServerDisconnect(connection);
            SetGameplayStatus($"玩家已断开（当前 {NetworkServer.connections.Count} 人）");
        }

        public override void OnStopClient()
        {
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(NetworkServer.active);
            NetworkClient.UnregisterHandler<NetworkProtocolRejected>();
            NetworkClient.UnregisterHandler<NetworkWorldSnapshotBegin>();
            NetworkClient.UnregisterHandler<NetworkWorldSnapshotChunk>();
            incomingWorldSnapshot = null;
            if (itemStateCoordinator != null)
                itemStateCoordinator.StopClientSide();
            if (weatherStateCoordinator != null)
                weatherStateCoordinator.StopClientSide();
            base.OnStopClient();
            SetGameplayStatus("客户端已停止");
        }

        public override void OnStopServer()
        {
            ModRuntimeManager.Instance?.SetWorldMutationAuthority(!NetworkClient.active);
            NetworkServer.UnregisterHandler<NetworkProtocolHello>();
            NetworkServer.UnregisterHandler<NetworkJoinRequest>();
            NetworkServer.UnregisterHandler<NetworkWorldReady>();
            if (itemStateCoordinator != null)
                itemStateCoordinator.StopServerSide();
            if (weatherStateCoordinator != null)
                weatherStateCoordinator.StopServerSide();
            pendingPlayerNames.Clear();
            clientProtocolHellos.Clear();
            pendingPlayerSpawns.Clear();
            base.OnStopServer();
            SetGameplayStatus("服务器已停止");
        }

        private void OnServerProtocolHello(NetworkConnectionToClient connection, NetworkProtocolHello hello)
        {
            if ((hello.ModSetHash?.Length ?? 0) > 128 || (hello.ModSummary?.Length ?? 0) > 4096)
            {
                connection.Disconnect();
                return;
            }
            clientProtocolHellos[connection.connectionId] = hello;
        }

        private void OnServerJoinRequest(NetworkConnectionToClient connection, NetworkJoinRequest request)
        {
            if (connection.identity != null)
                return;

            if (!clientProtocolHellos.TryGetValue(connection.connectionId, out NetworkProtocolHello hello) ||
                hello.Version != NetworkGameplayProtocol.CurrentVersion)
            {
                connection.Send(new NetworkProtocolRejected
                {
                    ServerVersion = NetworkGameplayProtocol.CurrentVersion,
                    ClientVersion = hello.Version,
                    Reason = "联机脚本版本不一致，请在两个 Unity 编辑器中按 Ctrl+R 刷新后重试。"
                });
                StartCoroutine(DisconnectProtocolMismatchNextFrame(connection));
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
                StartCoroutine(DisconnectProtocolMismatchNextFrame(connection));
                return;
            }

            string playerName = MakeUniquePlayerName(SanitizePlayerName(request.PlayerName), connection.connectionId);
            pendingPlayerNames[connection.connectionId] = playerName;

            CaptureLoadedChunks();
            byte[] snapshot = SaveDataMgr.Instance.CreateCompressedNetworkSnapshot();
            LastSnapshotBytes = snapshot.Length;

            string planetName = ResolveCurrentPlanetName();
            PlanetData synchronizedPlanet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[planetName];
            NormalizeGenerationSettings(synchronizedPlanet);
            uint generationSettingsHash = NetworkMapGenerationProtocol.CalculateSettingsHash(
                SaveDataMgr.Instance.SaveData.Seed,
                synchronizedPlanet.Radius,
                synchronizedPlanet.NoiseScale,
                synchronizedPlanet.AutoGenerateMap,
                synchronizedPlanet.ChunkSize.x,
                synchronizedPlanet.ChunkSize.y,
                synchronizedPlanet.TopologyMode);
            int transferId = unchecked(++nextSnapshotTransferId);
            if (transferId == 0)
                transferId = unchecked(++nextSnapshotTransferId);

            int chunkCount = Mathf.CeilToInt(snapshot.Length / (float)NetworkGameplayProtocol.SnapshotChunkBytes);
            connection.Send(new NetworkWorldSnapshotBegin
            {
                TransferId = transferId,
                PlanetName = planetName,
                Seed = SaveDataMgr.Instance.SaveData.Seed,
                GenerationProtocol = NetworkMapGenerationProtocol.CurrentVersion,
                PlanetRadius = synchronizedPlanet.Radius,
                NoiseScale = synchronizedPlanet.NoiseScale,
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

            SetGameplayStatus($"正在向 {playerName} 同步地图（{snapshot.Length / 1024f:0.0} KB）");
        }

        private IEnumerator DisconnectProtocolMismatchNextFrame(NetworkConnectionToClient connection)
        {
            yield return null;
            connection?.Disconnect();
        }

        private void OnClientProtocolRejected(NetworkProtocolRejected rejected)
        {
            string reason = string.IsNullOrWhiteSpace(rejected.Reason)
                ? $"联机协议不匹配：服务器={rejected.ServerVersion}，客户端={rejected.ClientVersion}"
                : rejected.Reason;
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
                FailIncomingSnapshot(
                    $"世界快照头无效：bytes={begin.CompressedBytes}, chunks={begin.ChunkCount}, expected={expectedChunks}");
                return;
            }

            incomingWorldSnapshot = new IncomingWorldSnapshot
            {
                TransferId = begin.TransferId,
                CompressedBytes = begin.CompressedBytes,
                PayloadHash = begin.PayloadHash,
                Chunks = new byte[begin.ChunkCount][],
                Metadata = new NetworkWorldSnapshot
                {
                    PlanetName = begin.PlanetName,
                    Seed = begin.Seed,
                    GenerationProtocol = begin.GenerationProtocol,
                    PlanetRadius = begin.PlanetRadius,
                    NoiseScale = begin.NoiseScale,
                    AutoGenerateMap = begin.AutoGenerateMap,
                    ChunkSizeX = begin.ChunkSizeX,
                    ChunkSizeY = begin.ChunkSizeY,
                    TopologyMode = begin.TopologyMode,
                    GenerationSettingsHash = begin.GenerationSettingsHash
                }
            };

            SetGameplayStatus($"正在接收世界快照：{begin.CompressedBytes / 1024f:0.0} KB / {begin.ChunkCount} 片");
        }

        private void OnClientWorldSnapshotChunk(NetworkWorldSnapshotChunk chunk)
        {
            IncomingWorldSnapshot incoming = incomingWorldSnapshot;
            if (incoming == null || chunk.TransferId != incoming.TransferId)
                return;

            if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= incoming.Chunks.Length || chunk.Data == null)
            {
                FailIncomingSnapshot($"世界快照分片无效：index={chunk.ChunkIndex}");
                return;
            }

            int offset = chunk.ChunkIndex * NetworkGameplayProtocol.SnapshotChunkBytes;
            int expectedBytes = Mathf.Min(
                NetworkGameplayProtocol.SnapshotChunkBytes,
                incoming.CompressedBytes - offset);
            if (chunk.Data.Length != expectedBytes)
            {
                FailIncomingSnapshot(
                    $"世界快照分片长度错误：index={chunk.ChunkIndex}, actual={chunk.Data.Length}, expected={expectedBytes}");
                return;
            }

            if (incoming.Chunks[chunk.ChunkIndex] != null)
                return;

            incoming.Chunks[chunk.ChunkIndex] = chunk.Data;
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
                FailIncomingSnapshot(
                    $"世界快照完整性校验失败：bytes={writeOffset}/{incoming.CompressedBytes}, " +
                    $"hash={actualHash:X8}/{incoming.PayloadHash:X8}");
                return;
            }

            NetworkWorldSnapshot completed = incoming.Metadata;
            completed.CompressedSaveData = compressedData;
            incomingWorldSnapshot = null;
            OnClientWorldSnapshot(completed);
        }

        private void FailIncomingSnapshot(string reason)
        {
            incomingWorldSnapshot = null;
            SetGameplayStatus(reason);
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

            if (!TryApplySynchronizedSnapshot(snapshot))
                yield break;

            SaveDataMgr.Instance.CurrentContrrolPlayerName = localPlayerName;
            currentPlanetName = snapshot.PlanetName;
            LastSnapshotBytes = snapshot.CompressedSaveData?.Length ?? 0;
            SetGameplayStatus($"地图同步完成，正在进入 {currentPlanetName}");

            if (!GameManager.Instance.IsInGameWorld)
            {
                bool worldReady = false;
                GameManager.Instance.RunWorld(currentPlanetName, () => worldReady = true);
                while (!worldReady)
                    yield return null;
            }

            if (NetworkClient.active)
                NetworkClient.Send(new NetworkWorldReady());

            worldEnterCoroutine = null;
            SetGameplayStatus($"世界已就绪：{currentPlanetName} / Seed {snapshot.Seed}");
        }

        private bool TryApplySynchronizedSnapshot(NetworkWorldSnapshot snapshot)
        {
            try
            {
                SaveDataMgr.Instance.ApplyCompressedNetworkSnapshot(snapshot.CompressedSaveData);
                RepairSynchronizedPlanet(snapshot);
                return true;
            }
            catch (Exception exception)
            {
                string reason = $"世界快照应用失败：{exception.Message}";
                worldEnterCoroutine = null;
                SetGameplayStatus(reason);
                Debug.LogError($"[联机] {reason}\n{exception}", this);
                if (NetworkClient.active)
                    NetworkClient.Disconnect();
                return false;
            }
        }

        private void OnServerWorldReady(NetworkConnectionToClient connection, NetworkWorldReady message)
        {
            if (connection.identity == null)
                OnServerAddPlayer(connection);
        }

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

            int chunkSizeX = IsValidChunkSize(snapshot.ChunkSizeX) ? snapshot.ChunkSizeX : 16;
            int chunkSizeY = IsValidChunkSize(snapshot.ChunkSizeY) ? snapshot.ChunkSizeY : 16;
            SaveDataMgr.Instance.SaveData.Seed = snapshot.Seed == 0 ? 1 : snapshot.Seed;
            SaveDataMgr.Instance.SaveData.SaveSeed = SaveDataMgr.Instance.SaveData.Seed.ToString();
            planet.Radius = Mathf.Max(1, snapshot.PlanetRadius);
            planet.NoiseScale = PlanetData.NormalizeNoiseScale(snapshot.NoiseScale);
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
                planet.NoiseScale,
                planet.AutoGenerateMap,
                planet.ChunkSize.x,
                planet.ChunkSize.y,
                planet.TopologyMode);
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
                $"[联机地图] 噪声配置已同步：Seed={SaveDataMgr.Instance.SaveData.Seed}, " +
                $"Scale={planet.NoiseScale}, Chunk={planet.ChunkSize.x}x{planet.ChunkSize.y}, " +
                $"Hash={localSettingsHash:X8}, 已有地图数={planet.MapData_Dict.Count}", this);
        }

        private static bool IsValidChunkSize(int value) => value > 0 && value <= 256;

        private static void NormalizeGenerationSettings(PlanetData planet)
        {
            if (planet == null)
                throw new ArgumentNullException(nameof(planet));

            planet.Radius = Mathf.Max(1, planet.Radius);
            planet.NoiseScale = PlanetData.NormalizeNoiseScale(planet.NoiseScale);
            int chunkSizeX = IsValidChunkSize(planet.ChunkSize.x) ? planet.ChunkSize.x : 16;
            int chunkSizeY = IsValidChunkSize(planet.ChunkSize.y) ? planet.ChunkSize.y : 16;
            planet.ChunkSize = new Vector2Int(chunkSizeX, chunkSizeY);
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
            if (GameManager.Instance != null && GameManager.Instance.IsInGameWorld)
                currentPlanetName = SceneManager.GetActiveScene().name;

            if (string.IsNullOrWhiteSpace(currentPlanetName))
                currentPlanetName = SaveDataMgr.Instance.SaveData.PlanetData_Dict.Keys.FirstOrDefault();

            return string.IsNullOrWhiteSpace(currentPlanetName) ? "地球" : currentPlanetName;
        }

        private string MakeUniquePlayerName(string requestedName, int connectionId)
        {
            if (!pendingPlayerNames.Values.Contains(requestedName))
                return requestedName;

            return $"{requestedName}_{connectionId}";
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
            string value = string.IsNullOrWhiteSpace(playerName) ? "玩家" : playerName.Trim();
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

        private void SetGameplayStatus(string status)
        {
            GameplayStatus = status;
            GameplayStatusChanged?.Invoke(status);
            Debug.Log($"[联机] {status}", this);
        }
    }
}
