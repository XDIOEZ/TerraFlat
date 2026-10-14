using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region 太空权威坐标与玩家生命周期
        [SyncVar] private bool authoritativeInSpace;
        [SyncVar] private long authoritativeSectorX, authoritativeSectorY;
        [SyncVar] private double authoritativeLocalX, authoritativeLocalY;
        [SyncVar] private double authoritativeSpaceVelocityX, authoritativeSpaceVelocityY;
        [SyncVar] private string authoritativeShipId;
        [SyncVar] private string authoritativeWorldKey;
        [SyncVar] private uint authoritativeRealmRevision;
        private readonly Dictionary<Renderer, bool> hiddenRenderers = new();
        private readonly Dictionary<Collider2D, bool> hiddenColliders = new();
        private readonly Dictionary<Canvas, bool> hiddenCanvases = new();
        private bool networkVisible = true;
        private float serverStateTimer;
        private uint serverStateHash;
        private float nextCombatAim;
        [SyncVar(hook = nameof(OnGroundCombatPendingChanged))] private bool authoritativeGroundCombatPending;
        private bool serverGroundAttackHeld;
        private bool groundCombatFrozen;
        public bool GroundCombatPending => authoritativeGroundCombatPending;
        private float groundCombatReleaseAfter, lastGroundCombatInput;
        private uint acknowledgedGroundCombatHash, lastClientCombatAck, lastAppliedCombatStateHash;
        private static SpaceSession SpaceSessionForNetwork => SpaceSession.Current;
        public bool IsInSpace => authoritativeInSpace;
        public SpaceSectorPosition AuthoritativeSpacePosition => new(new SpaceSectorId(authoritativeSectorX, authoritativeSectorY),
            new SpaceVector2(authoritativeLocalX, authoritativeLocalY));

        public override void OnStartServer()
        {
            base.OnStartServer();
            SpaceSession.EnsureLoaded();
            EnsureCorePlayer(isOwned);
            RefreshSpaceAuthority();
        }
        public override void OnStopServer()
        {
            SpaceSessionForNetwork?.RemoveNetworkPassengerInput(displayName);
            SpaceSessionForNetwork?.EndNetworkAttack(displayName);
            if (corePlayer != null && ItemMgr.Instance != null)
                ItemMgr.Instance.ReleaseNetworkPlayer(corePlayer, persistData: true);
            corePlayer = null;
            base.OnStopServer();
        }
        [Server]
        public void RefreshSpaceAuthority()
        {
            if (SpaceSessionForNetwork == null || !SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer)) return;
            if (observer.RealmRevision != authoritativeRealmRevision && !observer.InSpace)
            {
                authoritativePosition = observer.Position.LocalMeters.ToVector2(); authoritativeVelocity = Vector2.zero;
                remoteTargetPosition = WorldLocalPresentation.ProjectPosition(authoritativePosition);
                remoteSnapshotStartPosition = remoteTargetPosition; remoteVelocity = Vector2.zero; remoteSnapshotElapsed = 0f;
                hasOwnedMotionSample = false;
            }
            authoritativeInSpace = observer.InSpace;
            authoritativeSectorX = observer.Position.Sector.X; authoritativeSectorY = observer.Position.Sector.Y;
            authoritativeLocalX = observer.Position.LocalMeters.X; authoritativeLocalY = observer.Position.LocalMeters.Y;
            authoritativeSpaceVelocityX = observer.Velocity.X; authoritativeSpaceVelocityY = observer.Velocity.Y;
            authoritativeShipId = observer.ShipId; authoritativeWorldKey = observer.WorldKey;
            authoritativeRealmRevision = observer.RealmRevision;
            if (observer.InSpace && corePlayer?.IsInitialized == true)
            {
                authoritativeFacingLeft = ReadOwnedFacing();
                authoritativeVisualState = CaptureOwnedVisualState(observer.Velocity.ToVector2());
            }
        }
        [Server]
        private void SynchronizeGroundObserver()
        {
            SpaceSession session = SpaceSessionForNetwork;
            SpacePassengerState passenger = session?.State?.Passengers.Find(value => value.ProfileId == displayName);
            if (passenger == null || passenger.IsInSpace || passenger.Supported || passenger.SurfaceDescending) return;
            if (passenger.RealmRevision != authoritativeRealmRevision) return;
            Vector2 logical = (Vector2)WorldTopologyRuntime.NormalizePosition(authoritativePosition);
            session.SetNetworkPlayerRealm(displayName, false, new SpaceSectorPosition(default, SpaceVector2.FromVector2(logical)), corePlayer?.Data?.CurrentSceneName ?? authoritativeWorldKey);
        }
        [Server]
        private void PublishServerPlayerState(bool force = false)
        {
            serverStateTimer -= Time.unscaledDeltaTime;
            if (!IsInSpace && groundCombatFrozen) return;
            if ((!IsInSpace && !authoritativeGroundCombatPending) || corePlayer == null || !corePlayer.IsInitialized || !force && serverStateTimer > 0f) return;
            serverStateTimer = .2f;
            byte[] payload = ItemNetworkStateSerialization.CaptureCachedNetworkPayload(corePlayer, true, out _, out uint hash);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload) || !force && hash == serverStateHash) return;
            serverStateHash = hash;
            authoritativeItemState = payload;
        }
        #region 同地表船体战斗的临时权威交接
        public bool SubmitGroundCombatBaseline()
        {
            if (!isOwned || isServer || corePlayer == null || !NetworkClient.ready) return false;
            byte[] payload = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload)) return false;
            CmdSubmitItemState(payload);
            return true;
        }
        public bool PrepareGroundCombat(SpaceNetworkIntentKind kind)
        {
            if (!isServer || IsInSpace || corePlayer == null || SpaceSessionForNetwork == null) return false;
            if (!SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer) ||
                !SpaceSessionForNetwork.HasAttackableSurfaceShip(observer.WorldKey) && !authoritativeGroundCombatPending) return false;
            if (!authoritativeGroundCombatPending || groundCombatFrozen)
            {
                ItemMgr.Instance?.InitializeNetworkAuthorityPlayer(corePlayer, surfaceCombat: true);
                authoritativeGroundCombatPending = true; acknowledgedGroundCombatHash = 0;
                groundCombatFrozen = false;
            }
            serverGroundAttackHeld = kind == SpaceNetworkIntentKind.Attack;
            lastGroundCombatInput = Time.unscaledTime;
            groundCombatReleaseAfter = Time.unscaledTime + 1f;
            return true;
        }
        public void AcknowledgeGroundCombat(uint hash)
        {
            if (isServer && authoritativeGroundCombatPending && hash == serverStateHash)
                acknowledgedGroundCombatHash = hash;
        }
        public void CancelRejectedGroundCombat()
        {
            if (!isServer || !authoritativeGroundCombatPending) return;
            SpaceSessionForNetwork?.EndNetworkAttack(displayName);
            PublishServerPlayerState(force: true);
            authoritativeGroundCombatPending = false; serverGroundAttackHeld = false; groundCombatFrozen = false;
            if (!IsInSpace) ItemMgr.Instance?.ReleaseNetworkAuthorityPlayer(corePlayer);
        }
        private void UpdateNetworkCombat()
        {
            if (isServer && authoritativeGroundCombatPending)
            {
                bool validWorld = SpaceSessionForNetwork != null && SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer) &&
                    !observer.InSpace && SpaceSessionForNetwork.HasAttackableSurfaceShip(observer.WorldKey);
                if ((!validWorld || Time.unscaledTime - lastGroundCombatInput > 1f) && serverGroundAttackHeld)
                { SpaceSessionForNetwork?.EndNetworkAttack(displayName); serverGroundAttackHeld = false; groundCombatReleaseAfter = Time.unscaledTime + 1f; }
                if (!serverGroundAttackHeld && !groundCombatFrozen && Time.unscaledTime >= groundCombatReleaseAfter)
                {
                    Item held = corePlayer?.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
                    Mod_ColdWeapon cold = held?.GetComponentInChildren<Mod_ColdWeapon>(true);
                    Mod_Damage damage = held?.GetComponentInChildren<Mod_Damage>(true);
                    if ((cold == null || cold.CurrentState == AttackState.Idle) && (damage == null || !damage.IsDamageEnabled()))
                    {
                        SpaceSessionForNetwork?.EndNetworkAttack(displayName);
                        PublishServerPlayerState(force: true);
                        if (!IsInSpace) ItemMgr.Instance?.ReleaseNetworkAuthorityPlayer(corePlayer);
                        groundCombatFrozen = true;
                    }
                }
                if (IsInSpace || groundCombatFrozen && acknowledgedGroundCombatHash == serverStateHash)
                { authoritativeGroundCombatPending = false; groundCombatFrozen = false; }
            }
            if (!isOwned || isServer || corePlayer == null) return;
            if (SpaceSession.ClientAttackHeld && Time.unscaledTime >= nextCombatAim)
            {
                nextCombatAim = Time.unscaledTime + .1f;
                Mod_GameController controller = corePlayer.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
                if (controller != null) SpaceSession.SubmitNetworkAttack(corePlayer, 0, controller.GetAimWorldPosition(8192f));
            }
            if (authoritativeGroundCombatPending && ItemNetworkStateSerialization.IsValidPayload(authoritativeItemState))
            {
                uint hash = ItemNetworkStateSerialization.CalculateHash(authoritativeItemState);
                if (hash == lastAppliedCombatStateHash && hash != lastClientCombatAck)
                {
                    lastClientCombatAck = hash;
                    SpaceNetworkBridge.SendIntent(displayName, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.CombatStateApplied, Action = unchecked((int)hash) });
                }
            }
        }
        private void OnGroundCombatPendingChanged(bool previous, bool pending)
        {
            if (!isOwned || isServer) return;
            lastClientCombatAck = 0;
            if (pending)
            {
                if (ItemNetworkStateSerialization.IsValidPayload(authoritativeItemState))
                    ApplyOwnedCombatState(authoritativeItemState);
            }
            else
            {
                SpaceSession.FinishClientGroundCombat();
                lastSubmittedItemStateHash = 0; itemStateDirty = true;
            }
        }
        private void ApplyOwnedCombatState(byte[] payload)
        {
            if (corePlayer == null || !ItemNetworkStateSerialization.IsValidPayload(payload)) return;
            Mod_HotBar hotbar = corePlayer.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            int selectedIndex = hotbar?.CurrentIndex ?? -1;
            byte[] appliedPayload = payload;
            if (!IsInSpace)
            {
                // 地表只回收本次战斗交接的模块，氧气、环境和其它本地状态继续使用当前数据。
                byte[] localPayload = ItemNetworkStateSerialization.Capture(corePlayer, true);
                if (!ItemNetworkStateSerialization.TryDeserializeItemData(localPayload, out ItemData merged) ||
                    !ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData authority) || merged.Guid != authority.Guid) return;
                foreach (Module module in corePlayer.Mods.Values)
                    if ((module is Mod_HotBar or Mod_Inventory or Mod_DamageReceiver or Mod_Stamina or Mod_PlayerDeathState) &&
                        authority.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state))
                        merged.ModuleDataDic[module.StableName] = state;
                if (!ItemNetworkStateSerialization.TrySerializeItemData(merged, out appliedPayload)) return;
            }
            if (!ItemNetworkStateSerialization.Apply(corePlayer, appliedPayload, reloadRuntimeModules: true, requireMatchingGuid: true)) return;
            if (selectedIndex >= 0) hotbar?.ApplyNetworkSelectedIndex(selectedIndex);
            lastAppliedCombatStateHash = ItemNetworkStateSerialization.CalculateHash(payload);
            lastSubmittedItemStateHash = 0; itemStateDirty = true;
        }
        #endregion
        public bool TryGetSpaceObserver(out SpaceNetworkObserver observer)
        {
            if (isServer && SpaceSessionForNetwork != null && SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out observer)) return true;
            observer = new SpaceNetworkObserver { InSpace = IsInSpace, Position = AuthoritativeSpacePosition,
                Velocity = new SpaceVector2(authoritativeSpaceVelocityX, authoritativeSpaceVelocityY),
                ShipId = authoritativeShipId, WorldKey = authoritativeWorldKey, RealmRevision = authoritativeRealmRevision };
            return !string.IsNullOrWhiteSpace(displayName);
        }
        public bool SharesLocalRealm()
        {
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (local == null) return !IsInSpace;
            if (local == this) return true;
            if (local.IsInSpace != IsInSpace) return false;
            return IsInSpace || string.IsNullOrEmpty(authoritativeWorldKey) || string.IsNullOrEmpty(local.authoritativeWorldKey) ||
                string.Equals(authoritativeWorldKey, local.authoritativeWorldKey, StringComparison.Ordinal);
        }
        #endregion

        #region 规范坐标投影与本机可见性
        private void UpdateSpaceMovement()
        {
            BindSpacePresentation();
            bool visible = SharesLocalRealm();
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (visible && IsInSpace && local != null && local != this)
                visible = SpaceSectorCoordinates.Difference(AuthoritativeSpacePosition, local.AuthoritativeSpacePosition).Magnitude <= NetworkSpaceStateCoordinator.ObserverRangeMeters;
            SetNetworkVisibility(visible);
            if (!visible || !IsInSpace || SpaceSessionForNetwork == null) return;
            Vector2 projected = SpaceSessionForNetwork.ProjectPosition(AuthoritativeSpacePosition);
            if (!isOwned && SpaceSessionForNetwork.TryGetPassengerPresentationPosition(displayName, out Vector2 presentation))
                projected = presentation;
            if (!float.IsFinite(projected.x) || !float.IsFinite(projected.y)) return;
            if (!isOwned)
            {
                transform.position = projected;
                SyncCorePlayerPosition(projected);
                if (corePlayer != null) corePlayer.transform.position = projected;
                if (!isServer)
                {
                    ApplyRemoteFacing(authoritativeFacingLeft);
                    ApplyRemoteVisualState(authoritativeVisualState);
                }
                coreTurnBack?.ModUpdate(Time.deltaTime);
            }
            else if (corePlayer != null)
            {
                transform.position = corePlayer.transform.position;
            }
        }

        private SpaceSession spacePresentationSession;

        private void BindSpacePresentation()
        {
            if (spacePresentationSession == SpaceSessionForNetwork) return;
            if (spacePresentationSession != null) spacePresentationSession.PresentationSynchronized -= SynchronizeSpacePresentation;
            spacePresentationSession = SpaceSessionForNetwork;
            if (spacePresentationSession != null) spacePresentationSession.PresentationSynchronized += SynchronizeSpacePresentation;
        }

        private void SynchronizeSpacePresentation()
        {
            if (!IsInSpace || !networkVisible || spacePresentationSession == null) return;
            if (isOwned)
            {
                if (corePlayer != null) transform.position = corePlayer.transform.position;
                return;
            }
            Vector2 projected = spacePresentationSession.ProjectPosition(AuthoritativeSpacePosition);
            if (spacePresentationSession.TryGetPassengerPresentationPosition(displayName, out Vector2 presentation)) projected = presentation;
            if (!float.IsFinite(projected.x) || !float.IsFinite(projected.y)) return;
            transform.position = projected;
            SyncCorePlayerPosition(projected);
            if (corePlayer != null) corePlayer.transform.position = projected;
        }

        private void OnDisable()
        {
            if (spacePresentationSession != null) spacePresentationSession.PresentationSynchronized -= SynchronizeSpacePresentation;
            spacePresentationSession = null;
        }
        public void SetNetworkVisibility(bool visible)
        {
            if (isOwned) visible = true;
            if (!visible)
            {
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true)) HideRenderer(renderer);
                if (corePlayer != null)
                {
                    foreach (Renderer renderer in corePlayer.GetComponentsInChildren<Renderer>(true)) HideRenderer(renderer);
                    foreach (Canvas canvas in corePlayer.GetComponentsInChildren<Canvas>(true))
                    { if (!hiddenCanvases.ContainsKey(canvas)) hiddenCanvases.Add(canvas, canvas.enabled); canvas.enabled = false; }
                    // 远区副本不占用本机物理空间，服务器仍按纯数据结算碰撞。
                    foreach (Collider2D collider in corePlayer.GetComponentsInChildren<Collider2D>(true))
                    {
                        if (isServer && collider.GetComponent<Mod_Damage>() != null) continue;
                        if (!hiddenColliders.ContainsKey(collider)) hiddenColliders.Add(collider, collider.enabled); collider.enabled = false;
                    }
                }
            }
            else if (!networkVisible)
            {
                foreach (var pair in hiddenRenderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
                foreach (var pair in hiddenColliders) if (pair.Key != null) pair.Key.enabled = pair.Value;
                foreach (var pair in hiddenCanvases) if (pair.Key != null) pair.Key.enabled = pair.Value;
                hiddenRenderers.Clear(); hiddenColliders.Clear(); hiddenCanvases.Clear();
                HideNetworkProxyRenderer();
            }
            networkVisible = visible;
        }
        private void HideRenderer(Renderer renderer)
        { if (!hiddenRenderers.ContainsKey(renderer)) hiddenRenderers.Add(renderer, renderer.enabled); renderer.enabled = false; }
        #endregion
    }
}
