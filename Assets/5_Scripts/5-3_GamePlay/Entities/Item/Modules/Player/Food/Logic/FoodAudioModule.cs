using FlatWorld.Audio;

/// <summary>
/// 食物音频模块：只负责播放进食音效。
/// Mod_Food 主文件不依赖 AudioService，音频配置也放在独立分部文件中。
/// </summary>
public sealed class FoodAudioModule : IFoodMechanic, IFoodUseRule
{
    private readonly Mod_Food.ConsumeAudioSettings settings;

    public FoodAudioModule(IFoodRuntimeContext context)
    {
        Mod_Food food = context?.Item?.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);
        settings = food?.ConsumeAudio ?? new Mod_Food.ConsumeAudioSettings();
    }

    public string MechanicId => "core.audio";
    public int Priority => 0;

    public void OnFoodUse(FoodUseContext context)
    {
        if (context.Food?.Item == null)
            return;

        PlayEatSound();
    }

    private void PlayEatSound()
    {
        if (!settings.Enabled || AudioService.Instance == null)
            return;

        AudioService.Instance.Play(
            settings.ResolveCueId(),
            AudioPlayOptions.Global(settings.VolumeScale, settings.SamplePitch()));
    }
}
