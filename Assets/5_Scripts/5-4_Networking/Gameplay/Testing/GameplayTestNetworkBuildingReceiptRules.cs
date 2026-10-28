#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static class GameplayTestNetworkBuildingReceiptRules
    {
        #region 建造与拆除回执采样

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register() => GameplayTestSystemProbe.Register(
            "networking", "building_request_ledger", CaptureBuildingRequestLedger);

        // 只调用正式账本，记录回执、到期、断线与回调中新请求，业务断言由 Lua 持有。
        private static JObject CaptureBuildingRequestLedger(JObject args)
        {
            var rejected = new JArray();
            NetworkBuildingRequestLedger ledger = null;
            ledger = new NetworkBuildingRequestLedger((request, reason) =>
            {
                rejected.Add(new JObject { ["guid"] = request.ItemGuid,
                    ["kind"] = request.Kind.ToString(), ["reason"] = reason });
                if (request.ItemGuid == 103)
                    ledger.Add(NetworkBuildingRequestKind.Place, null, 104, 112f);
            });
            uint place = ledger.Add(NetworkBuildingRequestKind.Place, null, 101, 100f);
            uint dismantle = ledger.Add(NetworkBuildingRequestKind.Dismantle, null, 102, 100f);
            bool wrongKindAccepted = ledger.TryTake(place, NetworkBuildingRequestKind.Dismantle, out _);
            bool matched = ledger.TryTake(place, NetworkBuildingRequestKind.Place, out var accepted);
            bool duplicateAccepted = ledger.TryTake(place, NetworkBuildingRequestKind.Place, out _);
            bool onlyDismantle = !ledger.HasPending(NetworkBuildingRequestKind.Place) &&
                ledger.HasPending(NetworkBuildingRequestKind.Dismantle);
            ledger.Add(NetworkBuildingRequestKind.Place, null, 103, 104f);
            ledger.Expire(107f);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++) ledger.Expire(107f);
            long waitingAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            int beforeDeadline = rejected.Count;
            ledger.Expire(108f);
            int firstExpiryCount = rejected.Count, remainingAfterFirst = ledger.Count;
            ledger.Expire(112f);
            int callbackNewRequestCount = ledger.Count;
            ledger.Add(NetworkBuildingRequestKind.Dismantle, null, 105, 112f);
            ledger.RejectAll("测试断线");
            ledger.Expire(1000f);
            return new JObject { ["placeToken"] = place, ["dismantleToken"] = dismantle,
                ["wrongKindAccepted"] = wrongKindAccepted, ["matched"] = matched,
                ["acceptedGuid"] = accepted.ItemGuid, ["duplicateAccepted"] = duplicateAccepted,
                ["onlyDismantle"] = onlyDismantle, ["beforeDeadline"] = beforeDeadline,
                ["waitingAllocatedBytes"] = waitingAllocatedBytes, ["firstExpiryCount"] = firstExpiryCount,
                ["remainingAfterFirst"] = remainingAfterFirst, ["callbackNewRequestCount"] = callbackNewRequestCount,
                ["remaining"] = ledger.Count, ["rejected"] = rejected };
        }

        #endregion
    }
}
#endif
