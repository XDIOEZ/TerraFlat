using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 降落建筑的分帧准备
        private const double GroundPreparationFrameMilliseconds = 2d;
        private const float GroundPreparationTimeoutSeconds = 30f;
        private const double GroundPreparationHoldHeight = .5d;
        private static readonly ProfilerMarker GroundPreparationMarker = new("SpaceSession.Late.GroundPreparation");
        private static readonly ProfilerMarker GroundCaptureMarker = new("SpaceSession.Late.GroundCapture");
        private static readonly ProfilerMarker GroundCommitMarker = new("SpaceSession.Late.GroundCommit");
        private readonly Dictionary<string, GroundShipPreparation> groundPreparations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> groundPreparationHistory = new(StringComparer.Ordinal);
        private readonly Queue<string> groundPreparationHistoryOrder = new();
        private readonly List<string> groundPreparationRemoval = new();
        private readonly List<GroundShipPreparation> groundPreparationWork = new();
        private int groundPreparationNext;

        private sealed class GroundShipPreparation : IDisposable
        {
            public readonly ShipState Ship;
            public readonly ShipGroundRestoreGate RestoreGate = new();
            public readonly string World;
            public readonly GameplayWorldRuntime TargetWorld;
            public readonly Scene TargetScene;
            public readonly GameRes Resources;
            public readonly ulong DefinitionRevision, StructureRevision;
            public readonly int ResourceRevision;
            public readonly List<MachineEntity> Machines = new();
            public ChunkTilemapRenderer.MechanicalLandingPreparation MachinePresentation;
            public int Cursor, HoldSteps, Invalidations;
            public int PreparedMachines, ConsumedMachines;
            public float ActiveSeconds;
            public bool Failed, ReadyAtCommit;
            public string Error;
            public bool Ready => !Failed && Cursor == Ship.Pieces.Count && MachinePresentation?.IsReady == true;

            public GroundShipPreparation(ShipState ship, string world, int invalidations = 0)
            {
                Ship = ship; World = world; Resources = GameRes.ExistingInstance;
                TargetWorld = GameplayWorldRegistry.GetOrCreate(world);
                TargetScene = TargetWorld.Scene;
                DefinitionRevision = Resources != null ? Resources.ItemDefinitionRevision : 0;
                ResourceRevision = Resources != null ? Resources.ResourceReloadVersion : 0;
                StructureRevision = ship.StructureRevision; Invalidations = invalidations;
            }

            public bool Matches(ShipState ship, string world)
            {
                GameRes resources = GameRes.ExistingInstance;
                // 同名地表重建后旧准备根已失效，按世界实例和场景重新准备。
                if (!GameplayWorldRegistry.TryGet(world, out var current) || !ReferenceEquals(TargetWorld, current) ||
                    TargetScene != current.Scene || !ReferenceEquals(Ship, ship) || World != world || !ReferenceEquals(Resources, resources) ||
                    StructureRevision != ship.StructureRevision ||
                    DefinitionRevision != (resources != null ? resources.ItemDefinitionRevision : 0) ||
                    ResourceRevision != (resources != null ? resources.ResourceReloadVersion : 0)) return false;
                return true;
            }

            public void Dispose()
            {
                if (MachinePresentation != null)
                { PreparedMachines = MachinePresentation.PreparedCount; ConsumedMachines = MachinePresentation.ConsumedCount; }
                MachinePresentation?.Dispose(); MachinePresentation = null;
            }
        }

        private GroundShipPreparation EnsureGroundPreparation(ShipState ship, string world)
        {
            int invalidations = 0;
            if (groundPreparations.TryGetValue(ship.ShipId, out GroundShipPreparation existing))
            {
                if (existing.Matches(ship, world)) return existing;
                invalidations = existing.Invalidations + 1;
                existing.Dispose();
                RememberGroundPreparation(existing, "invalidated");
                groundPreparations.Remove(ship.ShipId);
            }
            var preparation = new GroundShipPreparation(ship, world, invalidations);
            groundPreparations.Add(ship.ShipId, preparation);
            return preparation;
        }

        private void BeginGroundPreparation(ShipAssemblyState assembly, string world)
        {
            if (!GameNetwork.HasStateAuthority) return;
            GameplayWorldRegistry.GetOrCreate(world);
            foreach (string id in assembly.MemberShipIds)
                if (GetShip(id) is ShipState ship && !ship.IsWreck) EnsureGroundPreparation(ship, world);
        }

        private void TickGroundPreparations()
        {
            if (State == null || !GameNetwork.HasStateAuthority) { ClearGroundPreparations(); return; }
            groundPreparationRemoval.Clear();
            foreach (var pair in groundPreparations)
            {
                GroundShipPreparation preparation = pair.Value;
                ShipState ship = GetShip(pair.Key);
                ShipFlightState flight = GetFlight(pair.Key);
                if (ship == null || ship.IsWreck || flight == null || preparation.World != flight.SurfaceWorldKey ||
                    flight.Phase is not (ShipFlightPhase.SurfaceDescending or ShipFlightPhase.Landed or ShipFlightPhase.Floating))
                    groundPreparationRemoval.Add(pair.Key);
            }
            foreach (string id in groundPreparationRemoval) CancelGroundPreparation(id);
            if (Time.timeScale <= 0f) return;
            groundPreparationWork.Clear();
            foreach (ShipState ship in State.Ships)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (ship.IsWreck || flight == null || !GameplayWorldRegistry.TryGet(flight.SurfaceWorldKey, out _) ||
                    flight.Phase is not (ShipFlightPhase.SurfaceDescending or ShipFlightPhase.Landed or ShipFlightPhase.Floating)) continue;
                GroundShipPreparation preparation = EnsureGroundPreparation(ship, flight.SurfaceWorldKey);
                if (preparation.Failed || preparation.Ready) continue;
                preparation.ActiveSeconds += Time.unscaledDeltaTime;
                if (preparation.ActiveSeconds > GroundPreparationTimeoutSeconds)
                { FailGroundPreparation(preparation, new TimeoutException("地面建筑准备超时，改用正式落地恢复。")); continue; }
                groundPreparationWork.Add(preparation);
            }
            if (groundPreparationWork.Count == 0) return;
            using var sample = GroundPreparationMarker.Auto();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsedMilliseconds = 0d;
            int steps = 0;
            // 整轮共用预算，并保证至少前进一个对象，避免小预算下永远无法完成。
            while (steps < 32 && (steps == 0 || elapsedMilliseconds < GroundPreparationFrameMilliseconds))
            {
                groundPreparationNext %= groundPreparationWork.Count;
                GroundShipPreparation preparation = groundPreparationWork[groundPreparationNext++];
                if (preparation.Failed || preparation.Ready)
                {
                    bool pending = false;
                    foreach (GroundShipPreparation candidate in groundPreparationWork) pending |= !candidate.Failed && !candidate.Ready;
                    if (!pending) break;
                    continue;
                }
                try
                {
                    using var worldScope = GameplayWorldRegistry.UseWorld(preparation.World);
                    StepGroundPreparation(preparation, Math.Max(0d, GroundPreparationFrameMilliseconds - elapsedMilliseconds));
                }
                catch (Exception error) { FailGroundPreparation(preparation, error); }
                steps++;
                elapsedMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        private void StepGroundPreparation(GroundShipPreparation preparation, double milliseconds)
        {
            if (preparation.Cursor < preparation.Ship.Pieces.Count)
            {
                ShipPieceState piece = preparation.Ship.Pieces[preparation.Cursor++];
                if (!piece.IsAlive) return;
                int id = int.Parse(piece.PieceId, System.Globalization.CultureInfo.InvariantCulture);
                if (MachineCatalog.Get(piece.ItemId) != null)
                {
                    using var scope = MachineWorld.UseScope(MachineWorld.GetShipScopeKey(preparation.Ship.ShipId));
                    MachineEntity node = MachineWorld.GetById(id) ?? throw new InvalidOperationException("待落地机器已离开船体：" + piece.ItemId);
                    preparation.Machines.Add(node);
                }
                return;
            }
            preparation.MachinePresentation ??= new ChunkTilemapRenderer.MechanicalLandingPreparation(preparation.Machines);
            if (!preparation.MachinePresentation.IsReady) preparation.MachinePresentation.Step(milliseconds);
        }

        private bool WaitForGroundPreparation(ShipAssemblyState assembly)
        {
            bool pending = false;
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id);
                ShipFlightState flight = GetFlight(id);
                if (ship == null || flight == null || !GameplayWorldRegistry.TryGet(flight.SurfaceWorldKey, out _)) continue;
                GroundShipPreparation preparation = EnsureGroundPreparation(ship, flight.SurfaceWorldKey);
                if (preparation.Failed || preparation.Ready) continue;
                pending = true;
                if (flight.HeightMeters <= GroundPreparationHoldHeight + .001d) preparation.HoldSteps++;
            }
            return pending;
        }

        private void FailGroundPreparation(GroundShipPreparation preparation, Exception error)
        {
            if (preparation.Failed) return;
            preparation.Failed = true; preparation.Error = error.Message;
            preparation.Dispose();
            RememberGroundPreparation(preparation, "failed");
            Debug.LogWarning("[飞船落地] 提前准备失败，保留真实飞行并改用正式恢复：" + error.Message, this);
        }

        private void CancelGroundPreparation(string id)
        {
            if (!groundPreparations.Remove(id, out GroundShipPreparation preparation)) return;
            preparation.Dispose();
            RememberGroundPreparation(preparation, "cancelled");
        }

        private void ClearGroundPreparations()
        {
            foreach (GroundShipPreparation preparation in groundPreparations.Values) preparation.Dispose();
            groundPreparations.Clear(); groundPreparationHistory.Clear(); groundPreparationHistoryOrder.Clear();
            groundPreparationWork.Clear(); groundPreparationRemoval.Clear(); groundPreparationNext = 0;
            groundRestoreShips.Clear(); groundRestoreMembers.Clear(); groundRestoreHandled.Clear(); lastGroundRestoreError = null;
        }
        #endregion

        #region 降落准备的真实状态采样
        public JObject GroundPreparationSnapshot(string shipId)
        {
            if (groundPreparations.TryGetValue(shipId, out GroundShipPreparation preparation))
            {
                JObject current = DescribeGroundPreparation(preparation, preparation.Failed ? "failed" : preparation.Ready ? "ready" : "preparing");
                current["sessionRunning"] = running;
                current["currentSession"] = Current == this;
                current["shipIsWreck"] = GetShip(shipId)?.IsWreck;
                current["authority"] = GameNetwork.HasStateAuthority;
                current["scene"] = gameObject.scene.name;
                current["worldRegistered"] = GameplayWorldRegistry.TryGet(preparation.World, out _);
                current["presentationWorld"] = GameplayWorldRegistry.LocalPresentationWorldKey;
                current["lastCleanup"] = groundPreparationHistory.TryGetValue(shipId, out JObject cleanup) ? cleanup.DeepClone() : null;
                return current;
            }
            return groundPreparationHistory.TryGetValue(shipId, out JObject result) ? (JObject)result.DeepClone() : new JObject { ["state"] = "none" };
        }

        private static JObject DescribeGroundPreparation(GroundShipPreparation preparation, string state)
        {
            return new JObject
            {
                ["state"] = state, ["shipId"] = preparation.Ship.ShipId, ["world"] = preparation.World,
                ["targetSceneHandle"] = preparation.TargetScene.handle,
                ["targetSceneCurrent"] = GameplayWorldRegistry.TryGet(preparation.World, out var current) &&
                    ReferenceEquals(preparation.TargetWorld, current) && preparation.TargetScene == current.Scene,
                ["cursor"] = preparation.Cursor, ["pieceCount"] = preparation.Ship.Pieces.Count,
                ["activeSeconds"] = preparation.ActiveSeconds,
                ["structureRevision"] = preparation.StructureRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["preparedMachines"] = preparation.MachinePresentation?.PreparedCount ?? preparation.PreparedMachines,
                ["consumedMachines"] = preparation.MachinePresentation?.ConsumedCount ?? preparation.ConsumedMachines,
                ["hiddenMachines"] = preparation.MachinePresentation?.HiddenCount ?? 0,
                ["remainingMachines"] = preparation.MachinePresentation?.AvailableCount ?? 0,
                ["machineCount"] = preparation.Machines.Count,
                ["holdSteps"] = preparation.HoldSteps, ["invalidations"] = preparation.Invalidations,
                ["ready"] = preparation.Ready || preparation.ReadyAtCommit, ["error"] = preparation.Error
            };
        }

        private void RememberGroundPreparation(GroundShipPreparation preparation, string state)
        {
            string id = preparation.Ship.ShipId;
            if (!groundPreparationHistory.ContainsKey(id)) groundPreparationHistoryOrder.Enqueue(id);
            groundPreparationHistory[id] = DescribeGroundPreparation(preparation, state);
            while (groundPreparationHistoryOrder.Count > 32) groundPreparationHistory.Remove(groundPreparationHistoryOrder.Dequeue());
        }
        #endregion
    }
}
