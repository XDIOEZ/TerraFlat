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
        public bool WholeShipBody { get; private set; }
        public int ActiveBodyCount => body != null && body.simulated && body.gameObject.activeInHierarchy ? 1 : 0;
        public int ActiveBlockerCount => blocker != null && blocker.enabled && ActiveBodyCount > 0 ? 1 : 0;

        public ShipColliderPresenter(string shipId)
        {
            Root = new GameObject("ShipView_" + shipId);
            int layer = LayerMask.NameToLayer("Collider");
            if (layer >= 0) Root.layer = layer;
            body = Root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
            body.useFullKinematicContacts = true; body.interpolation = RigidbodyInterpolation2D.None;
            body.simulated = false;
            blocker = Root.AddComponent<CustomCollider2D>();
            blocker.enabled = false;
        }

        public void Synchronize(ShipState ship, ShipFlightState flight, Vector2 position, float degrees)
        {
            WholeShipBody = ShipSurfaceState.UsesWholeShipBody(ship, flight);
            bool changed = geometry.Synchronize(ship) || cellSize != ship.CellSizeMeters;
            if (changed)
            {
                cellSize = ship.CellSizeMeters; shapes.Clear();
                foreach (ShipCollisionRect rectangle in geometry.Rectangles)
                {
                    if (!rectangle.BlocksPassage) continue;
                    Vector2 center = new((float)((rectangle.X + rectangle.Width * .5d) * cellSize),
                        (float)((rectangle.Y + rectangle.Height * .5d) * cellSize));
                    shapes.AddBox(center, new Vector2((float)(rectangle.Width * cellSize), (float)(rectangle.Height * cellSize)));
                }
                if (shapes.shapeCount == 0) blocker.ClearCustomShapes();
                else blocker.SetCustomShapes(shapes);
            }
            bool blocking = WholeShipBody && shapes.shapeCount > 0;
            if (blocker.enabled != blocking) blocker.enabled = blocking;
            if (body.simulated != WholeShipBody) body.simulated = WholeShipBody;
            if (body.position != position || Mathf.Abs(Mathf.DeltaAngle(body.rotation, degrees)) > .001f ||
                (Vector2)Root.transform.position != position)
            {
                body.position = position; body.rotation = degrees;
                Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, degrees));
            }
        }

        public bool OwnsCollider(Collider2D collider) => collider != null && Root != null &&
            collider.transform.IsChildOf(Root.transform);

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
            if (Root != null) { Root.SetActive(false); UnityEngine.Object.Destroy(Root); }
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
            if (body.simulated != WholeShipBody) body.simulated = WholeShipBody;
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
            presenter.Synchronize(ship, GetFlight(ship.ShipId), DisplayPresentationPoint(ship, 0d, 0d), angle);
            presenter.Resume();
            return presenter;
        }

        internal void RefreshShipPassageGeometry(ShipState ship)
        {
            if (ship != null && shipColliderPresenters.TryGetValue(ship.ShipId, out ShipColliderPresenter presenter) && presenter.Root != null)
                presenter.Synchronize(ship, GetFlight(ship.ShipId), DisplayPresentationPoint(ship, 0d, 0d), (float)(GetShipPresentationPose(ship).AngleRadians * Mathf.Rad2Deg));
        }

        internal bool HasShipColliderPresenter(string shipId) => shipId != null && shipColliderPresenters.ContainsKey(shipId);

        private bool IsShipPresentationCollider(Collider2D collider)
        {
            foreach (ShipColliderPresenter presenter in shipColliderPresenters.Values)
                if (presenter.OwnsCollider(collider)) return true;
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
