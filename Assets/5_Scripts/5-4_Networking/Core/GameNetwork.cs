using System;

namespace FlatWorld.Networking
{
    /// <summary>
    /// Single access point used by gameplay systems. It falls back to an
    /// offline session so the existing single-player game keeps working.
    /// </summary>
    public static class GameNetwork
    {
        private static readonly INetworkSession OfflineSession = new OfflineNetworkSession();
        private static INetworkSession currentSession;

        public static INetworkSession Session => currentSession ?? OfflineSession;

        public static bool IsOnline => Session.IsOnline;

        /// <summary>
        /// Offline games and the server/host are authoritative. Use this gate
        /// for world simulation, spawning, combat resolution and saving.
        /// </summary>
        public static bool HasStateAuthority => !Session.IsOnline || Session.IsServer;

        public static bool TryRegister(INetworkSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (currentSession != null && !ReferenceEquals(currentSession, session))
                return false;

            currentSession = session;
            return true;
        }

        public static void Unregister(INetworkSession session)
        {
            if (ReferenceEquals(currentSession, session))
                currentSession = null;
        }

        private sealed class OfflineNetworkSession : INetworkSession
        {
            private const string NotRegisteredError =
                "No online network session is registered. Add FlatWorldNetworkManager to a bootstrap scene.";

            public NetworkRole Role => NetworkRole.Offline;
            public NetworkSessionState State => NetworkSessionState.Offline;
            public bool IsOnline => false;
            public bool IsServer => false;
            public bool IsClient => false;

            public event Action<NetworkSessionState> StateChanged
            {
                add { }
                remove { }
            }

            public event Action<string> Error
            {
                add { }
                remove { }
            }

            public NetworkStartResult StartHost(ushort port = 7777)
            {
                return NetworkStartResult.Failed(NotRegisteredError);
            }

            public NetworkStartResult StartServer(ushort port = 7777)
            {
                return NetworkStartResult.Failed(NotRegisteredError);
            }

            public NetworkStartResult StartClient(string address, ushort port = 7777)
            {
                return NetworkStartResult.Failed(NotRegisteredError);
            }

            public void Stop()
            {
            }
        }
    }
}
