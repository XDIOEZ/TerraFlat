using UnityEngine;

/// <summary>
/// 配置手持物美术主轴与通用瞄准轴之间的角度差。
/// 通用瞄准默认让本地 +X 指向目标；例如火把火苗位于本地 +Y，因此使用 -90° 修正。
/// </summary>
public sealed class HandAimOrientation : MonoBehaviour
{
    #region Configuration

    [Tooltip("手持时追加到鼠标瞄准角度上的本地角度偏移")]
    [SerializeField] private float angleOffsetDegrees;

    /// <summary>手持瞄准角度偏移。</summary>
    public float AngleOffsetDegrees => angleOffsetDegrees;

    #endregion
}
