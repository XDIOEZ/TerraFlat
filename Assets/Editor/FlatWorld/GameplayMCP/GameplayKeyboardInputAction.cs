using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// GamePlayMCP 的桌面键盘输入动作。
    /// 通过独立虚拟 Keyboard 进入 Unity Input System，使 UI 快捷键、交互键和其它正式 InputAction 监听者收到真实按下/松开语义。
    /// </summary>
    [GameplayMcpAction(
        "press_key",
        "Press one desktop keyboard key for a bounded duration. Prefer inputAction (B/E/H/P/ESC/OpenChat...) so current player rebinding is respected; key presses a physical keyboard control directly.")]
    internal sealed class GameplayMcpPressKeyAction : IGameplayMcpAction
    {
        private const float DefaultHoldSeconds = 0.05f;
        private const float MinHoldSeconds = 0.02f;
        private const float MaxHoldSeconds = 5f;
        private const float ReleaseSettleSeconds = 0.02f;
        private const string VirtualKeyboardName = "FlatWorldGamePlayMCPKeyboard";

        /// <summary>解析目标绑定并在专用虚拟键盘上完成一次有界短按。</summary>
        public async Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters)
        {
            string inputActionName = ReadString(parameters, "inputAction");
            string requestedKey = ReadString(parameters, "key");
            string keyboardPath;

            if (!string.IsNullOrEmpty(inputActionName))
            {
                if (!TryResolveKeyboardBinding(context.Controller, inputActionName, out keyboardPath, out string bindingError))
                {
                    return GameplayMcpRuntime.BuildActionError(
                        "keyboard_binding_missing",
                        bindingError,
                        false);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(requestedKey))
                {
                    return GameplayMcpRuntime.BuildActionError(
                        "missing_key",
                        "press_key 需要 inputAction 或 key；优先使用 inputAction 以跟随玩家改键。",
                        false);
                }

                keyboardPath = NormalizeKeyboardPath(requestedKey);
            }

            float holdSeconds = ReadHoldSeconds(parameters);
            Keyboard keyboard = null;
            try
            {
                keyboard = UnityInputSystem.AddDevice<Keyboard>(VirtualKeyboardName);
                UnityInputSystem.SetDeviceUsage(keyboard, GameController.ExternalGameplayInputDeviceUsage);

                if (!context.Controller.IsGameplayInputAllowed(keyboard))
                {
                    return GameplayMcpRuntime.BuildActionError(
                        "external_keyboard_rejected",
                        "GameController 未接受 GamePlayMCP 虚拟键盘；请重新获取外部控制租约。",
                        false);
                }

                if (!TryResolveKey(keyboard, keyboardPath, out Key key))
                {
                    return GameplayMcpRuntime.BuildActionError(
                        "keyboard_key_not_found",
                        $"无法把 '{keyboardPath}' 解析为可按下的 Keyboard Key。",
                        false);
                }

                UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                if (!await GameplayMcpRuntime.WaitEditorSecondsAsync(holdSeconds))
                {
                    return GameplayMcpRuntime.BuildActionError(
                        "play_mode_ended",
                        "按键保持期间 Play Mode 已结束。",
                        false);
                }

                UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState());
                if (!await GameplayMcpRuntime.WaitEditorSecondsAsync(ReleaseSettleSeconds))
                {
                    return GameplayMcpRuntime.BuildActionError(
                        "play_mode_ended",
                        "按键释放期间 Play Mode 已结束。",
                        false);
                }

                return GameplayMcpRuntime.BuildActionSuccess("press_key", context.Player, new JObject
                {
                    ["inputAction"] = string.IsNullOrEmpty(inputActionName)
                        ? JValue.CreateNull()
                        : new JValue(inputActionName),
                    ["key"] = key.ToString(),
                    ["bindingPath"] = keyboardPath,
                    ["seconds"] = Math.Round(holdSeconds, 3, MidpointRounding.AwayFromZero)
                });
            }
            finally
            {
                if (keyboard != null && keyboard.added)
                    UnityInputSystem.RemoveDevice(keyboard);
            }
        }

        /// <summary>从玩家当前 InputAction 的生效绑定中选择第一个直接 Keyboard 绑定。</summary>
        private static bool TryResolveKeyboardBinding(
            GameController controller,
            string inputActionName,
            out string keyboardPath,
            out string error)
        {
            keyboardPath = string.Empty;
            error = string.Empty;
            InputActionAsset inputAsset = controller?.InputAsset;
            if (inputAsset == null)
            {
                error = "玩家 InputActionAsset 尚未就绪。";
                return false;
            }

            InputAction inputAction = inputAsset.FindAction(inputActionName, false);
            if (inputAction == null)
            {
                error = $"找不到 InputAction '{inputActionName}'。";
                return false;
            }

            for (int i = 0; i < inputAction.bindings.Count; i++)
            {
                InputBinding binding = inputAction.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite)
                    continue;

                string effectivePath = binding.effectivePath;
                if (string.IsNullOrWhiteSpace(effectivePath) ||
                    !effectivePath.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                keyboardPath = effectivePath;
                return true;
            }

            error = $"InputAction '{inputActionName}' 没有可用的直接 Keyboard 绑定；可能已被清除、只绑定了手柄，或属于组合输入。";
            return false;
        }

        /// <summary>把绑定路径或用户输入的键名解析为 KeyboardState 可提交的 Key。</summary>
        private static bool TryResolveKey(Keyboard keyboard, string keyboardPath, out Key key)
        {
            key = Key.None;
            if (keyboard == null || string.IsNullOrWhiteSpace(keyboardPath))
                return false;

            string controlPath = keyboardPath.Trim();
            const string keyboardPrefix = "<Keyboard>/";
            if (controlPath.StartsWith(keyboardPrefix, StringComparison.OrdinalIgnoreCase))
                controlPath = controlPath.Substring(keyboardPrefix.Length);

            KeyControl keyControl = InputControlPath.TryFindControl<KeyControl>(keyboard, controlPath);
            if (keyControl != null && keyControl.keyCode != Key.None)
            {
                key = keyControl.keyCode;
                return true;
            }

            switch (controlPath.Trim().ToLowerInvariant())
            {
                case "shift":
                    key = Key.LeftShift;
                    return true;
                case "ctrl":
                case "control":
                    key = Key.LeftCtrl;
                    return true;
                case "alt":
                    key = Key.LeftAlt;
                    return true;
                case "meta":
                case "command":
                case "cmd":
                    key = Key.LeftMeta;
                    return true;
                case "esc":
                    key = Key.Escape;
                    return true;
                case "return":
                    key = Key.Enter;
                    return true;
            }

            return Enum.TryParse(controlPath, true, out key) && key != Key.None;
        }

        /// <summary>把简写键名统一转换为标准 Keyboard 控制路径。</summary>
        private static string NormalizeKeyboardPath(string requestedKey)
        {
            string value = requestedKey?.Trim() ?? string.Empty;
            return value.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)
                ? value
                : $"<Keyboard>/{value}";
        }

        /// <summary>读取 press_key 的有界按压时长。</summary>
        private static float ReadHoldSeconds(JObject parameters)
        {
            string raw = parameters?["seconds"]?.ToString();
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                seconds = DefaultHoldSeconds;
            return Mathf.Clamp(seconds, MinHoldSeconds, MaxHoldSeconds);
        }

        /// <summary>读取并清理字符串参数。</summary>
        private static string ReadString(JObject parameters, string key)
        {
            return parameters?[key]?.ToString()?.Trim() ?? string.Empty;
        }
    }
}
