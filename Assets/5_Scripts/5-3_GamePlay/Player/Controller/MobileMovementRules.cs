using UnityEngine;

namespace FlatWorld.Mobile
{
    /// <summary>手机移动的分段速度曲线，以正常走速为一倍，保留独立的低速、走路平台和奔跑区。</summary>
    public static class MobileMovementRules
    {
        #region 摇杆速度曲线
        public const float LowSpeedEnd = 0.2f;
        public const float FullWalkStart = 0.5f;
        public const float RunStart = 0.7f;
        public const float LowSpeedFactor = 0.5f;

        public static float EvaluateSpeedFactor(float deflection, float deadZone, float runSpeedRate)
        {
            if (!float.IsFinite(deflection) || !float.IsFinite(deadZone)) return 0f;
            float distance = Mathf.Clamp01(deflection);
            float dead = Mathf.Clamp(deadZone, 0f, 0.95f);
            if (distance <= dead) return 0f;
            if (distance < LowSpeedEnd)
                return LowSpeedFactor * EaseOut(Mathf.InverseLerp(dead, LowSpeedEnd, distance));
            if (distance < FullWalkStart)
                return Mathf.Lerp(LowSpeedFactor, 1f,
                    EaseOut(Mathf.InverseLerp(LowSpeedEnd, FullWalkStart, distance)));
            if (distance <= RunStart) return 1f;
            float rate = float.IsFinite(runSpeedRate) ? Mathf.Max(1f, runSpeedRate) : 1f;
            return Mathf.Lerp(1f, rate, EaseOut(Mathf.InverseLerp(RunStart, 1f, distance)));
        }

        // 提速段前快后缓，让玩家更容易停在正常走速或接近满跑的速度。
        private static float EaseOut(float value) => value * (2f - value);
        #endregion
    }
}
