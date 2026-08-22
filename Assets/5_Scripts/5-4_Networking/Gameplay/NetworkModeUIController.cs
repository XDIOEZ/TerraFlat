// AI-Context: 联机控制器的会话逻辑分部；UI 组合与动态界面构建位于同一控制器的 UI partial。

using System;
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
            subscribedSession = GameNetwork.Session;
            subscribedSession.StateChanged += OnSessionStateChanged;
            subscribedSession.Error += SetStatus;
            SceneManager.sceneLoaded += OnSceneLoaded;
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

            if (subscribedSession != null)
            {
                subscribedSession.StateChanged -= OnSessionStateChanged;
                subscribedSession.Error -= SetStatus;
                subscribedSession = null;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
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

        private void StartHost()
        {
            try
            {
                networkManager.PrepareLocalPlayer(playerNameInput.text);
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

        private void StartClient()
        {
            networkManager.PrepareLocalPlayer(playerNameInput.text);
            NetworkStartResult result = GameNetwork.Session.StartClient(addressInput.text, ReadPort());
            if (!result.Success)
                SetStatus(result.Error);
        }

        private void StopSession() => GameNetwork.Session.Stop();

        private ushort ReadPort()
        {
            return ushort.TryParse(portInput.text, out ushort port) && port > 0 ? port : (ushort)7777;
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
