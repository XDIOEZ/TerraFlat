/// <summary>物品动作自行控制位置时，通用手持跟随让出位置控制权。</summary>
public interface IHeldItemPositionControl
{
    #region 手持位置控制
    bool ControlsHeldItemPosition { get; }
    #endregion
}
