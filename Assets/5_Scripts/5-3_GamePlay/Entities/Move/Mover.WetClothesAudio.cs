using System;
using FlatWorld.Audio;
using UnityEngine;

/// <summary>玩家潮湿衣物移动音效；由移动、Buff 和装备事件共同驱动。</summary>
public partial class Mover
{
    #region 潮湿衣物音效

    private const float WetClothesAudioFadeSeconds = 0.08f;
    private const int WetClothesFallbackMaxStacks = 10;

    private BuffManager wetClothesBuffManager;
    private Mod_Equipment wetClothesEquipment;
    private AudioHandle wetClothesAudioHandle = AudioHandle.Invalid;
    private int wetClothesAppliedStacks;

    private void InitializeWetClothesAudio()
    {
        DisposeWetClothesAudio();

        if (item is not Player player || !player.IsLocalProfile)
            return;

        wetClothesBuffManager = item.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        wetClothesEquipment = item.itemMods?.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);

        if (wetClothesBuffManager != null)
        {
            wetClothesBuffManager.BuffAdded += HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffRemoved += HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffStacksChanged += HandleWetClothesBuffChanged;
        }

        if (wetClothesEquipment != null)
            wetClothesEquipment.EquipmentChanged += HandleWetClothesEquipmentChanged;

        RefreshWetClothesAudio();
    }

    private void DisposeWetClothesAudio()
    {
        if (wetClothesBuffManager != null)
        {
            wetClothesBuffManager.BuffAdded -= HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffRemoved -= HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffStacksChanged -= HandleWetClothesBuffChanged;
        }

        if (wetClothesEquipment != null)
            wetClothesEquipment.EquipmentChanged -= HandleWetClothesEquipmentChanged;

        StopWetClothesAudio();
        wetClothesBuffManager = null;
        wetClothesEquipment = null;
    }

    private void HandleWetClothesEquipmentChanged()
    {
        RefreshWetClothesAudio();
    }

    private void HandleWetClothesBuffChanged(BuffInstance runtime)
    {
        if (runtime == null ||
            !string.Equals(runtime.DefinitionId, WetBuffIds.Wet, StringComparison.OrdinalIgnoreCase))
            return;

        RefreshWetClothesAudio();
    }

    private void RefreshWetClothesAudio()
    {
        int wetStacks = ResolveWetClothesStacks(out int maxStacks);
        bool shouldPlay = IsMoving &&
                          wetStacks > 0 &&
                          wetClothesEquipment != null &&
                          wetClothesEquipment.HasAnyEquippedItem();

        if (!shouldPlay)
        {
            StopWetClothesAudio();
            return;
        }

        if (wetClothesAudioHandle.IsPlaying && wetClothesAppliedStacks == wetStacks)
            return;

        StopWetClothesAudio();

        float wetness = maxStacks <= 1
            ? 1f
            : Mathf.InverseLerp(1f, maxStacks, wetStacks);
        float volumeScale = Mathf.Lerp(0.48f, 1.12f, wetness);
        float pitchScale = Mathf.Lerp(1.03f, 0.84f, wetness);
        AudioPlayOptions options = AudioPlayOptions.Attached(item.transform, volumeScale, pitchScale);
        options.OverrideLoop = true;
        options.Loop = true;
        options.FadeIn = WetClothesAudioFadeSeconds;

        wetClothesAudioHandle = AudioService.Instance.Play(
            AudioEventIds.PlayerWetClothesMoveLoop,
            options);
        wetClothesAppliedStacks = wetClothesAudioHandle.IsValid ? wetStacks : 0;
    }

    private int ResolveWetClothesStacks(out int maxStacks)
    {
        maxStacks = WetClothesFallbackMaxStacks;
        if (wetClothesBuffManager == null ||
            !wetClothesBuffManager.TryGetBuff(WetBuffIds.Wet, out BuffInstance wetBuff) ||
            wetBuff == null)
            return 0;

        maxStacks = Mathf.Max(1, wetBuff.Definition?.MaxStacks ?? WetClothesFallbackMaxStacks);
        return Mathf.Clamp(wetBuff.StackCount, 0, maxStacks);
    }

    private void StopWetClothesAudio()
    {
        if (wetClothesAudioHandle.IsValid)
            wetClothesAudioHandle.Stop(WetClothesAudioFadeSeconds);

        wetClothesAudioHandle = AudioHandle.Invalid;
        wetClothesAppliedStacks = 0;
    }

    #endregion
}
