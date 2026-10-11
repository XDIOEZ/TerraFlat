using System;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    [Serializable]
    public sealed class SpaceGameplaySettings
    {
        #region 可调整的飞行与地表移动参数
        public double EarthGravity = 9.81d;
        public double MinimumGravityForWalking = .25d;
        public double MinimumWalkSpeedMultiplier = .35d;
        public double MaximumWalkSpeedMultiplier = 2d;
        public double GravityWalkExponent = .5d;
        public double MinimumLaunchHeightMeters = 40d;
        public double LaunchHeightRadiusFraction = .1d;
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
        private static SpaceGameplaySettings current;
        public static SpaceGameplaySettings Current
        {
            get
            {
                if (current != null) return current;
                TextAsset source = Resources.Load<TextAsset>("Config/Space/space-gameplay");
                current = source == null ? new SpaceGameplaySettings() : JsonConvert.DeserializeObject<SpaceGameplaySettings>(source.text);
                if (current == null || current.EarthGravity <= 0d || current.MinimumGravityForWalking <= 0d ||
                    current.MinimumWalkSpeedMultiplier <= 0d || current.MaximumWalkSpeedMultiplier < current.MinimumWalkSpeedMultiplier ||
                    current.ImpactJoulesPerDurability <= 0d || current.LandingChoiceRadiusMeters <= 0d ||
                    current.ImpactForcePerDurability <= 0d || current.ImpactStoppingSeconds <= 0d || current.SurfaceEntryHeightMeters <= 0d ||
                    current.MinimumLaunchHeightMeters <= 0d || current.LaunchHeightRadiusFraction < 0d ||
                    current.MinimumSpaceExitSpeedMetersPerSecond <= 0d ||
                    !SpaceVector2.Finite(current.TakeoffDurationSeconds) || current.TakeoffDurationSeconds <= 0d ||
                    !SpaceVector2.Finite(current.LandingDurationSeconds) || current.LandingDurationSeconds <= 0d ||
                    !SpaceVector2.Finite(current.SpaceEntryClearanceMeters) || current.SpaceEntryClearanceMeters < 0d ||
                    !SpaceVector2.Finite(current.MaximumSafeLandingSpeedMetersPerSecond) || current.MaximumSafeLandingSpeedMetersPerSecond <= 0d ||
                    current.CruiseMaxSpeedMetersPerSecond <= 0d || current.CruiseAccelerationMetersPerSecondSquared <= 0d ||
                    current.CruiseBrakingMetersPerSecondSquared <= 0d ||
                    !SpaceVector2.Finite(current.CruiseTurnDegreesPerSecond) || current.CruiseTurnDegreesPerSecond <= 0d ||
                    !SpaceVector2.Finite(current.NavigationArrivalDistanceMeters) || current.NavigationArrivalDistanceMeters <= 0d ||
                    !SpaceVector2.Finite(current.NavigationArrivalSpeedMetersPerSecond) || current.NavigationArrivalSpeedMetersPerSecond <= 0d ||
                    !SpaceVector2.Finite(current.FreeFloatMaxSpeedMetersPerSecond) || current.FreeFloatMaxSpeedMetersPerSecond <= 0d ||
                    !SpaceVector2.Finite(current.FreeFloatAccelerationMultiplier) || current.FreeFloatAccelerationMultiplier <= 0f ||
                    !SpaceVector2.Finite(current.DrivingCameraSize) || current.DrivingCameraSize <= 0f ||
                    !SpaceVector2.Finite(current.DrivingCameraMaxSize) || current.DrivingCameraMaxSize < current.DrivingCameraSize ||
                    !SpaceVector2.Finite(current.DrivingCameraZoomMultiplier) || current.DrivingCameraZoomMultiplier <= 1f)
                    throw new InvalidOperationException("太空移动与冲击配置无效。");
                return current;
            }
        }
        public double WalkingMultiplier(double gravity)
            => Math.Clamp(Math.Pow(EarthGravity / Math.Max(MinimumGravityForWalking, gravity), GravityWalkExponent),
                MinimumWalkSpeedMultiplier, MaximumWalkSpeedMultiplier);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => current = null;
        #endregion
    }
}
