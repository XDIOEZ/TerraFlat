using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GameController 的外部 Agent 控制契约。
/// 通过唯一控制租约向现有输入链注入移动、瞄准与攻击语义，不绕过 Mover、武器、体力和地形等正式玩法系统。
/// </summary>
public partial class GameController
{
    public const string ExternalGameplayInputDeviceUsage = "FlatWorldExternalGameplayControl"; // 外部控制虚拟输入设备的稳定 Usage。

    #region 外部控制状态

    private object _externalGameplayControlOwner; // 当前外部控制租约持有者。
    private Vector2 _externalMoveInput; // Agent 注入的二维移动输入。
    private Vector3 _externalAimWorldPosition; // Agent 注入的世界瞄准点。
    private bool _externalAimWorldPositionInitialized; // 是否已有有效 Agent 瞄准点。
    private bool _externalAttackHeld; // Agent 攻击键是否处于按住状态。

    /// <summary>当前是否由外部 Agent 接管玩法输入。</summary>
    public bool HasExternalGameplayControl => _externalGameplayControlOwner != null;

    /// <summary>当前外部控制租约持有者的诊断名称。</summary>
    public string ExternalGameplayControlOwnerName =>
        _externalGameplayControlOwner?.GetType().FullName ?? string.Empty;

    #endregion

    #region 外部控制 API

    /// <summary>尝试获取唯一外部控制租约；同一持有者重复获取视为成功。</summary>
    public bool TryAcquireExternalGameplayControl(object owner)
    {
        if (owner == null)
            return false;

        if (_externalGameplayControlOwner != null &&
            !ReferenceEquals(_externalGameplayControlOwner, owner))
        {
            return false;
        }

        if (_externalGameplayControlOwner == null)
        {
            CancelActiveAttackAndMobileInput();
            _externalGameplayControlOwner = owner;
            _externalMoveInput = Vector2.zero;
            _externalAimWorldPosition = transform.position + Vector3.right;
            _externalAimWorldPosition.z = 0f;
            _externalAimWorldPositionInitialized = true;
            _externalAttackHeld = false;
        }

        return true;
    }

    /// <summary>判断指定对象是否持有当前外部控制租约。</summary>
    public bool IsExternalGameplayControlOwner(object owner)
    {
        return owner != null && ReferenceEquals(_externalGameplayControlOwner, owner);
    }

    /// <summary>判断输入设备是否属于当前外部控制通道，防止真实设备与 Agent 同时驱动玩法。</summary>
    private bool IsExternalGameplayInputDevice(InputDevice device)
    {
        if (!HasExternalGameplayControl || device == null)
            return false;

        for (int i = 0; i < device.usages.Count; i++)
        {
            if (string.Equals(
                    device.usages[i].ToString(),
                    ExternalGameplayInputDeviceUsage,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>由租约持有者写入移动输入，幅度保持在 0～1。</summary>
    public bool TrySetExternalMoveInput(object owner, Vector2 input)
    {
        if (!IsExternalGameplayControlOwner(owner))
            return false;

        _externalMoveInput = Vector2.ClampMagnitude(input, 1f);
        return true;
    }

    /// <summary>由租约持有者写入世界瞄准点。</summary>
    public bool TrySetExternalAimWorldPosition(object owner, Vector3 worldPosition)
    {
        if (!IsExternalGameplayControlOwner(owner))
            return false;

        if (float.IsNaN(worldPosition.x) || float.IsInfinity(worldPosition.x) ||
            float.IsNaN(worldPosition.y) || float.IsInfinity(worldPosition.y))
        {
            return false;
        }

        worldPosition.z = 0f;
        _externalAimWorldPosition = WorldTopologyRuntime.NormalizePosition(worldPosition);
        _externalAimWorldPositionInitialized = true;
        return true;
    }

    /// <summary>由租约持有者提交攻击按下或松开语义，继续复用现有武器事件链。</summary>
    public bool TrySetExternalAttackHeld(object owner, bool held)
    {
        if (!IsExternalGameplayControlOwner(owner))
            return false;

        if (_externalAttackHeld == held)
            return true;

        _externalAttackHeld = held;
        if (held)
            AttackStarted?.Invoke();
        else
            AttackEnded?.Invoke();
        return true;
    }

    /// <summary>释放外部控制租约，并清理该租约留下的持续输入。</summary>
    public bool ReleaseExternalGameplayControl(object owner)
    {
        if (!IsExternalGameplayControlOwner(owner))
            return false;

        ResetExternalGameplayControl();
        return true;
    }

    /// <summary>读取外部移动输入；仅供 GameController 自身输入仲裁调用。</summary>
    private bool TryReadExternalMoveInput(out Vector2 input)
    {
        input = _externalMoveInput;
        return HasExternalGameplayControl;
    }

    /// <summary>读取外部瞄准点；仅供 GameController 自身指针查询调用。</summary>
    private bool TryGetExternalAimWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = _externalAimWorldPosition;
        return HasExternalGameplayControl && _externalAimWorldPositionInitialized;
    }

    /// <summary>无条件清理外部控制状态，供禁用、销毁和租约释放共用。</summary>
    private void ResetExternalGameplayControl()
    {
        if (_externalAttackHeld)
            AttackEnded?.Invoke();

        _externalAttackHeld = false;
        _externalMoveInput = Vector2.zero;
        _externalAimWorldPositionInitialized = false;
        _externalGameplayControlOwner = null;
    }

    #endregion
}
