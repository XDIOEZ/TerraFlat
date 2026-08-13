using System;
using FlatWorld.Mobile;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace FlatWorld.Automation
{
    /// <summary>手机输入黄金路径：验证真实自定义设备、方向保留、攻击语义分流与输入锁清理。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 状态

        private static GameController mobileControlsController;
        private static int mobileAttackStartedCount;
        private static int mobileAttackEndedCount;
        private static int mobileLeftClickCount;
        private static bool mobileControlsCompleted;

        #endregion

        #region 场景阶段

        private static void ResetMobileControlsScenario()
        {
            MobileInputRuntime.ResetAll();
            mobileControlsController = null;
            mobileAttackStartedCount = 0;
            mobileAttackEndedCount = 0;
            mobileLeftClickCount = 0;
            mobileControlsCompleted = false;
        }

        private static void RunMobileControlsScenario(FlatWorldGoldenPathScenarioContext context)
        {
            mobileControlsController = context.Player != null
                ? context.Player.GetComponentInChildren<GameController>(true)
                : null;
            if (mobileControlsController == null)
                throw new InvalidOperationException("MobileControls: 真实玩家缺少 GameController。");

            mobileControlsController.AttackStarted += RecordMobileAttackStarted;
            mobileControlsController.AttackEnded += RecordMobileAttackEnded;
            mobileControlsController.LeftClick += RecordMobileLeftClick;
            FlatWorldMobileDevice device = MobileInputRuntime.EnsureDevice();
            if (device == null || !device.added || device.layout == "Gamepad")
                throw new InvalidOperationException("MobileControls: 自定义手机设备未创建或错误伪装为 Gamepad。");

            // 同时保持移动与普通指向，并在攻击死区内按下：攻击必须立即开始且不能合成交互/LeftClick。
            MobileInputRuntime.SetMove(new Vector2(0.75f, 0.25f));
            MobileInputRuntime.SetAim(Vector2.up);
            MobileInputRuntime.SetButton(MobileVirtualButton.Attack, true);
            UnityInputSystem.Update();

            if (mobileControlsController.CurrentInputDevice != GameController.InputDeviceType.Mobile)
                throw new InvalidOperationException("MobileControls: 输入源没有切换到 Mobile。");
            if (mobileAttackStartedCount != 1 || mobileLeftClickCount != 0)
                throw new InvalidOperationException("MobileControls: 攻击未开始一次，或错误触发了交互 LeftClick。");

            MobileInputRuntime.SetAttackAim(Vector2.left);
            UnityInputSystem.Update();
            MobileInputRuntime.SetButton(MobileVirtualButton.Attack, false);
            UnityInputSystem.Update();
            if (mobileAttackEndedCount != 1)
                throw new InvalidOperationException("MobileControls: 攻击松开没有可靠结束。");

            // 输入锁必须清零移动、方向按钮与攻击所有权。
            mobileControlsController.SetGameplayInputLocked(true);
            UnityInputSystem.Update();
            FlatWorldMobileState state = MobileInputRuntime.State;
            if (state.move != Vector2.zero || state.attackAim != Vector2.zero || state.buttons != 0u)
                throw new InvalidOperationException("MobileControls: 输入锁后仍残留移动或攻击状态。");
            mobileControlsController.SetGameplayInputLocked(false);

            mobileControlsCompleted = true;
        }

        private static void AssertMobileControlsScenarioCompleted()
        {
            if (!mobileControlsCompleted)
                throw new InvalidOperationException("MobileControls: 场景未完成。");
        }

        private static void CleanupMobileControlsScenario()
        {
            if (mobileControlsController != null)
            {
                mobileControlsController.AttackStarted -= RecordMobileAttackStarted;
                mobileControlsController.AttackEnded -= RecordMobileAttackEnded;
                mobileControlsController.LeftClick -= RecordMobileLeftClick;
                mobileControlsController.SetGameplayInputLocked(false);
                mobileControlsController.CancelActiveAttackAndMobileInput();
            }

            ResetMobileControlsScenario();
        }

        private static void RecordMobileAttackStarted() => mobileAttackStartedCount++;
        private static void RecordMobileAttackEnded() => mobileAttackEndedCount++;
        private static void RecordMobileLeftClick() => mobileLeftClickCount++;

        #endregion
    }
}
