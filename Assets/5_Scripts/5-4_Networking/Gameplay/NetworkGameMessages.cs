// AI-Context: Mirror 联机协议 DTO；字段变更必须同步升级协议版本，并同时更新发送端和接收端。
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static class NetworkGameplayProtocol
    {
        public const int CurrentVersion = 8;
        public const int SnapshotChunkBytes = 24 * 1024;
        public const int MaxSnapshotBytes = 64 * 1024 * 1024;

        public static uint CalculatePayloadHash(byte[] payload)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (payload == null)
                    return hash;

                for (int i = 0; i < payload.Length; i++)
                    hash = (hash ^ payload[i]) * 16777619u;
                return hash;
            }
        }
    }

    public struct NetworkProtocolHello : NetworkMessage
    {
        public int Version;
        public int ModApiVersion;
        public string ModSetHash;
        public string ModSummary;
    }

    public struct NetworkProtocolRejected : NetworkMessage
    {
        public int ServerVersion;
        public int ClientVersion;
        public string Reason;
    }

    public struct NetworkJoinRequest : NetworkMessage
    {
        public string PlayerName;
    }

    public struct NetworkWorldSnapshot : NetworkMessage
    {
        public string PlanetName;
        public int Seed;
        public int GenerationProtocol;
        public int PlanetRadius;
        public float NoiseScale;
        public bool AutoGenerateMap;
        public int ChunkSizeX;
        public int ChunkSizeY;
        public uint GenerationSettingsHash;
        public byte[] CompressedSaveData;
    }

    public struct NetworkWorldSnapshotBegin : NetworkMessage
    {
        public int TransferId;
        public string PlanetName;
        public int Seed;
        public int GenerationProtocol;
        public int PlanetRadius;
        public float NoiseScale;
        public bool AutoGenerateMap;
        public int ChunkSizeX;
        public int ChunkSizeY;
        public uint GenerationSettingsHash;
        public int CompressedBytes;
        public int ChunkCount;
        public uint PayloadHash;
    }

    public struct NetworkWorldSnapshotChunk : NetworkMessage
    {
        public int TransferId;
        public int ChunkIndex;
        public byte[] Data;
    }

    public static class NetworkMapGenerationProtocol
    {
        public const int CurrentVersion = 1;

        public static uint CalculateSettingsHash(
            int seed,
            int planetRadius,
            float noiseScale,
            bool autoGenerateMap,
            int chunkSizeX,
            int chunkSizeY)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Add(hash, CurrentVersion);
                hash = Add(hash, seed);
                hash = Add(hash, planetRadius);
                hash = Add(hash, (int)System.Math.Round(noiseScale * 1000000d));
                hash = Add(hash, autoGenerateMap ? 1 : 0);
                hash = Add(hash, chunkSizeX);
                hash = Add(hash, chunkSizeY);
                return hash;
            }
        }

        private static uint Add(uint hash, int value)
        {
            unchecked
            {
                hash = (hash ^ (byte)value) * 16777619u;
                hash = (hash ^ (byte)(value >> 8)) * 16777619u;
                hash = (hash ^ (byte)(value >> 16)) * 16777619u;
                hash = (hash ^ (byte)(value >> 24)) * 16777619u;
                return hash;
            }
        }
    }

    public struct NetworkWorldReady : NetworkMessage
    {
    }

    public struct NetworkWeatherStateRequest : NetworkMessage
    {
    }

    /// <summary>服务器广播的权威天气事件状态；客户端只应用，不参与阶段调度。</summary>
    public struct NetworkWeatherStateMessage : NetworkMessage
    {
        public string PlanetName;
        public WeatherType Weather;
        public WeatherPhase Phase;
        public float Intensity;
        public float PhaseStartedTotalTime;
        public float PhaseEndTotalTime;
        public float NextWeatherEventTotalTime;
        public int RandomCursor;
        public int EventSequence;
        public int DataVersion;
    }

    public struct NetworkItemStateRequest : NetworkMessage
    {
    }

    /// <summary>客户端提交自己交互后产生的 Item/Module 状态。</summary>
    public struct NetworkItemStateSubmit : NetworkMessage
    {
        public int ItemGuid;
        public uint BaseRevision;
        public byte[] Payload;
    }

    /// <summary>客户端预测生成的世界掉落物，请求服务端确认并创建同 GUID 权威实例。</summary>
    public struct NetworkItemSpawnRequest : NetworkMessage
    {
        public int ItemGuid;
        public string ItemId;
        public byte[] Payload;
        public Vector2 StartPosition;
        public Vector2 EndPosition;
        public float Duration;
        public Vector3 Scale;
    }

    /// <summary>服务端确认的世界掉落物生命周期消息，客户端缺少实例时必须创建。</summary>
    public struct NetworkItemSpawnMessage : NetworkMessage
    {
        public int ItemGuid;
        public string ItemId;
        public uint Revision;
        public uint PayloadHash;
        public byte[] Payload;
        public Vector2 StartPosition;
        public Vector2 EndPosition;
        public float Duration;
        public Vector3 Scale;
        public bool AnimateDrop;
    }

    /// <summary>客户端请求放置建筑；服务端必须重新校验距离、地块、碰撞和建筑模块。</summary>
    public struct NetworkBuildingPlaceRequest : NetworkMessage
    {
        public uint RequestToken;
        public int SourceItemGuid;
        public string ItemId;
        public byte[] SourcePayload;
        public Vector3 Position;
    }

    /// <summary>只有 Accepted=true 时客户端才能扣除一个建造材料。</summary>
    public struct NetworkBuildingPlaceResponse : NetworkMessage
    {
        public uint RequestToken;
        public int SourceItemGuid;
        public bool Accepted;
        public float RemainingAmount;
        public string Reason;
    }

    public struct NetworkBuildingDismantleRequest : NetworkMessage
    {
        public uint RequestToken;
        public int BuildingGuid;
    }

    public struct NetworkBuildingDismantleResponse : NetworkMessage
    {
        public uint RequestToken;
        public int BuildingGuid;
        public bool Accepted;
        public string Reason;
    }

    public struct NetworkItemPickupRequest : NetworkMessage
    {
        public int ItemGuid;
    }

    public struct NetworkItemPickupResponse : NetworkMessage
    {
        public int ItemGuid;
        public uint ReservationToken;
        public bool Granted;
        public byte[] Payload;
    }

    public struct NetworkItemPickupCommit : NetworkMessage
    {
        public int ItemGuid;
        public uint ReservationToken;
        public bool AcceptedIntoInventory;
    }

    /// <summary>服务器确认并广播的权威 Item/Module 状态。</summary>
    public struct NetworkItemStateMessage : NetworkMessage
    {
        public int ItemGuid;
        public string ItemId;
        public uint Revision;
        public uint PayloadHash;
        public byte[] Payload;
        public bool SpawnIfMissing;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    public struct NetworkItemDespawnMessage : NetworkMessage
    {
        public int ItemGuid;
        public string ItemId;
    }
}
