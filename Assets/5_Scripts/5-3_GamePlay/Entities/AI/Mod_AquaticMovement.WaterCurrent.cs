public sealed partial class Mod_AquaticMovement : IWaterCurrentExposure
{
    #region 水流免疫
    // 鱼主动控制游动，海洋与河流的表层水流都不接管其位移。
    public bool ReceivesWaterCurrent => false;
    #endregion
}
