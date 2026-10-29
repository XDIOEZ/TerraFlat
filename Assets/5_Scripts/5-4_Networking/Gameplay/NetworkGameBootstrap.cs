using System;
using FlatWorld.Networking.MirrorAdapter;
using kcp2k;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 在主菜单场景自动装配正式联机入口，不要求手工修改现有场景。
    /// </summary>
    public sealed class NetworkGameBootstrap : MonoBehaviour
    {
        internal const string StartSceneName = "GameStartScene";
        private const string PlayerResourcePath = "Networking/FlatWorldNetworkPlayer";

        private static NetworkGameBootstrap instance;
        private static bool sceneHookInstalled;
        private FlatWorldGameNetworkManager manager;
        private GameSaveData attemptedWorld;
        private static int automaticSessionSuppressions;
        public static IDisposable SuppressAutomaticSession()
        {
            automaticSessionSuppressions++;
            return new AutomaticSessionSuppression();
        }
        private sealed class AutomaticSessionSuppression : IDisposable
        {
            private bool disposed;
            public void Dispose() { if (disposed) return; disposed = true; automaticSessionSuppressions--; }
        }
        private void LateUpdate()
        {
            if (manager == null || automaticSessionSuppressions > 0 || Mirror.NetworkServer.active || Mirror.NetworkClient.active) return;
            GameManager game = GameManager.ExistingInstance;
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            Player player = ItemMgr.Instance?.User_Player;
            if (game?.IsInGameWorld != true || !game.IsGameplayReady || player?.IsInitialized != true ||
                save == null || ReferenceEquals(save, attemptedWorld)) return;
            attemptedWorld = save;
            try
            {
                // 单人也创建正式本机连接，公开房间只改变加入资格而不重建世界。
                manager.PrepareHostWorld();
                Mirror.NetworkServer.listen = false;
                NetworkStartResult result = GameNetwork.Session.StartHost();
                if (!result.Success) Debug.LogError("[单人联机] 本机会话启动失败：" + result.Error, this);
            }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!sceneHookInstalled)
            {
                sceneHookInstalled = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            TryCreate(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryCreate(scene);

        private static void TryCreate(Scene scene)
        {
            if (instance != null || scene.name != StartSceneName)
                return;

            GameObject bootstrapObject = new GameObject("FlatWorld联机系统");
            instance = bootstrapObject.AddComponent<NetworkGameBootstrap>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(gameObject, PersistentRuntimeObjectCategory.Networking);

            GameObject playerPrefab = Resources.Load<GameObject>(PlayerResourcePath);
            if (playerPrefab == null)
            {
                Debug.LogError($"[联机] 找不到玩家预制体 Resources/{PlayerResourcePath}", this);
                return;
            }

            GameObject managerObject = new GameObject("FlatWorldNetworkManager");
            KcpTransport transport = managerObject.AddComponent<KcpTransport>();
            manager = managerObject.AddComponent<FlatWorldGameNetworkManager>();
            managerObject.transform.SetParent(transform, false);
            manager.transport = transport;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = false;
            manager.maxConnections = 2;
            manager.sendRate = 30;
            manager.networkAddress = "127.0.0.1";
            // 常驻由启动宿主负责，避免 Mirror 每次启动会话都把管理器拆回场景根。
            manager.dontDestroyOnLoad = false;
            transport.Port = 7777;

            NetworkModeUIController uiController = gameObject.AddComponent<NetworkModeUIController>();
            uiController.Initialize(manager);
            Debug.Log("[联机] 正式联机入口已初始化（Mirror + KCP）", this);
        }
    }
}
