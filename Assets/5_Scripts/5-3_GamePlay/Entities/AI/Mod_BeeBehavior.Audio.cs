public sealed partial class Mod_BeeBehavior : IAnimalAmbientAudioActivity
{
    #region 环境发声条件
    // 护巢追击和夜间归巢不叠加悠闲蜂鸣，归巢卸载由动物音频模块统一释放声源。
    public bool AllowsAmbientAudio => IsRuntimeLoaded && !state.Angry && !nightSleepRequested;
    #endregion
}
