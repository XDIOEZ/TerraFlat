using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private readonly HashSet<uint> groundCombatSentHashes = new();
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
            RestoreSinglePlayerStreaming();
            SpaceMgr.ExistingInstance?.ReleaseAuthoritySector(displayName);
            UnbindNetworkRoll(endSession: true);
            SpaceSessionForNetwork?.RemoveNetworkPassengerInput(displayName);
            SpaceSessionForNetwork?.EndNetworkAttack(displayName);
            if (!reusedLocalWorldPlayer && corePlayer != null && ItemMgr.Instance != null)
                ItemMgr.Instance.ReleaseNetworkPlayer(corePlayer, persistData: true);
            corePlayer = null;
            base.OnStopServer();
        }
        [Server]
        public void RefreshSpaceAuthority()
        {
            if (SpaceSessionForNetwork == null || !SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer)) return;
            if (!string.IsNullOrEmpty(authoritativeWorldKey) &&
                (authoritativeWorldKey != observer.WorldKey || authoritativeInSpace != observer.InSpace))
            {
                authoritativeWorldTransferPending = true;
                SpaceSessionForNetwork.RemoveNetworkPassengerInput(displayName);
            }
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
            SynchronizeAuthorityScene(observer);
            if (observer.InSpace && corePlayer?.IsInitialized == true)
            {
                authoritativeFacingLeft = ReadOwnedFacing();
                authoritativeVisualState = CaptureOwnedVisualState(observer.Velocity.ToVector2());
            }
        }
        private void SynchronizeAuthorityScene(SpaceNetworkObserver observer)
        {
            if (corePlayer == null || coreAvatarIsLocal) return;
            Scene target;
            string key;
            if (observer.InSpace)
            {
                key = SpaceSectorCoordinates.SceneKey(observer.Position.Sector);
                target = SpaceMgr.Instance.BindAuthoritySector(displayName, observer.Position.Sector);
            }
            else
            {
                key = observer.WorldKey;
                SpaceMgr.ExistingInstance?.ReleaseAuthoritySector(displayName);
                target = GameplayWorldRegistry.GetOrCreate(key).Scene;
            }
            if (!target.IsValid() || !target.isLoaded) return;
            corePlayer.Data.CurrentSceneName = key;
            if (corePlayer.gameObject.scene != target)
            {
                corePlayer.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(corePlayer.gameObject, target);
                foreach (Item item in corePlayer.GetComponentsInChildren<Item>(true)) ItemMgr.Instance.NotifyRuntimeItemMoved(item);
            }
            if (coreBody != null) coreBody.simulated = !observer.InSpace;
        }
        [Server]
        private void SynchronizeGroundObserver()
        {
            SpaceSession session = SpaceSessionForNetwork;
            // 每帧同步不分配捕获委托；这里直接只读现有乘员，不创建新成员。
            var passengers = session?.State?.Passengers;
            SpacePassengerState passenger = null;
            if (passengers != null)
                for (int i = 0; i < passengers.Count; i++)
                {
                    SpacePassengerState candidate = passengers[i];
                    if (candidate != null && candidate.ProfileId == displayName)
                    {
                        passenger = candidate;
                        break;
                    }
                }
            if (passenger == null || passenger.IsInSpace || passenger.Supported || passenger.SurfaceDescending) return;
            if (passenger.RealmRevision != authoritativeRealmRevision) return;
            Vector2 logical = (Vector2)WorldTopologyRuntime.NormalizePosition(authoritativePosition);
            session.SetNetworkPlayerRealm(displayName, false, new SpaceSectorPosition(default, SpaceVector2.FromVector2(logical)), corePlayer?.Data?.CurrentSceneName ?? authoritativeWorldKey);
        }
        [Server]
        private void PublishServerPlayerState(bool force = false)
        {
            bool ownedDirty = isOwned && itemStateDirty && pendingTeleportRequest == 0 && !authoritativeWorldTransferPending &&
                !ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction();
            if (corePlayer == null || !corePlayer.IsInitialized || !force && !ownedDirty && serverStateTimer > 0f) return;
            serverStateTimer = .2f;
            // 捕获前只消费已有脏标记，保存回调产生的新变化仍留给下一次发布。
            if (isOwned) itemStateDirty = false;
            byte[] payload;
            uint hash;
            try
            {
                using var captureDebug = StateCapturePoint.Measure();
                using var worldScope = NetworkGameplayWorldScope.Use(this);
                payload = ItemNetworkStateSerialization.CaptureCachedNetworkPayload(corePlayer, true, out _, out hash);
            }
            catch
            {
                if (isOwned) itemStateDirty = true;
                throw;
            }
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
            {
                if (ownedDirty) itemStateDirty = true;
                return;
            }
            if (!force && hash == serverStateHash) return;
            serverStateHash = hash;
            if (authoritativeGroundCombatPending)
            {
                if (groundCombatSentHashes.Count >= 32) groundCombatSentHashes.Clear();
                groundCombatSentHashes.Add(hash);
            }
            authoritativeItemState = payload;
        }
        #region 同地表船体战斗的临时权威交接
        public bool SubmitGroundCombatBaseline()
        {
            if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return false;
            if (!isOwned || isServer || corePlayer == null || !NetworkClient.ready) return false;
            byte[] payload = ItemNetworkStateSerialization.Capture(corePlayer, true);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload)) return false;
            CmdSubmitItemState(payload, authoritativeRealmRevision);
            return true;
        }
        public bool PrepareGroundCombat(SpaceNetworkIntentKind kind)
        {
            if (!isServer || IsInSpace || corePlayer == null || SpaceSessionForNetwork == null) return false;
            if (!SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer) ||
                string.IsNullOrWhiteSpace(observer.WorldKey)) return false;
            if (!authoritativeGroundCombatPending || groundCombatFrozen)
            {
                ItemMgr.Instance?.InitializeNetworkAuthorityPlayer(corePlayer, surfaceCombat: true);
                authoritativeGroundCombatPending = true; acknowledgedGroundCombatHash = 0; groundCombatSentHashes.Clear();
                groundCombatFrozen = false;
            }
            if (kind != SpaceNetworkIntentKind.OffhandParry)
                serverGroundAttackHeld = kind == SpaceNetworkIntentKind.Attack;
            lastGroundCombatInput = Time.unscaledTime;
            groundCombatReleaseAfter = Time.unscaledTime + 1f;
            return true;
        }
        public void AcknowledgeGroundCombat(uint hash)
        {
            if (isServer && authoritativeGroundCombatPending && groundCombatFrozen && groundCombatSentHashes.Contains(hash))
                acknowledgedGroundCombatHash = hash;
        }
        public void CancelRejectedGroundCombat()
        {
            if (!isServer || !authoritativeGroundCombatPending) return;
            SpaceSessionForNetwork?.EndNetworkAttack(displayName);
            PublishServerPlayerState(force: true);
            authoritativeGroundCombatPending = false; serverGroundAttackHeld = false; groundCombatFrozen = false;
            if (!IsInSpace && !HasServerPickupTransaction) MaintainPlayerAuthority();
        }
        private void UpdateNetworkCombat()
        {
            if (isServer && authoritativeGroundCombatPending)
            {
                using var worldScope = NetworkGameplayWorldScope.Use(this);
                bool validWorld = SpaceSessionForNetwork != null && SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out SpaceNetworkObserver observer) &&
                    !observer.InSpace && !string.IsNullOrWhiteSpace(observer.WorldKey);
                if ((!validWorld || Time.unscaledTime - lastGroundCombatInput > 1f) && serverGroundAttackHeld)
                { SpaceSessionForNetwork?.EndNetworkAttack(displayName); serverGroundAttackHeld = false; groundCombatReleaseAfter = Time.unscaledTime + 1f; }
                if (!serverGroundAttackHeld && !groundCombatFrozen && Time.unscaledTime >= groundCombatReleaseAfter)
                {
                    Item held = corePlayer?.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
                    Mod_ColdWeapon cold = held?.GetComponentInChildren<Mod_ColdWeapon>(true);
                    Mod_Damage damage = held?.GetComponentInChildren<Mod_Damage>(true);
                    if ((cold == null || cold.CurrentState == AttackState.Idle) && (damage == null || !damage.IsDamageEnabled()) &&
                        corePlayer?.itemMods.GetMod_ByID<Mod_ShieldCombat>(Mod_ShieldCombat.ModuleId)?.HasActiveAction != true)
                    {
                        SpaceSessionForNetwork?.EndNetworkAttack(displayName);
                        groundCombatSentHashes.Clear();
                        PublishServerPlayerState(force: true);
                        if (!IsInSpace && !HasServerPickupTransaction) MaintainPlayerAuthority();
                        groundCombatFrozen = true;
                    }
                }
                if (IsInSpace || groundCombatFrozen && acknowledgedGroundCombatHash != 0)
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
                    if ((module is IPlayerStateAuthorityModule ||
                        (authoritativeGroundCombatPending || SpaceSession.ClientGroundCombatPending) && (module is Mod_HotBar or Mod_Inventory)) &&
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
            // 服务端观察者丢失必须拒绝请求，不能退回旧 SyncVar 假装仍在原世界。
            if (isServer)
            {
                if (SpaceSessionForNetwork != null && SpaceSessionForNetwork.TryGetNetworkObserver(displayName, out observer))
                    return true;
                observer = default;
                return false;
            }
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
            if (!networkVisible || spacePresentationSession == null) return;
            if (isOwned)
            {
                if (corePlayer != null) transform.position = corePlayer.transform.position;
                return;
            }
            Vector2 projected;
            if (spacePresentationSession.TryGetPassengerPresentationPosition(displayName, out Vector2 presentation)) projected = presentation;
            else if (IsInSpace) projected = spacePresentationSession.ProjectPosition(AuthoritativeSpacePosition);
            else return;
            if (!float.IsFinite(projected.x) || !float.IsFinite(projected.y)) return;
            transform.position = projected;
            SyncCorePlayerPosition(projected);
            if (corePlayer != null) corePlayer.transform.position = projected;
        }

        private void OnDisable()
        {
            UnbindNetworkRoll();
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
                        if (isServer) continue;
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
