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
        [SerializeField, Min(0.01f)] private float networkSendInterval = 0.05f;
        [SerializeField, Min(0.1f)] private float remotePositionLerpSpeed = 18f;

        [SyncVar(hook = nameof(OnDisplayNameChanged))]
        private string displayName = "玩家";

        [SyncVar(hook = nameof(OnPlayerColorChanged))]
        private Color playerColor = Color.white;

        [SyncVar(hook = nameof(OnAuthoritativePositionChanged))]
        private Vector3 authoritativePosition;

        private Camera followCamera;
        private Renderer cachedRenderer;
        private TextMeshPro nameLabel;
        private Vector3 remoteTargetPosition;
        private float networkSendTimer;

        public string DisplayName => displayName;

        [Server]
        public void InitializeOnServer(string playerName, int playerIndex)
        {
            displayName = playerName;
            int colorIndex = Mathf.Abs(playerIndex) % PlayerColors.Length;
            playerColor = PlayerColors[colorIndex];
            authoritativePosition = transform.position;
            remoteTargetPosition = transform.position;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DontDestroyOnLoad(gameObject);
            remoteTargetPosition = authoritativePosition;
            if (!isOwned && IsValidPosition(authoritativePosition))
                transform.position = authoritativePosition;

            ApplyPlayerColor(playerColor);
            EnsureNameLabel();
            ApplyDisplayName(displayName);
            NetworkChunkStreamingCoordinator.Register(transform);
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            if (ItemMgr.Instance != null)
                ItemMgr.Instance.RegisterExternalPlayerTransform(transform);

            CreateFollowCamera();
            Debug.Log($"[联机] 本地玩家已生成：{displayName} / netId={netId}", this);
        }

        public override void OnStopClient()
        {
            NetworkChunkStreamingCoordinator.Unregister(transform);
            if (isOwned && ItemMgr.Instance != null)
                ItemMgr.Instance.UnregisterExternalPlayerTransform(transform);

            if (followCamera != null)
                Destroy(followCamera.gameObject);

            base.OnStopClient();
        }

        private void Update()
        {
            if (!NetworkClient.active)
                return;

            if (isOwned)
            {
                UpdateOwnedMovement();
                return;
            }

            // 服务端直接使用 Cmd 校验后的坐标；纯客户端平滑显示远端玩家。
            if (!isServer && IsValidPosition(remoteTargetPosition))
            {
                float lerpFactor = 1f - Mathf.Exp(-remotePositionLerpSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, remoteTargetPosition, lerpFactor);
            }
        }

        private void UpdateOwnedMovement()
        {
            Vector2 input = ReadMovementInput();
            Vector3 nextPosition = transform.position +
                                   new Vector3(input.x, input.y, 0f) * (movementSpeed * Time.deltaTime);
            if (IsValidPosition(nextPosition))
                transform.position = nextPosition;

            networkSendTimer -= Time.deltaTime;
            if (networkSendTimer > 0f)
                return;

            networkSendTimer = networkSendInterval;
            if (isServer)
                ApplyPositionOnServer(transform.position);
            else
                CmdSubmitPosition(transform.position);
        }

        [Command]
        private void CmdSubmitPosition(Vector3 requestedPosition)
        {
            ApplyPositionOnServer(requestedPosition);
        }

        [Server]
        private void ApplyPositionOnServer(Vector3 requestedPosition)
        {
            if (!IsValidPosition(requestedPosition))
                return;

            requestedPosition.z = 0f;
            Vector3 delta = requestedPosition - transform.position;
            float maxStep = Mathf.Max(0.5f, movementSpeed * 0.35f);
            if (delta.sqrMagnitude > maxStep * maxStep)
                requestedPosition = transform.position + Vector3.ClampMagnitude(delta, maxStep);

            transform.position = requestedPosition;
            authoritativePosition = requestedPosition;
            remoteTargetPosition = requestedPosition;
        }

        private void OnAuthoritativePositionChanged(Vector3 oldPosition, Vector3 newPosition)
        {
            if (!IsValidPosition(newPosition))
                return;

            remoteTargetPosition = newPosition;
            float snapDistance = isOwned ? 4f : 10f;
            if ((transform.position - newPosition).sqrMagnitude > snapDistance * snapDistance)
                transform.position = newPosition;
        }

        private void LateUpdate()
        {
            if (!isOwned || followCamera == null)
                return;

            Vector3 playerPosition = transform.position;
            followCamera.transform.position = new Vector3(playerPosition.x, playerPosition.y, -10f);
        }

        private static bool IsValidPosition(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   !float.IsNaN(position.z) && !float.IsInfinity(position.z) &&
                   Mathf.Abs(position.x) <= MaxSupportedWorldCoordinate &&
                   Mathf.Abs(position.y) <= MaxSupportedWorldCoordinate;
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

            GameObject labelObject = new GameObject("玩家名称");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            nameLabel = labelObject.AddComponent<TextMeshPro>();
            nameLabel.alignment = TextAlignmentOptions.Center;
            nameLabel.fontSize = 2.4f;
            nameLabel.color = Color.white;
            nameLabel.sortingOrder = 100;

            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < fonts.Length; i++)
            {
                if (fonts[i] != null && fonts[i].name.Contains("fusion-pixel"))
                {
                    nameLabel.font = fonts[i];
                    break;
                }
            }
        }

        private void OnDisplayNameChanged(string oldName, string newName) => ApplyDisplayName(newName);

        private void ApplyDisplayName(string value)
        {
            EnsureNameLabel();
            nameLabel.text = value;
        }

        private void OnPlayerColorChanged(Color oldColor, Color newColor) => ApplyPlayerColor(newColor);

        private void ApplyPlayerColor(Color value)
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponentInChildren<Renderer>();

            if (cachedRenderer != null)
                cachedRenderer.material.color = value;
        }
    }
}
