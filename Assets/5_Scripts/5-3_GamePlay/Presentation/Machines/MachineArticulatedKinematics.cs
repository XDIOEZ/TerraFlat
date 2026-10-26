using System;

/// <summary>刚性曲柄连杆的纯运动学，表现和回归共用同一关节解。</summary>
public static class MachineArticulatedKinematics
{
    #region 曲柄连杆关节约束
    public readonly struct VerticalSliderCrankPose
    {
        public readonly double CrankPinX, CrankPinY, SliderPinX, SliderPinY, ConnectingRodAngleRadians;
        public VerticalSliderCrankPose(double crankX, double crankY, double sliderX, double sliderY, double angle)
        { CrankPinX = crankX; CrankPinY = crankY; SliderPinX = sliderX; SliderPinY = sliderY; ConnectingRodAngleRadians = angle; }
    }

    public static bool TrySolveVerticalSliderCrank(double angleRadians, double crankCenterX, double crankCenterY,
        double crankRadius, double sliderAxisX, double connectingRodLength, int sliderBranch,
        out VerticalSliderCrankPose pose)
    {
        pose = default;
        if (!Finite(angleRadians) || !Finite(crankCenterX) || !Finite(crankCenterY) ||
            !Finite(crankRadius) || !Finite(sliderAxisX) || !Finite(connectingRodLength) ||
            crankRadius < 0d || connectingRodLength <= 0d || (sliderBranch != -1 && sliderBranch != 1)) return false;
        double pinX = crankCenterX + Math.Cos(angleRadians) * crankRadius;
        double pinY = crankCenterY + Math.Sin(angleRadians) * crankRadius;
        double dx = sliderAxisX - pinX;
        double square = connectingRodLength * connectingRodLength - dx * dx;
        if (square < -1e-10d || !Finite(square)) return false;
        double dy = sliderBranch * Math.Sqrt(Math.Max(0d, square));
        pose = new VerticalSliderCrankPose(pinX, pinY, sliderAxisX, pinY + dy, Math.Atan2(dy, dx));
        return true;
    }

    public static double ResolveWorkingSignedRpm(bool active, bool powered, bool transferring, bool ruptured, double signedRpm)
        => active && powered && transferring && !ruptured && Finite(signedRpm) ? signedRpm : 0d;

    public static double AdvanceContinuousPhase(double phaseRadians, double signedRpm, double elapsedSeconds)
    {
        double phase = Finite(phaseRadians) ? phaseRadians : 0d;
        if (!Finite(signedRpm) || !Finite(elapsedSeconds) || elapsedSeconds <= 0d) return phase;
        double next = phase + signedRpm * (Math.PI / 30d) * elapsedSeconds;
        if (!Finite(next)) return phase;
        return Math.IEEERemainder(next, Math.PI * 2d);
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    #endregion
}

/// <summary>表现独立记录停机相位，改变速度前先积分旧速度。</summary>
public sealed class MachineArticulatedMotionClock
{
    #region 连续表现相位
    public double PhaseRadians { get; private set; }
    public double SignedRpm { get; private set; }
    public double SampleTimeSeconds { get; private set; }
    private bool initialized;

    public void Synchronize(double nowSeconds, double signedRpm)
    {
        if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds)) return;
        if (double.IsNaN(signedRpm) || double.IsInfinity(signedRpm)) signedRpm = 0d;
        if (!initialized) { initialized = true; SampleTimeSeconds = nowSeconds; SignedRpm = signedRpm; return; }
        if (SignedRpm == signedRpm && nowSeconds >= SampleTimeSeconds) return;
        PhaseRadians = Evaluate(nowSeconds);
        SampleTimeSeconds = nowSeconds;
        SignedRpm = signedRpm;
    }
    public double Evaluate(double nowSeconds)
        => MachineArticulatedKinematics.AdvanceContinuousPhase(PhaseRadians, SignedRpm, nowSeconds - SampleTimeSeconds);
    #endregion
}
