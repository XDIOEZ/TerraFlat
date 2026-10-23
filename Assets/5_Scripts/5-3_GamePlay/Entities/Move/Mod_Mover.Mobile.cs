using FlatWorld.Mobile;
using UnityEngine;

public partial class Mod_Mover
{
    #region 手机摇杆移动
    private bool mobileMovementInputActive;
    private bool manualRunBeforeMobile;
    private bool mobileRunExhausted;

    public bool RunPreference => mobileMovementInputActive ? manualRunBeforeMobile : IsRunning;

    // 自动奔跑只接管实际手机移动，离开手机输入后恢复键鼠的手动偏好。
    internal Vector2 ResolveMobileMoveInput(Vector2 input, bool isMobileMoveInput)
    {
        if (!isMobileMoveInput || !FiniteVelocity(input) || IsSpaceRunningDisabled || CarrierSource != null)
        {
            ReleaseMobileMovementInput();
            return FiniteVelocity(input) ? input : Vector2.zero;
        }

        if (!mobileMovementInputActive)
        {
            manualRunBeforeMobile = IsRunning;
            mobileMovementInputActive = true;
            mobileRunExhausted = false;
        }

        float deflection = Mathf.Clamp01(input.magnitude);
        bool runRequested = deflection > MobileMovementRules.RunStart && RunSpeedRate > 1f;
        if (!runRequested) mobileRunExhausted = false;
        else if (stamina != null && stamina.CurrentValue < RunStaminaThreshold)
            mobileRunExhausted = true;

        float walkingSpeed = GetWalkingSpeedValue();
        ApplyRunState(runRequested && !mobileRunExhausted);
        float modeSpeed = Mathf.Max(0f, Speed.Value);
        if (deflection <= 0f || walkingSpeed <= 0f || modeSpeed <= 0f) return Vector2.zero;

        float deadZone = inputController != null
            ? inputController.AimDeadZone : PlayerAimCursorSystem.DefaultDeadZone;
        float speedFactor = MobileMovementRules.EvaluateSpeedFactor(deflection, deadZone,
            IsRunning ? modeSpeed / walkingSpeed : 1f);
        // 以实际走速和跑速换算输入，模式切换与装备最终加算都不产生速度台阶。
        return input.normalized * Mathf.Clamp01(speedFactor * walkingSpeed / modeSpeed);
    }

    private void ReleaseMobileMovementInput()
    {
        if (!mobileMovementInputActive) return;
        bool restoreRun = manualRunBeforeMobile;
        mobileMovementInputActive = mobileRunExhausted = manualRunBeforeMobile = false;
        ApplyRunState(restoreRun);
    }

    private float GetWalkingSpeedValue()
    {
        float speed = Speed.Value;
        if (IsRunning)
            speed = (speed - Speed.FinalAdditive) / Mathf.Max(0.0001f, RunSpeedRate) + Speed.FinalAdditive;
        return Mathf.Max(0f, speed);
    }

    #endregion
}
