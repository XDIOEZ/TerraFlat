// AI-Context: NetworkModeUIController 的 UI 状态与生命周期分部；直接持有 BasePanel 并绑定联机控件。

using System.Collections;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FlatWorld.Localization;
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
        private string lastStatusTemplate;
        private object[] lastStatusArguments = Array.Empty<object>();
        private int lastDisplayedPlayerCount = -1;
        private bool lastContentReady;
        private bool lastInWorld;
        private bool showPanelWhenReady;
        private string lanAddressText = string.Empty;
        private bool lastServerActive;
        private bool lastPrivateSession;
        private string addressBeforeHosting;
        private bool showingHostAddress;

        #endregion

        #region 面板生命周期

        private IEnumerator EnsurePanelWhenUIReady()
        {
            // 先登记协程，再创建会广播交互事件的场景 UI，避免同步回调重复启动。
            yield return null;
            while (isActiveAndEnabled && !UIManager.IsShuttingDown)
            {
                UIManager uiManager = UIManager.ExistingInstance;
                if (uiManager == null)
                    uiManager = UIManager.Instance;
                if (uiManager == null ||
                    GameRes.Instance == null || !GameRes.Instance.IsStartupReady)
                {
                    yield return null;
                    continue;
                }

                SubscribeToUIManager(uiManager);

                Transform sceneRoot = uiManager.SafeAreaRoot;
                if (!IsPanelAttachedTo(sceneRoot))
                {
                    DestroyCurrentPanel();
                    panel = CreateNetworkPanel(sceneRoot);
                    if (panel == null)
                    {
                        Debug.LogError($"[联机UI] 无法从 GameRes 实例化 {NetworkPanelKey}，将在稍后重试。", this);
                        yield return new WaitForSecondsRealtime(1f);
                        continue;
                    }

                    BindNetworkPanelControls();
                    uiManager.ConfigureSettingsPanelLayer(panel);
                }

                TryBindMainMenuButton(uiManager);
                if (panel != null && (IsInWorld || mainMenuMultiplayerButton != null))
                {
                    if (showPanelWhenReady) I_ShowPanel();
                    break;
                }

                yield return null;
            }

            ensureUICoroutine = null;
        }

        /// <summary>只启动一个 UI 修复协程，覆盖首次启动、场景返回和 PanelRoot 重建。</summary>
        private void RequestUIRefresh()
        {
            if (!isActiveAndEnabled || UIManager.IsShuttingDown)
                return;

            if (ensureUICoroutine == null)
                ensureUICoroutine = StartCoroutine(EnsurePanelWhenUIReady());
        }

        /// <summary>面板注册、销毁或切换时，立即检查联机入口是否需要重绑。</summary>
        private void OnInteractionSurfaceChanged()
        {
            if (UIManager.IsShuttingDown) return;

            UIManager uiManager = subscribedUIManager;
            if (uiManager == null || !IsPanelAttachedTo(uiManager.SafeAreaRoot))
            {
                RequestUIRefresh();
                return;
            }

            TryBindMainMenuButton(uiManager);
            RefreshInteractableState();
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
            // 实时状态由业务更新，停用 Prefab 静态文本绑定，避免切语言时覆盖动态内容。
            DisableStaticBinding(statusText);
            DisableStaticBinding(playerCountText);
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

            if (string.IsNullOrEmpty(lastStatusTemplate))
                RefreshOfflineStatus();
            else
                RefreshLocalizedStatus();
            lastDisplayedPlayerCount = -1;
            RefreshInteractableState();
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

            // 面板随启动资源开放，创建和加入仍等待完整玩法目录就绪。
            if (lastContentReady != IsContentReady || lastInWorld != IsInWorld ||
                lastPrivateSession != (networkManager?.IsPrivateSession == true))
            {
                RefreshInteractableState();
                if (GameNetwork.Session.State == NetworkSessionState.Offline)
                    RefreshOfflineStatus();
            }

            int playerCount = 0;
            if (NetworkServer.active)
                playerCount = NetworkServer.connections.Count;
            else if (NetworkClient.active)
                playerCount = NetworkClient.spawned.Count;

            if (playerCountText != null && lastDisplayedPlayerCount != playerCount)
            {
                lastDisplayedPlayerCount = playerCount;
                playerCountText.text = FlatWorldLocalizationService.GetUiFormat("玩家：{0} / {1}", playerCount, 2);
            }

        }

        public void I_ShowPanel()
        {
            if (panel == null)
            {
                showPanelWhenReady = true;
                RequestUIRefresh();
                return;
            }

            showPanelWhenReady = false;
            RefreshLanAddresses();
            RefreshInteractableState();
            RefreshLocalizedStatus();
            subscribedUIManager?.ConfigureSettingsPanelLayer(panel);
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
            if (panel != null && panel.IsOpen()) I_ClosePanel();
            else I_ShowPanel();
        }

        private void RefreshInteractableState()
        {
            bool offline = GameNetwork.Session.State == NetworkSessionState.Offline;
            bool privateHost = NetworkServer.active && networkManager?.IsPrivateSession == true;
            lastPrivateSession = networkManager?.IsPrivateSession == true;
            lastContentReady = IsContentReady;
            lastInWorld = IsInWorld;
            Player localPlayer = ItemMgr.Instance?.User_Player;
            if (lastInWorld && playerNameInput != null && localPlayer != null)
                playerNameInput.SetTextWithoutNotify(localPlayer.Data?.Name_User ?? localPlayer.ProfileName);
            panel?.SetButtonVisible(HostButtonKey, lastInWorld);
            panel?.SetButtonVisible(JoinButtonKey, !lastInWorld);
            SetHostButtonLabel("公开到局域网");
            if (hostButton != null)
                hostButton.interactable = (offline || privateHost) && lastContentReady && lastInWorld && localPlayer != null &&
                    GameManager.ExistingInstance.IsGameplayReady;
            if (joinButton != null)
                joinButton.interactable = offline && lastContentReady && !lastInWorld;
            if (disconnectButton != null)
                disconnectButton.interactable = !offline && !privateHost;
            TMP_Text disconnectLabel = disconnectButton != null ? disconnectButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (disconnectLabel != null)
            {
                DisableStaticBinding(disconnectLabel);
                disconnectLabel.text = FlatWorldLocalizationService.GetUiText(NetworkServer.active ? "关闭公开" : "断开连接");
            }
            if (playerNameInput != null)
                playerNameInput.interactable = offline && !lastInWorld;

            if (portInput != null)
                portInput.interactable = offline || privateHost;
            if (NetworkServer.active && !lastServerActive) RefreshLanAddresses();
            lastServerActive = NetworkServer.active;
            RefreshAddressPresentation(offline);
            RefreshLocalizedStatus();
        }

        private static bool IsInWorld => GameManager.ExistingInstance?.IsInGameWorld == true;

        private static bool IsContentReady => GameRes.ExistingInstance != null && GameRes.ExistingInstance.isLoadFinish;

        private void RefreshOfflineStatus()
        {
            SetStatus(IsContentReady
                ? (IsInWorld ? "当前世界为单机，可公开到局域网供好友加入。" : "填写好友的连接地址，加入已有世界。")
                : "正在加载资源…");
        }

        private void SetStatus(string message)
        {
            SetStatus(message, Array.Empty<object>());
        }

        private void SetStatus(string template, object[] arguments)
        {
            lastStatusTemplate = template;
            lastStatusArguments = arguments ?? Array.Empty<object>();
            RefreshLocalizedStatus();
        }

        private void RefreshLocalizedStatus()
        {
            if (statusText == null || string.IsNullOrEmpty(lastStatusTemplate))
                return;
            statusText.text = FlatWorldLocalizationService.GetUiFormat(lastStatusTemplate, lastStatusArguments);
        }

        private void HandleLanguageChanged(string _)
        {
            RefreshLocalizedStatus();
            RefreshInteractableState();
            lastDisplayedPlayerCount = -1;
        }

        private static void DisableStaticBinding(TMP_Text text)
        {
            if (text != null && text.TryGetComponent(out LocalizedTextBinder binder))
                binder.enabled = false;
        }

        #region 局域网面板呈现

        private void RefreshAddressPresentation(bool offline)
        {
            if (addressInput == null) return;
            if (NetworkServer.active)
            {
                if (!showingHostAddress) addressBeforeHosting = addressInput.text;
                showingHostAddress = true;
                addressInput.SetTextWithoutNotify(lanAddressText);
            }
            else if (showingHostAddress)
            {
                showingHostAddress = false;
                addressInput.SetTextWithoutNotify(addressBeforeHosting ?? string.Empty);
            }
            // 公开后的地址只读但允许选中复制，关闭公开后恢复加入地址草稿。
            addressInput.readOnly = NetworkServer.active;
            addressInput.interactable = NetworkServer.active || (offline && !IsInWorld);
            TMP_Text label = panel.GetText(AddressInputKey + "_标签");
            if (label == null) return;
            DisableStaticBinding(label);
            label.text = FlatWorldLocalizationService.GetUiText(
                NetworkServer.active ? "局域网连接地址" : "主机连接地址");
        }

        private void SetHostButtonLabel(string text)
        {
            TMP_Text label = hostButton != null ? hostButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null) return;
            DisableStaticBinding(label);
            label.text = FlatWorldLocalizationService.GetUiText(text);
        }

        private void RefreshLanAddresses()
        {
            // 只在打开面板和会话变化时读取本机网卡，避免每帧枚举网络接口。
            var addresses = new List<string>();
            ushort port = NetworkServer.active && networkManager != null
                ? networkManager.GetComponent<kcp2k.KcpTransport>()?.Port ?? ReadPort() : ReadPort();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                            System.Net.IPAddress.IsLoopback(address.Address)) continue;
                        string endpoint = address.Address + ":" + port;
                        if (!addresses.Contains(endpoint)) addresses.Add(endpoint);
                    }
                }
            }
            catch (Exception exception) when (exception is NetworkInformationException ||
                                              exception is SocketException ||
                                              exception is PlatformNotSupportedException)
            {
                Debug.LogWarning($"[联机UI] 无法读取局域网地址：{exception.Message}", this);
            }
            lanAddressText = addresses.Count > 0 ? string.Join(" / ", addresses) :
                FlatWorldLocalizationService.GetUiText("未找到可用的局域网地址");
        }

        #endregion

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
            showingHostAddress = false;
            addressBeforeHosting = null;
            portInput = null;
            statusText = null;
            playerCountText = null;
            lastDisplayedPlayerCount = -1;
            hostButton = null;
            joinButton = null;
            disconnectButton = null;
            closeButton = null;
        }

        #endregion
    }
}
