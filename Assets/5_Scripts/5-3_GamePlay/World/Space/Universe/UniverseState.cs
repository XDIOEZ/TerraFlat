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
        public SpaceSectorId Sector;
        public int Seed;
        public double SimulationSeconds;
        public double FixedStepSeconds = 0.02d;
        public double MetersPerUnityUnit = 1d;
        public double OrbitTimeScale = 1d;
        public List<BodyState> Bodies = new();
        #endregion
    }

    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class BodyState
    {
        #region 星体轨道与地表配置
        public string BodyId;
        public string TemplateBodyId;
        public SpaceSectorId Sector;
        public string PlanetId;
        public string DisplayName;
        public PlanetVisualConfiguration Visual;
        public string ParentBodyId;
        public string SurfaceProfileId;
        public int Seed;
        public double RadiusMeters;
        public double SurfaceGravity;
        public double AtmosphereThickness;
        [MemoryPack.MemoryPackIgnore]
        [JsonIgnore] public double MassKg => SurfaceGravity * RadiusMeters * RadiusMeters / 6.67430e-11d;
        public double PlacementDistanceMeters;
        public double PlacementAngleRadians;
        public double OrbitalPeriodSeconds;
        public double OrbitalEccentricity;
        public double OrbitalPeriapsisRadians;
        public double RotationPeriodSeconds;
        public double RotationInitialRadians;
        public double EquatorTiltRadians;
        public SpaceVector2 PositionMeters;
        [MemoryPack.MemoryPackIgnore]
        [JsonIgnore] public SpaceVector2 VelocityMetersPerSecond;
        [MemoryPack.MemoryPackIgnore]
        [JsonIgnore] public WorldAddress SurfaceAddress => new(PlanetId, WorldAddress.SurfaceDimensionId);
        #endregion
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum PlanetSurfaceStyle { Rocky, Ocean, Banded, Stellar, Asteroid }

    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class PlanetVisualConfiguration
    {
        #region 星球模板外观参数
        public PlanetSurfaceStyle Style;
        public float[] BaseColor;
        public float[] AccentColor;
        public float DetailStrength;
        public float DetailScale;
        [MemoryPack.MemoryPackIgnore, JsonIgnore]
        public float SurfaceExtent => Style == PlanetSurfaceStyle.Stellar ? 1.3f : 1.12f;

        public void Validate(string bodyId)
        {
            if (!Enum.IsDefined(typeof(PlanetSurfaceStyle), Style) || !ValidColor(BaseColor) || !ValidColor(AccentColor) ||
                !SpaceVector2.Finite(DetailStrength) || DetailStrength < 0f || DetailStrength > 1f ||
                !SpaceVector2.Finite(DetailScale) || DetailScale < 1f)
                throw new System.IO.InvalidDataException($"星球外观配置无效：{bodyId}");
        }

        private static bool ValidColor(float[] color) => color?.Length == 3 &&
            SpaceVector2.Finite(color[0]) && color[0] >= 0f && color[0] <= 1f &&
            SpaceVector2.Finite(color[1]) && color[1] >= 0f && color[1] <= 1f &&
            SpaceVector2.Finite(color[2]) && color[2] >= 0f && color[2] <= 1f;

        // 每颗生成星球独占外观数据，修改随机实例不会染到模板或其它星球。
        public PlanetVisualConfiguration Clone() => new()
        {
            Style = Style, BaseColor = (float[])BaseColor.Clone(), AccentColor = (float[])AccentColor.Clone(),
            DetailStrength = DetailStrength, DetailScale = DetailScale
        };
        #endregion
    }

    [Serializable]
    public sealed class SpaceMotionState
    {
        #region 太空移动主体
        public SpaceSectorId Sector;
        public SpaceVector2 PositionMeters;
        public SpaceVector2 VelocityMetersPerSecond;
        public double RadiusMeters;
        #endregion
    }

    public struct SurfaceContact
    {
        #region 单次接触快照
        public string BodyId;
        public string PlanetId;
        public SpaceSectorId Sector;
        public double Fraction;
        public double SimulationSeconds;
        public double SurfaceAngleRadians;
        public SpaceVector2 PositionMeters;
        public SpaceVector2 RelativeVelocityMetersPerSecond;
        [JsonIgnore] public bool IsValid => !string.IsNullOrEmpty(BodyId);
        #endregion
    }
}
