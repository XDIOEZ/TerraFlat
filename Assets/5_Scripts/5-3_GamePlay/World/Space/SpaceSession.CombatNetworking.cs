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
        public bool ExecuteNetworkAttack(string profile, int action, SpaceSectorPosition aim, out string reason)
        {
            reason = "玩家或手持武器尚未准备好";
            Player player = FindNetworkPlayer(profile);
            if (!GameNetwork.HasStateAuthority || player == null || !TryGetNetworkObserver(profile, out SpaceNetworkObserver observer) || !observer.InSpace) return false;
            Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            Mod_HotBar hotbar = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            Mod_DamageReceiver health = player.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (controller == null || hotbar?.CurentSelectItem == null || health?.Hp <= 0f) return false;
            SpaceVector2 delta = SpaceSectorCoordinates.Difference(aim, observer.Position);
            if (!delta.IsFinite || delta.Magnitude > 8192d) { reason = "瞄准位置超出范围"; return false; }
            if (!controller.TryAcquireExternalGameplayControl(networkCombatControl)) return false;
            controller.TrySetExternalAimWorldPosition(networkCombatControl, player.transform.position + (Vector3)delta.ToVector2());
            player.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint)?.PlayerTakeItem_FaceMouse(.1f);
            if (action == 2) hotbar.CurentSelectItem.Act();
            else if (!controller.TrySetExternalAttackHeld(networkCombatControl, action == 0)) return false;
            reason = null;
            return true;
        }

        public void EndNetworkAttack(string profile)
        {
            Mod_GameController controller = FindNetworkPlayer(profile)?.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            controller?.TrySetExternalAttackHeld(networkCombatControl, false);
        }

        public static bool SubmitNetworkAttack(Item weapon, int action, Vector2 worldAim)
        {
            Player player = CombatPlayer(weapon);
            SpaceSession session = Current;
            if (!GameNetwork.IsOnline || GameNetwork.HasStateAuthority || player == null ||
                session == null || !session.TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer) || !observer.InSpace) return false;
            if (action != 2) ClientAttackHeld = action == 0;
            SpaceSectorPosition aim = session.UnprojectSectorPosition(worldAim);
            SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent
            {
                Kind = action == 0 ? SpaceNetworkIntentKind.Attack : action == 1 ? SpaceNetworkIntentKind.AttackEnd : SpaceNetworkIntentKind.UseSkill,
                Sector = aim.Sector, X = aim.LocalMeters.X, Y = aim.LocalMeters.Y
            });
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
            SpaceCombatFrame projectile = source.GetComponent<SpaceCombatFrame>();
            if (projectile != null && projectile.Active)
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
