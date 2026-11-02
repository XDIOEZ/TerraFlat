// AI-Context: 联机控制器的会话逻辑分部；UI 组合与动态界面构建位于同一控制器的 UI partial。

using System;
using System.Collections;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 联机会话逻辑：负责启动、停止、校验和状态转换。
    /// </summary>
    public sealed partial class NetworkModeUIController : MonoBehaviour, IInstanceUI
    {
        private FlatWorldGameNetworkManager networkManager;
        private INetworkSession subscribedSession;
        private bool initialized;
        private GameManager boundGameManager;
        private Coroutine pendingClientJoinCoroutine;
        private int clientJoinGeneration;

        private readonly struct ClientJoinSnapshot
        {
            public readonly string Address;
            public readonly ushort Port;
            public readonly string PlayerName;

            public ClientJoinSnapshot(string address, ushort port, string playerName)
            {
                Address = address;
                Port = port;
                PlayerName = playerName;
            }
        }

        #region 面板能力状态

        // 常驻控制器只报告当前场景中真实联机面板的状态。
        public bool IsPanelOpen => panel != null && panel.IsOpen();

        #endregion

        public void Initialize(FlatWorldGameNetworkManager manager)
        {
            if (manager == null)
            {
                Debug.LogError("[联机UI] 无法初始化：联机会话管理器为空。", this);
                return;
            }

            if (initialized && networkManager == manager)
            {
                RequestUIRefresh();
                return;
            }

            ReleaseSessionBindings();
            networkManager = manager;
            networkManager.GameplayStatusChanged += SetStatus;
            FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
            subscribedSession = GameNetwork.Session;
            subscribedSession.StateChanged += OnSessionStateChanged;
            subscribedSession.Error += SetStatus;
            SceneManager.sceneLoaded += OnSceneLoaded;
            boundGameManager = GameManager.Instance;
            boundGameManager.Event_NewWorldReady += OnNewWorldReady;
            GameNetwork.SettingsRequested += I_ShowPanel;
            initialized = true;
            RequestUIRefresh();
        }

        private void OnDestroy()
        {
            ReleaseSessionBindings();

            ReleaseUIBindings();
        }

        /// <summary>释放会话和场景监听，避免常驻联机对象引用旧场景事件。</summary>
        private void ReleaseSessionBindings()
        {
            CancelPendingClientJoin("已取消加入。");

            if (networkManager != null)
                networkManager.GameplayStatusChanged -= SetStatus;

            FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;

            if (subscribedSession != null)
            {
                subscribedSession.StateChanged -= OnSessionStateChanged;
                subscribedSession.Error -= SetStatus;
                subscribedSession = null;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (boundGameManager != null)
                boundGameManager.Event_NewWorldReady -= OnNewWorldReady;
            boundGameManager = null;
            GameNetwork.SettingsRequested -= I_ShowPanel;
            initialized = false;
        }

        /// <summary>场景重建后重新准备主菜单联机 UI；进入世界时只关闭残留面板。</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == NetworkGameBootstrap.StartSceneName)
            {
                RequestUIRefresh();
                return;
            }

            if (panel != null)
                panel.Close();
        }

        #region 当前世界的局域网公开

        private void OnNewWorldReady()
        {
            RefreshInteractableState();
        }

        private void StartHost()
        {
            try
            {
                if (Mirror.NetworkServer.active)
                {
                    networkManager.PublishSession(ReadPort());
                    SetStatus("已公开到局域网，好友可以使用连接地址加入。");
                    RefreshLanAddresses(); RefreshInteractableState();
                    return;
                }
                networkManager.PrepareHostWorld();
                NetworkStartResult result = GameNetwork.Session.StartHost(ReadPort());
                if (!result.Success) SetStatus(result.Error);
                else
                {
                    networkManager.PublishSession(ReadPort());
                    SetStatus("已公开到局域网，好友可以使用连接地址加入。");
                    RefreshInteractableState();
                }
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
                Debug.LogException(exception, this);
            }
        }

        #endregion

        private void StartClient()
        {
            GameRes resources = GameRes.ExistingInstance;
            INetworkSession session = GameNetwork.Session;
            if (!CanBeginClientJoinFromMenu(
                    resources != null && resources.IsStartupReady,
                    session?.State ?? NetworkSessionState.Stopping,
                    IsInWorld,
                    GameManager.ExistingInstance?.IsWorldEntryInProgress == true,
                    pendingClientJoinCoroutine != null))
                return;

            if (resources == null || networkManager == null || session == null)
                return;

            int requestGeneration = ++clientJoinGeneration;
            var request = new ClientJoinSnapshot(addressInput != null ? addressInput.text : string.Empty,
                ReadPort(), playerNameInput != null ? playerNameInput.text : string.Empty);

            if (!resources.RequestWorldResources())
            {
                SetStatus(resources.LastLoadError ?? $"世界资源尚不可用（{resources.LoadState}）。");
                return;
            }

            if (resources.LoadState == ResourceLoadState.Ready)
            {
                StartClientAfterWorldResourcesReady(requestGeneration, resources, session, request);
                return;
            }

            if (resources.LoadState != ResourceLoadState.Loading)
            {
                SetStatus(resources.LastLoadError ?? $"世界资源加载失败（{resources.LoadState}）。");
                return;
            }

            SetStatus("正在加载世界资源…");
            pendingClientJoinCoroutine = StartCoroutine(WaitForWorldResourcesThenStartClient(
                requestGeneration, resources, session, request));
            RefreshInteractableState();
        }

        private IEnumerator WaitForWorldResourcesThenStartClient(
            int requestGeneration,
            GameRes resources,
            INetworkSession session,
            ClientJoinSnapshot request)
        {
            yield return null;
            while (requestGeneration == clientJoinGeneration)
            {
                GameManager manager = GameManager.ExistingInstance;
                if (resources.LoadState != ResourceLoadState.Loading ||
                    GameRes.ExistingInstance != resources || GameNetwork.Session != session ||
                    session.State != NetworkSessionState.Offline || manager == null ||
                    manager.IsInGameWorld || manager.IsWorldEntryInProgress) break;
                yield return null;
            }

            if (requestGeneration != clientJoinGeneration) yield break;
            if (resources.LoadState == ResourceLoadState.Ready)
                StartClientAfterWorldResourcesReady(requestGeneration, resources, session, request);
            else
            {
                pendingClientJoinCoroutine = null;
                SetStatus(resources.LoadState == ResourceLoadState.Loading
                    ? "联机或世界状态已变化，已取消加入。"
                    : resources.LastLoadError ?? $"世界资源加载失败（{resources.LoadState}）。");
                RefreshInteractableState();
            }
        }

        private void StartClientAfterWorldResourcesReady(
            int requestGeneration,
            GameRes resources,
            INetworkSession session,
            ClientJoinSnapshot request)
        {
            GameManager manager = GameManager.ExistingInstance;
            if (GameRes.ExistingInstance != resources || GameNetwork.Session != session || manager == null ||
                networkManager == null ||
                !CanFinishClientJoinAfterResourcesReady(requestGeneration, clientJoinGeneration,
                    resources.LoadState, session.State, manager.IsInGameWorld, manager.IsWorldEntryInProgress))
            {
                pendingClientJoinCoroutine = null;
                SetStatus("联机或世界状态已变化，已取消加入。");
                RefreshInteractableState();
                return;
            }

            pendingClientJoinCoroutine = null;
            try
            {
                networkManager.PrepareLocalPlayer(request.PlayerName);
                NetworkStartResult result = session.StartClient(request.Address, request.Port);
                if (!result.Success)
                    SetStatus(result.Error);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
                Debug.LogException(exception, this);
            }
            RefreshInteractableState();
        }

        private bool CancelPendingClientJoin(string status)
        {
            Coroutine waiting = pendingClientJoinCoroutine;
            if (waiting == null)
                return false;

            clientJoinGeneration++;
            pendingClientJoinCoroutine = null;
            StopCoroutine(waiting);
            if (!string.IsNullOrEmpty(status))
                SetStatus(status);
            return true;
        }

        internal static bool CanBeginClientJoinFromMenu(
            bool startupReady,
            NetworkSessionState sessionState,
            bool isInWorld,
            bool isWorldEntryInProgress,
            bool joinAlreadyPending)
            => startupReady && sessionState == NetworkSessionState.Offline && !isInWorld &&
               !isWorldEntryInProgress && !joinAlreadyPending;

        internal static bool CanFinishClientJoinAfterResourcesReady(
            int requestGeneration,
            int activeGeneration,
            ResourceLoadState loadState,
            NetworkSessionState sessionState,
            bool isInWorld,
            bool isWorldEntryInProgress)
            => requestGeneration == activeGeneration && loadState == ResourceLoadState.Ready &&
               sessionState == NetworkSessionState.Offline && !isInWorld && !isWorldEntryInProgress;

        internal static bool CanEnableHostButton(
            bool offline,
            bool privateHost,
            bool contentReady,
            bool isInWorld,
            bool hasLocalPlayer,
            bool gameplayReady)
            => (offline || privateHost) && contentReady && isInWorld && hasLocalPlayer && gameplayReady;

        private void StopSession()
        {
            if (CancelPendingClientJoin("已取消加入。"))
            {
                RefreshInteractableState();
                return;
            }

            if (Mirror.NetworkServer.active && IsInWorld)
            {
                networkManager.MakeSessionPrivate();
                SetStatus("已关闭公开，当前世界继续保留本机玩家。");
                RefreshInteractableState();
                return;
            }
            GameNetwork.Session.Stop();
        }

        private ushort ReadPort()
        {
            return ushort.TryParse(portInput != null ? portInput.text : null, out ushort port) && port > 0 ? port : (ushort)7777;
        }

        private void OnSessionStateChanged(NetworkSessionState state)
        {
            if (state != NetworkSessionState.Offline)
                CancelPendingClientJoin("联机会话状态已变化，已取消等待加入。");

            SetStatus(state switch
            {
                NetworkSessionState.Starting => "正在建立联机会话",
                NetworkSessionState.Online => "网络已连接，正在准备世界",
                NetworkSessionState.Stopping => "正在断开",
                _ => "离线"
            });
            RefreshInteractableState();
        }

    }
}
