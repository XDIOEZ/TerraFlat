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
            using var worldScope = observer.InSpace ? default(IDisposable) : GameplayWorldRegistry.UseWorld(observer.WorldKey);
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
            controller.TrySetExternalAimWorldPosition(networkCombatControl, player.transform.position + (Vector3)delta.ToVector2());
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
            SpaceSectorPosition aim = observer.InSpace ? session.UnprojectSectorPosition(worldAim)
                : new SpaceSectorPosition(default, SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(worldAim)));
            bool sent = SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent
            {
                Kind = action == 3 ? SpaceNetworkIntentKind.OffhandParry : useItem ? SpaceNetworkIntentKind.UsePlayerItem : action == 0 ? SpaceNetworkIntentKind.Attack : action == 1 || action == 4 ? SpaceNetworkIntentKind.AttackEnd : SpaceNetworkIntentKind.UseSkill,
                Action = action == 4 ? 4 : 0,
                Sector = aim.Sector, X = aim.LocalMeters.X, Y = aim.LocalMeters.Y, TargetId = observer.InSpace ? null : observer.WorldKey,
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

    public sealed class SpaceCombatFrame : MonoBehaviour
    {
        #region 投射物固定规范坐标系
        public static readonly HashSet<SpaceCombatFrame> ActiveFrames = new();
        public SpaceSectorPosition CanonicalOrigin;
        public Vector2 PresentationOrigin;
        public bool Active;
        public Item Projectile;
        public void Initialize(Item projectile, SpaceSectorPosition origin, Vector2 presentation)
        { Projectile = projectile; CanonicalOrigin = origin; PresentationOrigin = presentation; Active = true; ActiveFrames.Add(this); }
        public void Finish() { Active = false; ActiveFrames.Remove(this); }
        private void OnDestroy() => Finish();
        #endregion
    }
}
