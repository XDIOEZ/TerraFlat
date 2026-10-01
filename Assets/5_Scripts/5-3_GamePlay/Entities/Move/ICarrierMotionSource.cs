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
