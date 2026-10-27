using System;
using FlatWorld.Spaceflight;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using Mirror;
using UnityEngine;
using UnityEngine.Pool;

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
        public bool HasGeneratedSource;
        public bool HasIndependentSource;
        public string SourceWorldKey;
        public int SourceChunkX, SourceChunkY;
    }

    public struct NetworkNaturalPhysicsPoseBatch : NetworkMessage
    {
        public string WorldKey;
        public uint RealmRevision;
        public NaturalEntityPhysicsNetworkPose[] Poses;
    }

    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 生态权威表现快照
        private void OnNaturalHarvestCommitted(string world, int guid, Vector2 position,
            string sourceWorldKey, int sourceX, int sourceY, ItemData snapshot)
        {
            if (!NetworkServer.active) return;
            byte[] payload = null;
            if (snapshot != null && !ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out payload)) return;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection is LocalConnectionToClient || !ResolveConnection(connection, out ConnectionState state, out _) ||
                    SpaceSession.Current == null || !SpaceSession.Current.TryGetNetworkObserver(state.Profile, out SpaceNetworkObserver observer) ||
                    observer.InSpace || observer.WorldKey != world) continue;
                connection.Send(new NetworkNaturalHarvestState { WorldKey = world, RealmRevision = observer.RealmRevision,
                    Guid = guid, Position = position, Snapshot = payload,
                    HasGeneratedSource = sourceWorldKey != null, SourceWorldKey = sourceWorldKey,
                    SourceChunkX = sourceX, SourceChunkY = sourceY });
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
            if (message.HasIndependentSource)
                NaturalEntityEcsService.ApplyIndependentPhysicsNetworkState(message.WorldKey, message.SourceWorldKey, message.SourceChunkX,
                    message.SourceChunkY, message.Guid, snapshot);
            else NaturalEntityEcsService.ApplyNetworkState(message.WorldKey, message.Guid, message.Position, snapshot,
                message.HasGeneratedSource ? message.SourceWorldKey ?? string.Empty : null,
                message.SourceChunkX, message.SourceChunkY);
        }

        private sealed class KnownIndependentPhysicsSources
        {
            internal string World;
            internal uint Realm;
            internal readonly HashSet<(string World, int X, int Y, int Guid)> Sources = new();
            internal readonly HashSet<(string World, int X, int Y, int Guid)> Visible = new();
        }
        private readonly Dictionary<NetworkConnectionToClient, KnownIndependentPhysicsSources> knownIndependentPhysicsSources = new();

        private void OnIndependentNaturalStateCommitted(string world, string sourceWorldKey,
            int sourceX, int sourceY, int guid, Vector2 position, ItemData snapshot)
        {
            if (!NetworkServer.active) return;
            byte[] payload = null;
            if (snapshot != null && !ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out payload)) return;
            var source = (sourceWorldKey, sourceX, sourceY, guid);
            if (snapshot == null)
                foreach (KnownIndependentPhysicsSources known in knownIndependentPhysicsSources.Values)
                    if (known.World == world) known.Sources.Remove(source);
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection is LocalConnectionToClient || !ResolveConnection(connection, out ConnectionState state, out _) ||
                    SpaceSession.Current == null || !SpaceSession.Current.TryGetNetworkObserver(state.Profile, out SpaceNetworkObserver observer) ||
                    observer.InSpace || observer.WorldKey != world) continue;
                connection.Send(new NetworkNaturalHarvestState { WorldKey = world, RealmRevision = observer.RealmRevision,
                    Guid = guid, Position = position, Snapshot = payload, HasIndependentSource = true,
                    SourceWorldKey = sourceWorldKey, SourceChunkX = sourceX, SourceChunkY = sourceY });
                if (snapshot != null && knownIndependentPhysicsSources.TryGetValue(connection, out var known) &&
                    known.World == world && known.Realm == observer.RealmRevision) known.Sources.Add(source);
            }
        }

        // 运动复用正式快照节奏，每个新可见源只发送一次完整玩法载荷。
        private void SendIndependentNaturalPhysicsSnapshot(NetworkConnectionToClient connection, SpaceNetworkObserver observer)
        {
            NaturalEntityPhysicsNetworkPose[] poses = NaturalEntityEcsService.CaptureIndependentPhysicsNetworkPoses(
                observer.WorldKey, observer.Position.LocalMeters.ToVector2(), 256f);
            if (!knownIndependentPhysicsSources.TryGetValue(connection, out var knownState))
                knownIndependentPhysicsSources.Add(connection, knownState = new KnownIndependentPhysicsSources());
            if (knownState.World != observer.WorldKey || knownState.Realm != observer.RealmRevision)
            { knownState.Sources.Clear(); knownState.World = observer.WorldKey; knownState.Realm = observer.RealmRevision; }
            var known = knownState.Sources;
            knownState.Visible.Clear();
            foreach (NaturalEntityPhysicsNetworkPose pose in poses)
                knownState.Visible.Add((pose.SourceWorldKey, pose.SourceChunkX, pose.SourceChunkY, pose.Guid));
            known.IntersectWith(knownState.Visible);
            if (poses.Length == 0) return;
            using var movingScope = ListPool<NaturalEntityPhysicsNetworkPose>.Get(out var movingPoses);
            foreach (NaturalEntityPhysicsNetworkPose pose in poses)
            {
                if (pose.IsDynamic) movingPoses.Add(pose);
                var source = (pose.SourceWorldKey, pose.SourceChunkX, pose.SourceChunkY, pose.Guid);
                if (known.Contains(source)) continue;
                if (!NaturalEntityEcsService.TryCaptureIndependentPhysicsNetworkState(observer.WorldKey,
                    pose.SourceWorldKey, pose.SourceChunkX, pose.SourceChunkY, pose.Guid, out ItemData snapshot) ||
                    !ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out byte[] payload)) continue;
                connection.Send(new NetworkNaturalHarvestState { WorldKey = observer.WorldKey,
                    RealmRevision = observer.RealmRevision, Guid = pose.Guid, Position = pose.Position,
                    Snapshot = payload, HasIndependentSource = true, SourceWorldKey = pose.SourceWorldKey,
                    SourceChunkX = pose.SourceChunkX, SourceChunkY = pose.SourceChunkY });
                known.Add(source);
            }
            for (int offset = 0; offset < movingPoses.Count; offset += 64)
            {
                var part = new NaturalEntityPhysicsNetworkPose[Math.Min(64, movingPoses.Count - offset)];
                movingPoses.CopyTo(offset, part, 0, part.Length);
                connection.Send(new NetworkNaturalPhysicsPoseBatch
                { WorldKey = observer.WorldKey, RealmRevision = observer.RealmRevision, Poses = part });
            }
        }

        private void OnIndependentNaturalPhysicsPoseBatch(NetworkNaturalPhysicsPoseBatch message)
        {
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (NetworkServer.active || player == null || message.RealmRevision != player.RealmRevision ||
                player.CorePlayer?.Data?.CurrentSceneName != message.WorldKey) return;
            NaturalEntityEcsService.ApplyIndependentPhysicsNetworkPoses(message.WorldKey, message.Poses);
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
            if (SpaceSession.Current == null ||
                !SpaceSession.Current.TryGetNetworkObserver(profile, out SpaceNetworkObserver observer) || observer.InSpace) return;
            SendIndependentNaturalPhysicsSnapshot(connection, observer);
            if (AiEcologyNetworkBridge.Capture == null) return;
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
            knownIndependentPhysicsSources.Clear();
            AiEcologyNetworkBridge.Clear?.Invoke();
        }
        #endregion
    }
}
