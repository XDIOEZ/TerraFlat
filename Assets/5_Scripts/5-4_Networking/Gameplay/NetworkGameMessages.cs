using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkJoinRequest : NetworkMessage
    {
        public string PlayerName;
    }

    public struct NetworkWorldSnapshot : NetworkMessage
    {
        public string PlanetName;
        public int Seed;
        public int ChunkSizeX;
        public int ChunkSizeY;
        public byte[] CompressedSaveData;
    }

    public struct NetworkWorldReady : NetworkMessage
    {
    }
}
