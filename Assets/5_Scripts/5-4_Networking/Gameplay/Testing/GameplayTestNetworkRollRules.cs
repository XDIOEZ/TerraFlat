#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>采样正式翻滚动作协议和体力交接保护，业务断言仍由 Lua 持有。</summary>
    public static class GameplayTestNetworkRollRules
    {
        #region 操作登记
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register()
        {
            GameplayTestSystemProbe.Register("networking", "roll_snapshot", CaptureSnapshot);
            GameplayTestSystemProbe.Register("networking", "roll_stamina_handoff", CaptureStaminaHandoff);
        }
        #endregion

        #region 正式协议与体力保护采样
        private static JObject CaptureSnapshot(JObject args)
        {
            var state = new NetworkPlayerRollState
            {
                Sequence = 17, Active = true, Direction = Vector2.left, Elapsed = .1f,
                Duration = .4f, TimeScale = 1f, SampleTime = 10000000d
            };
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.Write(state);
            using NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment());
            NetworkPlayerRollState decoded = reader.Read<NetworkPlayerRollState>();
            var result = new JObject
            {
                ["protocol"] = NetworkGameplayProtocol.CurrentVersion,
                ["roundtrip"] = state.Equals(decoded), ["remainingBytes"] = reader.Remaining,
                ["delayedElapsed"] = decoded.ResolveElapsed(10000000.15d),
                ["pastEndElapsed"] = decoded.ResolveElapsed(10000010d),
                ["futureSampleElapsed"] = decoded.ResolveElapsed(9999999d)
            };
            decoded.TimeScale = 0f;
            result["pausedElapsed"] = decoded.ResolveElapsed(10000010d);
            decoded.Elapsed = float.NaN;
            result["invalidElapsed"] = decoded.ResolveElapsed(10000000d);
            return result;
        }

        private static JObject CaptureStaminaHandoff(JObject args)
        {
            Ex_ModData_MemoryPackable State(float value)
            {
                var state = new Ex_ModData_MemoryPackable { StableName = "stamina", ModuleId = ModText.Stamina };
                state.WriteData(new Mod_Stamina.StaminaData { CurrentStamina = value, MaxStamina = 100f });
                return state;
            }
            var guard = new NetworkRollStaminaHandoff();
            var authority = State(90f);
            bool started = guard.Begin(7, authority, 90f);
            authority.WriteData(new Mod_Stamina.StaminaData { CurrentStamina = 1f, MaxStamina = 100f });
            var incoming = State(100f);
            var states = new Dictionary<string, ModuleData> { ["stamina"] = incoming };
            bool protectedState = guard.ProtectIncoming(states);
            bool missingRejected = !guard.ProtectIncoming(new Dictionary<string, ModuleData>());
            bool duplicateBeginRejected = !guard.Begin(8, State(80f), 80f);
            bool wrongRequestRejected = !guard.Acknowledge(8, 90f);
            bool wrongAmountRejected = !guard.Acknowledge(7, 100f);
            bool pendingAfterBadAck = guard.IsPending;
            bool acknowledged = guard.Acknowledge(7, 90f);
            var reducedCapacity = new NetworkRollStaminaHandoff();
            reducedCapacity.Begin(9, State(90f), 90f);
            bool reducedCapacityAcknowledged = reducedCapacity.Acknowledge(9, 50f);
            return new JObject
            {
                ["started"] = started, ["protected"] = protectedState,
                ["protectedCurrent"] = incoming.GetData<Mod_Stamina.StaminaData>().CurrentStamina,
                ["missingRejected"] = missingRejected, ["duplicateBeginRejected"] = duplicateBeginRejected,
                ["wrongRequestRejected"] = wrongRequestRejected, ["wrongAmountRejected"] = wrongAmountRejected,
                ["pendingAfterBadAck"] = pendingAfterBadAck, ["acknowledged"] = acknowledged,
                ["pendingAfterAck"] = guard.IsPending, ["duplicateAckRejected"] = !guard.Acknowledge(7, 90f),
                ["reducedCapacityAcknowledged"] = reducedCapacityAcknowledged,
                ["reducedCapacityPending"] = reducedCapacity.IsPending
            };
        }
        #endregion
    }
}
#endif
