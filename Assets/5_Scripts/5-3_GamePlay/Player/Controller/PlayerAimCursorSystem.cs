using UnityEngine;

/// <summary>
/// 玩家统一准星计算系统：集中处理摇杆死区、世界距离、屏幕径向位置和目标距离裁剪。
/// 默认死区从 0.18 降到 0.08，回中距离为 0，使准星可以回到玩家当前位置；具体最大距离由当前玩法提供。
/// </summary>
public sealed class PlayerAimCursorSystem
{
    public const float DefaultDeadZone = 0.08f;
    public const float DefaultMinWorldDistance = 0f;
    public const float DefaultMaxWorldDistance = 10f;

    private const float DirectionEpsilonSqr = 0.0001f;

    #region 配置

    public float DeadZone { get; set; } = DefaultDeadZone;

    #endregion

    #region 摇杆输入

    /// <summary>应用统一死区并返回有效方向和力度；进入死区时只清零力度，保留最后方向。</summary>
    public bool ApplyStickInput(Vector2 input, ref Vector2 direction, out float strength)
    {
        float magnitude = Mathf.Clamp01(input.magnitude);
        float deadZone = Mathf.Clamp01(DeadZone);
        if (input.sqrMagnitude <= DirectionEpsilonSqr || magnitude < deadZone)
        {
            strength = 0f;
            return false;
        }

        direction = input.normalized;
        strength = magnitude;
        return true;
    }

    #endregion

    #region 世界位置

    /// <summary>按方向和摇杆力度计算准星世界位置，最大距离由当前手持玩法传入。</summary>
    public Vector3 CalculateRadialWorldPosition(
        Vector3 origin,
        Vector2 direction,
        float strength,
        float minWorldDistance,
        float maxWorldDistance)
    {
        float minDistance = Mathf.Max(0f, minWorldDistance);
        float maxDistance = Mathf.Max(minDistance, maxWorldDistance);
        float distance = Mathf.Lerp(minDistance, maxDistance, Mathf.Clamp01(strength));
        Vector2 normalizedDirection = direction.sqrMagnitude > DirectionEpsilonSqr
            ? direction.normalized
            : Vector2.right;
        Vector3 position = origin + (Vector3)(normalizedDirection * distance);
        position.z = 0f;
        return WorldTopologyRuntime.NormalizePosition(position);
    }

    /// <summary>把鼠标或虚拟指针目标裁剪到玩家的有效世界距离内。</summary>
    public Vector3 ClampWorldPosition(Vector3 origin, Vector3 target, float maxWorldDistance)
    {
        if (float.IsNaN(maxWorldDistance) || float.IsPositiveInfinity(maxWorldDistance))
            return WorldTopologyRuntime.NormalizePosition(target);

        float maxDistance = Mathf.Max(0f, maxWorldDistance);
        Vector2 origin2D = new(origin.x, origin.y);
        Vector2 target2D = new(target.x, target.y);
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin2D, target2D);
        if (delta.sqrMagnitude <= maxDistance * maxDistance)
            return WorldTopologyRuntime.NormalizePosition(target);

        Vector3 clamped = origin + (Vector3)(delta.normalized * maxDistance);
        clamped.z = 0f;
        return WorldTopologyRuntime.NormalizePosition(clamped);
    }

    #endregion

    #region 屏幕位置

    /// <summary>按玩家屏幕位置和方向计算径向准星，并限制在屏幕安全范围内。</summary>
    public Vector2 CalculateRadialScreenPosition(
        Vector2 playerScreenPosition,
        Vector2 aimDirection,
        float radius,
        Vector2 screenSize,
        float padding)
    {
        Vector2 direction = aimDirection.sqrMagnitude > DirectionEpsilonSqr
            ? aimDirection.normalized
            : Vector2.right;
        Vector2 cursorPosition = playerScreenPosition + direction * Mathf.Max(1f, radius);

        float minX = Mathf.Min(Mathf.Max(0f, padding), screenSize.x * 0.5f);
        float maxX = Mathf.Max(minX, screenSize.x - padding);
        float minY = Mathf.Min(Mathf.Max(0f, padding), screenSize.y * 0.5f);
        float maxY = Mathf.Max(minY, screenSize.y - padding);
        cursorPosition.x = Mathf.Clamp(cursorPosition.x, minX, maxX);
        cursorPosition.y = Mathf.Clamp(cursorPosition.y, minY, maxY);
        return cursorPosition;
    }

    #endregion
}
