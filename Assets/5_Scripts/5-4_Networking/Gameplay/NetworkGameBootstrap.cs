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
        private const string StartSceneName = "GameStartScene";
        private const string PlayerResourcePath = "Networking/FlatWorldNetworkPlayer";

        private static NetworkGameBootstrap instance;
        private static bool sceneHookInstalled;

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
            DontDestroyOnLoad(bootstrapObject);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            GameObject playerPrefab = Resources.Load<GameObject>(PlayerResourcePath);
            if (playerPrefab == null)
            {
                Debug.LogError($"[联机] 找不到玩家预制体 Resources/{PlayerResourcePath}", this);
                return;
            }

            GameObject managerObject = new GameObject("FlatWorldNetworkManager");
            KcpTransport transport = managerObject.AddComponent<KcpTransport>();
            FlatWorldGameNetworkManager manager = managerObject.AddComponent<FlatWorldGameNetworkManager>();
            managerObject.transform.SetParent(transform, false);
            manager.transport = transport;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = false;
            manager.maxConnections = 2;
            manager.sendRate = 30;
            manager.networkAddress = "127.0.0.1";
            manager.dontDestroyOnLoad = true;
            transport.Port = 7777;

            NetworkModeUIController uiController = gameObject.AddComponent<NetworkModeUIController>();
            uiController.Initialize(manager);
            Debug.Log("[联机] 正式联机入口已初始化（Mirror + KCP）", this);
        }
    }
}
