using UnityEngine;

/// <summary>玩家选择屏幕方向，动力只决定矿车能够达到的速度和加速能力。</summary>
public static class MinecartDrivingRules
{
    #region 输入与动力运动规则
    public static float ResolveThrottle(Vector2 input, Vector2 tangent)
    {
        Vector2 heldDirection = Vector2.zero;
        float heldSign = 0f;
        return ResolveThrottle(input, tangent, ref heldDirection, ref heldSign);
    }

    /// <summary>按下时选择沿轨方向，长按保持该方向，油门只取输入幅度。</summary>
    public static float ResolveThrottle(Vector2 input, Vector2 tangent, ref Vector2 heldDirection, ref float heldSign)
    {
        if (!float.IsFinite(input.sqrMagnitude) || !float.IsFinite(tangent.sqrMagnitude) ||
            input.sqrMagnitude <= .001f || tangent.sqrMagnitude < .0001f)
        { heldDirection = Vector2.zero; heldSign = 0f; return 0f; }

        float strength = Mathf.Clamp01(input.magnitude);
        Vector2 direction = input.normalized;
        float alignment = Vector2.Dot(direction, heldDirection);
        if (Mathf.Abs(heldSign) > .5f && alignment >= .999f)
            return heldSign * strength;

        float projection = Vector2.Dot(direction, tangent.normalized);
        if (Mathf.Abs(projection) > .01f) heldSign = Mathf.Sign(projection);
        else if (Mathf.Abs(heldSign) > .5f && alignment <= -.999f) heldSign = -heldSign;
        else { heldDirection = Vector2.zero; heldSign = 0f; return 0f; }
        heldDirection = direction;
        return heldSign * strength;
    }

    public static float NextSpeed(float current, float throttle, float maximum, float supplied, float required,
        float acceleration, float braking, float seconds)
    {
        if (!(supplied > .0001f) || !(required > 0f) || Mathf.Abs(throttle) <= .01f) return 0f;
        float ratio = Mathf.Clamp01(supplied / required);
        float target = throttle * maximum * Mathf.Sqrt(ratio);
        float response = Mathf.Abs(target) < Mathf.Abs(current) ? braking : acceleration * ratio;
        return Mathf.MoveTowards(current, target, response * seconds);
    }

    /// <summary>各节地面阻力共同限制整列速度，轨道阻力基准为一。</summary>
    public static float SurfaceMultiplier(int cartCount, int groundCount, float groundMultiplier)
    {
        if (cartCount <= 0 || groundCount < 0 || groundCount > cartCount ||
            !float.IsFinite(groundMultiplier) || groundMultiplier <= 0f || groundMultiplier > 1f) return 0f;
        return cartCount / (cartCount - groundCount + groundCount / groundMultiplier);
    }

    public static Vector2 ResolvePushVelocity(Vector2 velocity, Vector2 tangent, bool onRail, float multiplier)
    {
        if (!float.IsFinite(velocity.sqrMagnitude) || !float.IsFinite(multiplier) || multiplier < 0f) return Vector2.zero;
        if (!onRail) return velocity * multiplier;
        if (!float.IsFinite(tangent.sqrMagnitude) || tangent.sqrMagnitude < .0001f) return Vector2.zero;
        Vector2 direction = tangent.normalized;
        return direction * (Vector2.Dot(velocity, direction) * multiplier);
    }

    public static Vector2 NextSurfaceVelocity(Vector2 current, Vector2 desired, float acceleration, float braking, float deltaTime)
    {
        if (!float.IsFinite(current.sqrMagnitude) || !float.IsFinite(desired.sqrMagnitude) ||
            !float.IsFinite(acceleration + braking + deltaTime) || acceleration < 0f || braking < 0f || deltaTime < 0f)
            return Vector2.zero;
        float response = desired.sqrMagnitude < current.sqrMagnitude || Vector2.Dot(current, desired) < 0f ? braking : acceleration;
        return Vector2.MoveTowards(current, desired, response * deltaTime);
    }

    public static bool ShouldCaptureRail(float distance, float captureDistance, float outwardSpeed, bool atEndpoint)
        => float.IsFinite(distance + captureDistance + outwardSpeed) && distance >= 0f && captureDistance > 0f &&
            distance <= captureDistance && (!atEndpoint || distance <= .0001f || outwardSpeed < -.001f);
    #endregion
}
