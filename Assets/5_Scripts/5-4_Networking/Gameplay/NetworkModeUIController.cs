// AI-Context: 联机 UI 与 Mirror 会话的控制器；负责校验、状态转换和视图更新，不承载玩家模块同步。

using System;
using System.Collections;
using FlatWorld.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 双脚本 UI 的逻辑脚本：负责会话启动、停止、校验和状态展示。
    /// </summary>
    public sealed class NetworkModeUIController : MonoBehaviour, IInstanceUI
    {
        private FlatWorldGameNetworkManager networkManager;
        private NetworkModePanelView panel;
        private TMP_InputField playerNameInput;
        private TMP_InputField addressInput;
        private TMP_InputField portInput;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI playerCountText;
        private Button hostButton;
        private Button joinButton;
        private Button disconnectButton;
        private Button mainMenuMultiplayerButton;

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

            if (mainMenuMultiplayerButton != null)
                mainMenuMultiplayerButton.onClick.RemoveListener(I_ShowPanel);
        }

        private IEnumerator CreatePanelWhenUIReady()
        {
            while (UIManager.Instance == null || UIManager.Instance.panelRoot == null)
                yield return null;

            // 主菜单资源异步加载完成后再创建，确保复用项目中文字库，并避免后来生成的菜单盖住本面板。
            float fontWaitDeadline = Time.realtimeSinceStartup + 15f;
            while (!NetworkModePanelView.IsProjectFontReady && Time.realtimeSinceStartup < fontWaitDeadline)
                yield return null;

            panel = NetworkModePanelView.Create(UIManager.Instance.panelRoot);
            playerNameInput = panel.GetInputField("玩家名称输入框");
            addressInput = panel.GetInputField("地址输入框");
            portInput = panel.GetInputField("端口输入框");
            statusText = panel.GetText("状态文本");
            playerCountText = panel.GetText("玩家数量文本");
            hostButton = panel.GetButton("创建主机按钮");
            joinButton = panel.GetButton("加入游戏按钮");
            disconnectButton = panel.GetButton("断开按钮");

            hostButton.onClick.AddListener(StartHost);
            joinButton.onClick.AddListener(StartClient);
            disconnectButton.onClick.AddListener(StopSession);
            panel.GetButton("关闭按钮").onClick.AddListener(I_ClosePanel);

            SetStatus("离线：可创建主机或加入 127.0.0.1");
            RefreshInteractableState();
            panel.Close();
            StartCoroutine(BindMainMenuButtonWhenReady());
        }

        private IEnumerator BindMainMenuButtonWhenReady()
        {
            while (mainMenuMultiplayerButton == null)
            {
                MainMenuPanelView mainMenu = FindObjectOfType<MainMenuPanelView>(true);
                if (mainMenu != null)
                {
                    mainMenuMultiplayerButton = mainMenu.GetButton(MainMenuPanelView.MultiplayerButtonKey);
                    if (mainMenuMultiplayerButton != null)
                    {
                        mainMenuMultiplayerButton.onClick.AddListener(I_ShowPanel);
                        yield break;
                    }
                }

                yield return null;
            }
        }

        private void Update()
        {
            if (panel == null)
                return;

            int playerCount = 0;
            if (NetworkServer.active)
                playerCount = NetworkServer.connections.Count;
            else if (NetworkClient.active)
                playerCount = NetworkClient.spawned.Count;

            playerCountText.text = $"玩家：{playerCount} / 2";
            if (networkManager != null && !string.IsNullOrEmpty(networkManager.GameplayStatus))
                statusText.text = networkManager.GameplayStatus;
        }

        public void I_ShowPanel()
        {
            if (panel != null)
            {
                panel.Open();
                panel.transform.SetAsLastSibling();
            }
        }

        public void I_ClosePanel()
        {
            if (panel != null)
                panel.Close();
        }

        public void I_TogglePanel()
        {
            if (panel != null)
                panel.Toggle();
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

        private void RefreshInteractableState()
        {
            if (hostButton == null)
                return;

            bool offline = GameNetwork.Session.State == NetworkSessionState.Offline;
            hostButton.interactable = offline;
            joinButton.interactable = offline;
            disconnectButton.interactable = !offline;
            playerNameInput.interactable = offline;
            addressInput.interactable = offline;
            portInput.interactable = offline;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }
    }
}
