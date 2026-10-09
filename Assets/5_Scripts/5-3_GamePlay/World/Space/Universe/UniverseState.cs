using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    [Serializable]
    public struct SpaceVector2 : IEquatable<SpaceVector2>
    {
        #region 双精度坐标
        public double X;
        public double Y;
        public SpaceVector2(double x, double y) { X = x; Y = y; }
        [JsonIgnore] public double SqrMagnitude => X * X + Y * Y;
        [JsonIgnore] public double Magnitude => Math.Sqrt(SqrMagnitude);
        [JsonIgnore] public bool IsFinite => Finite(X) && Finite(Y);
        [JsonIgnore] public SpaceVector2 Normalized => Magnitude > 1e-12d ? this / Magnitude : default;
        public Vector2 ToVector2() => new((float)X, (float)Y);
        public static SpaceVector2 FromVector2(Vector2 value) => new(value.x, value.y);
        public static SpaceVector2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));
        public static double Dot(SpaceVector2 left, SpaceVector2 right) => left.X * right.X + left.Y * right.Y;
        public static double Cross(SpaceVector2 left, SpaceVector2 right) => left.X * right.Y - left.Y * right.X;
        public static SpaceVector2 Lerp(SpaceVector2 left, SpaceVector2 right, double fraction) => left + (right - left) * fraction;
        public static SpaceVector2 operator +(SpaceVector2 left, SpaceVector2 right) => new(left.X + right.X, left.Y + right.Y);
        public static SpaceVector2 operator -(SpaceVector2 left, SpaceVector2 right) => new(left.X - right.X, left.Y - right.Y);
        public static SpaceVector2 operator -(SpaceVector2 value) => new(-value.X, -value.Y);
        public static SpaceVector2 operator *(SpaceVector2 value, double scale) => new(value.X * scale, value.Y * scale);
        public static SpaceVector2 operator *(double scale, SpaceVector2 value) => value * scale;
        public static SpaceVector2 operator /(SpaceVector2 value, double scale) => new(value.X / scale, value.Y / scale);
        public bool Equals(SpaceVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is SpaceVector2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public static bool operator ==(SpaceVector2 left, SpaceVector2 right) => left.Equals(right);
        public static bool operator !=(SpaceVector2 left, SpaceVector2 right) => !left.Equals(right);
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        #endregion
    }

    [Serializable]
    public sealed class UniverseState
    {
        #region 星系权威状态
        public string TemplateId;
        public int Seed;
        public double SimulationSeconds;
        public double FixedStepSeconds = 0.02d;
        public double MetersPerUnityUnit = 1d;
        public List<BodyState> Bodies = new();
        #endregion
    }

    [Serializable]
    public sealed class BodyState
    {
        #region 星体身份与物理配置
        public string BodyId;
        public string PlanetId;
        public string DisplayName;
        public string PrefabId;
        public string ParentBodyId;
        public string SurfaceProfileId;
        public int Seed;
        public double RadiusMeters;
        public double SurfaceGravity;
        public double InfluenceRadiusMeters;
        public double OrbitRadiusMeters;
        public double OrbitalPeriodSeconds;
        public double InitialPhaseRadians;
        public bool OrbitClockwise;
        public double RotationPeriodSeconds;
        public double RotationInitialRadians;
        public SpaceVector2 PositionMeters;
        public SpaceVector2 VelocityMetersPerSecond;
        [JsonIgnore] public double GravityParameter => SurfaceGravity * RadiusMeters * RadiusMeters;
        [JsonIgnore] public WorldAddress SurfaceAddress => new(PlanetId, WorldAddress.SurfaceDimensionId);
        #endregion
    }

    [Serializable]
    public sealed class OrbitState
    {
        #region 独立运动主体
        public SpaceVector2 PositionMeters;
        public SpaceVector2 VelocityMetersPerSecond;
        public string ReferenceBodyId;
        public string CapturedBodyId;
        public double RadiusMeters;
        public double RelativeAltitudeMeters;
        #endregion
    }

    public struct SurfaceContact
    {
        #region 单次接触快照
        public string BodyId;
        public string PlanetId;
        public double Fraction;
        public double SimulationSeconds;
        public double SurfaceAngleRadians;
        public SpaceVector2 PositionMeters;
        public SpaceVector2 RelativeVelocityMetersPerSecond;
        [JsonIgnore] public bool IsValid => !string.IsNullOrEmpty(BodyId);
        #endregion
    }
}
