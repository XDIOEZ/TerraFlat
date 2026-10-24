using System;

namespace FlatWorld.Navigation
{
    /// <summary>追近停止和动作范围分别判定，所有移动方式共用同一份内圈余量。</summary>
    public static class AIApproachRange
    {
        #region 接近与动作距离
        public const float MaximumStopRangeRatio = 0.5f;

        public static float ResolveStopDistance(float preferredStopDistance, float actionRange)
        {
            if (!(actionRange > 0f && actionRange < float.PositiveInfinity)) return 0f;
            float maximum = actionRange * MaximumStopRangeRatio;
            if (!(preferredStopDistance >= 0f && preferredStopDistance < float.PositiveInfinity))
                return maximum;
            return Math.Min(preferredStopDistance, maximum);
        }

        public static bool IsInActionRange(float squaredDistance, float actionRange)
            => squaredDistance >= 0f && squaredDistance < float.PositiveInfinity &&
               actionRange > 0f && actionRange < float.PositiveInfinity &&
               squaredDistance <= actionRange * actionRange;

        public static float ClampMovementStep(float distance, float maximumStep, float stopDistance)
            => Math.Min(Math.Max(0f, distance - stopDistance), Math.Max(0f, maximumStep));

        // 接近中也能推进动作，只有真正离开动作范围才清零，不能以是否停车作为条件。
        public static float AdvanceActionTimer(float elapsed, float deltaTime, float squaredDistance, float actionRange)
            => IsInActionRange(squaredDistance, actionRange)
                ? Math.Max(0f, elapsed) + Math.Max(0f, deltaTime)
                : 0f;
        #endregion
    }
}
