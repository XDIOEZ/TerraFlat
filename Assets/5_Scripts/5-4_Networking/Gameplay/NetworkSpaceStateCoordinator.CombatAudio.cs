using FlatWorld.Spaceflight;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkCombatAudioFeedback : NetworkMessage
    {
        public ulong Sequence;
        public uint RealmRevision;
        public string CueId;
        public CombatAudioLocation Location;
    }

    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 权威短音事件复制
        private ulong combatAudioSequence, lastCombatAudioSequence;

        private void PublishCombatAudio(string cueId, CombatAudioLocation location)
        {
            ulong sequence = ++combatAudioSequence;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection is LocalConnectionToClient || !ResolveConnection(connection, out _, out NetworkWorldPlayer player)) continue;
                if (!player.TryGetSpaceObserver(out SpaceNetworkObserver observer) || observer.InSpace != location.InSpace) continue;
                // 地表连同洞内观察者接收事件，实际可听性由音频服务的世界与洞口规则裁定。
                connection.Send(new NetworkCombatAudioFeedback { Sequence = sequence, RealmRevision = observer.RealmRevision,
                    CueId = cueId, Location = location });
            }
        }

        private void ApplyCombatAudio(NetworkCombatAudioFeedback message)
        {
            if (NetworkServer.active) return;
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (player == null || !player.TryGetSpaceObserver(out SpaceNetworkObserver observer) ||
                !TryAcceptCombatAudioFeedback(message, observer, ref lastCombatAudioSequence)) return;
            CombatAudioFeedback.PlayLocally(message.CueId, message.Location);
        }

        internal static bool TryAcceptCombatAudioFeedback(NetworkCombatAudioFeedback message,
            SpaceNetworkObserver observer, ref ulong lastSequence)
        {
            if (message.Sequence <= lastSequence) return false;
            lastSequence = message.Sequence;
            return observer.RealmRevision == message.RealmRevision && observer.InSpace == message.Location.InSpace;
        }
        #endregion
    }
}
