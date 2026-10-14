using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    internal sealed class ShipColliderPresenter : IDisposable
    {
        #region 整船表现刚体与阻挡合批
        public readonly GameObject Root;
        private readonly Rigidbody2D body;
        private readonly CustomCollider2D blocker;
        private readonly ShipColliderGeometry geometry = new();
        private readonly PhysicsShapeGroup2D shapes = new();
        private double cellSize;

        public ShipColliderPresenter(string shipId)
        {
            Root = new GameObject("ShipView_" + shipId);
            int layer = LayerMask.NameToLayer("Collider");
            if (layer >= 0) Root.layer = layer;
            body = Root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
            body.useFullKinematicContacts = true; body.interpolation = RigidbodyInterpolation2D.Interpolate;
            blocker = Root.AddComponent<CustomCollider2D>();
            blocker.enabled = false;
        }

        public void Synchronize(ShipState ship, Vector2 position, float degrees)
        {
            bool changed = geometry.Synchronize(ship);
            if (changed || cellSize != ship.CellSizeMeters)
            {
                cellSize = ship.CellSizeMeters; shapes.Clear();
                foreach (ShipCollisionRect rectangle in geometry.Rectangles)
                {
                    if (!rectangle.BlocksPassage) continue;
                    Vector2 center = new((float)((rectangle.X + rectangle.Width * .5d) * cellSize),
                        (float)((rectangle.Y + rectangle.Height * .5d) * cellSize));
                    // 只给合并后的外边缘留原有间隙，内部格缝不再割开碰撞体。
                    Vector2 size = new((float)Math.Max(.001d, rectangle.Width * cellSize - .05d),
                        (float)Math.Max(.001d, rectangle.Height * cellSize - .05d));
                    shapes.AddBox(center, size);
                }
                if (shapes.shapeCount == 0) { blocker.enabled = false; blocker.ClearCustomShapes(); }
                else { blocker.SetCustomShapes(shapes); blocker.enabled = true; }
            }
            if (body.position != position || Mathf.Abs(Mathf.DeltaAngle(body.rotation, degrees)) > .001f ||
                (Vector2)Root.transform.position != position)
            {
                body.position = position; body.rotation = degrees;
                Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, degrees));
            }
        }

        public void Dispose()
        {
            if (body != null) body.simulated = false;
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 整船碰撞表现生命周期
        private readonly Dictionary<string, ShipColliderPresenter> shipColliderPresenters = new(StringComparer.Ordinal);
        private readonly HashSet<string> visibleShipColliderPresenters = new(StringComparer.Ordinal);
        private readonly List<string> expiredShipColliderPresenters = new();

        private ShipColliderPresenter SynchronizeShipColliderPresenter(ShipState ship, float angle)
        {
            if (!shipColliderPresenters.TryGetValue(ship.ShipId, out ShipColliderPresenter presenter) || presenter.Root == null)
            {
                presenter?.Dispose();
                shipColliderPresenters[ship.ShipId] = presenter = new ShipColliderPresenter(ship.ShipId);
            }
            visibleShipColliderPresenters.Add(ship.ShipId);
            if (IsSpaceView) SpaceMgr.ExistingInstance?.MovePresentationToSector(presenter.Root, ship.Sector);
            presenter.Synchronize(ship, DisplayPoint(ship, 0d, 0d), angle);
            return presenter;
        }

        internal void RefreshShipPassageGeometry(ShipState ship)
        {
            if (ship != null && shipColliderPresenters.TryGetValue(ship.ShipId, out ShipColliderPresenter presenter) && presenter.Root != null)
                presenter.Synchronize(ship, DisplayPoint(ship, 0d, 0d), (float)(ship.AngleRadians * Mathf.Rad2Deg));
        }

        private bool IsShipPresentationCollider(Collider2D collider)
        {
            foreach (ShipColliderPresenter presenter in shipColliderPresenters.Values)
                if (presenter.Root != null && collider.attachedRigidbody?.gameObject == presenter.Root) return true;
            return false;
        }

        private void RemoveExpiredShipColliderPresenters()
        {
            expiredShipColliderPresenters.Clear();
            foreach (var pair in shipColliderPresenters)
                if (!visibleShipColliderPresenters.Contains(pair.Key)) expiredShipColliderPresenters.Add(pair.Key);
            foreach (string id in expiredShipColliderPresenters)
            {
                shipColliderPresenters[id].Dispose(); shipColliderPresenters.Remove(id);
            }
        }

        private void ReleaseShipColliderPresenters()
        {
            foreach (ShipColliderPresenter presenter in shipColliderPresenters.Values) presenter.Dispose();
            shipColliderPresenters.Clear(); visibleShipColliderPresenters.Clear(); expiredShipColliderPresenters.Clear();
        }
        #endregion
    }
}
