using UnityEngine;

/// <summary>承载源拥有位移积分；乘员提交输入并应用座位位置，速度和力始终可追溯到源。</summary>
public interface ICarrierMotionSource
{
    #region 承载契约
    Component SourceComponent { get; }
    Vector2 SeatPosition { get; }
    Vector2 CurrentVelocity { get; }
    Vector2 CurrentForce { get; }
    bool IsAvailable { get; }
    void AdvanceMotion(Mod_Mover rider, Vector2 input, float deltaTime, bool controlsLocked, bool boostRequested);
    void ReleaseRider(Mod_Mover rider);
    #endregion
}

/// <summary>载具决定乘员能否接触地形；船舱隔绝环境，敞开车辆保留真实接触。</summary>
public interface ICarrierRiderTerrainContactPolicy
{
    bool AllowsRiderTerrainContact { get; }
}

/// <summary>侧视载具可让座位偏移跟着车体视觉左右翻面。</summary>
public interface ICarrierSeatOffsetResolver
{
    Vector2 ResolveSeatOffset(Vector2 configuredOffset);
}
