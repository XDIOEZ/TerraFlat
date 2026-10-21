using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

/// <summary>独立输入资产采样手柄 UI 停用恢复，不改变玩家输入绑定或设备偏好。</summary>
public static class GameplayTestGamepadInputLifecycleProbe
{
    #region 白名单登记与输入生命周期
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestSystemProbe.Register(
        "ui_settings", "gamepad_input_reenable", Capture);

    // 固定读取正式控制器的绑定与确认检测，避免为测试新增生产状态或复制输入判断。
    private static JObject Capture(JObject args)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(GamepadUIRuntimeController);
        FieldInfo assetField = type.GetField("inputAsset", flags);
        FieldInfo cancelField = type.GetField("cancelAction", flags);
        FieldInfo submitField = type.GetField("submitAction", flags);
        MethodInfo detectSubmit = type.GetMethod("WasGamepadSubmitPressedThisFrame", flags);
        if (assetField == null || cancelField == null || submitField == null || detectSubmit == null)
            throw new InvalidOperationException("手柄 UI 正式输入状态无法采样。");

        var root = new GameObject("[GamepadInputLifecycle]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        InputActionAsset first = CreateAsset("buttonNorth"), second = CreateAsset("buttonWest");
        Gamepad previousGamepad = Gamepad.current, gamepad = null;
        GamepadUIRuntimeController controller = null;
        try
        {
            controller = root.AddComponent<GamepadUIRuntimeController>();
            controller.Configure(first);
            bool deferredInitialBinding = cancelField.GetValue(controller) == null && submitField.GetValue(controller) == null;
            root.SetActive(true);
            if (!Application.isPlaying) controller.SendMessage("OnEnable", SendMessageOptions.RequireReceiver);
            gamepad = UnityInputSystem.AddDevice<Gamepad>();
            UnityInputSystem.AddDeviceUsage(gamepad, "FlatWorldUiLifecycle");
            first.Enable();
            second.Enable();

            bool Press(GamepadButton button)
            {
                UnityInputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
                UnityInputSystem.Update();
                bool detected = (bool)detectSubmit.Invoke(controller, null);
                UnityInputSystem.QueueStateEvent(gamepad, default(GamepadState));
                UnityInputSystem.Update();
                return detected;
            }

            JObject Snapshot(InputActionAsset expected) => new()
            {
                ["assetMatches"] = ReferenceEquals(assetField.GetValue(controller), expected),
                ["cancelMatches"] = ReferenceEquals(cancelField.GetValue(controller), expected["FlatWorldUI/Cancel"]),
                ["submitMatches"] = ReferenceEquals(submitField.GetValue(controller), expected["FlatWorldUI/Submit"])
            };

            JObject initial = Snapshot(first);
            initial["reboundRecognized"] = Press(GamepadButton.North);
            var rounds = new JArray();
            for (int i = 0; i < 3; i++)
            {
                SetEnabled(controller, false);
                bool disabledUnbound = cancelField.GetValue(controller) == null && submitField.GetValue(controller) == null;
                SetEnabled(controller, true);
                JObject round = Snapshot(first);
                round["disabledUnbound"] = disabledUnbound;
                round["reboundRecognized"] = Press(GamepadButton.North);
                round["defaultRejected"] = !Press(GamepadButton.South);
                rounds.Add(round);
            }

            SetEnabled(controller, false);
            controller.Configure(second);
            bool replacedWhileDisabledUnbound = cancelField.GetValue(controller) == null && submitField.GetValue(controller) == null;
            SetEnabled(controller, true);
            JObject replaced = Snapshot(second);
            replaced["newRecognized"] = Press(GamepadButton.West);
            replaced["oldRejected"] = !Press(GamepadButton.North);

            SetEnabled(controller, false);
            controller.Configure(null);
            SetEnabled(controller, true);
            bool withdrawn = assetField.GetValue(controller) == null &&
                cancelField.GetValue(controller) == null && submitField.GetValue(controller) == null;
            SetEnabled(controller, false);
            controller.Configure(first);
            UnityEngine.Object.DestroyImmediate(first);
            first = null;
            SetEnabled(controller, true);
            bool destroyedWithdrawn = ReferenceEquals(assetField.GetValue(controller), null) &&
                cancelField.GetValue(controller) == null && submitField.GetValue(controller) == null;
            return new JObject
            {
                ["deferredInitialBinding"] = deferredInitialBinding, ["initial"] = initial, ["rounds"] = rounds,
                ["replacedWhileDisabledUnbound"] = replacedWhileDisabledUnbound, ["replaced"] = replaced,
                ["withdrawn"] = withdrawn, ["destroyedWithdrawn"] = destroyedWithdrawn
            };
        }
        finally
        {
            if (controller != null) SetEnabled(controller, false);
            UnityEngine.Object.DestroyImmediate(root);
            if (first != null) { first.Disable(); UnityEngine.Object.DestroyImmediate(first); }
            if (second != null) { second.Disable(); UnityEngine.Object.DestroyImmediate(second); }
            if (gamepad != null) UnityInputSystem.RemoveDevice(gamepad);
            if (previousGamepad != null && previousGamepad.added) previousGamepad.MakeCurrent();
        }
    }

    private static void SetEnabled(GamepadUIRuntimeController controller, bool enabled)
    {
        controller.enabled = enabled;
        // 编辑态规则不由 PlayerLoop 分发生命周期，直接调用相同正式回调补足采样。
        if (!Application.isPlaying)
            controller.SendMessage(enabled ? "OnEnable" : "OnDisable", SendMessageOptions.RequireReceiver);
    }

    private static InputActionAsset CreateAsset(string button)
    {
        InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
        var map = new InputActionMap("FlatWorldUI");
        map.AddAction("Cancel", InputActionType.Button, "<Keyboard>/f10");
        map.AddAction("Submit", InputActionType.Button, "<Gamepad>{FlatWorldUiLifecycle}/" + button);
        asset.AddActionMap(map);
        return asset;
    }
    #endregion
}
