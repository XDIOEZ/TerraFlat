using System;

#region 蓄力发射时序
public enum ProjectileChargeReleaseMode
{
    ReleaseToFire,
    LoadThenReleaseToFire
}

public enum ProjectileChargeReleaseResult
{
    None,
    Cancelled,
    Loaded,
    Fire
}

/// <summary>只管理蓄力、上膛和再次松开的时序，弹药与体力仍由持有模块结算。</summary>
public struct ProjectileChargeCycle
{
    private enum Phase { Idle, Charging, Loaded, TriggerHeld }

    private Phase _phase;
    private float _chargeSeconds;

    public bool IsActive => _phase != Phase.Idle;
    public bool IsCharging => _phase == Phase.Charging;

    /// <summary>新一轮按下开始蓄力，已上膛时只等待这次按键松开。</summary>
    public bool TryPress()
    {
        if (_phase == Phase.Loaded)
        {
            _phase = Phase.TriggerHeld;
            return false;
        }
        if (IsActive) return false;
        _phase = Phase.Charging;
        _chargeSeconds = 0f;
        return true;
    }

    /// <summary>返回本帧应扣体力的秒数，上膛模式只计算达到满蓄力之前的部分。</summary>
    public float Advance(float deltaTime, float fullChargeSeconds, ProjectileChargeReleaseMode mode)
    {
        if (!IsCharging) return 0f;
        float duration = Math.Max(0f, fullChargeSeconds);
        float elapsed = Math.Max(0f, deltaTime);
        float consumptionSeconds = mode == ProjectileChargeReleaseMode.LoadThenReleaseToFire
            ? Math.Min(elapsed, Math.Max(0f, duration - _chargeSeconds)) : elapsed;
        _chargeSeconds = Math.Min(duration, _chargeSeconds + elapsed);
        return consumptionSeconds;
    }

    public float GetCharge01(float fullChargeSeconds)
        => fullChargeSeconds <= 0f ? 1f : Math.Min(1f, _chargeSeconds / fullChargeSeconds);

    /// <summary>第一次满蓄力松开只上膛，第二次完整按下并松开才允许发射。</summary>
    public ProjectileChargeReleaseResult Release(float fullChargeSeconds, ProjectileChargeReleaseMode mode)
    {
        if (_phase == Phase.TriggerHeld)
        {
            Cancel();
            return ProjectileChargeReleaseResult.Fire;
        }
        if (!IsCharging) return ProjectileChargeReleaseResult.None;
        if (mode == ProjectileChargeReleaseMode.LoadThenReleaseToFire)
        {
            if (GetCharge01(fullChargeSeconds) < 1f)
            {
                Cancel();
                return ProjectileChargeReleaseResult.Cancelled;
            }
            _phase = Phase.Loaded;
            return ProjectileChargeReleaseResult.Loaded;
        }
        Cancel();
        return ProjectileChargeReleaseResult.Fire;
    }

    public void Cancel()
    {
        _phase = Phase.Idle;
        _chargeSeconds = 0f;
    }
}
#endregion
