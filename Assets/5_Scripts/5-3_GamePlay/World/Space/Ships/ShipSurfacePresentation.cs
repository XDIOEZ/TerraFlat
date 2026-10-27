using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

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
            // 地表玩家通常没有升空表现组件，保留缺失即返回且避免编辑器逐帧分配。
            if (!root.TryGetComponent(out ShipSurfacePresentation presenter) && !elevated) return;
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
            WorldRenderingManager.Instance.GetSortingKey(category, out int layer, out int order);
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

        private void OnDisable()
        {
            RestorePassenger();
            RestoreSorting();
        }
        #endregion

        #region 乘员整体高度与碰撞恢复
        private float passengerRise;
        private Rigidbody2D passengerBody;
        private readonly List<Collider2D> passengerColliders = new();
        private readonly HashSet<Collider2D> passengerBlockingColliders = new();

        public static Vector3 GroundPosition(GameObject root, Vector3 position)
            => root != null && root.TryGetComponent(out ShipSurfacePresentation presenter)
                ? position - Vector3.up * presenter.passengerRise : position;

        // 从未升高的基准坐标整体摆放，重复同步不会把高度累加到玩家身上。
        public static void SetPassengerPose(GameObject root, Rigidbody2D body, Vector2 groundPosition, float rise)
        {
            if (root == null) return;
            if (!root.TryGetComponent(out ShipSurfacePresentation presenter) && rise <= 0f)
            {
                root.transform.position = new Vector3(groundPosition.x, groundPosition.y, root.transform.position.z);
                if (body != null) body.position = groundPosition;
                return;
            }
            if (presenter == null) presenter = root.AddComponent<ShipSurfacePresentation>();
            presenter.passengerBody = body;
            presenter.passengerRise = Mathf.Max(0f, rise);
            Vector2 position = groundPosition + Vector2.up * presenter.passengerRise;
            root.transform.position = new Vector3(position.x, position.y, root.transform.position.z);
            if (body != null) body.position = position;
            if (presenter.passengerRise > 0f)
            {
                root.GetComponentsInChildren(true, presenter.passengerColliders);
                foreach (Collider2D collider in presenter.passengerColliders)
                {
                    if (collider.attachedRigidbody != body || collider.usedByComposite || collider.isTrigger) continue;
                    presenter.passengerBlockingColliders.Add(collider);
                    collider.isTrigger = true;
                }
            }
            else presenter.RestorePassengerCollisions();
        }

        public static void ClearPassenger(GameObject root)
        {
            if (root != null && root.TryGetComponent(out ShipSurfacePresentation presenter))
                presenter.RestorePassenger();
        }

        private void RestorePassengerCollisions()
        {
            foreach (Collider2D collider in passengerBlockingColliders)
                if (collider != null) collider.isTrigger = false;
            passengerBlockingColliders.Clear();
            passengerColliders.Clear();
        }

        private void RestorePassenger()
        {
            if (passengerRise > 0f)
            {
                transform.position -= Vector3.up * passengerRise;
                if (passengerBody != null) passengerBody.position = transform.position;
            }
            passengerRise = 0f;
            passengerBody = null;
            RestorePassengerCollisions();
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 乘员整体高度与排序恢复
        private readonly System.Collections.Generic.HashSet<Player> surfacePassengers = new();
        private readonly System.Collections.Generic.HashSet<Player> visibleSurfacePassengers = new();
        private readonly System.Collections.Generic.List<Player> expiredSurfacePassengers = new();

        private void SynchronizeRemotePassengerSurfaceVisuals()
        {
            visibleSurfacePassengers.Clear();
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                Player player = FindNetworkPlayer(passenger.ProfileId);
                if (player == null || player == ItemMgr.Instance?.User_Player) continue;
                ShipState ship = GetShip(passenger.ShipId);
                if (passenger.Supported && IsInPresentationWorld(ship))
                    PlacePassenger(player, passenger, ship, true);
                else if (IsPassengerSurfaceDescendingHere(passenger))
                    PlaceDescendingPassenger(player, passenger);
                else continue;
                visibleSurfacePassengers.Add(player);
            }
            expiredSurfacePassengers.Clear();
            foreach (Player player in surfacePassengers)
                if (player != ItemMgr.Instance?.User_Player &&
                    (player == null || !visibleSurfacePassengers.Contains(player))) expiredSurfacePassengers.Add(player);
            foreach (Player player in expiredSurfacePassengers) RestorePassengerSurface(player);
        }

        private void RestorePassengerSurface(Player player)
        {
            surfacePassengers.Remove(player);
            ShipSurfacePresentation.ClearPassenger(player != null ? player.gameObject : null);
            if (player != null) ShipSurfacePresentation.SetElevated(player.gameObject, false, ShipSurfacePresentation.PassengerCategory);
        }

        private void ReleasePassengerSurfacePresentation()
        {
            expiredSurfacePassengers.Clear(); expiredSurfacePassengers.AddRange(surfacePassengers);
            foreach (Player player in expiredSurfacePassengers) RestorePassengerSurface(player);
            visibleSurfacePassengers.Clear(); expiredSurfacePassengers.Clear();
            ShipSurfacePresentation.SetElevated(ItemMgr.Instance?.User_Player?.gameObject, false, ShipSurfacePresentation.PassengerCategory);
        }

        private bool IsPassengerSurfaceDescendingHere(SpacePassengerState passenger) => passenger.SurfaceDescending && !IsSpaceView &&
            passenger.LandingWorldKey == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        private void PlaceDescendingPassenger(Player player, SpacePassengerState passenger)
        {
            Vector2 ground = WorldLocalPresentation.ProjectPosition(new Vector2((float)passenger.LandingX, (float)passenger.LandingY));
            SetPassengerSurfacePose(player, ground, ground,
                (float)ShipLandingGuidance.SurfaceVisualRise(passenger.SurfaceHeight), passenger.SurfaceHeight > 0d);
        }

        private void SetPassengerSurfacePose(Player player, Vector2 position, Vector2 authoritativePosition, float rise, bool elevated)
        {
            Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            ShipSurfacePresentation.SetPassengerPose(player.gameObject, mover?.rb, position, rise);
            Vector3 savedPosition = IsSpaceView ? authoritativePosition : WorldLocalPresentation.ToLogical(authoritativePosition);
            savedPosition.z = player.transform.position.z;
            player.itemData.transform.position = savedPosition;
            ShipSurfacePresentation.SetElevated(player.gameObject, elevated, ShipSurfacePresentation.PassengerCategory);
            if (rise > 0f || elevated) surfacePassengers.Add(player);
            else surfacePassengers.Remove(player);
            ItemMgr.Instance.NotifyRuntimeItemMoved(player);
        }
        #endregion
    }
}
