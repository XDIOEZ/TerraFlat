// AI-Context: Mirror 玩家网络聚合器；负责运动、全模块快照、转身、动画与远程手持物，严禁远程副本获得本地输入权。

using System.Collections.Generic;
using FlatWorld.Diagnostics;
using FlatWorld.Spaceflight;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 第一阶段正式联机玩家：本地预测移动、服务端校验位置、所有客户端共同观察区块。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class NetworkWorldPlayer : NetworkBehaviour
    {
        #region 玩家同步性能采样
        private static readonly PerformanceDebugPoint AuthorityUpdatePoint = new("FlatWorld.Network.Player.Authority");
        private static readonly PerformanceDebugPoint NetworkActionsPoint = new("FlatWorld.Network.Player.Actions");
        private static readonly PerformanceDebugPoint OwnedMovementPoint = new("FlatWorld.Network.Player.OwnedMovement");
        private static readonly PerformanceDebugPoint OwnedStatePoint = new("FlatWorld.Network.Player.OwnedState");
        private static readonly PerformanceDebugPoint StateCapturePoint = new("FlatWorld.Network.Player.StateCapture");
        #endregion

        private const float MaxSupportedWorldCoordinate = 100000f;

        private static readonly Color[] PlayerColors =
        {
            new Color(1f, 0.72f, 0.12f),
            new Color(0.95f, 0.2f, 0.58f)
        };

        [SerializeField, Min(0.1f)] private float movementSpeed = 3.5f;
        [SerializeField, Min(1f)] private float cameraOrthographicSize = 8f;
        [SerializeField, Min(0.01f)] private float networkSendInterval = 0.033f;
        [SerializeField, Min(0.1f)] private float remotePositionLerpSpeed = 18f;
        [SerializeField, Min(0.02f)] private float maxRemoteExtrapolation = 0.08f;
        [SerializeField, Min(0.1f)] private float itemStateFallbackInterval = 0.75f;

        [SyncVar(hook = nameof(OnDisplayNameChanged))]
        private string displayName = "玩家";

        [SyncVar(hook = nameof(OnPlayerColorChanged))]
        private Color playerColor = Color.white;

        [SyncVar(hook = nameof(OnAuthoritativePositionChanged))]
        private Vector3 authoritativePosition;

        [SyncVar(hook = nameof(OnAuthoritativeVelocityChanged))]
        private Vector2 authoritativeVelocity;

        [SyncVar(hook = nameof(OnAuthoritativeItemStateChanged))]
        private byte[] authoritativeItemState;

        [SyncVar(hook = nameof(OnAuthoritativeFacingChanged))]
        private bool authoritativeFacingLeft;

        [SyncVar(hook = nameof(OnAuthoritativeVisualStateChanged))]
        private NetworkPlayerVisualState authoritativeVisualState;

        private Camera followCamera;
        private Renderer cachedRenderer;
        private TextMeshPro nameLabel;
        private Player corePlayer;
        private Mod_Mover coreMover;
        private Rigidbody2D coreBody;
        private Mod_TurnBack coreTurnBack;
        private Mod_AnimatorController coreAnimator;
        private Mod_AnimatorController_Receiver coreAnimatorReceiver;
        private Vector3 remoteTargetPosition;
        private Vector3 remoteSnapshotStartPosition;
        private Vector2 remoteVelocity;
        private float remoteSnapshotElapsed;
        private float remoteSnapshotDuration = 0.05f;
        private float lastRemoteSnapshotTime = -1f;
        private float networkSendTimer;
        private float itemStateSendTimer;
        private Vector3 lastOwnedMotionSamplePosition;
        private float lastOwnedMotionSampleTime;
        private bool hasOwnedMotionSample;
        private uint lastSubmittedItemStateHash;
        private readonly Dictionary<string, uint> remoteModuleStateHashes = new();
        private float nextCorePlayerRetryTime;
        private bool coreAvatarIsLocal;
        private bool localPlayerEnterNotified;
        private bool manualCameraFollow;
        private bool itemStateDirty = true;
        private bool lastSubmittedFacingLeft;
        private int lastAppliedAnimatorStateHash;
        private bool lastAppliedVisualAttacking;

        public string DisplayName => displayName;
        public Player CorePlayer => corePlayer;
        /// <summary>服务器权威逻辑坐标；协议、距离校验和存档禁止读取本机表现 Transform 代替它。</summary>
        public Vector3 AuthoritativeLogicalPosition => IsInSpace && SpaceSessionForNetwork != null
            ? (Vector3)SpaceSessionForNetwork.ProjectPosition(AuthoritativeSpacePosition)
            : IsValidPosition(authoritativePosition)
            ? WorldTopologyRuntime.NormalizePosition(authoritativePosition)
            : WorldTopologyRuntime.NormalizePosition(transform.position);

        /// <summary>区块观察者使用的逻辑位置；本机拥有者可使用尚未发包的最新预测位置。</summary>
        public Vector3 ObserverLogicalPosition => IsInSpace ? AuthoritativeLogicalPosition : isOwned && corePlayer != null
            ? WorldTopologyRuntime.NormalizePosition(corePlayer.transform.position)
            : AuthoritativeLogicalPosition;

        [Server]
        public void InitializeOnServer(string playerName, int playerIndex)
        {
            displayName = playerName;
            int colorIndex = Mathf.Abs(playerIndex) % PlayerColors.Length;
            playerColor = PlayerColors[colorIndex];
            authoritativePosition = transform.position;
            authoritativePosition = WorldTopologyRuntime.NormalizePosition(authoritativePosition);
            remoteTargetPosition = authoritativePosition;
            remoteSnapshotStartPosition = authoritativePosition;
            authoritativeVelocity = Vector2.zero;
            authoritativeFacingLeft = false;
            authoritativeVisualState = NetworkPlayerVisualState.Idle;
            RefreshSpaceAuthority();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DontDestroyOnLoad(gameObject);
            remoteTargetPosition = IsInSpace ? AuthoritativeLogicalPosition : WorldLocalPresentation.ProjectPosition(authoritativePosition);
            remoteSnapshotStartPosition = transform.position;
            remoteVelocity = authoritativeVelocity;
            if (!isOwned && IsValidPosition(authoritativePosition))
                transform.position = remoteTargetPosition;

            ApplyPlayerColor(playerColor);
            EnsureNameLabel();
            ApplyDisplayName(displayName);
            if (isOwned)
                SaveDataMgr.Instance.CurrentContrrolPlayerName = displayName;
            EnsureCorePlayer(isOwned);
            NetworkChunkStreamingCoordinator.Register(transform);
            ItemNetworkStateSerialization.RuntimeStateChanged += OnRuntimeItemStateChanged;
            WorldTopologyRuntime.LocalPlayerWrapped += HandleLocalPresentationWrap;
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            BindTeleport();
            ApplyDisplayName(displayName);
            SaveDataMgr.Instance.CurrentContrrolPlayerName = displayName;
            EnsureCorePlayer(true);

            if (corePlayer != null && !localPlayerEnterNotified && !reusedLocalWorldPlayer)
            {
                localPlayerEnterNotified = true;
                GameManager.Instance.NotifyNetworkLocalPlayerEntered(corePlayer);
            }

            CreateFollowCamera();
            Debug.Log($"[联机] 本地核心 Player Item 已生成：{displayName} / netId={netId}", this);
        }

        public override void OnStopClient()
        {
            ReleaseNetworkShield();
            ReleaseNetworkActionConstraints();
            UnbindTeleport();
            RestoreSinglePlayerStreaming();
            UnbindNetworkRoll(endSession: true);
            ItemNetworkStateSerialization.RuntimeStateChanged -= OnRuntimeItemStateChanged;
            WorldTopologyRuntime.LocalPlayerWrapped -= HandleLocalPresentationWrap;
            NetworkChunkStreamingCoordinator.Unregister(transform);

            if (manualCameraFollow && followCamera != null)
                Destroy(followCamera.gameObject);

            if (!isServer && corePlayer != null && ItemMgr.Instance != null)
            {
                ItemMgr.Instance.ReleaseNetworkPlayer(corePlayer, persistData: isServer || NetworkServer.active);
                corePlayer = null;
                coreMover = null;
                coreBody = null;
                coreTurnBack = null;
                coreAnimator = null;
                coreAnimatorReceiver = null;
                lastAppliedVisualAttacking = false;
                remoteModuleStateHashes.Clear();
            }

            base.OnStopClient();
        }

        private void Update()
        {
            if (GameManager.ExistingInstance?.IsInGameWorld != true)
                return;
            if (isServer)
            {
                using var authorityDebug = AuthorityUpdatePoint.Measure();
                serverStateTimer -= Time.unscaledDeltaTime;
                using var worldScope = NetworkGameplayWorldScope.Use(this);
                if (corePlayer == null) EnsureCorePlayer(isOwned);
                MaintainPlayerAuthority();
                SynchronizeGroundObserver();
                RefreshSpaceAuthority();
            }
            using (NetworkActionsPoint.Measure())
            {
                UpdateNetworkCombat();
                UpdateNetworkRoll();
                UpdateNetworkShield();
                UpdateNetworkActionConstraints();
            }
            if (!NetworkClient.active)
            {
                if (isServer) PublishServerPlayerState();
                return;
            }

            if (corePlayer == null && Time.unscaledTime >= nextCorePlayerRetryTime)
            {
                nextCorePlayerRetryTime = Time.unscaledTime + 1f;
                EnsureCorePlayer(isOwned);
            }

            if (isOwned)
            {
                using (OwnedMovementPoint.Measure()) UpdateOwnedMovement();
                using (OwnedStatePoint.Measure()) UpdateOwnedItemState();
            }
            else
                using (isServer ? NetworkGameplayWorldScope.Use(this) : null) UpdateRemoteMovement();

            // 本机与服务器共用角色，只在本帧动作完成后统一发布最终状态。
            if (isServer) PublishServerPlayerState();
        }

        private void UpdateOwnedMovement()
        {
            if (authoritativeForcedFollowing) return;
            if (pendingTeleportRequest != 0 || authoritativeWorldTransferPending) return;
            if (IsInSpace)
            {
                UpdateSpaceMovement();
                return;
            }
            if (corePlayer != null)
            {
                Vector3 itemPosition = WorldTopologyRuntime.NormalizePosition(
                    ShipSurfacePresentation.GroundPosition(corePlayer.gameObject, corePlayer.transform.position));
                if (IsValidPosition(itemPosition))
                    transform.position = itemPosition;
            }
            else
            {
                // Item 系统尚未就绪时保留轻量移动作为降级路径。
                Vector2 input = ReadMovementInput();
                Vector3 nextPosition = transform.position +
                                       new Vector3(input.x, input.y, 0f) * (movementSpeed * Time.deltaTime);
                nextPosition = WorldTopologyRuntime.NormalizePosition(nextPosition);
                if (IsValidPosition(nextPosition))
                    transform.position = nextPosition;
            }

            networkSendTimer -= Time.deltaTime;
            if (networkSendTimer > 0f)
                return;

            networkSendTimer = networkSendInterval;
            Vector2 velocity = ReadOwnedVelocity();
            bool facingLeft = ReadOwnedFacing();
            NetworkPlayerVisualState visualState = CaptureOwnedVisualState(velocity);
            if (isServer)
                ApplyMotionOnServer(transform.position, velocity, facingLeft, visualState);
            else
                CmdSubmitMotion(transform.position, velocity, facingLeft, visualState, authoritativeRealmRevision);
        }

        private void UpdateOwnedItemState()
        {
            if (pendingTeleportRequest != 0 || authoritativeWorldTransferPending) return;
            if (corePlayer == null || !corePlayer.IsInitialized)
                return;

            if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return;

            if (!IsInSpace && corePlayer.Data?.transform != null)
                corePlayer.Data.transform.position = WorldTopologyRuntime.NormalizePosition(
                    ShipSurfacePresentation.GroundPosition(corePlayer.gameObject, corePlayer.transform.position));

            if (isServer) return;

            itemStateSendTimer -= Time.deltaTime;
            if (!itemStateDirty && itemStateSendTimer > 0f)
                return;

            itemStateDirty = false;
            itemStateSendTimer = itemStateFallbackInterval;

            try
            {
                using var captureDebug = StateCapturePoint.Measure();
                byte[] payload = ItemNetworkStateSerialization.CaptureCachedNetworkPayload(corePlayer, true, out _, out uint hash);
                if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                    return;

                if (hash == lastSubmittedItemStateHash)
                    return;

                lastSubmittedItemStateHash = hash;
                CmdSubmitItemState(payload, authoritativeRealmRevision);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[联机玩家] 捕获 Item 状态失败：{exception.Message}", this);
            }
        }

        private bool ReadOwnedFacing()
        {
            if (corePlayer == null)
                return lastSubmittedFacingLeft;

            if (coreTurnBack == null)
                coreTurnBack = corePlayer.itemMods?.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);

            if (coreTurnBack == null)
                return lastSubmittedFacingLeft;

            bool facingLeft = coreTurnBack.currentDirection.x < 0f;
            lastSubmittedFacingLeft = facingLeft;
            return facingLeft;
        }

        private NetworkPlayerVisualState CaptureOwnedVisualState(Vector2 velocity)
        {
            if (corePlayer == null)
                return NetworkPlayerVisualState.Idle;

            CacheAnimatorModules();

            Animator animator = coreAnimator?.animator;
            bool singleSprite = coreAnimator != null && coreAnimator.IsSingleSpriteMode;
            int animatorStateHash = 0;
            if (!singleSprite && animator != null && animator.enabled &&
                animator.runtimeAnimatorController != null && animator.layerCount > 0)
            {
                AnimatorStateInfo stateInfo = animator.IsInTransition(0)
                    ? animator.GetNextAnimatorStateInfo(0)
                    : animator.GetCurrentAnimatorStateInfo(0);
                animatorStateHash = stateInfo.fullPathHash;
            }

            return new NetworkPlayerVisualState
            {
                // 单图跟随主动移动，水流、船载与失重漂移不能被同步成迈步。
                IsMoving = singleSprite ? coreMover != null && coreMover.IsMoving : velocity.sqrMagnitude > 0.001f,
                IsRunning = coreMover != null && coreMover.IsRunning,
                IsAttacking = singleSprite ? coreAnimator.IsAttackPlaying : coreAnimatorReceiver != null && coreAnimatorReceiver.IsAttacking,
                CanUseSkill = coreAnimatorReceiver != null && coreAnimatorReceiver.CanUseSkill,
                SkillId = coreAnimatorReceiver != null ? coreAnimatorReceiver.SkillId : 0,
                AnimatorStateHash = animatorStateHash
            };
        }

        [Command]
        private void CmdSubmitItemState(byte[] payload, uint realmRevision)
        {
            if (!AcceptWorldSubmission(realmRevision)) return;
            using var world = NetworkGameplayWorldScope.Use(this);
            ApplyItemStateOnServer(payload);
        }

        [Server]
        private void ApplyItemStateOnServer(byte[] payload)
        {
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            // 拾取已交给服务器结算，确认完成前不能被更早的客户端全量库存覆盖。
            if (HasServerPickupTransaction || authoritativeWorldTransferPending) return;

            if (!ProtectPendingRollStamina(ref payload)) return;

            if (corePlayer == null) return;
            MaintainPlayerAuthority();
            if (!coreAvatarIsLocal && !PreserveServerSimulation(ref payload)) return;
            if (!IsInSpace && !authoritativeGroundCombatPending)
            {
                if (!isOwned)
                {
                    if (corePlayer.IsInitialized)
                        ItemNetworkStateSerialization.ApplyRemoteReplica(corePlayer, payload, (module, state) =>
                        {
                            if (module is Mod_HotBar hotbar) hotbar.ApplyNetworkAuthorityData(corePlayer, state);
                            else if (module is IRemoteNetworkModule remote) remote.ApplyRemoteNetworkData(corePlayer, state);
                        });
                    else ApplyRemoteItemState(payload);
                }
                authoritativeItemState = payload;
                PublishServerPlayerState(force: true);
                return;
            }
            if (!isOwned && !groundCombatFrozen)
            {
                if (!ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData incoming) ||
                    incoming is not Data_Player || incoming.Guid != corePlayer.itemData.Guid) return;
                // 客户端只提交快捷栏选择，数量和生命状态始终来自服务器事务。
                foreach (Module module in corePlayer.Mods.Values)
                    if (module is Mod_HotBar hotbar && incoming.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state))
                        hotbar.ApplyNetworkSelectedSlot(state);
            }
            PublishServerPlayerState(force: true);
        }

        [Command]
        private void CmdSubmitMotion(
            Vector3 requestedPosition,
            Vector2 requestedVelocity,
            bool facingLeft,
            NetworkPlayerVisualState visualState, uint realmRevision)
        {
            if (!AcceptWorldSubmission(realmRevision)) return;
            using var world = NetworkGameplayWorldScope.Use(this);
            ApplyMotionOnServer(requestedPosition, requestedVelocity, facingLeft, visualState);
        }

        [Server]
        private void ApplyMotionOnServer(
            Vector3 requestedPosition,
            Vector2 requestedVelocity,
            bool facingLeft,
            NetworkPlayerVisualState visualState)
        {
            if (IsInSpace || authoritativeWorldTransferPending) return;
            // 强制移动使用服务器技能租约的位置，客户端旧移动包不能挣脱控制。
            if (ActorActionConstraints.TryGetForcedPosition(corePlayer, out Vector3 forcedPosition))
            {
                requestedPosition = forcedPosition;
                requestedVelocity = Vector2.zero;
            }
            if (!IsValidPosition(requestedPosition))
                return;

            Vector3 acceptedPosition = IsValidPosition(authoritativePosition)
                ? authoritativePosition
                : WorldTopologyRuntime.NormalizePosition(transform.position);
            float maxStep = Mathf.Max(0.5f, movementSpeed * 0.35f);
            requestedPosition = CalculateAcceptedPosition(
                acceptedPosition,
                requestedPosition,
                maxStep,
                SaveDataMgr.Instance?.Active_PlanetData);

            float maxVelocity = Mathf.Max(movementSpeed * 4f, remotePositionLerpSpeed);
            if (!IsValidVelocity(requestedVelocity))
                requestedVelocity = Vector2.zero;
            requestedVelocity = Vector2.ClampMagnitude(requestedVelocity, maxVelocity);

            corePlayer?.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.ApplyNetworkMovementActivity(visualState.IsMoving, visualState.IsRunning);
            authoritativePosition = requestedPosition;
            authoritativeVelocity = requestedVelocity;
            authoritativeFacingLeft = facingLeft;
            visualState.IsMoving = requestedVelocity.sqrMagnitude > 0.001f;
            authoritativeVisualState = visualState;
            remoteTargetPosition = requestedPosition;

            // Host 上的远端角色也需要平滑显示；纯服务器则直接采用权威坐标。
            if (NetworkClient.active && !isOwned)
            {
                BeginRemoteSnapshot(requestedPosition, requestedVelocity);
                ApplyRemoteFacing(facingLeft);
                ApplyRemoteVisualState(visualState);
            }
            else
            {
                transform.position = requestedPosition;
                if (!isOwned)
                {
                    // 独立服务器同样推进远端角色本体，否则权威坐标变化但实体仍留在出生点。
                    SyncCorePlayerPosition(WorldLocalPresentation.ProjectPosition(requestedPosition));
                    ApplyRemoteFacing(facingLeft);
                    ApplyRemoteVisualState(visualState);
                }
            }
        }

        private void OnAuthoritativePositionChanged(Vector3 oldPosition, Vector3 newPosition)
        {
            if (isOwned && pendingTeleportRequest != 0) return;
            if (IsInSpace) return;
            if (!IsValidPosition(newPosition))
                return;

            BeginRemoteSnapshot(newPosition, authoritativeVelocity);
            float snapDistance = isOwned ? 4f : 10f;
            Vector3 comparisonPosition = isOwned && corePlayer != null
                ? corePlayer.transform.position
                : transform.position;
            Vector2 correctionDelta = WorldTopologyRuntime.ShortestDelta(comparisonPosition, newPosition);
            if (correctionDelta.sqrMagnitude > snapDistance * snapDistance)
            {
                Vector3 logicalPosition = WorldTopologyRuntime.NormalizePosition(newPosition);
                Vector3 presentationPosition = WorldLocalPresentation.ProjectPosition(logicalPosition);
                transform.position = isOwned ? logicalPosition : presentationPosition;
                remoteSnapshotStartPosition = presentationPosition;
                SyncCorePlayerPosition(presentationPosition, forceLocal: isOwned);
            }
        }

        private void OnAuthoritativeVelocityChanged(Vector2 oldVelocity, Vector2 newVelocity)
        {
            if (IsInSpace) return;
            remoteVelocity = IsValidVelocity(newVelocity) ? newVelocity : Vector2.zero;

            // 停止时可能没有新的位置脏数据，单独用速度 SyncVar 把外推位置拉回最终坐标。
            if (!isOwned && remoteVelocity.sqrMagnitude < 0.0001f)
                BeginRemoteSnapshot(remoteTargetPosition, Vector2.zero);
        }

        private void BeginRemoteSnapshot(Vector3 targetPosition, Vector2 velocity)
        {
            if (IsInSpace) return;
            if (!IsValidPosition(targetPosition))
                return;

            float now = Time.unscaledTime;
            float observedInterval = lastRemoteSnapshotTime >= 0f
                ? now - lastRemoteSnapshotTime
                : networkSendInterval;
            lastRemoteSnapshotTime = now;

            Vector3 presentationStart = transform.position;
            Vector3 reprojectedStart = WorldLocalPresentation.ProjectPosition(
                WorldTopologyRuntime.NormalizePosition(presentationStart));
            if ((reprojectedStart - presentationStart).sqrMagnitude > 0.000001f)
            {
                presentationStart = reprojectedStart;
                transform.position = presentationStart;
                SyncCorePlayerPosition(presentationStart);
            }

            remoteSnapshotStartPosition = presentationStart;
            remoteTargetPosition = WorldLocalPresentation.ProjectPosition(targetPosition);
            remoteVelocity = IsValidVelocity(velocity) ? velocity : Vector2.zero;
            remoteSnapshotElapsed = 0f;
            remoteSnapshotDuration = Mathf.Clamp(
                observedInterval,
                Mathf.Max(0.016f, networkSendInterval * 0.65f),
                Mathf.Max(0.05f, networkSendInterval * 2.5f));
        }

        private void UpdateRemoteMovement()
        {
            if (IsInSpace || !SharesLocalRealm())
            {
                UpdateSpaceMovement();
                return;
            }
            SetNetworkVisibility(true);
            if (SpaceSessionForNetwork != null && SpaceSessionForNetwork.TryGetPassengerPresentationPosition(displayName, out Vector2 passengerPosition))
            {
                transform.position = passengerPosition;
                SyncCorePlayerPosition(passengerPosition);
                ApplyRemoteFacing(authoritativeFacingLeft);
                ApplyRemoteVisualState(authoritativeVisualState);
                coreTurnBack?.ModUpdate(Time.deltaTime);
                return;
            }
            if (!IsValidPosition(remoteTargetPosition))
                return;

            remoteSnapshotElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(remoteSnapshotElapsed / Mathf.Max(0.001f, remoteSnapshotDuration));
            Vector3 visualPosition = Vector3.Lerp(remoteSnapshotStartPosition, remoteTargetPosition, t);

            if (remoteSnapshotElapsed > remoteSnapshotDuration && remoteVelocity.sqrMagnitude > 0.0001f)
            {
                float extrapolation = Mathf.Min(
                    remoteSnapshotElapsed - remoteSnapshotDuration,
                    maxRemoteExtrapolation);
                visualPosition += new Vector3(remoteVelocity.x, remoteVelocity.y, 0f) * extrapolation;
            }

            if (!IsValidPosition(visualPosition))
                return;

            transform.position = visualPosition;
            SyncCorePlayerPosition(visualPosition);

            // 远程 Item 不进入完整本地模块更新，这里按同一个运动快照驱动动画与转身。
            ApplyRemoteFacing(authoritativeFacingLeft);
            ApplyRemoteVisualState(authoritativeVisualState);
            coreTurnBack?.ModUpdate(Time.deltaTime);
        }

        private void OnAuthoritativeItemStateChanged(byte[] oldState, byte[] newState)
        {
            if (isServer) return;
            if (isOwned && ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction())
            {
                ApplyPickupNonInventoryState(newState);
                return;
            }
            if (isOwned && corePlayer != null)
                ApplyOwnedCombatState(newState);
            else if (!isOwned)
                ApplyRemoteItemState(newState);
        }

        private void OnAuthoritativeFacingChanged(bool oldFacingLeft, bool newFacingLeft)
        {
            if (!isOwned)
                ApplyRemoteFacing(newFacingLeft);
        }

        private void OnAuthoritativeVisualStateChanged(
            NetworkPlayerVisualState oldState,
            NetworkPlayerVisualState newState)
        {
            if (!isOwned)
                ApplyRemoteVisualState(newState);
        }

        private void ApplyRemoteFacing(bool facingLeft)
        {
            if (corePlayer == null)
                return;

            if (coreTurnBack == null)
                coreTurnBack = corePlayer.itemMods?.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);

            if (coreTurnBack == null)
                return;

            // 远程角色不应再由本地鼠标焦点驱动朝向。
            coreTurnBack.faceMouse = null;
            coreTurnBack.TurnBodyToDirection(facingLeft ? Vector2.left : Vector2.right);
        }

        private void ApplyRemoteVisualState(NetworkPlayerVisualState state)
        {
            // 本机动画始终读取自己的移动模块，传送后的待机快照不能接管它。
            if (corePlayer == null || isOwned)
                return;

            CacheAnimatorModules();

            bool singleSprite = coreAnimator != null && coreAnimator.IsSingleSpriteMode;
            coreAnimator?.SetNetworkPresentation(true, state.IsMoving, state.IsRunning);

            if (coreAnimatorReceiver != null)
            {
                if (!singleSprite)
                    coreAnimatorReceiver.IsAttacking = state.IsAttacking;
                coreAnimatorReceiver.CanUseSkill = state.CanUseSkill;
                coreAnimatorReceiver.SkillId = state.SkillId;
            }

            if (singleSprite)
            {
                // 远端状态每帧重复应用，只有新一轮攻击的上升沿触发一次身体动作。
                if (state.IsAttacking && !lastAppliedVisualAttacking)
                    coreAnimator.PlayAttack(authoritativeFacingLeft ? Vector2.left : Vector2.right);
                lastAppliedVisualAttacking = state.IsAttacking;
                lastAppliedAnimatorStateHash = 0;
                return;
            }

            lastAppliedVisualAttacking = state.IsAttacking;
            Animator animator = coreAnimator?.animator;
            if (animator != null)
            {
                animator.enabled = true;
                animator.speed = 1f;
            }

            SetAnimatorBoolIfPresent(animator, AnimationText.Move, state.IsMoving);
            SetAnimatorBoolIfPresent(animator, AnimationText.Run, state.IsRunning);

            if (animator == null || state.AnimatorStateHash == 0 ||
                state.AnimatorStateHash == lastAppliedAnimatorStateHash ||
                !animator.HasState(0, state.AnimatorStateHash))
            {
                return;
            }

            lastAppliedAnimatorStateHash = state.AnimatorStateHash;
            animator.Play(state.AnimatorStateHash, 0, 0f);
        }

        private void CacheAnimatorModules()
        {
            if (corePlayer == null)
                return;

            if (coreAnimator == null)
                coreAnimator = corePlayer.itemMods?.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);

            if (coreAnimatorReceiver == null)
                coreAnimatorReceiver = coreAnimator as Mod_AnimatorController_Receiver;
        }

        private static void SetAnimatorBoolIfPresent(Animator animator, string parameterName, bool value)
        {
            if (animator == null || string.IsNullOrEmpty(parameterName))
                return;

            int nameHash = Animator.StringToHash(parameterName);
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.nameHash == nameHash && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(nameHash, value);
                    return;
                }
            }
        }

        private void ApplyRemoteItemState(byte[] payload)
        {
            if (corePlayer == null || !ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            ItemNetworkStateSerialization.ApplyRemoteReplica(corePlayer, payload, ApplyRemoteModuleState);
        }

        private void ApplyRemoteModuleState(Module module, ModuleData state)
        {
            if (module == null || state == null)
                return;

            string key = !string.IsNullOrEmpty(state.Name) ? state.Name : state.ID;
            uint hash = ItemNetworkStateSerialization.CalculateModuleHash(state);
            if (remoteModuleStateHashes.TryGetValue(key, out uint previousHash) && previousHash == hash)
                return;

            remoteModuleStateHashes[key] = hash;
            if (module is IRemoteNetworkModule remoteModule)
                remoteModule.ApplyRemoteNetworkData(corePlayer, state);
        }

        private void LateUpdate()
        {
            if (!isOwned || followCamera == null)
                return;

            if (corePlayer != null && IsValidPosition(corePlayer.transform.position))
                transform.position = IsInSpace ? corePlayer.transform.position : WorldTopologyRuntime.NormalizePosition(corePlayer.transform.position);

            if (manualCameraFollow)
            {
                Vector3 playerPosition = corePlayer != null ? corePlayer.transform.position : transform.position;
                followCamera.transform.position = new Vector3(playerPosition.x, playerPosition.y, -10f);
            }
        }

        private void EnsureCorePlayer(bool localControl)
        {
            if (GameManager.ExistingInstance?.IsInGameWorld != true ||
                ItemMgr.Instance == null || SaveDataMgr.Instance?.SaveData == null)
                return;

            try
            {
                Player existingLocalPlayer = ItemMgr.Instance.User_Player;
                if (corePlayer == null && existingLocalPlayer != null && existingLocalPlayer.IsInitialized &&
                    existingLocalPlayer.ProfileName == displayName && existingLocalPlayer.IsLocalProfile)
                    reusedLocalWorldPlayer = true;
                int networkGuid = unchecked((int)(0x40000000u | (netId & 0x3fffffffu)));
                Vector3 logicalSpawnPosition = IsInSpace ? AuthoritativeLogicalPosition : localControl
                    ? WorldTopologyRuntime.NormalizePosition(transform.position)
                    : AuthoritativeLogicalPosition;
                corePlayer = ItemMgr.Instance.LoadNetworkPlayer(displayName, networkGuid, logicalSpawnPosition, localControl);
                if (corePlayer == null)
                    return;

                if (localControl && !coreAvatarIsLocal)
                    ItemMgr.Instance.PromoteNetworkPlayerToLocal(corePlayer, logicalSpawnPosition);

                coreAvatarIsLocal |= localControl;
                if (!coreAvatarIsLocal)
                {
                    BindRemoteVisualModules(corePlayer);
                    if (isServer && IsInSpace) ItemMgr.Instance.InitializeNetworkAuthorityPlayer(corePlayer);
                }

                coreMover = corePlayer.GetComponentInChildren<Mod_Mover>(true);
                coreBody = corePlayer.GetComponent<Rigidbody2D>();
                coreTurnBack = corePlayer.itemMods?.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
                coreAnimator = corePlayer.itemMods?.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);
                coreAnimatorReceiver = coreAnimator as Mod_AnimatorController_Receiver;
                BindNetworkRoll();
                if (coreAvatarIsLocal)
                    coreAnimator?.SetNetworkPresentation(false, false);
                if (!coreAvatarIsLocal && !isServer && coreTurnBack != null)
                    coreTurnBack.faceMouse = null;

                Mod_GameController controller = corePlayer.GetComponentInChildren<Mod_GameController>(true);
                controller?.SetGameplayInputLocked(!coreAvatarIsLocal && !isServer);
                if (coreMover != null)
                    coreMover.IsLock = !coreAvatarIsLocal;

                Mod_ChunkLoader chunkLoader = corePlayer.GetComponentInChildren<Mod_ChunkLoader>(true);
                chunkLoader?.SetExternalStreamingManaged(true);

                HideNetworkProxyRenderer();
                SyncCorePlayerPosition(localControl
                    ? corePlayer.transform.position
                    : IsInSpace ? AuthoritativeLogicalPosition : WorldLocalPresentation.ProjectPosition(AuthoritativeLogicalPosition));
                if (isServer) SpaceSessionForNetwork?.EnsureNetworkPassenger(corePlayer);
                itemStateDirty = true;
                ApplyDisplayName(displayName);

                if (!isServer && !isOwned && ItemNetworkStateSerialization.IsValidPayload(authoritativeItemState))
                    ApplyRemoteItemState(authoritativeItemState);

                if (!isOwned)
                {
                    ApplyRemoteFacing(authoritativeFacingLeft);
                    ApplyRemoteVisualState(authoritativeVisualState);
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[联机] 创建核心 Player Item 失败：{displayName}\n{exception}", this);
            }
        }

        #region 已有单机主角接管

        private bool reusedLocalWorldPlayer;

        private void RestoreSinglePlayerStreaming()
        {
            if (!reusedLocalWorldPlayer || corePlayer == null)
                return;
            // 停止公开后把区块窗口交还原主角，保留当前角色、背包和相机。
            Mod_ChunkLoader loader = corePlayer.GetComponentInChildren<Mod_ChunkLoader>(true);
            loader?.SetExternalStreamingManaged(false);
            if (GameManager.ExistingInstance?.IsInGameWorld == true)
                loader?.RefreshConfiguredChunkWindow();
        }

        #endregion

        private static void BindRemoteVisualModules(Player player)
        {
            if (player == null || player.itemData == null || player.itemMods == null || player.itemMods.Mods.Count > 0)
                return;

            Module[] modules = player.GetComponentsInChildren<Module>(true);
            for (int i = 0; i < modules.Length; i++)
            {
                Module module = modules[i];
                if (module == null || module._Data == null)
                    continue;

                ModuleData networkData = FindRemoteModuleData(
                    player.itemData.ModuleDataDic,
                    module._Data.StableName,
                    module._Data.ModuleId,
                    out string stableName);
                if (networkData != null)
                    module._Data = networkData;

                if (string.IsNullOrWhiteSpace(module._Data.StableName))
                    module._Data.StableName = string.IsNullOrWhiteSpace(stableName)
                        ? module.gameObject.name
                        : stableName;

                module.ModuleInit(player, module._Data, player.itemData);
                player.itemMods.AddMod(module);
            }

            Mod_TurnBack turnBack = player.itemMods.GetMod_ByID(ModText.TrunBody) as Mod_TurnBack;
            if (turnBack?.Enabled == true)
            {
                turnBack.Load();
                turnBack.faceMouse = null;
            }

            Mod_AnimatorController animator =
                player.itemMods.GetMod_ByID(ModText.AnimatorReceiver) as Mod_AnimatorController;
            if (animator?.Enabled == true)
                animator.Load();
        }

        private static ModuleData FindRemoteModuleData(
            System.Collections.Generic.IReadOnlyDictionary<string, ModuleData> states,
            string stableName,
            string moduleId,
            out string resolvedStableName)
        {
            resolvedStableName = stableName;
            if (states == null)
                return null;

            if (!string.IsNullOrEmpty(stableName) && states.TryGetValue(stableName, out ModuleData exact))
                return exact;

            ModuleData match = null;
            foreach (System.Collections.Generic.KeyValuePair<string, ModuleData> pair in states)
            {
                ModuleData state = pair.Value;
                if (state == null || !string.Equals(state.ModuleId, moduleId, System.StringComparison.Ordinal))
                    continue;
                if (match != null)
                    return null;

                match = state;
                resolvedStableName = pair.Key;
            }

            return match;
        }

        private void SyncCorePlayerPosition(Vector3 position, bool forceLocal = false)
        {
            if (corePlayer == null || !IsValidPosition(position))
                return;

            if (!coreAvatarIsLocal || forceLocal)
            {
                if (coreBody != null)
                {
                    // 远端服务端假人使用不参与物理模拟的刚体，必须同步 Transform 才能真正移动实体。
                    if (!coreBody.simulated)
                        corePlayer.transform.position = position;
                    coreBody.position = position;
                    coreBody.velocity = isServer ? Vector2.zero : remoteVelocity;
                }
                else
                    corePlayer.transform.position = position;
            }

            if (!IsInSpace && corePlayer.Data != null)
                corePlayer.Data.transform.position = WorldTopologyRuntime.NormalizePosition(
                    SpaceSessionForNetwork != null && SpaceSessionForNetwork.TryGetPassengerSurfacePosition(displayName, out Vector2 ground)
                        ? (Vector3)ground : position);
        }

        /// <summary>本机锚点跨周时只重选远端玩家的显示镜像，服务器 SyncVar 逻辑坐标保持不变。</summary>
        private void HandleLocalPresentationWrap()
        {
            if (IsInSpace) return;
            if (isOwned || !IsValidPosition(authoritativePosition))
                return;

            Vector3 projected = WorldLocalPresentation.ProjectPosition(AuthoritativeLogicalPosition);
            transform.position = projected;
            remoteSnapshotStartPosition = projected;
            remoteTargetPosition = projected;
            remoteSnapshotElapsed = 0f;
            SyncCorePlayerPosition(projected);
        }

        private void HideNetworkProxyRenderer()
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponentInChildren<Renderer>();

            if (cachedRenderer != null)
                cachedRenderer.enabled = false;
        }

        private static bool IsValidPosition(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   !float.IsNaN(position.z) && !float.IsInfinity(position.z) &&
                   Mathf.Abs(position.x) <= MaxSupportedWorldCoordinate &&
                   Mathf.Abs(position.y) <= MaxSupportedWorldCoordinate;
        }

        private static bool IsValidVelocity(Vector2 velocity)
        {
            return !float.IsNaN(velocity.x) && !float.IsInfinity(velocity.x) &&
                   !float.IsNaN(velocity.y) && !float.IsInfinity(velocity.y);
        }

        /// <summary>
        /// Pure server movement validator used by runtime synchronization and smoke tests.
        /// Wrapped seam crossings use their shortest displacement before step clamping.
        /// </summary>
        public static Vector3 CalculateAcceptedPosition(
            Vector3 acceptedPosition,
            Vector3 requestedPosition,
            float maxStep,
            PlanetData planetData)
        {
            maxStep = Mathf.Max(0f, maxStep);
            acceptedPosition.z = 0f;
            requestedPosition.z = 0f;

            Vector3 delta;
            if (WorldTopologyBounds.TryCreate(planetData, out WorldTopologyBounds bounds))
            {
                acceptedPosition = bounds.NormalizePosition(acceptedPosition);
                requestedPosition = bounds.NormalizePosition(requestedPosition);
                Vector2 shortest = bounds.ShortestDelta(acceptedPosition, requestedPosition);
                delta = new Vector3(shortest.x, shortest.y, 0f);
            }
            else
            {
                delta = requestedPosition - acceptedPosition;
            }

            if (delta.sqrMagnitude > maxStep * maxStep)
                delta = Vector3.ClampMagnitude(delta, maxStep);

            Vector3 result = acceptedPosition + delta;
            result.z = 0f;
            return WorldTopologyBounds.TryCreate(planetData, out bounds)
                ? bounds.NormalizePosition(result)
                : result;
        }

        private Vector2 ReadOwnedVelocity()
        {
            Vector3 currentPosition = corePlayer != null ? corePlayer.transform.position : transform.position;
            float now = Time.unscaledTime;
            Vector2 sampledVelocity = Vector2.zero;
            if (hasOwnedMotionSample)
            {
                float elapsed = now - lastOwnedMotionSampleTime;
                if (elapsed > 0.0001f)
                {
                    Vector2 delta = WorldTopologyRuntime.ShortestDelta(lastOwnedMotionSamplePosition, currentPosition);
                    sampledVelocity = delta / elapsed;
                }
            }

            lastOwnedMotionSamplePosition = currentPosition;
            lastOwnedMotionSampleTime = now;
            hasOwnedMotionSample = true;

            if (coreBody != null && IsValidVelocity(coreBody.velocity) && coreBody.velocity.sqrMagnitude > 0.001f)
                return coreBody.velocity;

            if (IsValidVelocity(sampledVelocity) && sampledVelocity.sqrMagnitude > 0.001f)
                return sampledVelocity;

            return ReadMovementInput() * movementSpeed;
        }

        private void OnRuntimeItemStateChanged(Item changedItem)
        {
            if (isOwned && changedItem == corePlayer)
                itemStateDirty = true;
        }

        private static Vector2 ReadMovementInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return Vector2.zero;

            float horizontal = 0f;
            float vertical = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;
            return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        }

        private void CreateFollowCamera()
        {
            if (followCamera != null)
                return;

            Mod_Cam coreCamera = corePlayer != null ? corePlayer.GetComponentInChildren<Mod_Cam>(true) : null;
            if (coreCamera != null && coreCamera.ControllerCamera != null)
            {
                followCamera = coreCamera.ControllerCamera;
                manualCameraFollow = false;
                return;
            }

            Camera existingCamera = Camera.main;
            if (existingCamera != null)
            {
                followCamera = existingCamera;
            }
            else
            {
                GameObject cameraObject = new GameObject("联机玩家相机");
                cameraObject.tag = "MainCamera";
                followCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                DontDestroyOnLoad(cameraObject);
            }

            manualCameraFollow = true;

            followCamera.orthographic = true;
            followCamera.orthographicSize = cameraOrthographicSize;
            followCamera.clearFlags = CameraClearFlags.SolidColor;
            followCamera.backgroundColor = new Color(0.06f, 0.08f, 0.1f, 1f);
            followCamera.cullingMask = ~0;
            followCamera.targetTexture = null;
            followCamera.targetDisplay = 0;
            followCamera.depth = 100f;
            followCamera.enabled = true;
        }

        #region 玩家名称表现

        private void EnsureNameLabel()
        {
            if (nameLabel != null)
                return;

            Transform labelTransform = transform.Find("玩家名称");
            nameLabel = labelTransform != null ? labelTransform.GetComponent<TextMeshPro>() : null;
            if (nameLabel == null)
                Debug.LogError("[联机玩家] FlatWorldNetworkPlayer.prefab 缺少“玩家名称”TextMeshPro 节点。", this);
        }

        private void OnDisplayNameChanged(string oldName, string newName) => ApplyDisplayName(newName);

        private void ApplyDisplayName(string value)
        {
            EnsureNameLabel();
            if (nameLabel != null)
            {
                // 每个客户端只显示其他玩家的头顶名字。
                nameLabel.enabled = !isLocalPlayer;
                nameLabel.text = SaveDataMgr.Instance?.SaveData?.PlayerData_Dict is { } profiles &&
                    profiles.TryGetValue(value, out Data_Player data) && !string.IsNullOrWhiteSpace(data.Name_User)
                    ? data.Name_User : value;
            }
        }

        #endregion

        private void OnPlayerColorChanged(Color oldColor, Color newColor) => ApplyPlayerColor(newColor);

        private void ApplyPlayerColor(Color value)
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponentInChildren<Renderer>();

            if (cachedRenderer != null)
                cachedRenderer.material.color = value;

            if (nameLabel != null)
                nameLabel.color = value;
        }
    }
}
