using System;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class SpaceGameplaySettings
    {
        private const string ResourcePath = "Config/Space/space-gameplay";

        #region 可调整的飞行与地表移动参数
        public double EarthGravity = 9.81d;
        public double MinimumGravityForWalking = .25d;
        public double MinimumWalkSpeedMultiplier = .35d;
        public double MaximumWalkSpeedMultiplier = 2d;
        public double GravityWalkExponent = .5d;
        public double TakeoffSpeedMetersPerSecond = 16.6666666667d;
        public double MinimumSpaceExitSpeedMetersPerSecond = 2d;
        public double TakeoffDurationSeconds = 12d;
        public double LandingDurationSeconds = 10d;
        public double SpaceEntryClearanceMeters = 12d;
        public double MaximumSafeLandingSpeedMetersPerSecond = .5d;
        public double CruiseMaxSpeedMetersPerSecond = 120d;
        public double CruiseAccelerationMetersPerSecondSquared = 60d;
        public double CruiseBrakingMetersPerSecondSquared = 100d;
        public double CruiseTurnDegreesPerSecond = 45d; // 飞船驾驶与导航共用的直接旋转速度上限。
        public double NavigationArrivalDistanceMeters = 1d;
        public double NavigationArrivalSpeedMetersPerSecond = .25d;
        public double FreeFloatMaxSpeedMetersPerSecond = 12d; // 仅限制玩家主动移动，不限制继承的飞船速度与碰撞速度。
        public float FreeFloatAccelerationMultiplier = .35f / 3f; // 失重按键推力响应较慢，松键不衰减速度。
        public double SurfaceEntryHeightMeters = 80d;
        public double ImpactStoppingSeconds = .25d;
        public double ImpactForcePerDurability = 1000d;
        public double LandingChoiceRadiusMeters = 10d;
        public float DrivingCameraSize = 30f;
        public float DrivingCameraMaxSize = 120000f;
        public float DrivingCameraZoomMultiplier = 1.25f;
        public double ImpactJoulesPerDurability = 1000d;
        #region 太空电梯原型参数
        public double ElevatorTravelSeconds = 12d;
        public int ElevatorCargoSlots = 24;
        public float ElevatorCargoMaxWeightKg = 2000f;
        public float ElevatorCargoMaxVolumeLiters = 4000f;
        #endregion
        #region 引力捕获与大气参数
        // 参考圈取地球尺寸加余量，中心速度是物体从圈边静止释放的基准。
        public double GravityReferenceRadiusMeters = 2000d;
        public double GravityCaptureMarginMeters = 400d;
        public double GravityReferenceCenterSpeedMetersPerSecond = 8d; // 星体引力带来的基准速度降为原来的十分之一。
        public double AtmosphereDampingPerSecond = .01d;
        public double MaximumGravityDampingRatio = .75d;
        public double GravityRestDistanceMeters = .05d;
        public double GravityRestSpeedMetersPerSecond = .01d;
        public int SpacePhysicsSubsteps = 4;
        public double SpacePhysicsIslandDistanceMeters = 512d;
        #endregion
        private static SpaceGameplaySettings current;
        public static SpaceGameplaySettings Current
        {
            get
            {
                if (current != null) return current;
                return current = LoadValidatedConfiguration();
            }
        }

        [BuiltInContentBake]
        public static void BakeBuiltInConfiguration() => _ = LoadValidatedConfiguration();

        private static SpaceGameplaySettings LoadValidatedConfiguration()
        {
            SpaceGameplaySettings settings = BuiltInContentBinaryCatalog.ReadResource(
                ResourcePath, ParseResourceConfiguration) ?? new SpaceGameplaySettings();
            ValidateConfiguration(settings);
            return settings;
        }

        private static SpaceGameplaySettings ParseResourceConfiguration(string json)
            => JsonConfigurationCache.Deserialize<SpaceGameplaySettings>(json)
               ?? throw new NullReferenceException();

        private static void ValidateConfiguration(SpaceGameplaySettings settings)
        {
            if (settings.EarthGravity <= 0d || settings.MinimumGravityForWalking <= 0d ||
                settings.MinimumWalkSpeedMultiplier <= 0d || settings.MaximumWalkSpeedMultiplier < settings.MinimumWalkSpeedMultiplier ||
                settings.ImpactJoulesPerDurability <= 0d || settings.LandingChoiceRadiusMeters <= 0d ||
                settings.ImpactForcePerDurability <= 0d || settings.ImpactStoppingSeconds <= 0d || settings.SurfaceEntryHeightMeters <= 0d ||
                !SpaceVector2.Finite(settings.TakeoffSpeedMetersPerSecond) || settings.TakeoffSpeedMetersPerSecond <= 0d ||
                settings.MinimumSpaceExitSpeedMetersPerSecond <= 0d ||
                !SpaceVector2.Finite(settings.TakeoffDurationSeconds) || settings.TakeoffDurationSeconds <= 0d ||
                !SpaceVector2.Finite(settings.LandingDurationSeconds) || settings.LandingDurationSeconds <= 0d ||
                !SpaceVector2.Finite(settings.SpaceEntryClearanceMeters) || settings.SpaceEntryClearanceMeters < 0d ||
                !SpaceVector2.Finite(settings.MaximumSafeLandingSpeedMetersPerSecond) || settings.MaximumSafeLandingSpeedMetersPerSecond <= 0d ||
                settings.CruiseMaxSpeedMetersPerSecond <= 0d || settings.CruiseAccelerationMetersPerSecondSquared <= 0d ||
                settings.CruiseBrakingMetersPerSecondSquared <= 0d ||
                !SpaceVector2.Finite(settings.CruiseTurnDegreesPerSecond) || settings.CruiseTurnDegreesPerSecond <= 0d ||
                !SpaceVector2.Finite(settings.NavigationArrivalDistanceMeters) || settings.NavigationArrivalDistanceMeters <= 0d ||
                !SpaceVector2.Finite(settings.NavigationArrivalSpeedMetersPerSecond) || settings.NavigationArrivalSpeedMetersPerSecond <= 0d ||
                !SpaceVector2.Finite(settings.FreeFloatMaxSpeedMetersPerSecond) || settings.FreeFloatMaxSpeedMetersPerSecond <= 0d ||
                !SpaceVector2.Finite(settings.FreeFloatAccelerationMultiplier) || settings.FreeFloatAccelerationMultiplier <= 0f ||
                !SpaceVector2.Finite(settings.DrivingCameraSize) || settings.DrivingCameraSize <= 0f ||
                !SpaceVector2.Finite(settings.DrivingCameraMaxSize) || settings.DrivingCameraMaxSize < settings.DrivingCameraSize ||
                !SpaceVector2.Finite(settings.DrivingCameraZoomMultiplier) || settings.DrivingCameraZoomMultiplier <= 1f ||
                !SpaceVector2.Finite(settings.GravityReferenceRadiusMeters) || settings.GravityReferenceRadiusMeters <= 0d ||
                !SpaceVector2.Finite(settings.GravityCaptureMarginMeters) || settings.GravityCaptureMarginMeters <= 0d ||
                !SpaceVector2.Finite(settings.GravityReferenceCenterSpeedMetersPerSecond) || settings.GravityReferenceCenterSpeedMetersPerSecond <= 0d ||
                !SpaceVector2.Finite(settings.AtmosphereDampingPerSecond) || settings.AtmosphereDampingPerSecond < 0d ||
                !SpaceVector2.Finite(settings.MaximumGravityDampingRatio) || settings.MaximumGravityDampingRatio <= 0d || settings.MaximumGravityDampingRatio >= 1d ||
                !SpaceVector2.Finite(settings.GravityRestDistanceMeters) || settings.GravityRestDistanceMeters <= 0d ||
                !SpaceVector2.Finite(settings.GravityRestSpeedMetersPerSecond) || settings.GravityRestSpeedMetersPerSecond <= 0d ||
                settings.SpacePhysicsSubsteps < 1 || settings.SpacePhysicsSubsteps > 32 ||
                !SpaceVector2.Finite(settings.SpacePhysicsIslandDistanceMeters) || settings.SpacePhysicsIslandDistanceMeters <= 0d ||
                !double.IsFinite(settings.ElevatorTravelSeconds) || settings.ElevatorTravelSeconds <= 0d ||
                settings.ElevatorCargoSlots < 1 || settings.ElevatorCargoSlots > 256 ||
                !float.IsFinite(settings.ElevatorCargoMaxWeightKg) || settings.ElevatorCargoMaxWeightKg <= 0f ||
                !float.IsFinite(settings.ElevatorCargoMaxVolumeLiters) || settings.ElevatorCargoMaxVolumeLiters <= 0f)
                throw new InvalidOperationException("太空移动与冲击配置无效。");
        }
        public double WalkingMultiplier(double gravity)
            => Math.Clamp(Math.Pow(EarthGravity / Math.Max(MinimumGravityForWalking, gravity), GravityWalkExponent),
                MinimumWalkSpeedMultiplier, MaximumWalkSpeedMultiplier);
        // 低重力按相对重力降低起步、转向和刹停响应，地球及更高重力保持原手感。
        public double WalkingResponseMultiplier(double gravity)
            => Math.Min(1d, Math.Max(MinimumGravityForWalking, gravity) / EarthGravity);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => current = null;
        #endregion
    }
}
