using FlatWorld.Mobile;
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
    }
}
