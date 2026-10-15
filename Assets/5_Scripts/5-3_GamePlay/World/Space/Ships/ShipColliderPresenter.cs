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
        private readonly Dictionary<string, BoxCollider2D> groundColliders = new(StringComparer.Ordinal);
        private readonly HashSet<string> groundPieceIds = new(StringComparer.Ordinal);
        private readonly List<string> expiredGroundPieceIds = new();
        private Transform groundRoot;
        private double cellSize;
        public bool WholeShipBody { get; private set; }
        public int ActiveBodyCount => body != null && body.simulated && body.gameObject.activeInHierarchy ? 1 : 0;
        public int ActiveBlockerCount => blocker != null && blocker.enabled && ActiveBodyCount > 0 ? 1 : 0;
        public int GroundBlockerCount => groundRoot != null && groundRoot.gameObject.activeInHierarchy ? groundColliders.Count : 0;

        public ShipColliderPresenter(string shipId)
        {
            Root = new GameObject("ShipView_" + shipId);
            int layer = LayerMask.NameToLayer("Collider");
            if (layer >= 0) Root.layer = layer;
            // 刚体只管理飞行碰撞，停泊部件放在它的同级，避免被刚体停用一起隐藏。
            GameObject flightRoot = new("FlightBody");
            flightRoot.transform.SetParent(Root.transform, false); flightRoot.layer = Root.layer;
            body = flightRoot.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
            body.useFullKinematicContacts = true; body.interpolation = RigidbodyInterpolation2D.None;
            body.simulated = false;
            blocker = flightRoot.AddComponent<CustomCollider2D>();
            blocker.enabled = false;
        }

        public void Synchronize(ShipState ship, ShipFlightState flight, Vector2 position, float degrees)
        {
            bool wholeShip = ShipSurfaceState.UsesWholeShipBody(ship, flight);
            bool modeChanged = WholeShipBody != wholeShip;
            WholeShipBody = wholeShip;
            bool changed = geometry.Synchronize(ship) || cellSize != ship.CellSizeMeters;
            if (changed)
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
                if (shapes.shapeCount == 0) blocker.ClearCustomShapes();
                else blocker.SetCustomShapes(shapes);
            }
            // 同一帧先关闭旧碰撞再启用新碰撞，原有部件数据和机器作用域无需搬运。
            if (!wholeShip)
            {
                if (body.simulated) body.simulated = false;
                if (blocker.enabled) blocker.enabled = false;
            }
            if (groundRoot != null && wholeShip && groundRoot.gameObject.activeSelf) groundRoot.gameObject.SetActive(false);
            if (changed || modeChanged || !wholeShip && groundRoot == null)
                SynchronizeGroundColliders(ship, !wholeShip);
            if ((Vector2)Root.transform.position != position ||
                Mathf.Abs(Mathf.DeltaAngle(Root.transform.eulerAngles.z, degrees)) > .001f)
            {
                Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, degrees));
            }
            if (body.position != position || Mathf.Abs(Mathf.DeltaAngle(body.rotation, degrees)) > .001f || modeChanged)
            { body.position = position; body.rotation = degrees; }
            if (groundRoot != null && !wholeShip && !groundRoot.gameObject.activeSelf) groundRoot.gameObject.SetActive(true);
            bool blocking = wholeShip && shapes.shapeCount > 0;
            if (blocker.enabled != blocking) blocker.enabled = blocking;
            if (body.simulated != wholeShip) body.simulated = wholeShip;
        }

        private void SynchronizeGroundColliders(ShipState ship, bool createMissing)
        {
            if (groundRoot == null && createMissing)
            {
                GameObject root = new("ParkedPieces"); root.layer = Root.layer;
                root.transform.SetParent(Root.transform, false); groundRoot = root.transform;
            }
            groundPieceIds.Clear();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || !piece.BlocksPassage) continue;
                groundPieceIds.Add(piece.PieceId);
                if (!groundColliders.TryGetValue(piece.PieceId, out BoxCollider2D collider) || collider == null)
                {
                    if (!createMissing) continue;
                    GameObject root = new("Piece_" + piece.PieceId); root.layer = Root.layer;
                    root.transform.SetParent(groundRoot, false);
                    groundColliders[piece.PieceId] = collider = root.AddComponent<BoxCollider2D>();
                }
                int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
                int height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
                collider.transform.localPosition = new Vector3((float)((piece.CellX + width * .5d) * cellSize),
                    (float)((piece.CellY + height * .5d) * cellSize), 0f);
                collider.size = new Vector2((float)(width * cellSize), (float)(height * cellSize));
            }
            expiredGroundPieceIds.Clear();
            foreach (string id in groundColliders.Keys)
                if (!groundPieceIds.Contains(id)) expiredGroundPieceIds.Add(id);
            foreach (string id in expiredGroundPieceIds)
            {
                BoxCollider2D collider = groundColliders[id]; groundColliders.Remove(id);
                if (collider == null) continue;
                collider.enabled = false; UnityEngine.Object.Destroy(collider.gameObject);
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
