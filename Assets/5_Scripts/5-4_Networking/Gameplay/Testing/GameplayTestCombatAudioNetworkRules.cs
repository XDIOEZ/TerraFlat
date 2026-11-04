#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FlatWorld.Spaceflight;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static class GameplayTestCombatAudioNetworkRules
    {
        #region 正式音频消息与接收门禁
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register() => GameplayTestSystemProbe.Register("networking", "combat_audio_wire", Capture);

        private static JObject Capture(JObject args)
        {
            var source = new NetworkCombatAudioFeedback { Sequence = 17, RealmRevision = 4,
                CueId = "combat.blocked.stone", Location = new CombatAudioLocation { InSpace = true,
                    WorldKey = "space:test", ShipId = "test-ship", ShipLocalPosition = new Vector2(2.5f, -3.25f),
                    Position = new SpaceSectorPosition(default, new SpaceVector2(123456.125d, -98765.5d)) } };
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.Write(source);
            using NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment());
            NetworkCombatAudioFeedback decoded = reader.Read<NetworkCombatAudioFeedback>();
            var observer = new SpaceNetworkObserver { InSpace = true, RealmRevision = 4 };
            ulong sequence = 0;
            bool first = NetworkSpaceStateCoordinator.TryAcceptCombatAudioFeedback(decoded, observer, ref sequence);
            bool duplicate = NetworkSpaceStateCoordinator.TryAcceptCombatAudioFeedback(decoded, observer, ref sequence);
            decoded.Sequence++;
            observer.RealmRevision++;
            bool staleRealm = NetworkSpaceStateCoordinator.TryAcceptCombatAudioFeedback(decoded, observer, ref sequence);
            decoded.Sequence++;
            observer.RealmRevision = 4;
            observer.InSpace = false;
            bool wrongRealm = NetworkSpaceStateCoordinator.TryAcceptCombatAudioFeedback(decoded, observer, ref sequence);
            decoded.Sequence++;
            observer.InSpace = true;
            bool resumed = NetworkSpaceStateCoordinator.TryAcceptCombatAudioFeedback(decoded, observer, ref sequence);
            return new JObject { ["cue"] = decoded.CueId, ["world"] = decoded.Location.WorldKey,
                ["ship"] = decoded.Location.ShipId, ["x"] = decoded.Location.Position.LocalMeters.X,
                ["y"] = decoded.Location.Position.LocalMeters.Y, ["localX"] = decoded.Location.ShipLocalPosition.x,
                ["localY"] = decoded.Location.ShipLocalPosition.y, ["first"] = first, ["duplicate"] = duplicate,
                ["staleRealm"] = staleRealm, ["wrongRealm"] = wrongRealm, ["resumed"] = resumed };
        }
        #endregion
    }
}
#endif
