using FlatWorld.Networking;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.MirrorAdapter
{
    /// <summary>
    /// Add this beside NetworkIdentity on networked prefabs, then let gameplay
    /// query INetworkEntityContext for authority checks.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class MirrorNetworkEntityContext : NetworkBehaviour, INetworkEntityContext
    {
        public ulong NetworkId => netId;
        public bool IsSpawned => netId != 0 && (isServer || isClient);
        public bool IsLocalPlayer => isLocalPlayer;
        public bool HasStateAuthority => isServer;
        public bool HasInputAuthority => isOwned;
    }
}
