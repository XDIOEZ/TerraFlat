using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 太空真实攻击请求与查询坐标
        private readonly object networkCombatControl = new();
        public static bool ClientAttackHeld { get; private set; }
        public static bool ClientGroundCombatPending { get; private set; }
        public bool HasAttackableSurfaceShip(string worldKey)
        {
            if (string.IsNullOrEmpty(worldKey) || State == null) return false;
            foreach (ShipState ship in State.Ships)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (flight?.Phase != ShipFlightPhase.Cruising && flight?.SurfaceWorldKey == worldKey &&
                    ship.Pieces.Exists(piece => piece.IsAlive)) return true;
            }
            return false;
        }
        public static void FinishClientGroundCombat() => ClientGroundCombatPending = false;
        public static void ClearClientNetworkCombat()
        { ClientAttackHeld = false; ClientGroundCombatPending = false; }
        public static bool TryGetCombatWorldKey(Item source, out string worldKey)
        {
            worldKey = source?.GetComponentInChildren<Mod_Projectile>(true)?.NetworkSurfaceWorldKey;
            if (!string.IsNullOrEmpty(worldKey)) return true;
            Player player = CombatPlayer(source);
            // 本地地表战斗以当前场景为准，避免保存在乘员快照中的旧世界误筛飞船。
            string activeWorld = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (player != null && player == ItemMgr.Instance?.User_Player && !SpaceMgr.IsSpaceScene(activeWorld))
            { worldKey = activeWorld; return !string.IsNullOrEmpty(worldKey); }
            if (player != null && Current != null && Current.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) && !observer.InSpace)
            { worldKey = observer.WorldKey; return !string.IsNullOrEmpty(worldKey); }
            worldKey = source != null ? source.gameObject.scene.name : null;
            return !string.IsNullOrEmpty(worldKey) && !SpaceMgr.IsSpaceScene(worldKey);
        }
        internal readonly struct SurfaceCombatProjection
        {
            private readonly string worldKey;
            private readonly Vector2 origin;
            private readonly Vector2 canonicalOrigin;

            public SurfaceCombatProjection(string worldKey, Vector2 origin, Vector2 canonicalOrigin)
            { this.worldKey = worldKey; this.origin = origin; this.canonicalOrigin = canonicalOrigin; }

            public Vector2 Project(Vector2 logical) => origin +
                PressureExplosionQueue.DeltaInWorld(worldKey, canonicalOrigin, logical);
        }

        // 每次武器 Pulse 只读取一次投射状态与乘员权威坐标，船件逐块只做坐标差运算。
        internal static SurfaceCombatProjection CaptureSurfaceCombatProjection(Item source, string worldKey, Vector2 fallbackAnchor)
        {
            Mod_Projectile projectile = source?.GetComponentInChildren<Mod_Projectile>(true);
            if (projectile?.NetworkSurfaceWorldKey == worldKey)
                return new SurfaceCombatProjection(worldKey, projectile.NetworkSurfacePresentationOrigin, projectile.NetworkSurfaceCanonicalOrigin);
            Player player = CombatPlayer(source);
            if (player != null && player != ItemMgr.Instance?.User_Player && Current != null &&
                Current.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) && !observer.InSpace &&
                string.Equals(observer.WorldKey, worldKey, StringComparison.Ordinal))
            {
                // 远端玩家可能不在主机当前地表，必须保留其独立世界的权威位置作为投影原点。
                return new SurfaceCombatProjection(worldKey,
                    ShipSurfacePresentation.GroundPosition(player.gameObject, player.transform.position), observer.Position.LocalMeters.ToVector2());
            }
            // 本地地表武器按实际伤害盒和环绕规则投影，避免过期的乘员快照引起错位。
            return new SurfaceCombatProjection(worldKey, fallbackAnchor,
                PressureExplosionQueue.NormalizeInWorld(worldKey, fallbackAnchor));
        }

        public static Vector2 ProjectSurfaceCombatPoint(Item source, string worldKey, Vector2 logical, Vector2 fallbackAnchor) =>
            CaptureSurfaceCombatProjection(source, worldKey, fallbackAnchor).Project(logical);
        public bool ExecuteNetworkAttack(string profile, int action, SpaceSectorPosition aim, out string reason)
        {
            reason = "玩家或手持武器尚未准备好";
            Player player = FindNetworkPlayer(profile);
            if (!GameNetwork.HasStateAuthority || player == null || !TryGetNetworkObserver(profile, out SpaceNetworkObserver observer)) return false;
            using var worldScope = player.UseOwnedWorldForItemCallbacks();
            Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            Mod_HotBar hotbar = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (controller == null || health == null || health.Hp <= 0f ||
                (action == 0 || action == 2) && hotbar?.CurentSelectItem == null) return false;
            if (action == 4)
            {
                EndNetworkAttack(profile);
                reason = null;
                return true;
            }
            SpaceVector2 delta = observer.InSpace ? SpaceSectorCoordinates.Difference(aim, observer.Position)
                : SpaceVector2.FromVector2(PressureExplosionQueue.DeltaInWorld(observer.WorldKey, observer.Position.LocalMeters.ToVector2(), aim.LocalMeters.ToVector2()));
            if (!delta.IsFinite || delta.Magnitude > 8192d) { reason = "瞄准位置超出范围"; return false; }
            if (!controller.TryAcquireExternalGameplayControl(networkCombatControl)) return false;
            Vector2 aimOffset = delta.ToVector2();
            if (IsPlayerInShipInteriorWorld(player, out ShipState carrier, out _))
            {
                ShipGeometry.Rotate(delta.X, delta.Y, -carrier.AngleRadians, out double x, out double y);
                aimOffset = new Vector2((float)x, (float)y);
            }
            controller.TrySetExternalAimWorldPosition(networkCombatControl, player.transform.position + (Vector3)aimOffset, inItemWorld: true);
            player.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint)?.PlayerTakeItem_FaceMouse(.1f);
            if (action == 3)
            {
                if (player.itemMods.GetMod_ByID<Mod_ShieldCombat>(Mod_ShieldCombat.ModuleId)?.TryParry() != true)
                { reason = "副手没有可用盾牌，或弹反尚未冷却"; return false; }
            }
            else if (action == 2) hotbar.CurentSelectItem.Act();
            else if (!controller.TrySetExternalAttackHeld(networkCombatControl, action == 0)) return false;
            reason = null;
            return true;
        }

        public void EndNetworkAttack(string profile)
        {
            Mod_GameController controller = FindNetworkPlayer(profile)?.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            // 超时/断线取消不能被当成短按松开而额外盾击。正常松开走 action=1。
            FindNetworkPlayer(profile)?.itemMods.GetMod_ByID<Mod_ShieldCombat>(Mod_ShieldCombat.ModuleId)?.CancelAction();
            controller?.TrySetExternalAttackHeld(networkCombatControl, false);
        }

        public static bool SubmitNetworkAttack(Item weapon, int action, Vector2 worldAim)
            => SubmitNetworkItemAction(weapon, action, worldAim, false);

        public static bool SubmitNetworkParry(Item player, Vector2 worldAim)
            => SubmitNetworkItemAction(player, 3, worldAim, false);

        public static bool SubmitNetworkShieldCancel(Item player)
            => SubmitNetworkItemAction(player, 4, player != null ? (Vector2)player.transform.position : Vector2.zero, false);

        public static bool SubmitNetworkUse(Item heldItem, Vector2 worldAim)
            => SubmitNetworkItemAction(heldItem, 2, worldAim, true);

        private static bool SubmitNetworkItemAction(Item weapon, int action, Vector2 worldAim, bool useItem)
        {
            Player player = CombatPlayer(weapon);
            SpaceSession session = Current;
            if (!GameNetwork.IsOnline || GameNetwork.HasStateAuthority) return false;
            if (player == null || session == null ||
                !session.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer)) return true;
            if (action == 4)
            {
                ClientAttackHeld = false;
                if (!observer.InSpace && !ClientGroundCombatPending) return true;
            }
            bool startsGroundCombat = !observer.InSpace && !ClientGroundCombatPending;
            if (startsGroundCombat && SpaceNetworkBridge.CombatPreparationSender?.Invoke(player.ProfileName) != true) return true;
            if (action == 0 || action == 1) ClientAttackHeld = action == 0;
            if (!observer.InSpace) ClientGroundCombatPending = true;
            string actionWorld = null;
            SpaceSectorPosition aim = observer.InSpace ? session.UnprojectSectorPosition(worldAim)
                : new SpaceSectorPosition(default, SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(worldAim)));
            if (session.IsPlayerInShipInteriorWorld(player, out ShipState ship, out _))
            {
                actionWorld = MachineWorld.GetShipScopeKey(ship.ShipId);
                ShipGeometry.LocalToWorld(ship, worldAim.x, worldAim.y, out double x, out double y);
                aim = observer.InSpace ? SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y))
                    : new SpaceSectorPosition(default, new SpaceVector2(x, y));
            }
            bool sent = SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent
            {
                Kind = action == 3 ? SpaceNetworkIntentKind.OffhandParry : useItem ? SpaceNetworkIntentKind.UsePlayerItem : action == 0 ? SpaceNetworkIntentKind.Attack : action == 1 || action == 4 ? SpaceNetworkIntentKind.AttackEnd : SpaceNetworkIntentKind.UseSkill,
                Action = action == 4 ? 4 : 0,
                Sector = aim.Sector, X = aim.LocalMeters.X, Y = aim.LocalMeters.Y, TargetId = actionWorld ?? (observer.InSpace ? null : observer.WorldKey),
                SelectedSlot = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurrentIndex ?? -1, StartsGroundCombat = startsGroundCombat
            });
            if (!sent) ClearClientNetworkCombat();
            return true;
        }
        public static bool RejectClientGroundInventoryMutation()
        {
            if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction())
            {
                Debug.LogWarning("[库存] 正在等待服务器确认库存，请稍后再整理物品或制作。");
                return true;
            }
            if (!GameNetwork.IsOnline || GameNetwork.HasStateAuthority || !ClientGroundCombatPending) return false;
            Debug.LogWarning("[库存] 正在等待服务器完成本次战斗结算，请稍后再整理物品或制作。");
            return true;
        }

        internal static Player CombatPlayer(Item item)
        {
            for (int depth = 0; item != null && depth < 8; depth++, item = item.Owner)
                if (item is Player player) return player;
            return null;
        }

        // 首次离开地板边界时分割扫掠，避免高速投射跳过船体空隙。
        internal bool TryFindProjectileShipExit(Item item, Vector2 start, Vector2 end, out ShipState ship, out float fraction)
        {
            ship = null; fraction = 0f;
            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            if (world?.StartsWith("ship:", StringComparison.Ordinal) != true ||
                (ship = GetShip(world.Substring(5))) == null || ship.CellSizeMeters <= 0d) return false;
            float size = (float)ship.CellSizeMeters;
            int x = Mathf.FloorToInt(start.x / size), y = Mathf.FloorToInt(start.y / size);
            if (!ShipAtmosphereService.HasFloor(ship, x, y)) return true;
            Vector2 delta = end - start;
            int sx = Math.Sign(delta.x), sy = Math.Sign(delta.y);
            float tx = sx == 0 ? float.PositiveInfinity : ((sx > 0 ? x + 1 : x) * size - start.x) / delta.x;
            float ty = sy == 0 ? float.PositiveInfinity : ((sy > 0 ? y + 1 : y) * size - start.y) / delta.y;
            float dx = sx == 0 ? float.PositiveInfinity : size / Math.Abs(delta.x);
            float dy = sy == 0 ? float.PositiveInfinity : size / Math.Abs(delta.y);
            while (true)
            {
                float crossing = Mathf.Min(tx, ty);
                if (crossing > 1f || float.IsInfinity(crossing)) return false;
                if (tx <= crossing) { x += sx; tx += dx; }
                if (ty <= crossing) { y += sy; ty += dy; }
                if (ShipAtmosphereService.HasFloor(ship, x, y)) continue;
                fraction = Mathf.Clamp01(crossing);
                return true;
            }
        }

        // 物理留在船内时，联机只广播换算后的真实位置与速度。
        public bool TryGetLocalWorldProjectilePose(Mod_Projectile projectile, out SpaceSectorPosition position,
            out Vector2 velocity, out float rotation, out string surfaceWorld)
        {
            position = default; velocity = default; rotation = 0f; surfaceWorld = null;
            Item item = projectile != null ? projectile.item : null;
            string world = GameplayWorldRegistry.GetItemWorldKey(item);
            ShipState ship = world?.StartsWith("ship:", StringComparison.Ordinal) == true ? GetShip(world.Substring(5)) : null;
            ShipFlightState flight = ship != null ? GetFlight(ship.ShipId) : null;
            if (item == null || ship == null || flight == null) return false;
            Vector2 local = item.transform.position;
            ShipGeometry.LocalToWorld(ship, local.x, local.y, out double x, out double y);
            ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
            Vector2 sourceVelocity = projectile.NetworkSurfaceVelocity;
            ShipGeometry.Rotate(sourceVelocity.x, sourceVelocity.y, ship.AngleRadians, out double wx, out double wy);
            velocity = new Vector2((float)(vx + wx), (float)(vy + wy));
            rotation = item.transform.eulerAngles.z + (float)(ship.AngleRadians * Mathf.Rad2Deg);
            if (flight.Phase == ShipFlightPhase.Cruising)
                position = SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y));
            else
            {
                surfaceWorld = flight.SurfaceWorldKey;
                if (string.IsNullOrWhiteSpace(surfaceWorld)) return false;
                position = new SpaceSectorPosition(default, new SpaceVector2(x, y + ShipSurfaceState.VisualRise(flight)));
            }
            return true;
        }

        public static bool TryGetCombatFrame(Item source, out SpaceSectorPosition canonical, out Vector2 presentation)
        {
            canonical = default; presentation = default;
            if (source == null || Current == null) return false;
            if (!string.IsNullOrEmpty(source.GetComponentInChildren<Mod_Projectile>(true)?.NetworkSurfaceWorldKey)) return false;
            // 普通武器没有投射坐标组件，失败查询不应在编辑器中逐帧产生分配。
            if (source.TryGetComponent(out SpaceCombatFrame projectile) && projectile.Active)
            { canonical = projectile.CanonicalOrigin; presentation = projectile.PresentationOrigin; return true; }
            Player player = CombatPlayer(source);
            if (player == null || !Current.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) || !observer.InSpace) return false;
            canonical = observer.Position; presentation = player.transform.position;
            return true;
        }

        public static bool TryGetCombatPosition(Item item, Vector2 worldPosition, out SpaceSectorPosition canonical)
        {
            canonical = default;
            Player player = CombatPlayer(item);
            if (Current != null && player != null && Current.IsPlayerInShipInteriorWorld(player, out ShipState ship, out _))
            {
                ShipGeometry.LocalToWorld(ship, worldPosition.x, worldPosition.y, out double x, out double y);
                canonical = SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y));
                return true;
            }
            if (!TryGetCombatFrame(item, out SpaceSectorPosition origin, out Vector2 presentation)) return false;
            canonical = SpaceSectorCoordinates.Normalize(origin.Sector,
                origin.LocalMeters + SpaceVector2.FromVector2(worldPosition - presentation));
            return true;
        }

        public void RecoverNetworkProjectile(Item item, SpaceSectorPosition position, Vector2 velocity)
        {
            if (!GameNetwork.HasStateAuthority || item?.itemData == null) return;
            item.ModuleSave();
            ItemData data = FastCloner.FastCloner.DeepClone(item.itemData);
            data.inHand = false; data.Stack.CanBePickedUp = true; data.Stack.Amount = 1f;
            State.LooseItems.Add(new SpaceLooseItemState
            {
                InSpace = true, WorldKey = SpaceSectorCoordinates.SceneKey(position.Sector), Snapshot = Encode(data),
                RotationDegrees = item.transform.eulerAngles.z, Scale = item.transform.localScale,
                Motion = new SpaceMotionState { Sector = position.Sector, PositionMeters = position.LocalMeters,
                    VelocityMetersPerSecond = SpaceVector2.FromVector2(velocity), RadiusMeters = .15d }
            });
        }
        public void RecoverNetworkSurfaceProjectile(Item item, string worldKey, Vector2 position)
        {
            if (!GameNetwork.HasStateAuthority || item?.itemData == null || string.IsNullOrEmpty(worldKey)) return;
            item.ModuleSave();
            ItemData data = FastCloner.FastCloner.DeepClone(item.itemData);
            data.inHand = false; data.Stack.CanBePickedUp = true; data.Stack.Amount = 1f;
            State.LooseItems.Add(new SpaceLooseItemState { InSpace = false, WorldKey = worldKey, Snapshot = Encode(data),
                RotationDegrees = item.transform.eulerAngles.z, Scale = item.transform.localScale,
                Motion = new SpaceMotionState { PositionMeters = SpaceVector2.FromVector2(position), RadiusMeters = .15d } });
        }
        #endregion
    }

    [DefaultExecutionOrder(10001)]
    public sealed class SpaceCombatFrame : MonoBehaviour
    {
        #region 投射物固定规范坐标系
        public static readonly HashSet<SpaceCombatFrame> ActiveFrames = new();
        public SpaceSectorPosition CanonicalOrigin;
        public Vector2 PresentationOrigin;
        public bool Active;
        public Item Projectile;
        public bool SectorLocal { get; private set; }
        internal string DepartingShipId { get; set; }
        private string sectorLease;
        private WorldPresentationProjection display;
        public void Initialize(Item projectile, SpaceSectorPosition origin, Vector2 presentation)
        { Projectile = projectile; CanonicalOrigin = origin; PresentationOrigin = presentation; Active = true; ActiveFrames.Add(this); }

        // 真实星区持有投射物，显示副本独立跟随当前浮动原点。
        internal bool TryEnterSectorLocal(Item projectile, SpaceSectorPosition position, string departingShipId)
        {
            SpaceMgr manager = SpaceMgr.ExistingInstance;
            SpaceSession session = SpaceSession.Current;
            if (manager == null || session == null || projectile == null) return false;
            string lease = $"projectile:{GetInstanceID()}:0";
            var scene = manager.BindAuthoritySector(lease, position.Sector);
            if (!scene.IsValid() || !scene.isLoaded) { manager.ReleaseAuthoritySector(lease); return false; }
            string world = SpaceSectorCoordinates.SceneKey(position.Sector);
            try
            {
                using var scope = GameplayWorldRegistry.UseSpaceScene(world);
                if (!scope.IsActive) { manager.ReleaseAuthoritySector(lease); return false; }
                GameplayWorldRegistry.AssignItemToWorld(projectile, world);
                if (GameplayWorldRegistry.GetItemWorldKey(projectile) != world)
                    throw new InvalidOperationException("投射物星区移交未完成。" );
            }
            catch { manager.ReleaseAuthoritySector(lease); throw; }
            Initialize(projectile, position, position.LocalMeters.ToVector2());
            SectorLocal = true; sectorLease = lease; DepartingShipId = departingShipId;
            display = WorldPresentationProjection.Create(projectile.transform, session.transform);
            return true;
        }

        internal void SynchronizeSectorPresentation()
        {
            if (!SectorLocal || display == null || Projectile == null) return;
            SpaceSession session = SpaceSession.Current;
            Player local = ItemMgr.Instance?.User_Player;
            bool hasPosition = SpaceSession.TryGetCombatPosition(Projectile, Projectile.transform.position, out SpaceSectorPosition point);
            bool visible = session != null && session.IsSpaceView && local != null && hasPosition &&
                session.TryGetNetworkObserver(local.ProfileName, out var observer) && observer.InSpace &&
                SpaceSectorCoordinates.Difference(point, observer.Position).Magnitude <= 8192d;
            Vector2 offset = hasPosition && session != null ? session.ProjectPosition(point) - (Vector2)Projectile.transform.position : Vector2.zero;
            display.Synchronize(Matrix4x4.Translate(offset), visible);
        }

        internal bool RebaseSectorIfNeeded(out Vector2 shift)
        {
            shift = Vector2.zero;
            if (!SectorLocal || Projectile == null || !SpaceSession.TryGetCombatPosition(Projectile, Projectile.transform.position, out var point) ||
                point.Sector == CanonicalOrigin.Sector) return false;
            SpaceMgr manager = SpaceMgr.ExistingInstance;
            if (manager == null) return false;
            string nextLease = sectorLease.EndsWith(":0", StringComparison.Ordinal)
                ? sectorLease.Substring(0, sectorLease.Length - 1) + "1" : sectorLease.Substring(0, sectorLease.Length - 1) + "0";
            var scene = manager.BindAuthoritySector(nextLease, point.Sector);
            if (!scene.IsValid() || !scene.isLoaded) { manager.ReleaseAuthoritySector(nextLease); return false; }
            string world = SpaceSectorCoordinates.SceneKey(point.Sector);
            try
            {
                using var scope = GameplayWorldRegistry.UseSpaceScene(world);
                if (!scope.IsActive) { manager.ReleaseAuthoritySector(nextLease); return false; }
                GameplayWorldRegistry.AssignItemToWorld(Projectile, world);
                if (GameplayWorldRegistry.GetItemWorldKey(Projectile) != world)
                    throw new InvalidOperationException("投射物跨星区移交未完成。" );
            }
            catch { manager.ReleaseAuthoritySector(nextLease); throw; }
            Vector2 old = Projectile.transform.position;
            Vector2 next = point.LocalMeters.ToVector2();
            shift = next - old;
            Projectile.transform.position = new Vector3(next.x, next.y, Projectile.transform.position.z);
            Rigidbody2D body = Projectile.GetComponent<Rigidbody2D>();
            if (body != null) { body.position = next; WorldPhysicsService2D.SetItemBodySimulationEnabled(body, false); }
            CanonicalOrigin = point; PresentationOrigin = next;
            manager.ReleaseAuthoritySector(sectorLease); sectorLease = nextLease;
            ItemMgr.Instance?.NotifyRuntimeItemMoved(Projectile);
            return true;
        }

        public void Finish()
        {
            Active = false; ActiveFrames.Remove(this); SectorLocal = false; DepartingShipId = null;
            if (display != null) Destroy(display.gameObject);
            display = null;
            if (sectorLease != null) SpaceMgr.ExistingInstance?.ReleaseAuthoritySector(sectorLease);
            sectorLease = null;
        }
        private void LateUpdate() => SynchronizeSectorPresentation();
        private void OnDestroy() => Finish();
        #endregion
    }
}
