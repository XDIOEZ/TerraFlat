// AI-Context: 联机控制器的会话逻辑分部；UI 组合与动态界面构建位于同一控制器的 UI partial。

using System;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 联机会话逻辑：负责启动、停止、校验和状态转换。
    /// </summary>
    public sealed partial class NetworkModeUIController : MonoBehaviour, IInstanceUI
    {
        private FlatWorldGameNetworkManager networkManager;

        public void Initialize(FlatWorldGameNetworkManager manager)
        {
            networkManager = manager;
            networkManager.GameplayStatusChanged += SetStatus;
            GameNetwork.Session.StateChanged += OnSessionStateChanged;
            GameNetwork.Session.Error += SetStatus;
            StartCoroutine(CreatePanelWhenUIReady());
        }

        private void OnDestroy()
        {
            if (networkManager != null)
                networkManager.GameplayStatusChanged -= SetStatus;

            GameNetwork.Session.StateChanged -= OnSessionStateChanged;
            GameNetwork.Session.Error -= SetStatus;

            ReleaseUIBindings();
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
