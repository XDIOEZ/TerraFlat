// AI-Context: NetworkModeUIController 的 UI 状态与生命周期分部；直接持有 BasePanel 并绑定联机控件。

using System.Collections;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private UIManager subscribedUIManager;
        private Coroutine ensureUICoroutine;

        #endregion

        #region 面板生命周期

        private IEnumerator EnsurePanelWhenUIReady()
        {
            while (isActiveAndEnabled &&
                   SceneManager.GetActiveScene().name == NetworkGameBootstrap.StartSceneName)
            {
                UIManager uiManager = UIManager.ExistingInstance;
                if (uiManager == null)
                    uiManager = UIManager.Instance;
                if (uiManager == null || uiManager.panelRoot == null ||
                    GameRes.Instance == null || !GameRes.Instance.isLoadFinish)
                {
                    yield return null;
                    continue;
                }

                SubscribeToUIManager(uiManager);

                if (!IsPanelAttachedTo(uiManager.panelRoot))
                {
                    DestroyCurrentPanel();
                    panel = CreateNetworkPanel(uiManager.panelRoot);
                    if (panel == null)
                    {
                        Debug.LogError($"[联机UI] 无法从 GameRes 实例化 {NetworkPanelKey}，将在稍后重试。", this);
                        yield return new WaitForSecondsRealtime(1f);
                        continue;
                    }

                    BindNetworkPanelControls();
                }

                TryBindMainMenuButton(uiManager);
                if (panel != null && mainMenuMultiplayerButton != null)
                    break;

                yield return null;
            }

            ensureUICoroutine = null;
        }

        /// <summary>只启动一个 UI 修复协程，覆盖首次启动、场景返回和 PanelRoot 重建。</summary>
        private void RequestUIRefresh()
        {
            if (!isActiveAndEnabled ||
                SceneManager.GetActiveScene().name != NetworkGameBootstrap.StartSceneName)
                return;

            if (ensureUICoroutine == null)
                ensureUICoroutine = StartCoroutine(EnsurePanelWhenUIReady());
        }

        /// <summary>面板注册、销毁或切换时，立即检查联机入口是否需要重绑。</summary>
        private void OnInteractionSurfaceChanged()
        {
            if (SceneManager.GetActiveScene().name != NetworkGameBootstrap.StartSceneName)
                return;

            UIManager uiManager = subscribedUIManager;
            if (uiManager == null || !IsPanelAttachedTo(uiManager.panelRoot))
            {
                RequestUIRefresh();
                return;
            }

            TryBindMainMenuButton(uiManager);
        }

        /// <summary>切换 PanelRoot 订阅对象，避免旧 UIManager 持有常驻控制器。</summary>
        private void SubscribeToUIManager(UIManager uiManager)
        {
            if (subscribedUIManager == uiManager)
                return;

            if (subscribedUIManager != null)
                subscribedUIManager.InteractionSurfaceChanged -= OnInteractionSurfaceChanged;

            subscribedUIManager = uiManager;
            subscribedUIManager.InteractionSurfaceChanged += OnInteractionSurfaceChanged;
        }

        /// <summary>检查当前联机面板是否仍挂在本次场景的 PanelRoot 下。</summary>
        private bool IsPanelAttachedTo(Transform root)
        {
            return panel != null && root != null && panel.transform.parent == root;
        }

        /// <summary>实例化后只绑定一次联机面板控件。</summary>
        private void BindNetworkPanelControls()
        {
            playerNameInput = panel.GetInputField(PlayerNameInputKey);
            addressInput = panel.GetInputField(AddressInputKey);
            portInput = panel.GetInputField(PortInputKey);
            statusText = panel.GetText(StatusTextKey);
            playerCountText = panel.GetText(PlayerCountTextKey);
            hostButton = panel.GetButton(HostButtonKey);
            joinButton = panel.GetButton(JoinButtonKey);
            disconnectButton = panel.GetButton(DisconnectButtonKey);
            closeButton = panel.GetButton(CloseButtonKey);

            if (playerNameInput != null)
                playerNameInput.SetTextWithoutNotify(NewWorldCreationRequest.CreateRandomPlayerName());

            if (hostButton != null)
                hostButton.onClick.AddListener(StartHost);
            if (joinButton != null)
                joinButton.onClick.AddListener(StartClient);
            if (disconnectButton != null)
                disconnectButton.onClick.AddListener(StopSession);
            if (closeButton != null)
                closeButton.onClick.AddListener(I_ClosePanel);

            SetStatus("离线：可创建主机，或粘贴好友提供的 UDP 穿透地址");
            RefreshInteractableState();
            panel.Close();
        }

        /// <summary>按当前主菜单实例幂等绑定联机入口，主菜单重建后自动切换引用。</summary>
        private void TryBindMainMenuButton(UIManager uiManager)
        {
            if (uiManager == null ||
                !uiManager.TryGetPanel(GameManager.MainMenuPanelKey, out BasePanel mainMenu))
                return;

            Button candidate = mainMenu.GetButton(GameManager.MainMenuMultiplayerButtonKey);
            if (candidate == null)
                return;

            if (candidate == mainMenuMultiplayerButton)
                return;

            if (mainMenuMultiplayerButton != null)
                mainMenuMultiplayerButton.onClick.RemoveListener(I_ShowPanel);

            mainMenuMultiplayerButton = candidate;
            mainMenuMultiplayerButton.onClick.RemoveListener(I_ShowPanel);
            mainMenuMultiplayerButton.onClick.AddListener(I_ShowPanel);
        }

        /// <summary>面板所属根变化时清理旧实例及其监听，避免 UIManager 留下重复面板。</summary>
        private void DestroyCurrentPanel()
        {
            ReleaseNetworkPanelBindings();
            if (panel != null)
                panel.Destroy();
            panel = null;
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

        }

        public void I_ShowPanel()
        {
            if (panel == null)
            {
                RequestUIRefresh();
                return;
            }

            panel.Open();
            panel.transform.SetAsLastSibling();
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

        private void RefreshInteractableState()
        {
            bool offline = GameNetwork.Session.State == NetworkSessionState.Offline;
            if (hostButton != null)
                hostButton.interactable = offline;
            if (joinButton != null)
                joinButton.interactable = offline;
            if (disconnectButton != null)
                disconnectButton.interactable = !offline;
            if (playerNameInput != null)
                playerNameInput.interactable = offline;
            if (addressInput != null)
                addressInput.interactable = offline;
            if (portInput != null)
                portInput.interactable = offline;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void ReleaseUIBindings()
        {
            ReleaseNetworkPanelBindings();
            if (mainMenuMultiplayerButton != null)
                mainMenuMultiplayerButton.onClick.RemoveListener(I_ShowPanel);

            if (subscribedUIManager != null)
                subscribedUIManager.InteractionSurfaceChanged -= OnInteractionSurfaceChanged;
            subscribedUIManager = null;
        }

        /// <summary>清理当前联机面板控件监听，保证面板重建不会叠加回调。</summary>
        private void ReleaseNetworkPanelBindings()
        {
            if (hostButton != null)
                hostButton.onClick.RemoveListener(StartHost);
            if (joinButton != null)
                joinButton.onClick.RemoveListener(StartClient);
            if (disconnectButton != null)
                disconnectButton.onClick.RemoveListener(StopSession);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(I_ClosePanel);

            playerNameInput = null;
            addressInput = null;
            portInput = null;
            statusText = null;
            playerCountText = null;
            hostButton = null;
            joinButton = null;
            disconnectButton = null;
            closeButton = null;
        }

        #endregion
    }
}
