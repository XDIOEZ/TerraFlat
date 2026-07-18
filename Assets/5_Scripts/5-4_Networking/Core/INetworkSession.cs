using System;

namespace FlatWorld.Networking
{
    /// <summary>
    /// Game-facing network session API. Gameplay code should depend on this
    /// interface instead of calling Mirror directly.
    /// </summary>
    public interface INetworkSession
    {
        NetworkRole Role { get; }
        NetworkSessionState State { get; }
        bool IsOnline { get; }
        bool IsServer { get; }
        bool IsClient { get; }

        event Action<NetworkSessionState> StateChanged;
        event Action<string> Error;

        NetworkStartResult StartHost(ushort port = 7777);
        NetworkStartResult StartServer(ushort port = 7777);
        NetworkStartResult StartClient(string address, ushort port = 7777);
        void Stop();
    }
}
