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
        public double CruiseMaxSpeedMetersPerSecond = 120d;
        public double CruiseAccelerationMetersPerSecondSquared = 60d;
        public double CruiseBrakingMetersPerSecondSquared = 100d;
        public double CruiseTurnDegreesPerSecond = 45d; // 飞船驾驶与导航共用的直接旋转速度上限。
        public double NavigationArrivalDistanceMeters = 1d;
        public double NavigationArrivalSpeedMetersPerSecond = .25d;
        public double NavigationSurfaceClearanceMeters = 12d;
        public double FreeFloatMaxSpeedMetersPerSecond = 12d;
        public double FreeFloatBrakingMetersPerSecondSquared = 2d;
        public double SurfaceEntryHeightMeters = 80d;
        public double ImpactStoppingSeconds = .25d;
        public double ImpactForcePerDurability = 1000d;
        public double LandingChoiceRadiusMeters = 10d;
        public double WallPushSpeedMetersPerSecond = 8d;
        public float PlayerTurnDegreesPerSecond = 45f; // 失重玩家朝鼠标方向转动本体的最大角速度。
        public float DrivingCameraSize = 30f;
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
                    current.CruiseMaxSpeedMetersPerSecond <= 0d || current.CruiseAccelerationMetersPerSecondSquared <= 0d ||
                    current.CruiseBrakingMetersPerSecondSquared <= 0d ||
                    !SpaceVector2.Finite(current.CruiseTurnDegreesPerSecond) || current.CruiseTurnDegreesPerSecond <= 0d ||
                    !SpaceVector2.Finite(current.NavigationArrivalDistanceMeters) || current.NavigationArrivalDistanceMeters <= 0d ||
                    !SpaceVector2.Finite(current.NavigationArrivalSpeedMetersPerSecond) || current.NavigationArrivalSpeedMetersPerSecond <= 0d ||
                    !SpaceVector2.Finite(current.NavigationSurfaceClearanceMeters) || current.NavigationSurfaceClearanceMeters < current.NavigationArrivalDistanceMeters ||
                    current.FreeFloatMaxSpeedMetersPerSecond <= 0d || current.FreeFloatBrakingMetersPerSecondSquared <= 0d ||
                    !SpaceVector2.Finite(current.PlayerTurnDegreesPerSecond) || current.PlayerTurnDegreesPerSecond <= 0f)
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
