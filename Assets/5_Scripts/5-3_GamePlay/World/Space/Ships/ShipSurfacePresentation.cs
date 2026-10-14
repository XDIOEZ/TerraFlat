using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.Spaceflight
{
    [DisallowMultipleComponent]
    public sealed class ShipSurfacePresentation : MonoBehaviour
    {
        #region 空中排序与碰撞状态
        public const string VehicleCategory = "airborne-vehicle";
        public const string PassengerCategory = "airborne-passenger";
        private SortingGroup sortingGroup;
        private bool ownsGroup, sortingApplied;
        private int originalLayer, originalOrder;
        private bool originalEnabled, originalSortAtRoot;
        private Collider2D bodyCollider;
        private Vector2 originalColliderOffset, appliedColliderOffset;
        private bool colliderBound;

        // 外层排序组提高整体层级，舱门、罐体、手持物和喷焰保留各自内部顺序。
        public static void SetElevated(GameObject root, bool elevated, string category, int orderOffset = 0)
        {
            if (root == null) return;
            ShipSurfacePresentation presenter = root.GetComponent<ShipSurfacePresentation>();
            if (presenter == null && !elevated) return;
            if (presenter == null) presenter = root.AddComponent<ShipSurfacePresentation>();
            presenter.ApplySorting(elevated, category, orderOffset);
        }

        private void ApplySorting(bool elevated, string category, int orderOffset)
        {
            if (!elevated) { RestoreSorting(); return; }
            if (sortingGroup == null)
            {
                sortingGroup = GetComponent<SortingGroup>();
                ownsGroup = sortingGroup == null;
                if (ownsGroup) { sortingGroup = gameObject.AddComponent<SortingGroup>(); sortingGroup.enabled = false; }
            }
            if (!sortingApplied)
            {
                originalLayer = sortingGroup.sortingLayerID; originalOrder = sortingGroup.sortingOrder;
                originalEnabled = sortingGroup.enabled; originalSortAtRoot = sortingGroup.sortAtRoot;
                sortingApplied = true;
            }
            WorldSortingManager.GetInstance().GetSortingKey(category, out int layer, out int order);
            if (!sortingGroup.enabled) sortingGroup.enabled = true;
            if (!sortingGroup.sortAtRoot) sortingGroup.sortAtRoot = true;
            if (sortingGroup.sortingLayerID != layer) sortingGroup.sortingLayerID = layer;
            if (sortingGroup.sortingOrder != order + orderOffset) sortingGroup.sortingOrder = order + orderOffset;
        }

        private void RestoreSorting()
        {
            if (!sortingApplied || sortingGroup == null) return;
            sortingGroup.sortingLayerID = originalLayer; sortingGroup.sortingOrder = originalOrder;
            sortingGroup.sortAtRoot = originalSortAtRoot; sortingGroup.enabled = ownsGroup ? false : originalEnabled;
            sortingApplied = false;
            Item item = GetComponent<Item>();
            WorldSortingMember member = GetComponent<WorldSortingMember>();
            if (item != null && member != null && member.TargetRenderer != null)
                member.Bind(member.Category, member.TargetRenderer, item);
        }

        // 同一个阻挡体随贴图升高，空中保留查询但改为 Trigger，受伤资格交给战斗桥。
        public static void SetColliderState(Collider2D collider, Vector2 localRise, bool airborne, bool blocksPassage)
        {
            if (collider == null) return;
            if (collider.usedByComposite)
            {
                CompositeCollider2D composite = collider.GetComponentInParent<CompositeCollider2D>();
                if (composite != null && composite.attachedRigidbody == collider.attachedRigidbody)
                { collider = composite; blocksPassage = true; }
            }
            ShipSurfacePresentation presenter = collider.GetComponent<ShipSurfacePresentation>();
            if (presenter == null) presenter = collider.gameObject.AddComponent<ShipSurfacePresentation>();
            if (!presenter.colliderBound || presenter.bodyCollider != collider)
            {
                presenter.bodyCollider = collider;
                presenter.originalColliderOffset = presenter.appliedColliderOffset = collider.offset;
                presenter.colliderBound = true;
            }
            else if (collider.offset != presenter.appliedColliderOffset)
                presenter.originalColliderOffset = collider.offset;
            Vector2 offset = presenter.originalColliderOffset + localRise;
            if (collider.offset != offset) collider.offset = offset;
            presenter.appliedColliderOffset = offset;
            bool trigger = airborne || !blocksPassage;
            if (collider.isTrigger != trigger) collider.isTrigger = trigger;
        }

        private void OnDisable() => RestoreSorting();
        #endregion
    }

    public sealed partial class ShipPieceView
    {
        #region 船件升空兼容入口
        private Collider2D surfaceCollider;
        private int surfaceOrderOffset;
        private bool surfaceSortingBound;

        private void SynchronizeSurfacePresentation(Vector3 visualLiftOffset)
        {
            ShipFlightState flight = session?.GetFlight(shipId);
            if (surfaceCollider == null)
            {
                Rigidbody2D rigidbody = GetComponentInParent<Rigidbody2D>();
                if (rigidbody != null) surfaceCollider = rigidbody.GetComponent<Collider2D>();
            }
            bool wholeShip = surfaceCollider != null && surfaceCollider.transform != transform;
            if (visual != null)
            {
                if (!surfaceSortingBound)
                {
                    WorldSortingManager.GetInstance().GetSortingKey(WorldSortingManager.VehicleCategory, out _, out int order);
                    surfaceOrderOffset = visual.sortingOrder - order; surfaceSortingBound = true;
                }
                ShipSurfacePresentation.SetElevated(wholeShip ? surfaceCollider.gameObject : visual.gameObject,
                    ShipSurfaceState.IsElevated(flight), ShipSurfacePresentation.VehicleCategory, wholeShip ? 0 : surfaceOrderOffset);
            }
            if (surfaceCollider == null) return;
            ShipSurfacePresentation.SetColliderState(surfaceCollider, (Vector2)visualLiftOffset,
                ShipSurfaceState.IsAirborne(flight), wholeShip || boundPiece?.BlocksPassage == true);
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 船上远端乘员高度与排序恢复
        private readonly System.Collections.Generic.Dictionary<Player, (SpriteRenderer Renderer, Vector3 Position)> remoteSurfaceVisuals = new();
        private readonly System.Collections.Generic.HashSet<Player> visibleSurfacePassengers = new();
        private readonly System.Collections.Generic.List<Player> expiredSurfacePassengers = new();

        private void SynchronizeRemotePassengerSurfaceVisuals()
        {
            visibleSurfacePassengers.Clear();
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                Player player = FindNetworkPlayer(passenger.ProfileId);
                if (player == null || player == ItemMgr.Instance?.User_Player || !passenger.Supported) continue;
                ShipState ship = GetShip(passenger.ShipId);
                if (!IsInPresentationWorld(ship) || player.Sprite == null || player.Sprite.transform == player.transform) continue;
                visibleSurfacePassengers.Add(player);
                if (!remoteSurfaceVisuals.TryGetValue(player, out var baseline) || baseline.Renderer != player.Sprite)
                {
                    RestoreRemotePassengerSurfaceVisual(player);
                    baseline = (player.Sprite, player.Sprite.transform.localPosition);
                    remoteSurfaceVisuals[player] = baseline;
                }
                ShipFlightState flight = GetFlight(ship.ShipId);
                // 远端乘员与本地乘员共用高度，不能被升空后的地板遮住。
                baseline.Renderer.transform.localPosition = baseline.Position + baseline.Renderer.transform.parent.InverseTransformVector(
                    Vector3.up * (float)ShipSurfaceState.VisualRise(flight));
                ShipSurfacePresentation.SetElevated(player.gameObject, ShipSurfaceState.IsElevated(flight), ShipSurfacePresentation.PassengerCategory);
            }
            expiredSurfacePassengers.Clear();
            foreach (Player player in remoteSurfaceVisuals.Keys)
                if (player == null || !visibleSurfacePassengers.Contains(player)) expiredSurfacePassengers.Add(player);
            foreach (Player player in expiredSurfacePassengers) RestoreRemotePassengerSurfaceVisual(player);
        }

        private void RestoreRemotePassengerSurfaceVisual(Player player)
        {
            if (!remoteSurfaceVisuals.Remove(player, out var baseline)) return;
            if (baseline.Renderer != null) baseline.Renderer.transform.localPosition = baseline.Position;
            if (player != null) ShipSurfacePresentation.SetElevated(player.gameObject, false, ShipSurfacePresentation.PassengerCategory);
        }

        private void ReleasePassengerSurfacePresentation()
        {
            expiredSurfacePassengers.Clear(); expiredSurfacePassengers.AddRange(remoteSurfaceVisuals.Keys);
            foreach (Player player in expiredSurfacePassengers) RestoreRemotePassengerSurfaceVisual(player);
            visibleSurfacePassengers.Clear(); expiredSurfacePassengers.Clear();
            ShipSurfacePresentation.SetElevated(ItemMgr.Instance?.User_Player?.gameObject, false, ShipSurfacePresentation.PassengerCategory);
        }
        #endregion
    }
}
