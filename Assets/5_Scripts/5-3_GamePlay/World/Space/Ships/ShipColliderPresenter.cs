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
            body.useFullKinematicContacts = true; body.interpolation = RigidbodyInterpolation2D.None;
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
                    // 合并矩形使用完整占地，避免相邻矩形缩边后留下能穿过的细缝。
                    Vector2 size = new((float)(rectangle.Width * cellSize),
                        (float)(rectangle.Height * cellSize));
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

        /// <summary>整船只同步一次升空排序和共享阻挡体，不随部件数量重复查询组件。</summary>
        public void SynchronizeSurfaceState(ShipFlightState flight, Vector2 visualRise)
        {
            ShipSurfacePresentation.SetElevated(Root, ShipSurfaceState.IsElevated(flight),
                ShipSurfacePresentation.VehicleCategory);
            ShipSurfacePresentation.SetColliderState(blocker, visualRise,
                ShipSurfaceState.IsAirborne(flight), true);
        }

        public void Dispose()
        {
            if (body != null) body.simulated = false;
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }

        public void Suspend(Transform owner)
        {
            body.simulated = false;
            Root.SetActive(false);
            Root.transform.SetParent(owner, false);
        }

        public void Resume()
        {
            if (!Root.activeSelf) Root.SetActive(true);
            if (!body.simulated) body.simulated = true;
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
                if (!TryRestoreShipColliderPresenter(ship.ShipId, out presenter))
                {
                    presenter = new ShipColliderPresenter(ship.ShipId);
                    shipPresentersCreated++;
                }
                shipColliderPresenters[ship.ShipId] = presenter;
            }
            visibleShipColliderPresenters.Add(ship.ShipId);
            if (shipViewPoolRoot != null && presenter.Root.transform.parent == shipViewPoolRoot)
                presenter.Root.transform.SetParent(null, false);
            if (IsSpaceView)
            {
                SpaceMgr.ExistingInstance?.MovePresentationToSector(presenter.Root, ship.Sector);
                // 相邻星区尚未准备时先归当前加载场景，不能让醒来的碰撞体留在持久场景。
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (presenter.Root.scene == gameObject.scene && scene.IsValid() && scene.isLoaded)
                {
                    presenter.Root.transform.SetParent(null, false);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(presenter.Root, scene);
                }
            }
            else
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (scene.IsValid() && scene.isLoaded && presenter.Root.scene != scene)
                {
                    presenter.Root.transform.SetParent(null, false);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(presenter.Root, scene);
                }
            }
            presenter.Synchronize(ship, DisplayPresentationPoint(ship, 0d, 0d), angle);
            presenter.Resume();
            return presenter;
        }

        internal void RefreshShipPassageGeometry(ShipState ship)
        {
            if (ship != null && shipColliderPresenters.TryGetValue(ship.ShipId, out ShipColliderPresenter presenter) && presenter.Root != null)
                presenter.Synchronize(ship, DisplayPresentationPoint(ship, 0d, 0d), (float)(GetShipPresentationPose(ship).AngleRadians * Mathf.Rad2Deg));
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
                SuspendShipColliderPresenter(id);
            }
        }

        private void ReleaseShipColliderPresenters()
        {
            ClearShipViewPool();
            foreach (ShipColliderPresenter presenter in shipColliderPresenters.Values) presenter.Dispose();
            shipColliderPresenters.Clear(); visibleShipColliderPresenters.Clear(); expiredShipColliderPresenters.Clear();
        }
        #endregion
    }
}
