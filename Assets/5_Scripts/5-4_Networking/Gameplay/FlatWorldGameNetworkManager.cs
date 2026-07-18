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
        private string localPlayerName = "玩家";
        private string currentPlanetName;
        private Coroutine worldEnterCoroutine;

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

            SetGameplayStatus($"主机世界已准备：{currentPlanetName} / Seed {saveData.Seed}");
        }

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<NetworkJoinRequest>(OnServerJoinRequest, false);
            NetworkServer.RegisterHandler<NetworkWorldReady>(OnServerWorldReady, false);
            base.OnStartServer();
            SetGameplayStatus("服务器已启动，等待玩家");
        }

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<NetworkWorldSnapshot>(OnClientWorldSnapshot, false);
            base.OnStartClient();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            NetworkClient.Send(new NetworkJoinRequest { PlayerName = localPlayerName });
            SetGameplayStatus("已连接，正在同步世界数据");
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient connection)
        {
            string playerName = pendingPlayerNames.TryGetValue(connection.connectionId, out string pendingName)
                ? pendingName
                : $"玩家_{connection.connectionId}";

            // KCP connectionId 不是连续的小整数，不能直接用于出生点或玩家配色。
            // 这里按已经生成的玩家数量取得稳定的 0/1 序号。
            int playerIndex = NetworkServer.connections.Values.Count(activeConnection =>
                activeConnection != null && activeConnection.identity != null);
            float spawnOffset = playerIndex * 2.5f;
            GameObject playerObject = Instantiate(playerPrefab, new Vector3(spawnOffset, 0f, 0f), Quaternion.identity);
            playerObject.name = $"网络玩家_{playerName}";

            NetworkWorldPlayer player = playerObject.GetComponent<NetworkWorldPlayer>();
            if (player == null)
                throw new MissingComponentException("联机玩家预制体缺少 NetworkWorldPlayer");

            player.InitializeOnServer(playerName, playerIndex);
            NetworkServer.AddPlayerForConnection(connection, playerObject);
            SetGameplayStatus($"玩家已进入：{playerName}（当前 {NetworkServer.connections.Count} 人）");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            pendingPlayerNames.Remove(connection.connectionId);
            base.OnServerDisconnect(connection);
            SetGameplayStatus($"玩家已断开（当前 {NetworkServer.connections.Count} 人）");
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<NetworkWorldSnapshot>();
            base.OnStopClient();
            SetGameplayStatus("客户端已停止");
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<NetworkJoinRequest>();
            NetworkServer.UnregisterHandler<NetworkWorldReady>();
            pendingPlayerNames.Clear();
            base.OnStopServer();
            SetGameplayStatus("服务器已停止");
        }

        private void OnServerJoinRequest(NetworkConnectionToClient connection, NetworkJoinRequest request)
        {
            if (connection.identity != null)
                return;

            string playerName = MakeUniquePlayerName(SanitizePlayerName(request.PlayerName), connection.connectionId);
            pendingPlayerNames[connection.connectionId] = playerName;

            CaptureLoadedChunks();
            byte[] snapshot = SaveDataMgr.Instance.CreateCompressedNetworkSnapshot();
            LastSnapshotBytes = snapshot.Length;

            string planetName = ResolveCurrentPlanetName();
            PlanetData synchronizedPlanet = SaveDataMgr.Instance.SaveData.PlanetData_Dict[planetName];
            connection.Send(new NetworkWorldSnapshot
            {
                PlanetName = planetName,
                Seed = SaveDataMgr.Instance.SaveData.Seed,
                ChunkSizeX = synchronizedPlanet.ChunkSize.x,
                ChunkSizeY = synchronizedPlanet.ChunkSize.y,
                CompressedSaveData = snapshot
            });

            SetGameplayStatus($"正在向 {playerName} 同步地图（{snapshot.Length / 1024f:0.0} KB）");
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

            SaveDataMgr.Instance.ApplyCompressedNetworkSnapshot(snapshot.CompressedSaveData);
            RepairSynchronizedPlanet(snapshot);
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
            if (SaveDataMgr.Instance.SaveData?.PlanetData_Dict == null ||
                !SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(snapshot.PlanetName, out PlanetData planet) ||
                planet == null)
            {
                throw new InvalidOperationException($"快照中缺少星球数据：{snapshot.PlanetName}");
            }

            int chunkSizeX = IsValidChunkSize(snapshot.ChunkSizeX) ? snapshot.ChunkSizeX : 16;
            int chunkSizeY = IsValidChunkSize(snapshot.ChunkSizeY) ? snapshot.ChunkSizeY : 16;
            planet.ChunkSize = new Vector2Int(chunkSizeX, chunkSizeY);

            if (planet.MapData_Dict == null)
                planet.MapData_Dict = new Dictionary<string, MapSave>();

            foreach (KeyValuePair<string, MapSave> pair in planet.MapData_Dict)
            {
                if (pair.Value != null && TryParseChunkPosition(pair.Key, out Vector2Int mapPosition))
                    pair.Value.MapPosition = mapPosition;
            }

            Debug.Log($"[联机地图] 已修复区块尺寸为 {planet.ChunkSize.x}x{planet.ChunkSize.y}，地图数 {planet.MapData_Dict.Count}", this);
        }

        private static bool IsValidChunkSize(int value) => value > 0 && value <= 256;

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

        private static string SanitizePlayerName(string playerName)
        {
            string value = string.IsNullOrWhiteSpace(playerName) ? "玩家" : playerName.Trim();
            return value.Length <= 16 ? value : value.Substring(0, 16);
        }

        private void SetGameplayStatus(string status)
        {
            GameplayStatus = status;
            GameplayStatusChanged?.Invoke(status);
            Debug.Log($"[联机] {status}", this);
        }
    }
}
