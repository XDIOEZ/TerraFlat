using System;
using System.Collections.Generic;

public interface ICameraZoomPolicy
{
    #region 缩放策略契约

    bool TryGetProfile(Mod_Cam camera, out CameraZoomProfile profile);
    float ClampInputSize(Mod_Cam camera, float currentSize, float requestedSize);

    #endregion
}

public readonly struct CameraZoomProfile
{
    #region 连续缩放坐标

    public float MaximumSize { get; }
    public float ProportionalStartSize { get; }
    public float ZoomMultiplier { get; }
    public float MaximumRenderSize { get; }
    public bool Enabled => MaximumSize > 0f;

    internal bool IsValid => Enabled && IsFinite(MaximumSize) &&
        IsFinite(ProportionalStartSize) && ProportionalStartSize >= 0f &&
        IsFinite(ZoomMultiplier) && ZoomMultiplier >= 1f &&
        (ZoomMultiplier <= 1f || ProportionalStartSize > 0f) &&
        IsFinite(MaximumRenderSize) && MaximumRenderSize >= 0f;

    private bool HasProportionalRange => IsValid && ZoomMultiplier > 1f;

    public CameraZoomProfile(float maximumSize, float proportionalStartSize,
        float zoomMultiplier, float maximumRenderSize = 0f)
    {
        MaximumSize = maximumSize;
        ProportionalStartSize = proportionalStartSize;
        ZoomMultiplier = zoomMultiplier;
        MaximumRenderSize = maximumRenderSize;
    }

    // 输入和滑条共用连续步坐标，跨过倍率起点时仍可按相反输入返回。
    public float Step(float size, float delta)
    {
        if (!IsFinite(size)) size = 0f;
        if (!IsFinite(delta)) return size;
        return FromStepCoordinate(ToStepCoordinate(size) + delta);
    }

    public float ToSliderValue(float size, float minimum, float maximum)
    {
        if (!IsValidRange(minimum, maximum) || !IsFinite(size)) return 0f;
        double low = ToStepCoordinate(minimum);
        double high = ToStepCoordinate(maximum);
        double coordinate = ToStepCoordinate(Math.Max(minimum, Math.Min(size, maximum)));
        return (float)Math.Max(0d, Math.Min(1d, (coordinate - low) / (high - low)));
    }

    public float FromSliderValue(float value, float minimum, float maximum)
    {
        if (!IsValidRange(minimum, maximum)) return IsFinite(minimum) ? minimum : 0f;
        if (!IsFinite(value)) value = 0f;
        double low = ToStepCoordinate(minimum);
        double high = ToStepCoordinate(maximum);
        return FromStepCoordinate(low + (high - low) * Math.Max(0d, Math.Min(1d, value)));
    }

    private double ToStepCoordinate(float size)
    {
        if (!HasProportionalRange || size <= ProportionalStartSize) return size;
        return ProportionalStartSize +
            Math.Log((double)size / ProportionalStartSize) / Math.Log(ZoomMultiplier);
    }

    private float FromStepCoordinate(double coordinate)
    {
        double size = !HasProportionalRange || coordinate <= ProportionalStartSize
            ? coordinate
            : ProportionalStartSize * Math.Exp(
                (coordinate - ProportionalStartSize) * Math.Log(ZoomMultiplier));
        return (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, size));
    }

    private static bool IsValidRange(float minimum, float maximum)
        => IsFinite(minimum) && IsFinite(maximum) && maximum > minimum;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}

public static class CameraZoomExtensions
{
    #region 缩放策略注册

    private static readonly List<Registration> registrations = new();

    public static IDisposable Register(ICameraZoomPolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        var registration = new Registration(policy);
        registrations.Add(registration);
        return registration;
    }

    public static CameraZoomProfile GetProfile(Mod_Cam camera)
    {
        if (camera == null) return default;
        for (int index = registrations.Count - 1; index >= 0; index--)
        {
            if (registrations[index].Policy.TryGetProfile(camera, out CameraZoomProfile profile) &&
                profile.IsValid)
                return profile;
        }
        return default;
    }

    // 手动缩放共用策略约束，不影响读档、GM 或其它系统直接设置镜头。
    public static float ClampInputSize(Mod_Cam camera, float currentSize, float requestedSize)
    {
        if (camera == null || camera.HasFollowOverride) return requestedSize;
        for (int index = registrations.Count - 1; index >= 0; index--)
        {
            ICameraZoomPolicy policy = registrations[index].Policy;
            if (policy.TryGetProfile(camera, out CameraZoomProfile profile) && profile.IsValid)
                return policy.ClampInputSize(camera, currentSize, requestedSize);
        }
        return requestedSize;
    }

    private sealed class Registration : IDisposable
    {
        public ICameraZoomPolicy Policy { get; }
        private bool disposed;

        public Registration(ICameraZoomPolicy policy) => Policy = policy;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            registrations.Remove(this);
        }
    }

    #endregion
}
