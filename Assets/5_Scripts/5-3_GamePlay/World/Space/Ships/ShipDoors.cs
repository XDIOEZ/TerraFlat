using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 舱门开关与通道占位
        private readonly List<Collider2D> doorwayOverlaps = new();

        public bool TryToggleDoor(string pieceId)
        {
            if (SubmitLocalIntent(SpaceNetworkIntentKind.ToggleDoor, pieceId)) return true;
            if (!GameNetwork.HasStateAuthority || !FindPiece(pieceId, out ShipState ship, out ShipPieceState door) ||
                !door.IsAlive || door.Kind != ShipPieceKind.Door) return false;
            // 通道有人时保持打开，不通过挪动乘员来强行关门。
            if (door.DoorOpen && IsDoorwayOccupied(ship, door)) return false;
            door.DoorOpen = !door.DoorOpen;
            RefreshShipPassageGeometry(ship);
            return true;
        }

        private bool IsDoorwayOccupied(ShipState ship, ShipPieceState door)
        {
            int width = door.QuarterTurns % 2 == 0 ? door.Width : door.Height;
            int height = door.QuarterTurns % 2 == 0 ? door.Height : door.Width;
            double minX = door.CellX * ship.CellSizeMeters, minY = door.CellY * ship.CellSizeMeters;
            double maxX = minX + width * ship.CellSizeMeters, maxY = minY + height * ship.CellSizeMeters;
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                double x, y;
                if (passenger.Supported && passenger.ShipId == ship.ShipId)
                { x = passenger.LocalX; y = passenger.LocalY; }
                else if (!passenger.Supported && passenger.IsInSpace &&
                    !passenger.SurfaceDescending && !passenger.LandingResolved)
                    {
                    SpaceVector2 point = SpaceSectorCoordinates.Relative(passenger.FreeMotion.Sector, passenger.FreeMotion.PositionMeters, ship.Sector);
                    ShipGeometry.WorldToLocal(ship, point.X, point.Y, out x, out y);
                }
                else continue;
                double dx = x - Math.Clamp(x, minX, maxX), dy = y - Math.Clamp(y, minY, maxY);
                if (dx * dx + dy * dy <= .3d * .3d) return true;
            }
            Vector2 center = DisplayPoint(ship, (minX + maxX) * .5d, (minY + maxY) * .5d);
            Vector2 size = new((float)(maxX - minX - .05d), (float)(maxY - minY - .05d));
            doorwayOverlaps.Clear();
            GameplayPhysics2D.OverlapBox(center, size, (float)(ship.AngleRadians * Mathf.Rad2Deg),
                new ContactFilter2D { useTriggers = false }, doorwayOverlaps);
            foreach (Collider2D collider in doorwayOverlaps)
                if (collider != null && collider.attachedRigidbody != null &&
                    collider.attachedRigidbody.bodyType != RigidbodyType2D.Static &&
                    collider.GetComponentInParent<ShipPieceView>() == null &&
                    !IsShipPresentationCollider(collider)) return true;
            return false;
        }
        #endregion
    }

    public sealed partial class ShipPieceView
    {
        #region 双扇滑动舱门表现
        private DoorSlideVisual doorVisual;
        private Sprite doorSource;
        private bool? doorOpen;

        private void SynchronizeDoorVisual()
        {
            if (boundPiece?.Kind != ShipPieceKind.Door || visual == null || visual.sprite == null) return;
            if (doorVisual == null || doorSource != visual.sprite)
            {
                ReleaseDoorVisual();
                doorSource = visual.sprite;
                doorVisual = new DoorSlideVisual(visual, boundPiece.DoorOpen);
                doorOpen = boundPiece.DoorOpen;
            }
            if (doorOpen != boundPiece.DoorOpen)
            {
                doorOpen = boundPiece.DoorOpen;
                doorVisual.SetOpen(boundPiece.DoorOpen);
            }
            doorVisual.Advance(Time.deltaTime);
        }

        private void ReleaseDoorVisual()
        {
            doorVisual?.Dispose();
            doorVisual = null;
            doorSource = null;
            doorOpen = null;
        }
        #endregion
    }
}
