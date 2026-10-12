using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 成块残骸与攻击回收
        private readonly Dictionary<string, ShipWreckView> wreckViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (ShipState Pose, List<ShipPieceState> Pieces)> detachedWrecks = new(StringComparer.Ordinal);

        private void PrepareWreck(ShipState ship)
        {
            ship.IsWreck = true;
            if (ship.WreckMaxHealth > 0d) return;
            ship.NotifyStructureChanged();
            ship.WreckGuid = AllocateSessionDropGuid(0);
            double health = 0d;
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) continue;
                    ItemData data = GetPieceData(piece);
                    if (int.TryParse(piece.PieceId, out int id) && MachineWorld.GetById(id) is MachineEntity node)
                        data = MachineWorld.RemoveShipPieceForRecovery(node);
                    if (data != null)
                    {
                        CaptureRecoveredPieceHealth(data, piece);
                        contents[piece.PieceId] = data;
                        piece.SnapshotJson = Encode(data);
                    }
                    State.Devices.RemoveAll(device => device.PieceId == piece.PieceId);
                    RemovePieceView(piece.PieceId);
                    health += Math.Max(0d, piece.Health);
                }
            ship.WreckHealth = ship.WreckMaxHealth = Math.Max(20d, health * .25d);
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight != null) { flight.Ignited = false; flight.RequestedLandingBodyId = null; }
            State.Navigation.RemoveAll(value => value.ShipId == ship.ShipId);
            State.Docking.RemoveAll(value => value.ShipAId == ship.ShipId || value.ShipBId == ship.ShipId);
        }

        private void QueueDetachedWreck(ShipState ship, ShipPieceState piece)
        {
            if (!detachedWrecks.TryGetValue(ship.ShipId, out var entry))
            {
                ShipState pose = JsonConvert.DeserializeObject<ShipState>(JsonConvert.SerializeObject(ship));
                pose.Pieces.Clear(); pose.Compartments.Clear(); pose.MassContributions.Clear(); pose.DockingPorts.Clear();
                entry = (pose, new List<ShipPieceState>());
                detachedWrecks.Add(ship.ShipId, entry);
            }
            piece.Destroyed = false;
            entry.Pieces.Add(piece);
            RemovePieceView(piece.PieceId);
        }

        private void FlushDetachedWrecks()
        {
            foreach (var entry in detachedWrecks.Values)
            {
                var remaining = new HashSet<ShipPieceState>(entry.Pieces);
                while (remaining.Count > 0)
                {
                    var group = new List<ShipPieceState>();
                    foreach (ShipPieceState seed in remaining) { group.Add(seed); break; }
                    remaining.Remove(group[0]);
                    for (int i = 0; i < group.Count; i++)
                        foreach (ShipPieceState candidate in new List<ShipPieceState>(remaining))
                            if (PiecesTouch(group[i], candidate)) { remaining.Remove(candidate); group.Add(candidate); }
                    ShipState wreck = JsonConvert.DeserializeObject<ShipState>(JsonConvert.SerializeObject(entry.Pose));
                    string sourceShipId = wreck.ShipId;
                    wreck.ShipId = Guid.NewGuid().ToString("N");
                    wreck.Pieces = group;
                    wreck.NotifyStructureChanged();
                    wreck.IsWreck = true; wreck.WreckMaxHealth = 0d;
                    ShipStructureService.RecalculateMass(wreck);
                    State.Ships.Add(wreck);
                    ShipFlightState sourceFlight = GetFlight(sourceShipId);
                    ShipFlightState flight = sourceFlight == null ? new ShipFlightState { Phase = ShipFlightPhase.Cruising }
                        : JsonConvert.DeserializeObject<ShipFlightState>(JsonConvert.SerializeObject(sourceFlight));
                    flight.ShipId = wreck.ShipId;
                    flight.Motion.Sector = wreck.Sector;
                    flight.Motion.PositionMeters = new SpaceVector2(wreck.PositionX, wreck.PositionY);
                    flight.Motion.VelocityMetersPerSecond = new SpaceVector2(wreck.VelocityX, wreck.VelocityY);
                    State.Flights.Add(flight);
                    transferMassPieceIds.Clear();
                    foreach (ShipPieceState member in group)
                        if (int.TryParse(member.PieceId, out int id)) transferMassPieceIds.Add(id);
                    MachineWorld.TransferShipNodes(sourceShipId, wreck.ShipId, transferMassPieceIds);
                    PrepareWreck(wreck);
                }
            }
            detachedWrecks.Clear();
        }

        private static bool PiecesTouch(ShipPieceState a, ShipPieceState b)
        {
            foreach (ShipCell cell in ShipGeometry.Footprint(a))
                foreach (ShipCell other in ShipGeometry.Footprint(b))
                    if (Math.Abs(cell.X - other.X) + Math.Abs(cell.Y - other.Y) <= 1) return true;
            return false;
        }

        private void SynchronizeWreckView(ShipState ship)
        {
            PrepareWreck(ship);
            if (!IsVisible(ship)) { RemoveWreckView(ship.ShipId); return; }
            if (!wreckViews.TryGetValue(ship.ShipId, out ShipWreckView view) || view == null)
            {
                GameObject root = new("ShipWreck");
                view = root.AddComponent<ShipWreckView>();
                view.Initialize(this, ship);
                view.RefreshPayload();
                wreckViews[ship.ShipId] = view;
            }
            Vector2 point = IsSpaceView ? ProjectPosition(new SpaceSectorPosition(ship.Sector, new SpaceVector2(ship.PositionX, ship.PositionY)))
                : WorldLocalPresentation.ProjectPosition(new Vector2((float)ship.PositionX, (float)ship.PositionY));
            view.transform.SetPositionAndRotation(point, Quaternion.Euler(0, 0, (float)(ship.AngleRadians * 180d / Math.PI)));
        }

        private void RemoveWreckView(string shipId)
        {
            if (wreckViews.Remove(shipId, out ShipWreckView view) && view != null) Destroy(view.gameObject);
        }

        public double ApplyWreckDamage(ShipState wreck, double damage, string reason = "攻击")
        {
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || wreck?.IsWreck != true ||
                GetShip(wreck.ShipId) != wreck || !FluidUnits.IsFinite(damage) || damage <= 0d) return 0d;
            PrepareWreck(wreck);
            double applied = Math.Min(wreck.WreckHealth, damage);
            wreck.WreckHealth -= applied;
            RecordWreckDamage(wreck, applied, reason);
            if (wreck.WreckHealth > 0d)
            {
                if (wreckViews.TryGetValue(wreck.ShipId, out ShipWreckView view)) view.RefreshPayload();
                return applied;
            }
            // 回收时才拆出原材料或带库存的回收封包，不让残骸恢复机器运行。
            foreach (ShipPieceState piece in wreck.Pieces)
            {
                ItemData data = GetPieceData(piece);
                if (data != null)
                {
                    string carrier = Mod_Building.GetSummonerPrefabId(piece.ItemId);
                    QueueDrop(string.IsNullOrWhiteSpace(carrier) ? data : Mod_Building.CreateRecoveredCarrier(data, carrier), wreck, piece);
                }
                contents.Remove(piece.PieceId); pieceIndex.Remove(piece.PieceId); piece.Destroyed = true;
            }
            RemoveWreckView(wreck.ShipId);
            RemoveEmptyShip(wreck);
            RebuildPieceIndex();
            return applied;
        }
        #endregion
    }
}
