using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace FlatWorld.Mobile
{
    /// <summary>
    /// FlatWorld 手机触控层写入的独立虚拟设备状态。它不继承 Gamepad，因此不会误启用手柄焦点导航或手柄虚拟光标。
    /// 三组方向量允许移动、普通指向与攻击指向在同一帧由不同手指独立更新；按钮位只负责复用现有玩法 Action。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct FlatWorldMobileState : IInputStateTypeInfo
    {
        public static FourCC Format => new FourCC('F', 'W', 'M', 'B');

        [InputControl(name = "move", layout = "Vector2", displayName = "移动摇杆")]
        [FieldOffset(0)]
        public Vector2 move;

        [InputControl(name = "aim", layout = "Vector2", displayName = "普通指向")]
        [FieldOffset(8)]
        public Vector2 aim;

        [InputControl(name = "attackAim", layout = "Vector2", displayName = "攻击指向")]
        [FieldOffset(16)]
        public Vector2 attackAim;

        [InputControl(name = "attack", layout = "Button", bit = 0, displayName = "攻击")]
        [InputControl(name = "interact", layout = "Button", bit = 1, displayName = "交互")]
        [InputControl(name = "use", layout = "Button", bit = 2, displayName = "使用")]
        [InputControl(name = "run", layout = "Button", bit = 3, displayName = "奔跑")]
        [InputControl(name = "inventory", layout = "Button", bit = 4, displayName = "背包")]
        [InputControl(name = "equipment", layout = "Button", bit = 5, displayName = "装备")]
        [InputControl(name = "crafting", layout = "Button", bit = 6, displayName = "制作")]
        [InputControl(name = "survival", layout = "Button", bit = 7, displayName = "生存状态")]
        [InputControl(name = "settings", layout = "Button", bit = 8, displayName = "设置与返回")]
        [FieldOffset(24)]
        public uint buttons;

        public FourCC format => Format;
    }

    /// <summary>
    /// Input System 可绑定的手机虚拟设备。设备生命周期由 MobileInputRuntime 统一管理，HUD 不直接伪造键盘、鼠标或手柄事件。
    /// </summary>
    [InputControlLayout(stateType = typeof(FlatWorldMobileState), displayName = "FlatWorld Mobile Device")]
    public sealed class FlatWorldMobileDevice : InputDevice
    {
        #region 控件属性

        public Vector2Control move { get; private set; }
        public Vector2Control aim { get; private set; }
        public Vector2Control attackAim { get; private set; }
        public ButtonControl attack { get; private set; }
        public ButtonControl interact { get; private set; }
        public ButtonControl use { get; private set; }
        public ButtonControl run { get; private set; }
        public ButtonControl inventory { get; private set; }
        public ButtonControl equipment { get; private set; }
        public ButtonControl crafting { get; private set; }
        public ButtonControl survival { get; private set; }
        public ButtonControl settings { get; private set; }

        public static FlatWorldMobileDevice current { get; private set; }

        #endregion

        #region 注册与生命周期

        static FlatWorldMobileDevice()
        {
            RegisterLayout();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterLayout()
        {
            UnityInputSystem.RegisterLayout<FlatWorldMobileDevice>();
        }

        protected override void FinishSetup()
        {
            base.FinishSetup();
            move = GetChildControl<Vector2Control>(nameof(move));
            aim = GetChildControl<Vector2Control>(nameof(aim));
            attackAim = GetChildControl<Vector2Control>(nameof(attackAim));
            attack = GetChildControl<ButtonControl>(nameof(attack));
            interact = GetChildControl<ButtonControl>(nameof(interact));
            use = GetChildControl<ButtonControl>(nameof(use));
            run = GetChildControl<ButtonControl>(nameof(run));
            inventory = GetChildControl<ButtonControl>(nameof(inventory));
            equipment = GetChildControl<ButtonControl>(nameof(equipment));
            crafting = GetChildControl<ButtonControl>(nameof(crafting));
            survival = GetChildControl<ButtonControl>(nameof(survival));
            settings = GetChildControl<ButtonControl>(nameof(settings));
        }

        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        protected override void OnRemoved()
        {
            if (current == this)
                current = null;
            base.OnRemoved();
        }

        #endregion
    }

    /// <summary>手机虚拟按钮位定义，供正式 HUD 以类型安全的方式写入设备。</summary>
    public enum MobileVirtualButton
    {
        Attack = 0,
        Interact = 1,
        Use = 2,
        Run = 3,
        Inventory = 4,
        Equipment = 5,
        Crafting = 6,
        Survival = 7,
        Settings = 8
    }

    /// <summary>
    /// 手机输入的唯一写入口。每次提交都携带完整快照，避免多指在同一帧分别写摇杆与按钮时互相覆盖；
    /// ResetAll 会同步释放所有按钮和方向，供暂停、失焦、输入锁与玩家销毁共同调用。
    /// </summary>
    public static class MobileInputRuntime
    {
        #region 状态

        private static FlatWorldMobileDevice device;
        private static FlatWorldMobileState state;

        public static FlatWorldMobileDevice Device => EnsureDevice();
        public static FlatWorldMobileState State => state;

        #endregion

        #region 设备与状态写入

        public static FlatWorldMobileDevice EnsureDevice()
        {
            if (device != null && device.added)
                return device;

            device = FlatWorldMobileDevice.current;
            if (device == null || !device.added)
                device = UnityInputSystem.AddDevice<FlatWorldMobileDevice>();

            return device;
        }

        public static void SetMove(Vector2 value)
        {
            state.move = ClampDirection(value);
            QueueState();
        }

        public static void SetAim(Vector2 value)
        {
            state.aim = ClampDirection(value);
            QueueState();
        }

        public static void SetAttackAim(Vector2 value)
        {
            state.attackAim = ClampDirection(value);
            QueueState();
        }

        public static void SetButton(MobileVirtualButton button, bool pressed)
        {
            uint mask = 1u << (int)button;
            state.buttons = pressed ? state.buttons | mask : state.buttons & ~mask;
            QueueState();
        }

        public static void ResetAll()
        {
            state = default;
            QueueState(createDevice: false);
        }

        private static void QueueState(bool createDevice = true)
        {
            FlatWorldMobileDevice target = createDevice ? EnsureDevice() : device;
            if (target == null || !target.added)
                return;

            UnityInputSystem.QueueStateEvent(target, state);
        }

        private static Vector2 ClampDirection(Vector2 value)
        {
            return value.sqrMagnitude > 1f ? value.normalized : value;
        }

        #endregion
    }
}
