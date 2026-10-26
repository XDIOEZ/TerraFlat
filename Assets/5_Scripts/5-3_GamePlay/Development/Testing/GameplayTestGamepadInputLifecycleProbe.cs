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
    private static readonly MethodInfo EditorEnableCallback = typeof(GamepadUIRuntimeController)
        .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo EditorDisableCallback = typeof(GamepadUIRuntimeController)
        .GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
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
#if UNITY_EDITOR
        const string playerUpdatesFlag = "RUN_PLAYER_UPDATES_IN_EDIT_MODE";
        InputSettings settings = UnityInputSystem.settings;
        bool previousPlayerUpdates = (bool)typeof(InputSettings).GetMethod("IsFeatureEnabled", flags)
            .Invoke(settings, new object[] { playerUpdatesFlag });
#endif
        try
        {
#if UNITY_EDITOR
            // 编辑态使用玩家输入步计数，避免 Editor 更新结束后恢复计数造成按键漏判。
            if (!Application.isPlaying) settings.SetInternalFeatureFlag(playerUpdatesFlag, true);
#endif
            controller = root.AddComponent<GamepadUIRuntimeController>();
            controller.Configure(first);
            bool deferredInitialBinding = cancelField.GetValue(controller) == null && submitField.GetValue(controller) == null;
            root.SetActive(true);
            if (!Application.isPlaying) EditorEnableCallback.Invoke(controller, null);
            gamepad = UnityInputSystem.AddDevice<Gamepad>();
            UnityInputSystem.AddDeviceUsage(gamepad, "FlatWorldUiLifecycle");
            first.Enable();
            second.Enable();
            var presses = new JArray();

            bool Press(GamepadButton button)
            {
                UnityInputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
                UnityInputSystem.Update();
                bool detected = (bool)detectSubmit.Invoke(controller, null);
                var boundSubmit = (InputAction)submitField.GetValue(controller);
                presses.Add(new JObject { ["button"] = button.ToString(), ["value"] = gamepad[button].ReadValue(),
                    ["pressed"] = gamepad[button].wasPressedThisFrame, ["deviceUpdated"] = gamepad.wasUpdatedThisFrame,
                    ["controls"] = boundSubmit?.controls.Count ?? 0, ["detected"] = detected });
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
                ["withdrawn"] = withdrawn, ["destroyedWithdrawn"] = destroyedWithdrawn, ["presses"] = presses
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
#if UNITY_EDITOR
            if (!Application.isPlaying) settings.SetInternalFeatureFlag(playerUpdatesFlag, previousPlayerUpdates);
#endif
        }
    }

    private static void SetEnabled(GamepadUIRuntimeController controller, bool enabled)
    {
        controller.enabled = enabled;
        // 编辑态直接调用正式回调，避免 SendMessage 触发 Unity 运行态行为断言。
        if (!Application.isPlaying)
            (enabled ? EditorEnableCallback : EditorDisableCallback).Invoke(controller, null);
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
