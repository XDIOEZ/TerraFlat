using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 有界单机船体表现休眠
        public const int ShipPresenterPoolLimit = 16;
        public const int ShipPiecePoolLimit = 2048;
        private sealed class PooledPieceView
        {
            public ShipPieceView View;
            public string ShipId, ItemId;
            public ShipPieceKind Kind;
            public LinkedListNode<string> Order;
        }
        private sealed class PooledShipPresenter
        {
            public ShipColliderPresenter Presenter;
            public LinkedListNode<string> Order;
        }
        private readonly Dictionary<string, PooledPieceView> pooledPieceViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PooledShipPresenter> pooledShipPresenters = new(StringComparer.Ordinal);
        private readonly LinkedList<string> pieceViewPoolOrder = new();
        private readonly LinkedList<string> shipPresenterPoolOrder = new();
        private readonly HashSet<string> visibleShipPieceIds = new(StringComparer.Ordinal);
        private readonly List<string> pieceViewRemovalScratch = new();
        private readonly List<string> pooledPieceRemovalScratch = new();
        private Transform shipViewPoolRoot;
        private long shipPieceViewsCreated, shipPresentersCreated, shipPieceViewHits, shipPresenterHits;
        private GameRes shipViewResourceOwner;
        private ulong shipViewResourceRevision;
        private bool hasShipViewResourceGeneration;

        private Transform ShipViewPoolRoot
        {
            get
            {
                if (shipViewPoolRoot != null) return shipViewPoolRoot;
                var root = new GameObject("ShipViewCache");
                root.transform.SetParent(transform, false);
                root.SetActive(false);
                return shipViewPoolRoot = root.transform;
            }
        }

        // 迁移只休眠船体画面，其余世界相关表现沿用原来的解绑与释放。
        private void SuspendViews()
        {
            if (GameNetwork.IsOnline) { ReleaseViews(); return; }
            EnsureShipViewResourceGeneration();
            ReleasePassengerSurfacePresentation();
            ReleaseSurfaceShadows();
            foreach (string id in new List<string>(wreckViews.Keys)) RemoveWreckView(id);
            ReleaseDrivingCamera();
            UnbindShipMachineVisualEvents();
            if (State != null) ReleaseLooseViews();
            foreach (string id in new List<string>(views.Keys)) SuspendPieceView(id);
            foreach (string id in new List<string>(shipColliderPresenters.Keys)) SuspendShipColliderPresenter(id);
            visibleShipColliderPresenters.Clear();
            visibleShipPieceIds.Clear();
            if (passengerVisual != null) passengerVisual.transform.localPosition = passengerVisualPosition;
            passengerVisual = null;
            viewBindingsDirty = true;
        }

        private void SuspendPieceView(string id)
        {
            if (GameNetwork.IsOnline) { RemovePieceView(id); return; }
            if (!views.Remove(id, out ShipPieceView view) || view == null) return;
            if (view.Item != null || !FindPiece(id, out ShipState ship, out ShipPieceState piece) ||
                !piece.IsAlive || ship.IsWreck)
            {
                DestroyPieceView(view);
                return;
            }
            RemovePooledPieceView(id);
            view.SuspendView();
            view.transform.SetParent(ShipViewPoolRoot, false);
            pooledPieceViews.Add(id, new PooledPieceView
            {
                View = view, ShipId = ship.ShipId, ItemId = piece.ItemId, Kind = piece.Kind,
                Order = pieceViewPoolOrder.AddLast(id)
            });
            while (pooledPieceViews.Count > ShipPiecePoolLimit)
                RemovePooledPieceView(pieceViewPoolOrder.First.Value);
        }

        private bool TryRestorePieceView(ShipState ship, ShipPieceState piece,
            ShipColliderPresenter presenter, out ShipPieceView view)
        {
            view = null;
            if (GameNetwork.IsOnline || !pooledPieceViews.TryGetValue(piece.PieceId, out PooledPieceView cached))
                return false;
            if (cached.View == null || cached.ItemId != piece.ItemId || cached.Kind != piece.Kind)
            {
                RemovePooledPieceView(piece.PieceId);
                return false;
            }
            ItemData data = GetPieceData(piece);
            if (data == null) return false;
            pooledPieceViews.Remove(piece.PieceId);
            pieceViewPoolOrder.Remove(cached.Order);
            view = cached.View;
            view.transform.SetParent(presenter.Root.transform, false);
            view.ResumeView(this, ship, piece, data);
            shipPieceViewHits++;
            return true;
        }

        private void RemovePooledPieceView(string id)
        {
            if (!pooledPieceViews.Remove(id, out PooledPieceView cached)) return;
            pieceViewPoolOrder.Remove(cached.Order);
            DestroyPieceView(cached.View);
        }

        private void DestroyPieceView(ShipPieceView view)
        {
            if (view == null) return;
            ItemMgr items = view.Item != null ? ItemMgr.GetInstance() : null;
            if (view.Item != null && items != null)
                items.DespawnItem(view.Item, false);
            else Destroy(view.gameObject);
        }

        private void SuspendShipColliderPresenter(string id)
        {
            if (!shipColliderPresenters.Remove(id, out ShipColliderPresenter presenter)) return;
            ShipState ship = GetShip(id);
            if (GameNetwork.IsOnline || presenter.Root == null || ship == null || ship.IsWreck)
            {
                RemovePooledPiecesUnder(presenter.Root);
                presenter.Dispose();
                return;
            }
            RemovePooledShipPresenter(id);
            presenter.Suspend(ShipViewPoolRoot);
            pooledShipPresenters.Add(id, new PooledShipPresenter
            { Presenter = presenter, Order = shipPresenterPoolOrder.AddLast(id) });
            while (pooledShipPresenters.Count > ShipPresenterPoolLimit)
                RemovePooledShipPresenter(shipPresenterPoolOrder.First.Value);
        }

        private bool TryRestoreShipColliderPresenter(string id, out ShipColliderPresenter presenter)
        {
            presenter = null;
            if (GameNetwork.IsOnline || !pooledShipPresenters.Remove(id, out PooledShipPresenter cached))
                return false;
            shipPresenterPoolOrder.Remove(cached.Order);
            if (cached.Presenter.Root == null) { cached.Presenter.Dispose(); return false; }
            presenter = cached.Presenter;
            shipPresenterHits++;
            return true;
        }

        private void RemovePooledShipPresenter(string id)
        {
            if (!pooledShipPresenters.Remove(id, out PooledShipPresenter cached)) return;
            shipPresenterPoolOrder.Remove(cached.Order);
            RemovePooledPiecesUnder(cached.Presenter.Root);
            cached.Presenter.Dispose();
        }

        private void RemovePooledPiecesUnder(GameObject root)
        {
            if (root == null) return;
            pooledPieceRemovalScratch.Clear();
            foreach (var pair in pooledPieceViews)
                if (pair.Value.View != null && pair.Value.View.transform.IsChildOf(root.transform))
                    pooledPieceRemovalScratch.Add(pair.Key);
            foreach (string pieceId in pooledPieceRemovalScratch) RemovePooledPieceView(pieceId);
        }

        private void RemoveExpiredPieceViews()
        {
            pieceViewRemovalScratch.Clear();
            foreach (string id in views.Keys)
                if (!visibleShipPieceIds.Contains(id)) pieceViewRemovalScratch.Add(id);
            foreach (string id in pieceViewRemovalScratch) SuspendPieceView(id);
            pieceViewRemovalScratch.Clear();
            foreach (var pair in pooledPieceViews)
                if (pair.Value.View == null || !FindPiece(pair.Key, out ShipState ship, out ShipPieceState piece) ||
                    !piece.IsAlive || ship.IsWreck) pieceViewRemovalScratch.Add(pair.Key);
            foreach (string id in pieceViewRemovalScratch) RemovePooledPieceView(id);
            pieceViewRemovalScratch.Clear();
            foreach (var pair in pooledShipPresenters)
                if (pair.Value.Presenter.Root == null || GetShip(pair.Key) is not ShipState ship || ship.IsWreck)
                    pieceViewRemovalScratch.Add(pair.Key);
            foreach (string id in pieceViewRemovalScratch) RemovePooledShipPresenter(id);
        }

        private void ClearShipViewPool()
        {
            foreach (PooledPieceView cached in pooledPieceViews.Values) DestroyPieceView(cached.View);
            pooledPieceViews.Clear(); pieceViewPoolOrder.Clear();
            foreach (PooledShipPresenter cached in pooledShipPresenters.Values) cached.Presenter.Dispose();
            pooledShipPresenters.Clear(); shipPresenterPoolOrder.Clear();
            if (shipViewPoolRoot != null) Destroy(shipViewPoolRoot.gameObject);
            shipViewPoolRoot = null;
        }

        private void EnsureShipViewResourceGeneration()
        {
            GameRes resources = GameRes.ExistingInstance;
            ulong revision = resources != null ? resources.ItemDefinitionRevision : 0;
            if (hasShipViewResourceGeneration &&
                (!ReferenceEquals(shipViewResourceOwner, resources) || shipViewResourceRevision != revision))
            {
                // 新资源版本不能接收旧活动画面的材质或再唤醒旧池，统一重建正式定义外观。
                foreach (string id in new List<string>(views.Keys)) RemovePieceView(id);
                ReleaseShipColliderPresenters();
                ReleaseSurfaceShadows();
                viewBindingsDirty = true;
            }
            shipViewResourceOwner = resources;
            shipViewResourceRevision = revision;
            hasShipViewResourceGeneration = true;
        }
        #endregion

        #region 船体表现缓存现场诊断
        // 只报告真实表现身份和池计数，Lua 决定命中、容量与场景归属断言。
        public JObject ShipViewCacheSnapshot()
        {
            var presenters = new JArray();
            foreach (var pair in shipColliderPresenters)
                presenters.Add(PresenterViewIdentity(pair.Key, pair.Value, false));
            foreach (var pair in pooledShipPresenters)
                presenters.Add(PresenterViewIdentity(pair.Key, pair.Value.Presenter, true));
            var pieces = new JArray();
            foreach (var pair in views)
                pieces.Add(PieceViewIdentity(pair.Key, pair.Value, false));
            foreach (var pair in pooledPieceViews)
                pieces.Add(PieceViewIdentity(pair.Key, pair.Value.View, true));
            return new JObject
            {
                ["poolCount"] = pooledPieceViews.Count + pooledShipPresenters.Count,
                ["presenterPoolCount"] = pooledShipPresenters.Count, ["piecePoolCount"] = pooledPieceViews.Count,
                ["presenterLimit"] = ShipPresenterPoolLimit, ["pieceLimit"] = ShipPiecePoolLimit,
                ["hits"] = shipPieceViewHits + shipPresenterHits,
                ["created"] = shipPieceViewsCreated + shipPresentersCreated,
                ["presenterHits"] = shipPresenterHits, ["pieceHits"] = shipPieceViewHits,
                ["presenterCreated"] = shipPresentersCreated, ["pieceCreated"] = shipPieceViewsCreated,
                ["resourceOwnerId"] = shipViewResourceOwner != null ? shipViewResourceOwner.GetInstanceID() : 0,
                ["resourceRevision"] = shipViewResourceRevision,
                ["presenters"] = presenters, ["pieces"] = pieces
            };
        }

        private static JObject ViewIdentity(string id, GameObject root, bool pooled) => new()
        {
            ["id"] = id, ["instanceId"] = root != null ? root.GetInstanceID() : 0,
            ["pooled"] = pooled, ["active"] = root != null && root.activeInHierarchy,
            ["scene"] = root != null ? root.scene.name : string.Empty,
            ["parentId"] = root != null && root.transform.parent != null ? root.transform.parent.gameObject.GetInstanceID() : 0
        };

        // 模式诊断只采样当前实际启用的碰撞，不把休眠组件算成正在工作的整船刚体。
        private static JObject PresenterViewIdentity(string id, ShipColliderPresenter presenter, bool pooled)
        {
            JObject result = ViewIdentity(id, presenter.Root, pooled);
            result["wholeShipBody"] = presenter.WholeShipBody;
            result["activeBodies"] = presenter.ActiveBodyCount;
            result["activeBlockers"] = presenter.ActiveBlockerCount;
            result["groundBlockers"] = presenter.GroundBlockerCount;
            return result;
        }

        /// <summary>仅测试快照读取缓存身份，验证实际船件复用后绑定的船与部件未串号。</summary>
        private static JObject PieceViewIdentity(string id, ShipPieceView view, bool pooled)
        {
            JObject result = ViewIdentity(id, view != null ? view.gameObject : null, pooled);
            result["machineScope"] = view?.BoundMachineScopeKey;
            result["targetGuid"] = view?.TargetGuid ?? 0;
            return result;
        }
        #endregion
    }
}
