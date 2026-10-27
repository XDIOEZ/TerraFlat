using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 成块残骸与部件载荷
        private readonly Dictionary<string, ShipWreckView> wreckViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (ShipState Pose, List<ShipPieceState> Pieces)> detachedWrecks = new(StringComparer.Ordinal);

        private void PrepareWreck(ShipState ship)
        {
            ship.IsWreck = true;
            if (ship.WreckGuid != 0) return;
            ship.NotifyStructureChanged();
            ship.WreckGuid = AllocateSessionDropGuid(0);
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive) continue;
                    ReleaseShipInteriorItem(piece, capture: true);
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
                }
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight != null) { flight.Ignited = false; flight.RequestedLandingBodyId = null; }
            State.Navigation.RemoveAll(value => value.ShipId == ship.ShipId);
            State.Docking.RemoveAll(value => ship.Pieces.Exists(piece =>
                piece.PieceId == value.PortAId || piece.PieceId == value.PortBId));
            PreserveWreckInteriorWorldIfNeeded(ship);
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
            // 同一轮拆分复用命中列表，避免每扩展一块部件都复制整个待处理集合。
            var touchingCandidates = new List<ShipPieceState>();
            foreach (var entry in detachedWrecks.Values)
            {
                var remaining = new HashSet<ShipPieceState>(entry.Pieces);
                while (remaining.Count > 0)
                {
                    var group = new List<ShipPieceState>();
                    foreach (ShipPieceState seed in remaining) { group.Add(seed); break; }
                    remaining.Remove(group[0]);
                    for (int i = 0; i < group.Count; i++)
                    {
                        touchingCandidates.Clear();
                        foreach (ShipPieceState candidate in remaining)
                            if (PiecesTouch(group[i], candidate)) touchingCandidates.Add(candidate);
                        // HashSet 枚举期间不修改集合；按原枚举顺序追加，保持连通分组结果。
                        for (int candidateIndex = 0; candidateIndex < touchingCandidates.Count; candidateIndex++)
                        {
                            ShipPieceState candidate = touchingCandidates[candidateIndex];
                            remaining.Remove(candidate);
                            group.Add(candidate);
                        }
                    }
                    ShipState wreck = JsonConvert.DeserializeObject<ShipState>(JsonConvert.SerializeObject(entry.Pose));
                    string sourceShipId = wreck.ShipId;
                    wreck.ShipId = Guid.NewGuid().ToString("N");
                    wreck.Pieces = group;
                    wreck.NotifyStructureChanged();
                    wreck.IsWreck = true; wreck.WreckGuid = 0;
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
            // Footprint 是旋转后的完整矩形；两矩形的最小格曼哈顿距离等于 X/Y 轴间隙之和。
            // 不再为每一对部件创建占地迭代器并进行面积乘积次逐格比较。
            int widthA = Math.Max(1, a.Width), heightA = Math.Max(1, a.Height);
            int widthB = Math.Max(1, b.Width), heightB = Math.Max(1, b.Height);
            if ((a.QuarterTurns & 1) != 0) (widthA, heightA) = (heightA, widthA);
            if ((b.QuarterTurns & 1) != 0) (widthB, heightB) = (heightB, widthB);
            long gapX = AxisGap(a.CellX, widthA, b.CellX, widthB);
            if (gapX > 1) return false;
            long gapY = AxisGap(a.CellY, heightA, b.CellY, heightB);
            return gapX + gapY <= 1;
        }

        /// <summary>两个整数闭区间之间相隔的最少格数；long 避免大坐标与尺寸溢出。</summary>
        private static long AxisGap(int startA, int sizeA, int startB, int sizeB)
        {
            long endA = (long)startA + sizeA - 1;
            long endB = (long)startB + sizeB - 1;
            if (endA < startB) return startB - endA;
            if (endB < startA) return startA - endB;
            return 0;
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
            Vector2 point = IsSpaceView ? DisplayPresentationPoint(ship, ship.CenterOfMassLocalX, ship.CenterOfMassLocalY)
                : WorldLocalPresentation.ProjectPosition(new Vector2((float)ship.PositionX, (float)ship.PositionY));
            view.Synchronize(ship, point, (float)(GetShipPresentationPose(ship).AngleRadians * 180d / Math.PI));
            if (IsSpaceView) SpaceMgr.ExistingInstance?.MovePresentationToSector(view.gameObject, ship.Sector);
        }

        private void RemoveWreckView(string shipId)
        {
            if (wreckViews.Remove(shipId, out ShipWreckView view) && view != null)
            {
                view.ReleasePhysics();
                Destroy(view.gameObject);
            }
        }
        #endregion
    }
}
