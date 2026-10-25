using System;
using UnityEngine;

public sealed partial class WorldRenderingConfig
{
    #region 风条纹配置

    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class RenderingWindStreaks
    {
        public float startStrength;
        public float fullStrength;
        public float minimumSpeed;
        public float maximumSpeed;
        public float minimumLength;
        public float maximumLength;
        public float width;
        public Vector2 cellSize;
        public float maximumOpacity;
        public Color color;

        [MemoryPack.MemoryPackIgnore]
        public bool IsValid =>
            float.IsFinite(startStrength) && startStrength >= 0f &&
            float.IsFinite(fullStrength) && fullStrength > startStrength && fullStrength <= 1f &&
            float.IsFinite(minimumSpeed) && minimumSpeed > 0f &&
            float.IsFinite(maximumSpeed) && maximumSpeed >= minimumSpeed &&
            float.IsFinite(minimumLength) && minimumLength > 0f &&
            float.IsFinite(maximumLength) && maximumLength >= minimumLength &&
            float.IsFinite(width) && width > 0f &&
            float.IsFinite(cellSize.x) && cellSize.x > maximumLength * 2f &&
            float.IsFinite(cellSize.y) && cellSize.y > width * 8f &&
            float.IsFinite(maximumOpacity) && maximumOpacity > 0f && maximumOpacity <= 1f &&
            float.IsFinite(color.r) && color.r >= 0f && color.r <= 1f &&
            float.IsFinite(color.g) && color.g >= 0f && color.g <= 1f &&
            float.IsFinite(color.b) && color.b >= 0f && color.b <= 1f &&
            float.IsFinite(color.a) && color.a > 0f && color.a <= 1f;

        public bool TryResolve(float strength, out Vector4 parameters)
        {
            parameters = default;
            if (!IsValid || !float.IsFinite(strength) || strength <= startStrength)
                return false;

            // 风力只调整流速、透明度、数量和长度，不用总时间乘风速造成突然跳位。
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startStrength, fullStrength, strength));
            parameters = new Vector4(Mathf.Lerp(minimumSpeed, maximumSpeed, blend),
                maximumOpacity * color.a * blend, Mathf.Lerp(0.12f, 0.78f, blend),
                Mathf.Lerp(minimumLength, maximumLength, blend));
            return parameters.y > 0.0005f;
        }
    }

    #endregion
}
