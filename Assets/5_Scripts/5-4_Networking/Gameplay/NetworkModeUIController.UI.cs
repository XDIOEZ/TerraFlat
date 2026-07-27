// AI-Context: NetworkModeUIController 的 UI 状态与生命周期分部；直接持有 BasePanel 并绑定联机控件。

using System.Collections;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkModeUIController
    {
        #region 控件命名契约

        private const string PlayerNameInputKey = "玩家名称输入框";
        private const string AddressInputKey = "地址输入框";
        private const string PortInputKey = "端口输入框";
        private const string StatusTextKey = "状态文本";
        private const string PlayerCountTextKey = "玩家数量文本";
        private const string HostButtonKey = "创建主机按钮";
        private const string JoinButtonKey = "加入游戏按钮";
        private const string DisconnectButtonKey = "断开按钮";
        private const string CloseButtonKey = "关闭按钮";

        #endregion

        #region UI 状态

        private BasePanel panel;
        private TMP_InputField playerNameInput;
        private TMP_InputField addressInput;
        private TMP_InputField portInput;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI playerCountText;
        private Button hostButton;
        private Button joinButton;
        private Button disconnectButton;
        private Button closeButton;
        private Button mainMenuMultiplayerButton;

        #endregion

        #region 面板生命周期

        private static bool IsProjectFontReady => FindProjectFont() != null;

        private IEnumerator CreatePanelWhenUIReady()
        {
            while (UIManager.Instance == null || UIManager.Instance.panelRoot == null)
                yield return null;

            float fontWaitDeadline = Time.realtimeSinceStartup + 15f;
            while (!IsProjectFontReady && Time.realtimeSinceStartup < fontWaitDeadline)
                yield return null;

            panel = CreateNetworkPanel(UIManager.Instance.panelRoot);
            playerNameInput = panel.GetInputField(PlayerNameInputKey);
            addressInput = panel.GetInputField(AddressInputKey);
            portInput = panel.GetInputField(PortInputKey);
            statusText = panel.GetText(StatusTextKey);
            playerCountText = panel.GetText(PlayerCountTextKey);
            hostButton = panel.GetButton(HostButtonKey);
            joinButton = panel.GetButton(JoinButtonKey);
            disconnectButton = panel.GetButton(DisconnectButtonKey);
            closeButton = panel.GetButton(CloseButtonKey);

            hostButton?.onClick.AddListener(StartHost);
            joinButton?.onClick.AddListener(StartClient);
            disconnectButton?.onClick.AddListener(StopSession);
            closeButton?.onClick.AddListener(I_ClosePanel);

            SetStatus("离线：可创建主机或加入 127.0.0.1");
            RefreshInteractableState();
            panel.Close();
            StartCoroutine(BindMainMenuButtonWhenReady());
        }

        private IEnumerator BindMainMenuButtonWhenReady()
        {
            while (mainMenuMultiplayerButton == null)
            {
                if (UIManager.Instance.TryGetPanel(GameManager.MainMenuPanelKey, out BasePanel mainMenu))
                {
                    mainMenuMultiplayerButton = mainMenu.GetButton(GameManager.MainMenuMultiplayerButtonKey);
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

            if (playerCountText != null)
                playerCountText.text = $"玩家：{playerCount} / 2";

            if (statusText != null && networkManager != null && !string.IsNullOrEmpty(networkManager.GameplayStatus))
                statusText.text = networkManager.GameplayStatus;
        }

        public void I_ShowPanel()
        {
            if (panel == null)
                return;

            panel.Open();
            panel.transform.SetAsLastSibling();
        }

        public void I_ClosePanel() => panel?.Close();

        public void I_TogglePanel() => panel?.Toggle();

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

        private void ReleaseUIBindings()
        {
            hostButton?.onClick.RemoveListener(StartHost);
            joinButton?.onClick.RemoveListener(StartClient);
            disconnectButton?.onClick.RemoveListener(StopSession);
            closeButton?.onClick.RemoveListener(I_ClosePanel);
            mainMenuMultiplayerButton?.onClick.RemoveListener(I_ShowPanel);
        }

        #endregion
    }
}
