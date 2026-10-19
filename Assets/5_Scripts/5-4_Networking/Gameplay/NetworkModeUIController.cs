// AI-Context: 联机控制器的会话逻辑分部；UI 组合与动态界面构建位于同一控制器的 UI partial。

using System;
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
            if (GameNetwork.Session.State == NetworkSessionState.Offline)
                StartHost();
        }

        private void StartHost()
        {
            try
            {
                networkManager.PrepareHostWorld();
                NetworkStartResult result = GameNetwork.Session.StartHost(ReadPort());
                if (!result.Success)
                    SetStatus(result.Error);
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
            if (GameManager.Instance.IsInGameWorld || GameManager.Instance.IsWorldEntryInProgress)
                return;
            networkManager.PrepareLocalPlayer(playerNameInput.text);
            NetworkStartResult result = GameNetwork.Session.StartClient(addressInput.text, ReadPort());
            if (!result.Success)
                SetStatus(result.Error);
        }

        private void StopSession() => GameNetwork.Session.Stop();

        private ushort ReadPort()
        {
            return ushort.TryParse(portInput != null ? portInput.text : null, out ushort port) && port > 0 ? port : (ushort)7777;
        }

        private void OnSessionStateChanged(NetworkSessionState state)
        {
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
