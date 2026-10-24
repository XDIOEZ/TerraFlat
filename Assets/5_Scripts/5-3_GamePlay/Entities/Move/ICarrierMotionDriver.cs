using UnityEngine;

/// <summary>座位保留统一承载租约，载具通过组合替换自己的运动和交互规则。</summary>
public interface ICarrierMotionDriver
{
    #region 载具运动策略
    bool AllowsRiderBoost { get; }
    void ActivateCarrier(Mod_Carrier carrier, Rigidbody2D body);
    void StepCarrierMotion(Mod_Carrier carrier, Vector2 input, bool controlsLocked, float deltaTime);
    void StopCarrierMotion();
    bool CanInteractWithCarrier(Player player, Mod_Mover mover);
    void InteractWithCarrier(Player player, Mod_Mover mover);
    #endregion
}

public interface ICarrierBoostPolicy
{
    bool AllowsRiderBoost { get; }
}

/// <summary>运动策略只转换推动速度，接触来源和有效期仍由通用载具持有。</summary>
public interface ICarrierPushMotionPolicy
{
    Vector2 ResolveCarrierPush(Vector2 velocity, bool sourceInWater);
    Vector2 ResolvePusherLimit(Vector2 velocity);
}
