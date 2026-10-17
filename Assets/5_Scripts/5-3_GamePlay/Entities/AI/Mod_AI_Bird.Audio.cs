public sealed partial class Mod_AI_Bird : IAnimalAmbientAudioActivity
{
    #region 环境发声条件
    // 鸟和海鸥在普通停落、觅食和飞行时叫，逃离或捕鱼时暂停悠闲叫声。
    public bool AllowsAmbientAudio => loaded && escapeRemaining <= 0f &&
        !temperatureSafetyRetreat && !IsFishHunting &&
        (flightPilot is not IAnimalAmbientAudioActivity pilotActivity || pilotActivity.AllowsAmbientAudio);
    #endregion
}
