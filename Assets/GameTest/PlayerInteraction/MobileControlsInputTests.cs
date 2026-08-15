using FlatWorld.Mobile;
using InputSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace FlatWorld.GameTest.PlayerInteraction
{
    /// <summary>手机虚拟设备的可复用覆盖；测试只注入自定义设备状态，不读取真实键鼠或真机触摸。</summary>
    public sealed class MobileControlsInputTests
    {
        [TearDown]
        public void TearDown()
        {
            MobileInputRuntime.ResetAll();
            for (int i = UnityInputSystem.devices.Count - 1; i >= 0; i--)
            {
                if (UnityInputSystem.devices[i] is FlatWorldMobileDevice mobileDevice)
                    UnityInputSystem.RemoveDevice(mobileDevice);
            }
        }

        [Test]
        public void MobileDevice_ExposesIndependentDirectionsAndButtons()
        {
            FlatWorldMobileDevice device = UnityInputSystem.AddDevice<FlatWorldMobileDevice>();
            UnityInputSystem.QueueStateEvent(device, new FlatWorldMobileState
            {
                move = new Vector2(0.5f, 1f),
                aim = Vector2.right,
                attackAim = Vector2.left,
                buttons = 1u << (int)MobileVirtualButton.Attack
            });
            UnityInputSystem.Update();

            Assert.That(device, Is.Not.InstanceOf<Gamepad>());
            Assert.That(device.move.ReadValue(), Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(device.aim.ReadValue(), Is.EqualTo(Vector2.right));
            Assert.That(device.attackAim.ReadValue(), Is.EqualTo(Vector2.left));
            Assert.That(device.attack.isPressed, Is.True);
            Assert.That(device.interact.isPressed, Is.False);
        }

        [Test]
        public void ResetAll_ClearsEveryTouchOwnedState()
        {
            MobileInputRuntime.EnsureDevice();
            MobileInputRuntime.SetMove(Vector2.up);
            MobileInputRuntime.SetAim(Vector2.right);
            MobileInputRuntime.SetAttackAim(Vector2.left);
            MobileInputRuntime.SetButton(MobileVirtualButton.Attack, true);
            MobileInputRuntime.SetButton(MobileVirtualButton.Use, true);

            MobileInputRuntime.ResetAll();
            FlatWorldMobileState state = MobileInputRuntime.State;

            Assert.That(state.move, Is.EqualTo(Vector2.zero));
            Assert.That(state.aim, Is.EqualTo(Vector2.zero));
            Assert.That(state.attackAim, Is.EqualTo(Vector2.zero));
            Assert.That(state.buttons, Is.Zero);
        }

        [Test]
        public void ZeroInput_DoesNotCreateMobileDeviceBeforeFirstTouch()
        {
            RemoveMobileDevices();
            MobileInputRuntime.ResetAll();
            Assert.That(CountMobileDevices(), Is.Zero);

            MobileInputRuntime.SetAim(Vector2.zero);
            MobileInputRuntime.SetAttackAim(Vector2.zero);
            MobileInputRuntime.SetButton(MobileVirtualButton.Attack, false);
            Assert.That(CountMobileDevices(), Is.Zero);

            MobileInputRuntime.SetAim(Vector2.right);
            Assert.That(CountMobileDevices(), Is.EqualTo(1));
        }

        /// <summary>验证键盘 Shift 作为奔跑修饰键时，不会屏蔽同一时刻的手机摇杆和虚拟按钮。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void KeyboardShift_DoesNotInterruptMobileJoystickOrButtons()
        {
            RemoveMobileDevices();
            Keyboard keyboard = UnityInputSystem.AddDevice<Keyboard>();
            FlatWorldMobileDevice mobileDevice = UnityInputSystem.AddDevice<FlatWorldMobileDevice>();
            PlayerInputActions actions = new PlayerInputActions();

            try
            {
                // 设备偏好不应给玩法 ActionMap 设置互斥遮罩。
                actions.Win10.Get().bindingMask = null;
                actions.Enable();

                UnityInputSystem.QueueStateEvent(mobileDevice, new FlatWorldMobileState
                {
                    move = Vector2.up,
                    aim = Vector2.right,
                    buttons = 1u << (int)MobileVirtualButton.Run
                });
                UnityInputSystem.Update();

                // 模拟器点击走 Touchscreen，不依赖编辑器原生 Mouse 是否被模拟器禁用。
                UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
                UnityInputSystem.Update();

                Assert.That(actions.Win10.Shift.IsPressed(), Is.True);
                Assert.That(actions.Win10.Move_Player.ReadValue<Vector2>(), Is.EqualTo(Vector2.up));
                Assert.That(actions.Win10.MobileAim_Player.ReadValue<Vector2>(), Is.EqualTo(Vector2.right));
                Assert.That(actions.Win10.ToggleRun.IsPressed(), Is.True);
            }
            finally
            {
                actions.Dispose();
                if (mobileDevice != null && mobileDevice.added)
                    UnityInputSystem.RemoveDevice(mobileDevice);
                if (keyboard != null && keyboard.added)
                    UnityInputSystem.RemoveDevice(keyboard);
            }
        }

        /// <summary>验证 Device Simulator 转发的触摸点击在 Shift 长按期间仍进入独立 UI 动作。</summary>
        [Test]
        [Category("PlayerInteraction.Input")]
        public void DeviceSimulatorTouchClick_ReachesUiWhileShiftIsHeld()
        {
            Keyboard keyboard = UnityInputSystem.AddDevice<Keyboard>();
            Touchscreen simulatorTouchscreen = UnityInputSystem.AddDevice<Touchscreen>();
            PlayerInputActions actions = new PlayerInputActions();
            InputAction clickAction = null;
            System.Action<InputAction.CallbackContext> onClick = null;
            int clickCount = 0;

            try
            {
                EventSystemGuard.SynchronizeUIInputBindings(actions.asset);
                InputActionMap uiMap = actions.asset.FindActionMap("FlatWorldUI", true);
                InputAction pointAction = uiMap.FindAction("MousePoint", true);
                clickAction = uiMap.FindAction("MouseLeftClick", true);
                onClick = _ => clickCount++;
                clickAction.performed += onClick;
                actions.Enable();

                UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
                UnityInputSystem.QueueStateEvent(simulatorTouchscreen, new TouchState
                {
                    touchId = 1,
                    phase = UnityEngine.InputSystem.TouchPhase.Began,
                    position = new Vector2(320f, 180f)
                });
                UnityInputSystem.Update();

                Assert.That(actions.Win10.Shift.IsPressed(), Is.True);
                Assert.That(pointAction.ReadValue<Vector2>(), Is.EqualTo(new Vector2(320f, 180f)));
                Assert.That(clickCount, Is.EqualTo(1));
            }
            finally
            {
                if (clickAction != null && onClick != null)
                    clickAction.performed -= onClick;
                actions.Dispose();
                if (simulatorTouchscreen != null && simulatorTouchscreen.added)
                    UnityInputSystem.RemoveDevice(simulatorTouchscreen);
                if (keyboard != null && keyboard.added)
                    UnityInputSystem.RemoveDevice(keyboard);
            }
        }

        private static void RemoveMobileDevices()
        {
            for (int i = UnityInputSystem.devices.Count - 1; i >= 0; i--)
            {
                if (UnityInputSystem.devices[i] is FlatWorldMobileDevice mobileDevice)
                    UnityInputSystem.RemoveDevice(mobileDevice);
            }
        }

        private static int CountMobileDevices()
        {
            int count = 0;
            for (int i = 0; i < UnityInputSystem.devices.Count; i++)
            {
                if (UnityInputSystem.devices[i] is FlatWorldMobileDevice)
                    count++;
            }

            return count;
        }
    }
}
