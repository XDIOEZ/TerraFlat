using FlatWorld.Audio;
using UnityEngine;

public sealed partial class MechanicalDepthVisual
{
    #region 设施燃烧音效

    private MachineEntity burningAudioEntity;
    private AudioHandle burningAudioHandle;
    private string burningAudioCue;

    // 声音只读取设备的真实燃烧状态，燃料和熔炼规则仍由设备逻辑负责。
    private void UpdateFacilityAudio(MachineEntity entity)
    {
        burningAudioEntity = entity;
        MachineModuleConfiguration config = entity.Definition.Content.Find<Mod_Furnace>();
        string cue = config?.Authoring is Mod_Furnace source
            ? config.Value("burningLoopCueId", source.burningLoopCueId) : null;
        if (!isActiveAndEnabled || !IsFacilityBurning(entity) || string.IsNullOrWhiteSpace(cue))
        {
            StopFacilityAudio();
            return;
        }

        if (burningAudioCue != cue)
            StopFacilityAudio();
        if (burningAudioHandle.IsPlaying)
            return;

        if (!Application.isPlaying || !AudioService.HasInstance)
            return;
        var options = AudioPlayOptions.Attached(transform);
        options.OverrideLoop = true;
        options.Loop = true;
        options.FadeIn = 0.3f;
        burningAudioHandle = AudioService.Instance.Play(cue, options);
        burningAudioCue = cue;
    }

    private void StopFacilityAudio()
    {
        burningAudioHandle.Stop(0.3f);
        burningAudioHandle = AudioHandle.Invalid;
        burningAudioCue = null;
    }

    private void ReleaseFacilityAudio()
    {
        StopFacilityAudio();
        burningAudioEntity = null;
    }

    #endregion
}
