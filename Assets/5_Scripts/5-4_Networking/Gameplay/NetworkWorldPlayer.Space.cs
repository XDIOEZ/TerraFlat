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
            if (!IsInSpace || corePlayer == null || !corePlayer.IsInitialized || !force && serverStateTimer > 0f) return;
            serverStateTimer = .2f;
            byte[] payload = ItemNetworkStateSerialization.CaptureCachedNetworkPayload(corePlayer, true, out _, out uint hash);
            if (!ItemNetworkStateSerialization.IsValidPayload(payload) || hash == serverStateHash) return;
            serverStateHash = hash;
            authoritativeItemState = payload;
        }
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
            bool visible = SharesLocalRealm();
            NetworkWorldPlayer local = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (visible && IsInSpace && local != null && local != this)
                visible = SpaceSectorCoordinates.Difference(AuthoritativeSpacePosition, local.AuthoritativeSpacePosition).Magnitude <= NetworkSpaceStateCoordinator.ObserverRangeMeters;
            SetNetworkVisibility(visible);
            if (!visible || !IsInSpace || SpaceSessionForNetwork == null) return;
            Vector2 projected = SpaceSessionForNetwork.ProjectPosition(AuthoritativeSpacePosition);
            if (!float.IsFinite(projected.x) || !float.IsFinite(projected.y)) return;
            if (!isOwned)
            {
                transform.position = projected;
                SyncCorePlayerPosition(projected);
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
                if (!isServer && SpaceSession.ClientAttackHeld && Time.unscaledTime >= nextCombatAim)
                {
                    nextCombatAim = Time.unscaledTime + .1f;
                    Mod_GameController controller = corePlayer.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
                    if (controller != null) SpaceSession.SubmitNetworkAttack(corePlayer, 0, controller.GetAimWorldPosition(8192f));
                }
            }
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
