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
            GameplayTestSystemProbe.Register("networking", "initial_sync_gate", CaptureInitialSyncGate);
            GameplayTestSystemProbe.Register("networking", "world_request_gate", CaptureWorldRequestGate);
            GameplayTestSystemProbe.Register("networking", "local_observer_window", CaptureLocalObserverWindow);
            GameplayTestSystemProbe.Register("networking", "building_orientation_request", CaptureBuildingOrientationRequest);
        }
        #endregion

        #region 普通门正式转向与网络组包采样
        private static JObject CaptureBuildingOrientationRequest(JObject args)
        {
            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
            var doors = new JArray();
            foreach (string id in new[] { "Door_Wood_Summoner", "Door_Stone_Summoner" })
            {
                ItemDefinitionDto definition = definitions.Find(value => value.Id == id)
                    ?? throw new System.InvalidOperationException("缺少门召唤器定义：" + id);
                ItemModuleDefinitionDto module = definition.Modules[ModText.Building];
                var root = new GameObject("[LuaDoorPlacementRequest]") { hideFlags = HideFlags.HideAndDontSave };
                root.SetActive(false);
                GameItem owner = null, held = null;
                try
                {
                    owner = root.AddComponent<GameItem>();
                    owner.BindData(new Data_GeneralItem { IDName = "probe:door-owner" });
                    var child = new GameObject("held-door");
                    child.transform.SetParent(root.transform, false);
                    held = child.AddComponent<GameItem>();
                    held.BindData(new Data_GeneralItem { IDName = id, Guid = 91901,
                        Stack = new ItemStack { Amount = 1 } });
                    held.Owner = owner;
                    held.SetInHand(true);
                    Mod_Building building = child.AddComponent<Mod_Building>();
                    building.item = held;
                    building.BuildingData = module.Data.ToObject<Ex_ModData>();
                    building.BuildingData.ReadData(ref building.Data);
                    var samples = new JArray();
                    // 自有手持门直接走正式转向、组包和 Mirror 往返，不复算放置角度。
                    for (int step = 0; step <= 4; step++)
                    {
                        byte[] payload = { 1, 3, 7 };
                        NetworkBuildingPlaceRequest request = NetworkItemStateCoordinator.CreateBuildingPlaceRequest(
                            building, new Vector3(3.5f, -4.5f, 0f), (uint)(17 + step), payload);
                        using NetworkWriterPooled writer = NetworkWriterPool.Get();
                        writer.Write(request);
                        using NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment());
                        NetworkBuildingPlaceRequest decoded = reader.Read<NetworkBuildingPlaceRequest>();
                        samples.Add(new JObject { ["canRotate"] = building.CanRotatePlacement,
                            ["placementTurns"] = building.PlacementQuarterTurns,
                            ["requestTurns"] = request.RotationQuarterTurns, ["wireTurns"] = decoded.RotationQuarterTurns,
                            ["mirror"] = request.HorizontalMirrorX, ["wireMirror"] = decoded.HorizontalMirrorX,
                            ["requestToken"] = decoded.RequestToken, ["sourceGuid"] = decoded.SourceItemGuid,
                            ["itemId"] = decoded.ItemId, ["remainingBytes"] = reader.Remaining,
                            ["payloadPreserved"] = decoded.SourcePayload != null && decoded.SourcePayload.Length == 3 &&
                                decoded.SourcePayload[0] == 1 && decoded.SourcePayload[1] == 3 && decoded.SourcePayload[2] == 7 });
                        if (step < 4) building.RotatePlacement();
                    }
                    held.SetInHand(false);
                    building.RotatePlacement();
                    doors.Add(new JObject { ["id"] = id, ["samples"] = samples,
                        ["hasMechanicalNode"] = held.GetComponentInChildren<Mod_MechanicalNode>(true) != null,
                        ["afterLeavingHand"] = building.PlacementQuarterTurns,
                        ["canRotateOutsideHand"] = building.CanRotatePlacement });
                }
                finally
                {
                    try { held?.PrepareForDespawn(saveData: false); owner?.PrepareForDespawn(saveData: false); }
                    finally { Object.DestroyImmediate(root); }
                }
            }
            return new JObject { ["doors"] = doors };
        }
        #endregion

        #region 正式协议与体力保护采样
        private static JObject CaptureWorldRequestGate(JObject args) => new()
        {
            ["valid"] = NetworkGameplayWorldScope.GetRequestRejection(true, false, true, "planet", 7, 7) == null,
            ["notReady"] = NetworkGameplayWorldScope.GetRequestRejection(false, false, true, "planet", 7, 7),
            ["transferring"] = NetworkGameplayWorldScope.GetRequestRejection(true, true, true, "planet", 7, 7),
            ["missingObserver"] = NetworkGameplayWorldScope.GetRequestRejection(true, false, false, "planet", 7, 7),
            ["missingWorld"] = NetworkGameplayWorldScope.GetRequestRejection(true, false, true, "", 7, 7),
            ["staleRevision"] = NetworkGameplayWorldScope.GetRequestRejection(true, false, true, "planet", 6, 7),
            ["unversioned"] = NetworkGameplayWorldScope.GetRequestRejection(true, false, true, "planet", null, 7) == null
        };

        private static JObject CaptureLocalObserverWindow(JObject args)
        {
            Vector3 authority = new(15.5f, 0f);
            Vector3 predicted = new(16.5f, 0f);
            Vector3 local = NetworkChunkStreamingCoordinator.ResolveObserverWindowPosition(true, predicted, authority);
            Vector3 remote = NetworkChunkStreamingCoordinator.ResolveObserverWindowPosition(false, predicted, authority);
            Vector3 invalid = NetworkChunkStreamingCoordinator.ResolveObserverWindowPosition(true, new(float.NaN, 0f), authority);
            Vector3 wrapped = NetworkChunkStreamingCoordinator.ResolveObserverWindowPosition(true, new(-31.5f, 0f), new(31.5f, 0f));
            return new JObject { ["localX"] = local.x, ["remoteX"] = remote.x, ["invalidX"] = invalid.x, ["wrappedX"] = wrapped.x };
        }

        private static JObject CaptureInitialSyncGate(JObject args) => new()
        {
            ["valid"] = NetworkGameplayWorldScope.CanRequestInitialSurfaceState(true, true, false, "planet", "planet"),
            ["beforeReady"] = NetworkGameplayWorldScope.CanRequestInitialSurfaceState(false, true, false, "planet", "planet"),
            ["beforeSpawn"] = NetworkGameplayWorldScope.CanRequestInitialSurfaceState(true, false, false, "planet", "planet"),
            ["duringTransfer"] = NetworkGameplayWorldScope.CanRequestInitialSurfaceState(true, true, true, "planet", "planet"),
            ["otherWorld"] = NetworkGameplayWorldScope.CanRequestInitialSurfaceState(true, true, false, "planet", "other"),
            ["missingWorld"] = NetworkGameplayWorldScope.CanRequestInitialSurfaceState(true, true, false, "", "")
        };

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
