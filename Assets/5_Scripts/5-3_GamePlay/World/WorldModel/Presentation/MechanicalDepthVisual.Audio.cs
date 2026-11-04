public sealed partial class MechanicalDepthVisual
{
    #region 设施燃烧音效

    private MachineEntity burningAudioEntity;
    private CombustionAudioLoop burningAudio;

    // 声音只读取设备的真实燃烧状态，燃料和熔炼规则仍由设备逻辑负责。
    private void UpdateFacilityAudio(MachineEntity entity)
    {
        burningAudioEntity = entity;
        burningAudio.RefreshBurningAudio(transform, isActiveAndEnabled && IsFacilityBurning(entity));
    }

    private void StopFacilityAudio()
    {
        burningAudio.StopBurningAudio();
    }

    private void ReleaseFacilityAudio()
    {
        StopFacilityAudio();
        burningAudioEntity = null;
    }

    #endregion
}
