using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlatWorld.Networking.Testing
{
    [DisallowMultipleComponent]
    public sealed class NetworkTestPlayer : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float movementSpeed = 4f;

        [SyncVar(hook = nameof(OnColorChanged))]
        private Color playerColor = Color.white;

        private Renderer cachedRenderer;
        private float autoMovePhase;

        public override void OnStartServer()
        {
            base.OnStartServer();
            float hue = (netId * 0.217f) % 1f;
            playerColor = Color.HSVToRGB(hue, 0.75f, 1f);
            Debug.Log($"[NET_TEST] Player spawned on server netId={netId} owner={connectionToClient?.connectionId}");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyColor(playerColor);
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            autoMovePhase = netId * 0.73f;
            Debug.Log($"[NET_TEST] Local player ready netId={netId} autoMove={NetworkTestRuntime.AutoMove}");
        }

        private void Update()
        {
            if (!isOwned)
                return;

            Vector2 input = ReadInput();
            if (NetworkTestRuntime.AutoMove)
            {
                autoMovePhase += Time.deltaTime;
                input = new Vector2(Mathf.Cos(autoMovePhase), Mathf.Sin(autoMovePhase));
            }

            Vector3 delta = new Vector3(input.x, input.y, 0f) * (movementSpeed * Time.deltaTime);
            transform.position += delta;
        }

        private Vector2 ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return Vector2.zero;

            float horizontal = 0f;
            float vertical = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                horizontal -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                horizontal += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                vertical -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                vertical += 1f;

            return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        }

        private void OnColorChanged(Color oldColor, Color newColor)
        {
            ApplyColor(newColor);
        }

        private void ApplyColor(Color color)
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponentInChildren<Renderer>();

            if (cachedRenderer != null)
                cachedRenderer.material.color = color;
        }
    }
}
