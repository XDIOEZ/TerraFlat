using FlatWorld.Audio;

/// <summary>
/// 食物音频模块：只负责播放进食音效。
/// Mod_Food 主文件不依赖 AudioService，音频配置也放在独立分部文件中。
/// </summary>
public sealed class FoodAudioModule : IFoodMechanic, IFoodUseRule
{
    #region 进食音效

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

        PlayEatSound(context.Consumer?.Item ?? context.Food.Item);
    }

    private void PlayEatSound(Item consumer)
    {
        if (!settings.Enabled || consumer == null)
            return;

        // 进食声音跟随食用者，远处角色吃东西也遵守左右声道和传播范围。
        AudioService.Instance.Play(
            settings.ResolveCueId(),
            AudioPlayOptions.Attached(consumer.transform, settings.VolumeScale, settings.SamplePitch()));
    }

    #endregion
}
