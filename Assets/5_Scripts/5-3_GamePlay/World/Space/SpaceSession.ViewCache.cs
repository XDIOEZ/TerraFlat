using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 整船碰撞表现缓存
        public const int ShipPresenterPoolLimit = 16;
        private sealed class PooledShipPresenter
        {
            public ShipColliderPresenter Presenter;
            public LinkedListNode<string> Order;
        }
        private readonly Dictionary<string, PooledShipPresenter> pooledShipPresenters = new(StringComparer.Ordinal);
        private readonly LinkedList<string> shipPresenterPoolOrder = new();
        private readonly List<string> expiredPooledPresenters = new();
        private Transform shipViewPoolRoot;
        private long shipPresentersCreated, shipPresenterHits;
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

        // 物品与区块继续由普通世界持有，只休眠整船碰撞表现。
        private void SuspendViews()
        {
            if (GameNetwork.IsOnline) { ReleaseViews(); return; }
            HideShipInteriorPresentation();
            EnsureShipViewResourceGeneration();
            ReleasePassengerSurfacePresentation();
            ReleaseSurfaceShadows();
            foreach (string id in new List<string>(wreckViews.Keys)) RemoveWreckView(id);
            ReleaseDrivingCamera();
            if (State != null) ReleaseLooseViews();
            foreach (string id in new List<string>(shipColliderPresenters.Keys)) SuspendShipColliderPresenter(id);
            visibleShipColliderPresenters.Clear();
            viewBindingsDirty = true;
        }

        private void SuspendShipColliderPresenter(string id)
        {
            if (!shipColliderPresenters.Remove(id, out ShipColliderPresenter presenter)) return;
            ShipState ship = GetShip(id);
            if (GameNetwork.IsOnline || presenter.Root == null || ship == null || ship.IsWreck)
            {
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
            cached.Presenter.Dispose();
        }

        private void RemoveExpiredPooledShipPresenters()
        {
            expiredPooledPresenters.Clear();
            foreach (var pair in pooledShipPresenters)
                if (pair.Value.Presenter.Root == null || GetShip(pair.Key) is not ShipState ship || ship.IsWreck)
                    expiredPooledPresenters.Add(pair.Key);
            foreach (string id in expiredPooledPresenters) RemovePooledShipPresenter(id);
        }

        private void ClearShipViewPool()
        {
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
                ReleaseAllShipInteriorPresentations();
                ReleaseShipColliderPresenters();
                ReleaseSurfaceShadows();
                viewBindingsDirty = true;
            }
            shipViewResourceOwner = resources;
            shipViewResourceRevision = revision;
            hasShipViewResourceGeneration = true;
        }
        #endregion

        #region 真实世界投影与整船缓存诊断
        public JObject ShipViewCacheSnapshot()
        {
            var presenters = new JArray();
            foreach (var pair in shipColliderPresenters)
                presenters.Add(PresenterViewIdentity(pair.Key, pair.Value, false));
            foreach (var pair in pooledShipPresenters)
                presenters.Add(PresenterViewIdentity(pair.Key, pair.Value.Presenter, true));
            var items = new JArray();
            foreach (var pair in interiorItemProjections)
            {
                if (pair.Key == null || pair.Value == null) continue;
                JObject entry = ViewIdentity(pair.Key.itemData.Guid.ToString(), pair.Value.gameObject, false);
                entry["worldKey"] = GameplayWorldRegistry.GetItemWorldKey(pair.Key);
                entry["sourceId"] = pair.Key.gameObject.GetInstanceID();
                items.Add(entry);
            }
            var chunks = new JArray();
            foreach (var pair in interiorChunkProjections)
            {
                if (pair.Key == null || pair.Value == null) continue;
                JObject entry = ViewIdentity(pair.Key.Model?.Address.ToString() ?? string.Empty, pair.Value.gameObject, false);
                entry["sourceId"] = pair.Key.gameObject.GetInstanceID();
                chunks.Add(entry);
            }
            return new JObject
            {
                ["presenterPoolCount"] = pooledShipPresenters.Count,
                ["presenterLimit"] = ShipPresenterPoolLimit,
                ["presenterHits"] = shipPresenterHits, ["presenterCreated"] = shipPresentersCreated,
                ["resourceOwnerId"] = shipViewResourceOwner != null ? shipViewResourceOwner.GetInstanceID() : 0,
                ["resourceRevision"] = shipViewResourceRevision,
                ["presenters"] = presenters, ["itemProjections"] = items, ["chunkProjections"] = chunks
            };
        }

        private static JObject ViewIdentity(string id, GameObject root, bool pooled) => new()
        {
            ["id"] = id, ["instanceId"] = root != null ? root.GetInstanceID() : 0,
            ["pooled"] = pooled, ["active"] = root != null && root.activeInHierarchy,
            ["scene"] = root != null ? root.scene.name : string.Empty,
            ["parentId"] = root != null && root.transform.parent != null ? root.transform.parent.gameObject.GetInstanceID() : 0
        };

        private static JObject PresenterViewIdentity(string id, ShipColliderPresenter presenter, bool pooled)
        {
            JObject result = ViewIdentity(id, presenter.Root, pooled);
            result["wholeShipBody"] = presenter.WholeShipBody;
            result["activeBodies"] = presenter.ActiveBodyCount;
            result["activeBlockers"] = presenter.ActiveBlockerCount;
            return result;
        }
        #endregion
    }
}
