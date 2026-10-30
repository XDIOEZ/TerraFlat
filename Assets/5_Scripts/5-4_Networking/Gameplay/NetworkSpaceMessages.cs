using FlatWorld.Spaceflight;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    #region 太空独立协议
    // 太空只传规范坐标和操作意图，表现坐标不参与服务器结算。
    public struct NetworkSpaceSnapshotRequest : NetworkMessage { }
    public struct NetworkSpaceSnapshotBegin : NetworkMessage
    {
        public ulong Sequence;
        public int PayloadBytes, ChunkCount;
        public uint Hash;
    }
    public struct NetworkSpaceSnapshotChunk : NetworkMessage
    {
        public ulong Sequence;
        public int Index;
        public byte[] Data;
    }
    public struct NetworkSpaceInput : NetworkMessage
    {
        public uint RealmRevision;
        public uint Sequence;
        public SpacePassengerInput Input;
    }
    public struct NetworkSpaceIntentRequest : NetworkMessage
    {
        public uint Sequence;
        public SpaceNetworkIntent Intent;
    }
    public struct NetworkSpaceIntentResponse : NetworkMessage
    {
        public uint Sequence;
        public bool Accepted;
        public string Reason;
    }
    public struct NetworkSpaceProjectilePose
    {
        public int Guid;
        public string ItemId;
        public SpaceSectorPosition Position;
        public SpaceVector2 Velocity;
        public float Rotation;
        public string SurfaceWorldKey;
        public float VisualHeight, VisualRotation;
    }
    public struct NetworkSpaceProjectilePoses : NetworkMessage
    {
        public ulong Sequence;
        public NetworkSpaceProjectilePose[] Poses;
    }
    #endregion
}
