#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

/// <summary>不添加绑定和特殊 Usage，通过原始设备事件采样移动与异步翻滚。</summary>
public static class GameplayTestDeviceMovementRuntimeProbe
{
    #region 正式设备链路采样
    private const float SimulationBudgetSeconds = 3f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "player", "device_movement_roll", Capture, requiresDeviceInput: true);

    private static IEnumerator AdvanceSimulation(float seconds)
    {
        double start = GameplayFixedStepSimulation.CurrentTime;
        float deadline = Time.realtimeSinceStartup + seconds + SimulationBudgetSeconds;
        while (GameplayFixedStepSimulation.CurrentTime - start < seconds)
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("输入现场模拟时钟没有在预算内推进。");
            yield return null;
        }
    }

    private static IEnumerator Capture(Player player, JObject args, Action<JObject> completed)
    {
        using var scope = new DeviceScope(player, (string)args["device"] == "gamepad");
        string fault = (string)args["fault"] ?? "none";
        if (fault != "none" && fault != "drop_input" && fault != "wrong_speed" && fault != "double_charge")
            throw new ArgumentException("未知的隔离故障。");
        float expectedSpeed = scope.OriginalSpeed;
        if (fault == "wrong_speed") scope.Mover.Speed = new GameValue_float(scope.OriginalSpeed * 2f);
        Vector2 start = scope.Mover.rb.position;
        scope.SendMove(fault != "drop_input");
        float actionValue = scope.Move.ReadValue<Vector2>().magnitude;
        string actionDevice = scope.Move.activeControl?.device is Keyboard ? "keyboard" :
            scope.Move.activeControl?.device is Gamepad ? "gamepad" : "none";
        yield return AdvanceSimulation(scope.Mover.speedTransitionDuration + .35f);
        float speed = scope.Mover.rb.velocity.magnitude;
        float distance = Vector2.Distance(start, scope.Mover.rb.position);
        scope.SendMove(false);
        yield return AdvanceSimulation(scope.Mover.speedTransitionDuration + .15f);
        float stoppedSpeed = scope.Mover.rb.velocity.magnitude;
        scope.Controller.AcquireGameplayInputLock(scope);
        start = scope.Mover.rb.position;
        scope.SendMove(true);
        yield return AdvanceSimulation(.2f);
        float lockedDistance = Vector2.Distance(start, scope.Mover.rb.position);
        scope.SendMove(false);
        scope.Controller.ReleaseGameplayInputLock(scope);
        scope.Mover.Speed = scope.OriginalSpeed;
        scope.Mover.ResetMotion();

        bool focused = true;
        float focusedDistance = 0;
        if (!scope.IsGamepad)
        {
            scope.FocusText();
            yield return null;
            focused = EventSystemGuard.IsTextInputFocused;
            start = scope.Mover.rb.position;
            scope.SendMove(true);
            yield return AdvanceSimulation(.2f);
            focusedDistance = Vector2.Distance(start, scope.Mover.rb.position);
            scope.SendMove(false);
            scope.ClearTextFocus();
        }
        yield return AdvanceSimulation(.1f);
        scope.Mover.ResetMotion();
        float charged = 0;
        int charges = 0;
        void ObserveCharge(StaminaConsumptionRecord record)
        {
            if (record.SourceId != Mod_Roll.StaminaSourceId) return;
            charged += record.ConsumedAmount;
            charges++;
        }
        scope.Stamina.CurrentValue = scope.Stamina.MaxValue;
        scope.Stamina.StaminaConsumed += ObserveCharge;
        bool rolling;
        float rollDistance;
        bool completedInBudget;
        try
        {
            start = scope.Mover.rb.position;
            scope.SendRoll(true);
            rolling = scope.Roll.IsRolling;
            if (fault == "double_charge") scope.Stamina.TryConsumeStamina(Mod_Roll.StaminaSourceId, scope.Roll.StaminaCost);
            scope.SendRoll(false);
            float deadline = Time.realtimeSinceStartup + scope.Roll.Duration + SimulationBudgetSeconds;
            while (scope.Roll.IsRolling && Time.realtimeSinceStartup < deadline) yield return null;
            completedInBudget = !scope.Roll.IsRolling;
            rollDistance = Vector2.Distance(start, scope.Mover.rb.position);
        }
        finally { scope.Stamina.StaminaConsumed -= ObserveCharge; }
        completed(new JObject
        {
            ["coverage"] = "device-event/action/arbitration/controller/movement/roll",
            ["device"] = scope.IsGamepad ? "gamepad" : "keyboard", ["fault"] = fault,
            ["actionDevice"] = actionDevice,
            ["actionValue"] = actionValue, ["distance"] = distance, ["speed"] = speed,
            ["expectedSpeed"] = expectedSpeed, ["stoppedSpeed"] = stoppedSpeed,
            ["lockedDistance"] = lockedDistance, ["textFocused"] = focused, ["focusedDistance"] = focusedDistance,
            ["rollStarted"] = rolling, ["rollCompleted"] = completedInBudget, ["rollDistance"] = rollDistance,
            ["charged"] = charged, ["charges"] = charges, ["expectedCost"] = scope.Roll.StaminaCost
        });
    }
    #endregion

    #region 隔离设备与状态恢复
    private sealed class DeviceScope : IDisposable
    {
        public readonly Mod_GameController Controller;
        public readonly Mod_Mover Mover;
        public readonly Mod_Roll Roll;
        public readonly Mod_Stamina Stamina;
        public readonly InputAction Move;
        public readonly GameValue_float OriginalSpeed;
        public readonly bool IsGamepad;
        private readonly Keyboard keyboard;
        private readonly Gamepad gamepad;
        private readonly InputAction rollAction;
        private readonly ReadOnlyArray<InputDevice>? devices;
        private readonly Mod_GameController.InputDeviceType preference;
        private readonly bool preferenceExisted;
        private readonly int preferenceValue;
        private readonly float staminaValue;
        private readonly bool running;
        private readonly Vector2 position;
        private readonly GameObject selection;
        private GameObject textRoot;
        private InputField textField;
        private readonly Key moveKey;
        private readonly Key rollKey;
        private readonly Player player;

        public DeviceScope(Player player, bool isGamepad)
        {
            this.player = player;
            Controller = player.GetComponentInChildren<Mod_GameController>(true);
            Mover = player.GetComponentInChildren<Mod_Mover>(true);
            Roll = player.GetComponentInChildren<Mod_Roll>(true);
            Stamina = player.GetComponentInChildren<Mod_Stamina>(true);
            if (Controller == null || Mover == null || Roll == null || Stamina == null || Controller.HasExternalGameplayControl)
                throw new InvalidOperationException("正式设备现场缺模块或仍被 Agent 接管。");
            IsGamepad = isGamepad;
            Move = Controller._inputActions.Win10.Move_Player;
            rollAction = Controller._inputActions.Win10.Roll;
            devices = Controller.InputAsset.devices;
            OriginalSpeed = Mover.Speed;
            running = Mover.IsRunning;
            staminaValue = Stamina.CurrentValue;
            position = Mover.rb.position;
            preference = Controller.PreferredInputDevice;
            preferenceExisted = PlayerPrefs.HasKey("FlatWorld.Input.PreferredDevice");
            preferenceValue = PlayerPrefs.GetInt("FlatWorld.Input.PreferredDevice");
            selection = EventSystem.current?.currentSelectedGameObject;
            GameplayTestRuntimeProbe.RegisterCleanup(this);
            keyboard = UnityInputSystem.AddDevice<Keyboard>();
            gamepad = UnityInputSystem.AddDevice<Gamepad>();
            Controller.InputAsset.devices = new InputDevice[] { keyboard, gamepad };
            Controller.SetPreferredInputDevice(isGamepad ? Mod_GameController.InputDeviceType.Gamepad : Mod_GameController.InputDeviceType.KeyboardMouse);
            // 从正式有效绑定解析物理键，不添加为测试定制的绑定。
            foreach (var binding in Move.bindings)
                if (binding.name == "right" && InputControlPath.TryFindControl(keyboard, binding.effectivePath) is KeyControl key) { moveKey = key.keyCode; break; }
            foreach (var control in rollAction.controls)
                if (control.device == keyboard && control is KeyControl key) { rollKey = key.keyCode; break; }
            if (moveKey == Key.None || rollKey == Key.None) throw new InvalidOperationException("当前键盘配置缺少移动右向或翻滚键，属于配置前置条件失败。");
            Mover.SetRunState(false);
            Mover.ResetMotion();
        }

        public void SendMove(bool pressed)
        {
            if (IsGamepad) UnityInputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = pressed ? Vector2.right : Vector2.zero });
            else UnityInputSystem.QueueStateEvent(keyboard, pressed ? new KeyboardState(moveKey) : default);
            UnityInputSystem.Update();
        }
        public void SendRoll(bool pressed)
        {
            // 正式配置没有手柄 Roll 绑定；翻滚明确验证键盘路径，不伪造手柄覆盖。
            Controller.SetPreferredInputDevice(Mod_GameController.InputDeviceType.KeyboardMouse);
            UnityInputSystem.QueueStateEvent(keyboard, pressed ? new KeyboardState(rollKey) : default);
            UnityInputSystem.Update();
        }
        public void FocusText()
        {
            textRoot = new GameObject("Input focus fixture", typeof(Canvas));
            textRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var field = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            field.transform.SetParent(textRoot.transform, false);
            var rect = (RectTransform)field.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .85f);
            rect.sizeDelta = new Vector2(180f, 32f);
            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(field.transform, false);
            var text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textField = field.GetComponent<InputField>();
            textField.textComponent = text;
            EventSystem.current.SetSelectedGameObject(field);
            textField.ActivateInputField();
        }
        public void ClearTextFocus()
        {
            if (textField != null) textField.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(selection);
        }
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // 部分构造失败和单项收尾异常也必须归还其余独立状态。
            var errors = new List<Exception>();
            void Restore(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            if ((IsGamepad && gamepad != null) || (!IsGamepad && keyboard != null)) Restore(() => SendMove(false));
            if (keyboard != null) Restore(() => SendRoll(false));
            Restore(ClearTextFocus);
            if (textRoot != null) Restore(() => UnityEngine.Object.Destroy(textRoot));
            Restore(() => Controller.ReleaseGameplayInputLock(this));
            Restore(() => Roll.CancelRoll());
            Restore(() => Controller.InputAsset.devices = devices);
            if (keyboard != null) Restore(() => UnityInputSystem.RemoveDevice(keyboard));
            if (gamepad != null) Restore(() => UnityInputSystem.RemoveDevice(gamepad));
            Restore(() => Controller.SetPreferredInputDevice(preference));
            Restore(() => { if (preferenceExisted) PlayerPrefs.SetInt("FlatWorld.Input.PreferredDevice", preferenceValue); else PlayerPrefs.DeleteKey("FlatWorld.Input.PreferredDevice"); PlayerPrefs.Save(); });
            Restore(() => { Mover.Speed = OriginalSpeed; Mover.SetRunState(running); Stamina.CurrentValue = staminaValue; Mover.ResetMotion(); });
            Restore(() => PlayerTeleportService.ApplySurfacePose(player, position));
            if (errors.Count > 0) throw new AggregateException("设备输入夹具恢复失败。", errors);
        }
    }
    #endregion
}
#endif
