namespace FlatWorld.Networking
{
    /// <summary>
    /// Authority information exposed to gameplay without leaking Mirror types.
    /// </summary>
    public interface INetworkEntityContext
    {
        ulong NetworkId { get; }
        bool IsSpawned { get; }
        bool IsLocalPlayer { get; }
        bool HasStateAuthority { get; }
        bool HasInputAuthority { get; }
    }
}
