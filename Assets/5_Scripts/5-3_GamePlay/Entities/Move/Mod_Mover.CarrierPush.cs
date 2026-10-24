using UnityEngine;

public partial class Mod_Mover
{
    #region 载具推动阻力反馈
    private Mod_Carrier resistedCarrier;
    private Vector2 pushResistanceNormal;
    private float pushResistanceSpeed, pushResistanceUntil;
    public Vector2 CarrierPushIntentVelocity => DrivenVelocity;

    /// <summary>接触反馈只限制实际提交速度，保留未受限制的推动意图避免倍率闭环。</summary>
    public void SetCarrierPushResistance(Mod_Carrier carrier, Vector2 normal, float maximumSpeed)
    {
        if (carrier == null || !carrier.CanReceivePush(this) || !float.IsFinite(normal.sqrMagnitude) ||
            normal.sqrMagnitude < .0001f || !float.IsFinite(maximumSpeed) || maximumSpeed < 0f) return;
        resistedCarrier = carrier; pushResistanceNormal = normal.normalized;
        pushResistanceSpeed = maximumSpeed; pushResistanceUntil = Time.time + .1f;
        if (rb != null && rb.simulated) rb.velocity = ResolveCarrierPushResistance(DrivenVelocity + ExternalVelocity);
    }

    private Vector2 ResolveCarrierPushResistance(Vector2 velocity)
    {
        if (resistedCarrier == null) return velocity;
        if (rb == null || !resistedCarrier.IsAvailable || !resistedCarrier.CanReceivePush(this) ||
            resistedCarrier.gameObject.scene != gameObject.scene || Time.time > pushResistanceUntil ||
            Vector2.Dot(DrivenVelocity, pushResistanceNormal) <= .0001f ||
            WorldTopologyRuntime.Distance(rb.position, resistedCarrier.Body.position) >
                resistedCarrier.PushHalfExtents.magnitude + pushContactRadius + .25f)
        { ClearCarrierPushResistance(); return velocity; }
        return LimitPushIntentVelocity(velocity, DrivenVelocity, pushResistanceNormal, pushResistanceSpeed);
    }

    public static Vector2 LimitPushIntentVelocity(Vector2 velocity, Vector2 intent, Vector2 normal, float maximumSpeed)
    {
        if (!float.IsFinite(velocity.sqrMagnitude + intent.sqrMagnitude + normal.sqrMagnitude + maximumSpeed) ||
            normal.sqrMagnitude < .0001f || maximumSpeed < 0f) return Vector2.zero;
        normal.Normalize();
        float excess = Vector2.Dot(intent, normal) - maximumSpeed;
        return excess > 0f ? velocity - normal * excess : velocity;
    }

    private void ClearCarrierPushResistance()
    { resistedCarrier = null; pushResistanceNormal = Vector2.zero; pushResistanceSpeed = pushResistanceUntil = 0f; }
    #endregion
}
