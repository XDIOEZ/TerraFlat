using System;
using FlatWorld.Networking;
using kcp2k;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.MirrorAdapter
{
    /// <summary>
    /// Thin Mirror adapter. UI and gameplay code should use INetworkSession or
    /// GameNetwork instead of depending on this class directly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KcpTransport))]
    public class FlatWorldNetworkManager : NetworkManager, INetworkSession
    {
        private KcpTransport kcpTransport;
        private NetworkRole role = NetworkRole.Offline;
        private NetworkSessionState state = NetworkSessionState.Offline;
        private bool stopRequested;

        public NetworkRole Role => role;
        public NetworkSessionState State => state;
        public bool IsOnline => state == NetworkSessionState.Online;
        public bool IsServer => NetworkServer.active;
        public bool IsClient => NetworkClient.active;

        public event Action<NetworkSessionState> StateChanged;
        public event Action<string> Error;

        public override void Reset()
        {
            base.Reset();
            kcpTransport = GetComponent<KcpTransport>();
            transport = kcpTransport;
            maxConnections = 8;
            sendRate = 30;

            // Keep this disabled until the existing Player prefab has a
            // NetworkIdentity and local-input authority checks.
            autoCreatePlayer = false;
        }

        public override void Awake()
        {
            kcpTransport = GetComponent<KcpTransport>();
            if (transport == null)
                transport = kcpTransport;

            base.Awake();

            if (ReferenceEquals(singleton, this) && !GameNetwork.TryRegister(this))
                Debug.LogError("A different INetworkSession is already registered.", this);
        }

        public override void OnDestroy()
        {
            GameNetwork.Unregister(this);
            base.OnDestroy();
        }

        public NetworkStartResult StartHost(ushort port = 7777)
        {
            return StartSession(NetworkRole.Host, port, null);
        }

        public NetworkStartResult StartServer(ushort port = 7777)
        {
            return StartSession(NetworkRole.Server, port, null);
        }

        public NetworkStartResult StartClient(string address, ushort port = 7777)
        {
            if (string.IsNullOrWhiteSpace(address))
                return NetworkStartResult.Failed("Server address cannot be empty.");

            return StartSession(NetworkRole.Client, port, address.Trim());
        }

        public void Stop()
        {
            if (state == NetworkSessionState.Offline || state == NetworkSessionState.Stopping)
                return;

            stopRequested = true;
            SetState(NetworkSessionState.Stopping);

            if (NetworkServer.active && NetworkClient.active)
                StopHost();
            else if (NetworkServer.active)
                StopServer();
            else if (NetworkClient.active)
                StopClient();
            else
                ResetSessionState();
        }

        public override void OnStartHost()
        {
            base.OnStartHost();
            role = NetworkRole.Host;
            SetState(NetworkSessionState.Online);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (role != NetworkRole.Host)
                role = NetworkRole.Server;
            SetState(NetworkSessionState.Online);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (role == NetworkRole.Offline)
                role = NetworkRole.Client;
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            SetState(NetworkSessionState.Online);
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();

            if (stopRequested)
                return;

            if (NetworkServer.active)
            {
                role = NetworkRole.Server;
                SetState(NetworkSessionState.Online);
            }
            else
            {
                ResetSessionState();
            }
        }

        public override void OnStopHost()
        {
            stopRequested = true;
            SetState(NetworkSessionState.Stopping);
            base.OnStopHost();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ResetSessionState();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!NetworkServer.active)
                ResetSessionState();
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            RaiseError($"Client transport error ({error}): {reason}");
        }

        public override void OnServerError(NetworkConnectionToClient connection, TransportError error, string reason)
        {
            base.OnServerError(connection, error, reason);
            RaiseError($"Server transport error ({error}): {reason}");
        }

        private NetworkStartResult StartSession(NetworkRole requestedRole, ushort port, string address)
        {
            if (state != NetworkSessionState.Offline || NetworkServer.active || NetworkClient.active)
                return NetworkStartResult.Failed("A network session is already active or starting.");

            if (kcpTransport == null)
                return NetworkStartResult.Failed("KcpTransport is missing.");

            stopRequested = false;
            role = requestedRole;
            kcpTransport.Port = port;
            if (address != null)
                networkAddress = address;

            SetState(NetworkSessionState.Starting);

            try
            {
                switch (requestedRole)
                {
                    case NetworkRole.Host:
                        base.StartHost();
                        break;
                    case NetworkRole.Server:
                        base.StartServer();
                        break;
                    case NetworkRole.Client:
                        base.StartClient();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(requestedRole), requestedRole, null);
                }

                return NetworkStartResult.Started();
            }
            catch (Exception exception)
            {
                ResetSessionState();
                RaiseError(exception.Message);
                return NetworkStartResult.Failed(exception.Message);
            }
        }

        private void ResetSessionState()
        {
            stopRequested = false;
            role = NetworkRole.Offline;
            SetState(NetworkSessionState.Offline);
        }

        private void SetState(NetworkSessionState nextState)
        {
            if (state == nextState)
                return;

            state = nextState;
            StateChanged?.Invoke(state);
        }

        private void RaiseError(string message)
        {
            Error?.Invoke(message);
            Debug.LogError(message, this);
        }
    }
}
