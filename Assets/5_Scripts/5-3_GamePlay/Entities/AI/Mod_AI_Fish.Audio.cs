public sealed partial class Mod_AI_Fish : IAnimalAmbientAudioActivity
{
    #region 环境发声条件
    // 水动短音只在正常游动和觅食时出现，离水、逃离、钓住和被叼走时停止。
    public bool AllowsAmbientAudio => loaded && !temperatureSafetyRetreat && predatorCarrier == null &&
        !IsHooked && (machine.CurrentState == Behaviour.Swim || machine.CurrentState == Behaviour.Forage);
    #endregion
}
