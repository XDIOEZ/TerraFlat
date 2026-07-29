// AI-Context: Mirror 玩家网络聚合器；负责运动、全模块快照、转身、动画与远程手持物，严禁远程副本获得本地输入权。

using System.Collections.Generic;
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
    public sealed class NetworkWorldPlayer : NetworkBehaviour
    {
        private const float MaxSupportedWorldCoordinate = 100000f;

        private static readonly Color[] PlayerColors =
        {
            new Color(1f, 0.72f, 0.12f),
            new Color(0.95f, 0.2f, 0.58f)
        };

        [SerializeField, Min(0.1f)] private float movementSpeed = 5f;
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
        private Mover coreMover;
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

        public string DisplayName => displayName;
        public Player CorePlayer => corePlayer;

        [Server]
        public void InitializeOnServer(string playerName, int playerIndex)
        {
            displayName = playerName;
            int colorIndex = Mathf.Abs(playerIndex) % PlayerColors.Length;
            playerColor = PlayerColors[colorIndex];
            authoritativePosition = transform.position;
            remoteTargetPosition = transform.position;
            remoteSnapshotStartPosition = transform.position;
            authoritativeVelocity = Vector2.zero;
            authoritativeFacingLeft = false;
            authoritativeVisualState = NetworkPlayerVisualState.Idle;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DontDestroyOnLoad(gameObject);
            remoteTargetPosition = authoritativePosition;
            remoteSnapshotStartPosition = transform.position;
            remoteVelocity = authoritativeVelocity;
            if (!isOwned && IsValidPosition(authoritativePosition))
                transform.position = authoritativePosition;

            ApplyPlayerColor(playerColor);
            EnsureNameLabel();
            ApplyDisplayName(displayName);
            EnsureCorePlayer(isOwned);
            NetworkChunkStreamingCoordinator.Register(transform);
            ItemNetworkStateSerialization.RuntimeStateChanged += OnRuntimeItemStateChanged;
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            EnsureCorePlayer(true);

            if (corePlayer != null && !localPlayerEnterNotified)
            {
                localPlayerEnterNotified = true;
                GameManager.Instance.NotifyNetworkLocalPlayerEntered(corePlayer);
            }

            CreateFollowCamera();
            Debug.Log($"[联机] 本地核心 Player Item 已生成：{displayName} / netId={netId}", this);
        }

        public override void OnStopClient()
        {
            ItemNetworkStateSerialization.RuntimeStateChanged -= OnRuntimeItemStateChanged;
            NetworkChunkStreamingCoordinator.Unregister(transform);

            if (manualCameraFollow && followCamera != null)
                Destroy(followCamera.gameObject);

            if (corePlayer != null && ItemMgr.Instance != null)
            {
                ItemMgr.Instance.ReleaseNetworkPlayer(corePlayer, persistData: isServer || NetworkServer.active);
                corePlayer = null;
                coreMover = null;
                coreBody = null;
                coreTurnBack = null;
                coreAnimator = null;
                coreAnimatorReceiver = null;
                remoteModuleStateHashes.Clear();
            }

            base.OnStopClient();
        }

        private void Update()
        {
            if (!NetworkClient.active)
                return;

            if (corePlayer == null && Time.unscaledTime >= nextCorePlayerRetryTime)
            {
                nextCorePlayerRetryTime = Time.unscaledTime + 1f;
                EnsureCorePlayer(isOwned);
            }

            if (isOwned)
            {
                UpdateOwnedMovement();
                UpdateOwnedItemState();
                return;
            }

            UpdateRemoteMovement();
        }

        private void UpdateOwnedMovement()
        {
            if (corePlayer != null)
            {
                Vector3 itemPosition = corePlayer.transform.position;
                if (IsValidPosition(itemPosition))
                    transform.position = itemPosition;
            }
            else
            {
                // Item 系统尚未就绪时保留轻量移动作为降级路径。
                Vector2 input = ReadMovementInput();
                Vector3 nextPosition = transform.position +
                                       new Vector3(input.x, input.y, 0f) * (movementSpeed * Time.deltaTime);
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
                CmdSubmitMotion(transform.position, velocity, facingLeft, visualState);
        }

        private void UpdateOwnedItemState()
        {
            if (corePlayer == null || !corePlayer.IsInitialized)
                return;

            itemStateSendTimer -= Time.deltaTime;
            if (!itemStateDirty && itemStateSendTimer > 0f)
                return;

            itemStateDirty = false;
            itemStateSendTimer = itemStateFallbackInterval;

            try
            {
                byte[] payload = ItemNetworkStateSerialization.Capture(corePlayer, true);
                if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                    return;

                uint hash = ItemNetworkStateSerialization.CalculateHash(payload);
                if (hash == lastSubmittedItemStateHash)
                    return;

                lastSubmittedItemStateHash = hash;
                if (isServer)
                    ApplyItemStateOnServer(payload);
                else
                    CmdSubmitItemState(payload);
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
            int animatorStateHash = 0;
            if (animator != null && animator.runtimeAnimatorController != null && animator.layerCount > 0)
            {
                AnimatorStateInfo stateInfo = animator.IsInTransition(0)
                    ? animator.GetNextAnimatorStateInfo(0)
                    : animator.GetCurrentAnimatorStateInfo(0);
                animatorStateHash = stateInfo.fullPathHash;
            }

            return new NetworkPlayerVisualState
            {
                IsMoving = velocity.sqrMagnitude > 0.001f,
                IsRunning = coreMover != null && coreMover.IsRunning,
                IsAttacking = coreAnimatorReceiver != null && coreAnimatorReceiver.IsAttacking,
                CanUseSkill = coreAnimatorReceiver != null && coreAnimatorReceiver.CanUseSkill,
                SkillId = coreAnimatorReceiver != null ? coreAnimatorReceiver.SkillId : 0,
                AnimatorStateHash = animatorStateHash
            };
        }

        [Command]
        private void CmdSubmitItemState(byte[] payload)
        {
            ApplyItemStateOnServer(payload);
        }

        [Server]
        private void ApplyItemStateOnServer(byte[] payload)
        {
            if (!ItemNetworkStateSerialization.IsValidPayload(payload))
                return;

            if (corePlayer != null && !isOwned)
                ApplyRemoteItemState(payload);

            authoritativeItemState = payload;
        }

        [Command]
        private void CmdSubmitMotion(
            Vector3 requestedPosition,
            Vector2 requestedVelocity,
            bool facingLeft,
            NetworkPlayerVisualState visualState)
        {
            ApplyMotionOnServer(requestedPosition, requestedVelocity, facingLeft, visualState);
        }

        [Server]
        private void ApplyMotionOnServer(
            Vector3 requestedPosition,
            Vector2 requestedVelocity,
            bool facingLeft,
            NetworkPlayerVisualState visualState)
        {
            if (!IsValidPosition(requestedPosition))
                return;

            requestedPosition.z = 0f;
            Vector3 acceptedPosition = IsValidPosition(authoritativePosition)
                ? authoritativePosition
                : transform.position;
            Vector3 delta = requestedPosition - acceptedPosition;
            float maxStep = Mathf.Max(0.5f, movementSpeed * 0.35f);
            if (delta.sqrMagnitude > maxStep * maxStep)
                requestedPosition = acceptedPosition + Vector3.ClampMagnitude(delta, maxStep);

            float maxVelocity = Mathf.Max(movementSpeed * 4f, remotePositionLerpSpeed);
            if (!IsValidVelocity(requestedVelocity))
                requestedVelocity = Vector2.zero;
            requestedVelocity = Vector2.ClampMagnitude(requestedVelocity, maxVelocity);

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
                transform.position = requestedPosition;
        }

        private void OnAuthoritativePositionChanged(Vector3 oldPosition, Vector3 newPosition)
        {
            if (!IsValidPosition(newPosition))
                return;

            BeginRemoteSnapshot(newPosition, authoritativeVelocity);
            float snapDistance = isOwned ? 4f : 10f;
            if ((transform.position - newPosition).sqrMagnitude > snapDistance * snapDistance)
            {
                transform.position = newPosition;
                remoteSnapshotStartPosition = newPosition;
                SyncCorePlayerPosition(newPosition);
            }
        }

        private void OnAuthoritativeVelocityChanged(Vector2 oldVelocity, Vector2 newVelocity)
        {
            remoteVelocity = IsValidVelocity(newVelocity) ? newVelocity : Vector2.zero;

            // 停止时可能没有新的位置脏数据，单独用速度 SyncVar 把外推位置拉回最终坐标。
            if (!isOwned && remoteVelocity.sqrMagnitude < 0.0001f)
                BeginRemoteSnapshot(remoteTargetPosition, Vector2.zero);
        }

        private void BeginRemoteSnapshot(Vector3 targetPosition, Vector2 velocity)
        {
            if (!IsValidPosition(targetPosition))
                return;

            float now = Time.unscaledTime;
            float observedInterval = lastRemoteSnapshotTime >= 0f
                ? now - lastRemoteSnapshotTime
                : networkSendInterval;
            lastRemoteSnapshotTime = now;

            remoteSnapshotStartPosition = transform.position;
            remoteTargetPosition = targetPosition;
            remoteVelocity = IsValidVelocity(velocity) ? velocity : Vector2.zero;
            remoteSnapshotElapsed = 0f;
            remoteSnapshotDuration = Mathf.Clamp(
                observedInterval,
                Mathf.Max(0.016f, networkSendInterval * 0.65f),
                Mathf.Max(0.05f, networkSendInterval * 2.5f));
        }

        private void UpdateRemoteMovement()
        {
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
            if (!isOwned)
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
            if (corePlayer == null)
                return;

            CacheAnimatorModules();

            Animator animator = coreAnimator?.animator;
            if (animator != null)
            {
                animator.enabled = true;
                animator.speed = 1f;
            }

            SetAnimatorBoolIfPresent(animator, AnimationText.Move, state.IsMoving);
            SetAnimatorBoolIfPresent(animator, AnimationText.Run, state.IsRunning);

            if (coreAnimatorReceiver != null)
            {
                coreAnimatorReceiver.SetNetworkPresentation(true, state.IsMoving);
                coreAnimatorReceiver.IsAttacking = state.IsAttacking;
                coreAnimatorReceiver.CanUseSkill = state.CanUseSkill;
                coreAnimatorReceiver.SkillId = state.SkillId;
            }

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
                transform.position = corePlayer.transform.position;

            if (manualCameraFollow)
            {
                Vector3 playerPosition = corePlayer != null ? corePlayer.transform.position : transform.position;
                followCamera.transform.position = new Vector3(playerPosition.x, playerPosition.y, -10f);
            }
        }

        private void EnsureCorePlayer(bool localControl)
        {
            if (ItemMgr.Instance == null || SaveDataMgr.Instance?.SaveData == null)
                return;

            try
            {
                int networkGuid = unchecked((int)(0x40000000u | (netId & 0x3fffffffu)));
                corePlayer = ItemMgr.Instance.LoadNetworkPlayer(displayName, networkGuid, transform.position, localControl);
                if (corePlayer == null)
                    return;

                if (localControl && !coreAvatarIsLocal)
                    ItemMgr.Instance.PromoteNetworkPlayerToLocal(corePlayer, transform.position);

                coreAvatarIsLocal |= localControl;
                if (!coreAvatarIsLocal)
                    BindRemoteVisualModules(corePlayer);

                coreMover = corePlayer.GetComponentInChildren<Mover>(true);
                coreBody = corePlayer.GetComponent<Rigidbody2D>();
                coreTurnBack = corePlayer.itemMods?.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
                coreAnimator = corePlayer.itemMods?.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);
                coreAnimatorReceiver = coreAnimator as Mod_AnimatorController_Receiver;
                if (!coreAvatarIsLocal && coreTurnBack != null)
                    coreTurnBack.faceMouse = null;

                GameController controller = corePlayer.GetComponentInChildren<GameController>(true);
                controller?.SetGameplayInputLocked(!coreAvatarIsLocal);
                if (coreMover != null)
                    coreMover.IsLock = !coreAvatarIsLocal;

                Mod_ChunkLoader chunkLoader = corePlayer.GetComponentInChildren<Mod_ChunkLoader>(true);
                chunkLoader?.SetExternalStreamingManaged(true);

                HideNetworkProxyRenderer();
                SyncCorePlayerPosition(transform.position);
                itemStateDirty = true;

                if (!isOwned && ItemNetworkStateSerialization.IsValidPayload(authoritativeItemState))
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
                    module._Data.Name,
                    module._Data.ID);
                if (networkData != null)
                    module._Data = networkData;

                if (string.IsNullOrWhiteSpace(module._Data.Name))
                    module._Data.Name = Module.GenerateUniqueModName(module._Data.ID);

                module.ModuleInit(player, module._Data, player.itemData);
                player.itemMods.AddMod(module);
            }

            Mod_TurnBack turnBack = player.itemMods.GetMod_ByID(ModText.TrunBody) as Mod_TurnBack;
            if (turnBack != null)
            {
                turnBack.Load();
                turnBack.faceMouse = null;
            }

            Mod_AnimatorController animator =
                player.itemMods.GetMod_ByID(ModText.AnimatorReceiver) as Mod_AnimatorController;
            animator?.Load();
        }

        private static ModuleData FindRemoteModuleData(
            System.Collections.Generic.Dictionary<string, ModuleData> states,
            string moduleName,
            string moduleId)
        {
            if (states == null)
                return null;

            if (!string.IsNullOrEmpty(moduleName) && states.TryGetValue(moduleName, out ModuleData exact))
                return exact;

            foreach (ModuleData state in states.Values)
            {
                if (state != null && string.Equals(state.ID, moduleId, System.StringComparison.Ordinal))
                    return state;
            }

            return null;
        }

        private void SyncCorePlayerPosition(Vector3 position)
        {
            if (corePlayer == null || !IsValidPosition(position))
                return;

            if (!coreAvatarIsLocal)
            {
                if (coreBody != null)
                {
                    coreBody.position = position;
                    coreBody.velocity = remoteVelocity;
                }
                else
                    corePlayer.transform.position = position;
            }

            if (corePlayer.Data != null)
                corePlayer.Data.transform.position = corePlayer.transform.position;
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

        private Vector2 ReadOwnedVelocity()
        {
            Vector3 currentPosition = corePlayer != null ? corePlayer.transform.position : transform.position;
            float now = Time.unscaledTime;
            Vector2 sampledVelocity = Vector2.zero;
            if (hasOwnedMotionSample)
            {
                float elapsed = now - lastOwnedMotionSampleTime;
                if (elapsed > 0.0001f)
                    sampledVelocity = (currentPosition - lastOwnedMotionSamplePosition) / elapsed;
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
                nameLabel.text = value;
        }

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
