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
            Physics2D.OverlapBox(center, size, (float)(ship.AngleRadians * Mathf.Rad2Deg),
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
        private const float DoorSlideSeconds = .22f;
        private Sprite doorSource;
        private SpriteRenderer[] doorLeaves;
        private Vector3[] doorClosedOffsets;
        private float doorSlide;
        private bool doorVisualDirty = true;

        private void SynchronizeDoorVisual()
        {
            if (boundPiece?.Kind != ShipPieceKind.Door || visual == null || visual.sprite == null) return;
            if (doorLeaves == null || doorSource != visual.sprite)
            {
                bool firstBinding = doorLeaves == null;
                ReleaseDoorVisual();
                doorSource = visual.sprite;
                if (firstBinding) doorSlide = boundPiece.DoorOpen ? 1f : 0f;
                doorLeaves = new SpriteRenderer[2]; doorClosedOffsets = new Vector3[2];
                Rect rect = doorSource.rect;
                float splitWidth = Mathf.Floor(rect.width * .5f);
                for (int side = 0; side < 2; side++)
                {
                    Rect half = new(rect.x + (side == 0 ? 0f : splitWidth), rect.y,
                        side == 0 ? splitWidth : rect.width - splitWidth, rect.height);
                    GameObject leaf = new(side == 0 ? "左舱门" : "右舱门");
                    leaf.transform.SetParent(visual.transform, false);
                    SpriteRenderer renderer = doorLeaves[side] = leaf.AddComponent<SpriteRenderer>();
                    renderer.sprite = Sprite.Create(doorSource.texture, half, new Vector2(.5f, .5f),
                        doorSource.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    renderer.sprite.name = doorSource.name + "_DoorLeaf_" + side;
                    renderer.sprite.hideFlags = HideFlags.DontSave;
                    renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                    doorClosedOffsets[side] = new Vector3(
                        (half.center.x - rect.x - doorSource.pivot.x) / doorSource.pixelsPerUnit,
                        (half.center.y - rect.y - doorSource.pivot.y) / doorSource.pixelsPerUnit);
                }
            }
            float targetSlide = boundPiece.DoorOpen ? 1f : 0f;
            // 舱门静止且外观未变化时，不再每帧写入两扇门的渲染属性和变换。
            if (!doorVisualDirty && doorSlide == targetSlide &&
                doorLeaves[0].sharedMaterial == visual.sharedMaterial && doorLeaves[0].color == visual.color &&
                doorLeaves[0].sortingLayerID == visual.sortingLayerID &&
                doorLeaves[0].sortingOrder == visual.sortingOrder)
                return;
            // 两扇门沿自身横轴滑开，旋转安装和船体运动都沿用父节点变换。
            doorSlide = Mathf.MoveTowards(doorSlide, targetSlide, Time.deltaTime / DoorSlideSeconds);
            float travel = (float)(boundPiece.Width * boundCellSize * .55d) / Mathf.Max(.001f, Mathf.Abs(visual.transform.localScale.x));
            float easedSlide = Mathf.SmoothStep(0f, 1f, doorSlide);
            for (int side = 0; side < doorLeaves.Length; side++)
            {
                SpriteRenderer leaf = doorLeaves[side];
                leaf.sharedMaterial = visual.sharedMaterial; leaf.color = visual.color;
                leaf.sortingLayerID = visual.sortingLayerID; leaf.sortingOrder = visual.sortingOrder;
                leaf.transform.localPosition = doorClosedOffsets[side] + Vector3.right * ((side == 0 ? -1f : 1f) * travel * easedSlide);
            }
            visual.enabled = false;
            doorVisualDirty = false;
        }

        private void ReleaseDoorVisual()
        {
            if (doorLeaves != null)
                foreach (SpriteRenderer leaf in doorLeaves)
                    if (leaf != null) { Destroy(leaf.sprite); Destroy(leaf.gameObject); }
            doorLeaves = null; doorClosedOffsets = null; doorSource = null;
            doorVisualDirty = true;
        }
        #endregion
    }
}
