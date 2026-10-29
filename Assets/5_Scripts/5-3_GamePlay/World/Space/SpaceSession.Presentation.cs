using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 太空统一表现插值
        private readonly Dictionary<string, (ShipState Source, SpacePresentationTrack Track)> shipPresentation = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SpacePresentationPose> renderedShipPoses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PassengerPresentation> passengerPresentation = new(StringComparer.Ordinal);
        private readonly HashSet<string> presentationIds = new(StringComparer.Ordinal);
        private readonly List<string> expiredPresentationIds = new();
        private double presentationSampleTime, presentationInterval, presentationAlpha = 1d;
        private bool hasPresentationSample;
        public event Action PresentationSynchronized;

        private sealed class PassengerPresentation
        {
            public SpacePassengerState Source;
            public string ShipId;
            public bool Supported;
            public uint RealmRevision;
            public readonly SpacePresentationTrack Track = new();
        }

        private void ResetPresentationHistory()
        {
            shipPresentation.Clear(); renderedShipPoses.Clear(); passengerPresentation.Clear();
            hasPresentationSample = false; presentationAlpha = 1d;
        }

        private void CapturePresentationState(bool advance)
        {
            if (State == null) return;
            presentationIds.Clear();
            foreach (ShipState ship in State.Ships)
            {
                if (GetFlight(ship.ShipId)?.Phase != ShipFlightPhase.Cruising) continue;
                presentationIds.Add(ship.ShipId);
                SpacePresentationPose pose = SpacePresentationPose.FromShip(ship);
                if (!shipPresentation.TryGetValue(ship.ShipId, out var sample))
                    sample = (ship, new SpacePresentationTrack());
                if (GameNetwork.HasStateAuthority && !ReferenceEquals(sample.Source, ship)) sample.Track.Reset(pose);
                else if (advance) sample.Track.Advance(pose);
                else if (!shipPresentation.ContainsKey(ship.ShipId)) sample.Track.Reset(pose);
                shipPresentation[ship.ShipId] = (ship, sample.Track);
            }
            expiredPresentationIds.Clear();
            foreach (string id in shipPresentation.Keys)
                if (!presentationIds.Contains(id)) expiredPresentationIds.Add(id);
            foreach (string id in expiredPresentationIds) shipPresentation.Remove(id);

            presentationIds.Clear();
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                ShipState ship = GetShip(passenger.ShipId);
                bool supported = passenger.Supported && ship != null && GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising;
                if (!supported && (passenger.Supported || !passenger.IsInSpace || passenger.SurfaceDescending || passenger.LandingResolved)) continue;
                presentationIds.Add(passenger.ProfileId);
                SpacePresentationPose pose = new(supported
                    ? new SpaceSectorPosition(default, new SpaceVector2(passenger.LocalX, passenger.LocalY))
                    : new SpaceSectorPosition(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters), 0d);
                if (!passengerPresentation.TryGetValue(passenger.ProfileId, out PassengerPresentation sample))
                {
                    sample = new PassengerPresentation();
                    passengerPresentation.Add(passenger.ProfileId, sample);
                }
                bool reset = sample.Source == null || sample.Supported != supported || sample.ShipId != passenger.ShipId ||
                    sample.RealmRevision != passenger.RealmRevision ||
                    GameNetwork.HasStateAuthority && !ReferenceEquals(sample.Source, passenger);
                if (reset) sample.Track.Reset(pose);
                else if (advance) sample.Track.Advance(pose);
                sample.Source = passenger; sample.ShipId = passenger.ShipId; sample.Supported = supported;
                sample.RealmRevision = passenger.RealmRevision;
            }
            expiredPresentationIds.Clear();
            foreach (string id in passengerPresentation.Keys)
                if (!presentationIds.Contains(id)) expiredPresentationIds.Add(id);
            foreach (string id in expiredPresentationIds) passengerPresentation.Remove(id);
        }

        private void CompletePresentationSample(double interval, double sampleTime)
        {
            CapturePresentationState(true);
            presentationInterval = Math.Max(.000001d, interval);
            presentationSampleTime = sampleTime; hasPresentationSample = true;
        }

        private void UpdatePresentationClock()
        {
            // 所有显示对象共用同一个渲染进度，物理状态始终保留固定步结果。
            presentationAlpha = hasPresentationSample
                ? Math.Clamp((Time.timeAsDouble - presentationSampleTime) / presentationInterval, 0d, 1d) : 1d;
            renderedShipPoses.Clear();
            foreach (var sample in shipPresentation)
                renderedShipPoses.Add(sample.Key, sample.Value.Track.Evaluate(presentationAlpha));
        }

        private SpacePresentationPose GetShipPresentationPose(ShipState ship)
        {
            if (IsSpaceView && GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising &&
                renderedShipPoses.TryGetValue(ship.ShipId, out SpacePresentationPose pose))
                return pose;
            return SpacePresentationPose.FromShip(ship);
        }

        private Vector2 DisplayPresentationPoint(ShipState ship, double localX, double localY)
        {
            if (IsSpaceView) return ProjectPosition(GetShipPresentationPose(ship).TransformPoint(localX, localY));
            ShipGeometry.LocalToWorld(ship, localX, localY, out double x, out double y);
            Vector2 center = new((float)ship.PositionX, (float)ship.PositionY);
            // 整船选同一个地表镜像，不能让相邻船内点跨缝时各自跳到不同周。
            return ProjectSurfacePresentationPosition(center) +
                new Vector2((float)(x - ship.PositionX), (float)(y - ship.PositionY));
        }

        private static Vector2 ProjectSurfacePresentationPosition(Vector2 position)
        {
            if (!WorldTopologyRuntime.TryGetLocalPresentationBounds(out WorldTopologyBounds bounds)) return position;
            position = bounds.NormalizePosition(position);
            return WorldLocalPresentation.HasAnchor ? bounds.NearestImagePosition(WorldLocalPresentation.AnchorPosition, position) : position;
        }

        private void PresentationWorldToLocal(ShipState ship, SpaceSectorPosition point, out double x, out double y)
        {
            SpacePresentationPose pose = GetShipPresentationPose(ship);
            SpaceVector2 delta = SpaceSectorCoordinates.Difference(point, pose.Position);
            ShipGeometry.Rotate(delta.X, delta.Y, -pose.AngleRadians, out x, out y);
        }

        private Vector2 GetPassengerPresentationPosition(SpacePassengerState passenger, ShipState ship)
        {
            if (!IsSpaceView) return DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
            SpaceVector2 local = new(passenger.LocalX, passenger.LocalY);
            if (passengerPresentation.TryGetValue(passenger.ProfileId, out PassengerPresentation sample) &&
                ReferenceEquals(sample.Source, passenger) && sample.Supported == passenger.Supported && sample.ShipId == passenger.ShipId &&
                sample.RealmRevision == passenger.RealmRevision)
            {
                SpacePresentationPose pose = sample.Track.Evaluate(presentationAlpha);
                if (!passenger.Supported) return ProjectPosition(pose.Position);
                local = pose.Position.LocalMeters;
            }
            return passenger.Supported && ship != null ? DisplayPresentationPoint(ship, local.X, local.Y)
                : ProjectPosition(new SpaceSectorPosition(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters));
        }

        private Vector2 GetPlayerAuthoritativeDisplayPosition(Player player)
        {
            if (!IsSpaceView)
                return TryGetPassengerSurfacePosition(player.ProfileName, out Vector2 ground) ? ground
                    : ShipSurfacePresentation.GroundPosition(player.gameObject, player.transform.position);
            if (State == null) return player.transform.position;
            SpacePassengerState passenger = FindPassenger(player.ProfileName);
            return passenger == null ? (Vector2)player.transform.position : ProjectPosition(GetPassengerPosition(passenger));
        }

        public bool TryGetPassengerPresentationPosition(string profileId, out Vector2 position)
        {
            position = default;
            if (State == null) return false;
            SpacePassengerState passenger = FindPassenger(profileId);
            if (passenger == null) return false;
            if (IsPassengerSurfaceDescendingHere(passenger))
            {
                position = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY)) +
                    Vector2.up * (float)ShipLandingGuidance.SurfaceVisualRise(passenger.SurfaceHeight);
                return true;
            }
            if (passenger.SurfaceDescending) return false;
            ShipState ship = GetShip(passenger.ShipId);
            if (passenger.Supported ? ship == null || !IsInPresentationWorld(ship) : !passenger.IsInSpace || passenger.LandingResolved) return false;
            position = GetPassengerPresentationPosition(passenger, ship) +
                Vector2.up * (float)ShipSurfaceState.VisualRise(ship != null ? GetFlight(ship.ShipId) : null);
            return true;
        }

        // 地表权威坐标不含起降高度，供存档、联机和控制台距离校验共用。
        public bool TryGetPassengerSurfacePosition(string profileId, out Vector2 position)
        {
            position = default;
            if (IsSpaceView || State == null) return false;
            SpacePassengerState passenger = FindPassenger(profileId);
            if (passenger == null) return false;
            if (IsPassengerSurfaceDescendingHere(passenger))
            {
                position = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
                return true;
            }
            ShipState ship = GetShip(passenger.ShipId);
            if (!passenger.Supported || !IsInPresentationWorld(ship)) return false;
            position = DisplayPoint(ship, passenger.LocalX, passenger.LocalY);
            return true;
        }

        private SpaceSectorPosition GetAuthoritativePassengerActionPoint(Player player, SpacePassengerState passenger, Vector2 display)
        {
            if (IsPlayerInShipInteriorWorld(player, out ShipState ship, out _))
            {
                ShipGeometry.LocalToWorld(ship, display.x, display.y, out double x, out double y);
                return SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y));
            }
            SpaceSectorPosition position = GetPassengerPosition(passenger);
            return SpaceSectorCoordinates.Normalize(position.Sector,
                position.LocalMeters + SpaceVector2.FromVector2(display - (Vector2)player.transform.position));
        }

        private void CapturePassengerAuthoritativePositions()
        {
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                ShipState ship = GetShip(passenger.ShipId);
                if (!(passenger.Supported && ship != null && GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising) &&
                    !(passenger.IsInSpace && !passenger.SurfaceDescending && !passenger.LandingResolved)) continue;
                if (!owner.PlayerData_Dict.TryGetValue(passenger.ProfileId, out Data_Player data)) continue;
                SpaceSectorPosition position = GetPassengerPosition(passenger);
                data.transform.position = new Vector3((float)position.LocalMeters.X, (float)position.LocalMeters.Y, 0f);
                data.transform.rotation = Quaternion.identity;
            }
        }
        #endregion
    }
}
