using System;
using System.Globalization;
using FlatWorld.Spaceflight;

/// <summary>飞船高度尺的显示精度、量程与资格规则。</summary>
public static class ShipFlightHudPresentationRules
{
    #region 高度尺数值
    public const double NearGroundHeightMeters = 10d;
    public const double TouchdownDisplaySeconds = 2d;

    public static bool IsUsableHeight(double heightMeters)
        => !double.IsNaN(heightMeters) && !double.IsInfinity(heightMeters) && heightMeters >= 0d;

    public static double ChooseScaleMaximum(double heightMeters)
    {
        if (!IsUsableHeight(heightMeters) || heightMeters <= 10d) return 10d;
        double power = Math.Pow(10d, Math.Floor(Math.Log10(heightMeters)));
        double leading = heightMeters / power;
        double maximum = (leading <= 1d ? 1d : leading <= 2d ? 2d : leading <= 5d ? 5d : 10d) * power;
        return double.IsInfinity(maximum) ? heightMeters : maximum;
    }

    public static double ExpandScaleMaximum(double currentMaximum, double heightMeters)
        => Math.Max(ChooseScaleMaximum(heightMeters), IsUsableHeight(currentMaximum) ? Math.Max(10d, currentMaximum) : 10d);

    public static double GetNormalizedHeight(double heightMeters, double maximum, bool touchdown)
    {
        if (touchdown || !IsUsableHeight(heightMeters)) return 0d;
        double safeMaximum = IsUsableHeight(maximum) && maximum > 0d ? maximum : 10d;
        return Math.Max(.003d, Math.Min(1d, heightMeters / safeMaximum));
    }

    // 零只表示已经触地，仍在飞行的极小正数保留为小于一厘米。
    public static string FormatHeightNumber(double heightMeters, bool touchdown)
    {
        if (touchdown) return "0";
        if (!IsUsableHeight(heightMeters)) return "--";
        if (heightMeters < .01d) return "<0.01";
        return heightMeters.ToString(heightMeters >= 10d ? "0" : heightMeters >= 1d ? "0.0" : "0.00", CultureInfo.InvariantCulture);
    }

    public static bool IsNearGround(double heightMeters, bool descending)
        => descending && IsUsableHeight(heightMeters) && heightMeters <= NearGroundHeightMeters;
    #endregion

    #region 显示资格与正式落地凭证
    public static bool ShouldShowFlight(ShipFlightHudSnapshot snapshot, bool localAndAlive)
        => localAndAlive && snapshot.IsDrivingSurfaceFlight && !string.IsNullOrWhiteSpace(snapshot.ShipId) &&
           snapshot.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending && IsUsableHeight(snapshot.HeightMeters);

    public static bool ShouldAcceptLandingReceipt(ShipLandingReceipt receipt, string trackedShipId,
        string lastSeenEventId, string currentWorldKey, bool localAndAlive)
        => localAndAlive && receipt.IsValid && !string.IsNullOrWhiteSpace(trackedShipId) &&
           receipt.ShipId == trackedShipId && receipt.EventId != lastSeenEventId && receipt.WorldKey == currentWorldKey;
    #endregion
}

/// <summary>只跟踪本次实际驾驶，退出、死亡或换世界后不重放旧落地提示。</summary>
public sealed class ShipFlightHudPresentationTracker
{
    #region 本次驾驶的展示状态
    private bool initialized;
    private string worldKey, trackedShipId, lastSeenEventId, cancelledShipId;
    private double scaleMaximum = 10d, touchdownUntil = double.NegativeInfinity;

    public ShipFlightHudPresentationState Advance(ShipFlightHudSnapshot snapshot, bool localAndAlive,
        string currentWorldKey, double nowSeconds)
    {
        if (!initialized || worldKey != currentWorldKey)
        {
            if (initialized) cancelledShipId = null;
            ClearFlight();
            initialized = true;
            worldKey = currentWorldKey;
            lastSeenEventId = snapshot.LastLanding.EventId;
        }
        if (!localAndAlive)
        {
            ClearFlight();
            lastSeenEventId = snapshot.LastLanding.EventId;
            return default;
        }
        if (cancelledShipId != null)
        {
            if (snapshot.IsDrivingSurfaceFlight && snapshot.ShipId == cancelledShipId)
            {
                lastSeenEventId = snapshot.LastLanding.EventId;
                return default;
            }
            cancelledShipId = null;
        }
        if (ShipFlightHudPresentationRules.ShouldShowFlight(snapshot, true))
        {
            if (trackedShipId != snapshot.ShipId) scaleMaximum = 10d;
            trackedShipId = snapshot.ShipId;
            touchdownUntil = double.NegativeInfinity;
            scaleMaximum = ShipFlightHudPresentationRules.ExpandScaleMaximum(scaleMaximum, snapshot.HeightMeters);
            lastSeenEventId = snapshot.LastLanding.EventId;
            bool onLaunchGround = snapshot.Phase == ShipFlightPhase.Lifting && snapshot.HeightMeters == 0d && snapshot.VerticalSpeed >= 0d;
            return new ShipFlightHudPresentationState(true, false, onLaunchGround,
                ShipFlightHudPresentationRules.IsNearGround(snapshot.HeightMeters, snapshot.VerticalSpeed < 0d),
                snapshot.HeightMeters, scaleMaximum);
        }
        bool accepted = ShipFlightHudPresentationRules.ShouldAcceptLandingReceipt(snapshot.LastLanding,
            trackedShipId, lastSeenEventId, currentWorldKey, true);
        lastSeenEventId = snapshot.LastLanding.EventId;
        if (accepted)
        {
            touchdownUntil = nowSeconds + ShipFlightHudPresentationRules.TouchdownDisplaySeconds;
            trackedShipId = null;
        }
        if (nowSeconds < touchdownUntil)
            return new ShipFlightHudPresentationState(true, true, true, false, 0d, scaleMaximum);
        if (snapshot.HasLandedSurfaceFlight && trackedShipId == snapshot.ShipId && !string.IsNullOrWhiteSpace(trackedShipId))
            return new ShipFlightHudPresentationState(true, false, true, false, 0d, scaleMaximum);
        ClearFlight();
        return default;
    }

    public void Reset()
    {
        initialized = false;
        worldKey = lastSeenEventId = cancelledShipId = null;
        ClearFlight();
    }

    public void CancelCurrentFlight(string shipId)
    {
        ClearFlight();
        cancelledShipId = shipId;
    }

    public void ResumeCurrentFlight(string shipId)
    {
        if (cancelledShipId == shipId) cancelledShipId = null;
    }

    private void ClearFlight()
    {
        trackedShipId = null;
        scaleMaximum = 10d;
        touchdownUntil = double.NegativeInfinity;
    }
    #endregion
}

public readonly struct ShipFlightHudPresentationState
{
    #region 高度尺单帧读数
    public readonly bool Visible, Touchdown, GroundContact, NearGround;
    public readonly double HeightMeters, ScaleMaximum, NormalizedHeight;

    public ShipFlightHudPresentationState(bool visible, bool touchdown, bool groundContact, bool nearGround,
        double heightMeters, double scaleMaximum)
    {
        Visible = visible; Touchdown = touchdown; GroundContact = groundContact; NearGround = nearGround;
        HeightMeters = heightMeters; ScaleMaximum = scaleMaximum;
        NormalizedHeight = ShipFlightHudPresentationRules.GetNormalizedHeight(heightMeters, scaleMaximum, groundContact);
    }
    #endregion
}
