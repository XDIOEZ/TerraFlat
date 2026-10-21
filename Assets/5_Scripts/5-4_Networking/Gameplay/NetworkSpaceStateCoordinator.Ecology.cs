using System;
using FlatWorld.Spaceflight;
using FlatWorld.NaturalEntities;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkEcologySnapshotPart : NetworkMessage
    {
        public ulong Sequence;
        public uint RealmRevision;
        public string WorldKey;
        public int Index, Count;
        public AiEcologyNetworkActor[] Actors;
    }

    public struct NetworkNaturalHarvestState : NetworkMessage
    {
        public string WorldKey;
        public uint RealmRevision;
        public int Guid;
        public Vector2 Position;
        public byte[] Snapshot;
    }

    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 生态权威表现快照
        private void OnNaturalHarvestCommitted(string world, int guid, Vector2 position)
        {
            if (!NetworkServer.active) return;
            bool exists = NaturalEntityEcsService.TryCaptureNetworkState(world, guid, position, out ItemData snapshot);
            byte[] payload = null;
            if (exists && !ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out payload)) return;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection is LocalConnectionToClient || !ResolveConnection(connection, out ConnectionState state, out _) ||
                    SpaceSession.Current == null || !SpaceSession.Current.TryGetNetworkObserver(state.Profile, out SpaceNetworkObserver observer) ||
                    observer.InSpace || observer.WorldKey != world) continue;
                connection.Send(new NetworkNaturalHarvestState { WorldKey = world, RealmRevision = observer.RealmRevision,
                    Guid = guid, Position = position, Snapshot = payload });
            }
        }

        private void OnNaturalHarvestState(NetworkNaturalHarvestState message)
        {
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (NetworkServer.active || player == null || message.RealmRevision != player.RealmRevision ||
                player.CorePlayer?.Data?.CurrentSceneName != message.WorldKey || message.Guid == 0 ||
                !float.IsFinite(message.Position.x) || !float.IsFinite(message.Position.y)) return;
            ItemData snapshot = null;
            if (message.Snapshot != null && message.Snapshot.Length > 0 &&
                (!ItemNetworkStateSerialization.TryDeserializeItemData(message.Snapshot, out snapshot) || snapshot.Guid != message.Guid)) return;
            NaturalEntityEcsService.ApplyNetworkState(message.WorldKey, message.Guid, message.Position, snapshot);
        }

        private ulong ecologySequence;
        private readonly AiEcologySnapshotBuffer ecologyBuffer = new();
        private uint displayedEcologyRealm;
        private string displayedEcologyWorld;

        private void UpdateEcologyRealm()
        {
            if (!clientStarted || NetworkServer.active) return;
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            string world = player?.CorePlayer?.Data?.CurrentSceneName;
            uint realm = player?.RealmRevision ?? 0;
            if (displayedEcologyWorld == world && displayedEcologyRealm == realm) return;
            ClearEcologySnapshots(); displayedEcologyWorld = world; displayedEcologyRealm = realm;
        }

        private void SendEcologySnapshot(NetworkConnectionToClient connection, string profile)
        {
            if (AiEcologyNetworkBridge.Capture == null || SpaceSession.Current == null ||
                !SpaceSession.Current.TryGetNetworkObserver(profile, out SpaceNetworkObserver observer) || observer.InSpace) return;
            AiEcologyNetworkActor[] actors = AiEcologyNetworkBridge.Capture(observer.WorldKey, observer.Position.LocalMeters.ToVector2(), 256f);
            if (actors == null || actors.Length > 4096) return;
            if (++ecologySequence == 0) ++ecologySequence;
            int parts = Math.Max(1, (actors.Length + 63) / 64);
            for (int index = 0; index < parts; index++)
            {
                int offset = index * 64;
                var part = new AiEcologyNetworkActor[Math.Min(64, actors.Length - offset)];
                Array.Copy(actors, offset, part, 0, part.Length);
                connection.Send(new NetworkEcologySnapshotPart { Sequence = ecologySequence, RealmRevision = observer.RealmRevision,
                    WorldKey = observer.WorldKey, Index = index, Count = parts, Actors = part });
            }
        }

        private void OnEcologySnapshot(NetworkEcologySnapshotPart message)
        {
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (NetworkServer.active || player == null || message.RealmRevision != player.RealmRevision ||
                player.CorePlayer?.Data?.CurrentSceneName != message.WorldKey) return;
            UpdateEcologyRealm();
            if (ecologyBuffer.Accept(message.WorldKey, message.RealmRevision, message.Sequence,
                message.Index, message.Count, message.Actors, out AiEcologyNetworkActor[] completed))
                AiEcologyNetworkBridge.Apply?.Invoke(message.WorldKey, completed);
        }

        private void ClearEcologySnapshots()
        {
            ecologyBuffer.Reset();
            AiEcologyNetworkBridge.Clear?.Invoke();
        }
        #endregion
    }
}
