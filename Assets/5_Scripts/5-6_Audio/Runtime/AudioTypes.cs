using System;
using UnityEngine;

namespace FlatWorld.Audio
{
    public enum AudioBus
    {
        Music,
        Sfx,
        UI,
        Ambient,
        Voice
    }

    public enum AudioClipSelection
    {
        RandomNoRepeat,
        Random,
        Sequential
    }

    public enum AudioConcurrencyPolicy
    {
        RejectNew,
        StopOldest
    }

    [Serializable]
    public struct AudioFloatRange
    {
        public float Min;
        public float Max;

        public AudioFloatRange(float value)
        {
            Min = value;
            Max = value;
        }

        public AudioFloatRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Sample()
        {
            float min = Mathf.Min(Min, Max);
            float max = Mathf.Max(Min, Max);
            return Mathf.Approximately(min, max) ? min : UnityEngine.Random.Range(min, max);
        }
    }

    /// <summary>
    /// 单次播放的运行时覆盖参数。业务代码通常使用 Global/At/Attached 三个工厂方法。
    /// </summary>
    public struct AudioPlayOptions
    {
        public Transform FollowTarget;
        public Vector3 WorldPosition;
        public bool HasWorldPosition;
        public float VolumeScale;
        public float PitchScale;
        public float FadeIn;
        public bool OverrideLoop;
        public bool Loop;

        public static AudioPlayOptions Global(float volumeScale = 1f, float pitchScale = 1f)
        {
            return new AudioPlayOptions
            {
                VolumeScale = volumeScale,
                PitchScale = pitchScale
            };
        }

        public static AudioPlayOptions At(Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
        {
            return new AudioPlayOptions
            {
                WorldPosition = position,
                HasWorldPosition = true,
                VolumeScale = volumeScale,
                PitchScale = pitchScale
            };
        }

        public static AudioPlayOptions Attached(Transform target, float volumeScale = 1f, float pitchScale = 1f)
        {
            return new AudioPlayOptions
            {
                FollowTarget = target,
                WorldPosition = target != null ? target.position : Vector3.zero,
                HasWorldPosition = target != null,
                VolumeScale = volumeScale,
                PitchScale = pitchScale
            };
        }

        internal void Normalize()
        {
            if (VolumeScale <= 0f)
                VolumeScale = 1f;
            if (PitchScale <= 0f)
                PitchScale = 1f;
            FadeIn = Mathf.Max(0f, FadeIn);
        }
    }

    /// <summary>
    /// 安全的播放句柄。声音被复用后，旧句柄不会误停新的声音。
    /// </summary>
    public readonly struct AudioHandle : IEquatable<AudioHandle>
    {
        public static readonly AudioHandle Invalid = new AudioHandle(0);

        internal int VoiceId { get; }

        internal AudioHandle(int voiceId)
        {
            VoiceId = voiceId;
        }

        public bool IsValid => VoiceId > 0;
        public bool IsPlaying => AudioService.IsHandlePlaying(this);

        public void Stop(float fadeOut = 0f)
        {
            AudioService.TryStopHandle(this, fadeOut);
        }

        public bool Equals(AudioHandle other) => VoiceId == other.VoiceId;
        public override bool Equals(object obj) => obj is AudioHandle other && Equals(other);
        public override int GetHashCode() => VoiceId;
        public static bool operator ==(AudioHandle left, AudioHandle right) => left.Equals(right);
        public static bool operator !=(AudioHandle left, AudioHandle right) => !left.Equals(right);
    }

    [Serializable]
    public sealed class AudioUserSettings
    {
        [Range(0f, 1f)] public float Master = 1f;
        [Range(0f, 1f)] public float Music = 0.8f;
        [Range(0f, 1f)] public float Sfx = 1f;
        [Range(0f, 1f)] public float UI = 1f;
        [Range(0f, 1f)] public float Ambient = 0.8f;
        [Range(0f, 1f)] public float Voice = 1f;
        public bool Muted;

        public float GetBusVolume(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return Music;
                case AudioBus.Sfx: return Sfx;
                case AudioBus.UI: return UI;
                case AudioBus.Ambient: return Ambient;
                case AudioBus.Voice: return Voice;
                default: return 1f;
            }
        }

        public void SetBusVolume(AudioBus bus, float value)
        {
            value = Mathf.Clamp01(value);
            switch (bus)
            {
                case AudioBus.Music: Music = value; break;
                case AudioBus.Sfx: Sfx = value; break;
                case AudioBus.UI: UI = value; break;
                case AudioBus.Ambient: Ambient = value; break;
                case AudioBus.Voice: Voice = value; break;
            }
        }

        public void Clamp()
        {
            Master = Mathf.Clamp01(Master);
            Music = Mathf.Clamp01(Music);
            Sfx = Mathf.Clamp01(Sfx);
            UI = Mathf.Clamp01(UI);
            Ambient = Mathf.Clamp01(Ambient);
            Voice = Mathf.Clamp01(Voice);
        }
    }

    /// <summary>
    /// 项目内约定的稳定事件 ID。新音效可以直接使用同样的点号命名规则。
    /// </summary>
    public static class AudioEventIds
    {
        public const string UiClick = "ui.click";
        public const string UiHover = "ui.hover";
        public const string UiConfirm = "ui.confirm";
        public const string UiCancel = "ui.cancel";
        public const string ItemAct = "item.act";
        public const string ItemPickup = "item.pickup";
        public const string ItemDrop = "item.drop";
        public const string DoorOpen = "door.open";
        public const string DoorClose = "door.close";
        public const string CombatHit = "combat.hit";
        public const string CombatWeaponGenericAttack = "combat.weapon.generic.attack";
        public const string CombatWeaponKnifeAttack = "combat.weapon.knife.attack";
        public const string CombatWeaponAxeAttack = "combat.weapon.axe.attack";
        public const string CombatWeaponPickaxeAttack = "combat.weapon.pickaxe.attack";
        public const string CombatWeaponSpearAttack = "combat.weapon.spear.attack";
        public const string CombatWeaponBluntAttack = "combat.weapon.blunt.attack";
        public const string CombatImpactDefault = "combat.impact.default";
        public const string CombatImpactFoliage = "combat.impact.foliage";
        public const string CombatImpactWood = "combat.impact.wood";
        public const string CombatImpactStone = "combat.impact.stone";
        public const string CombatImpactMetal = "combat.impact.metal";
        public const string CombatImpactFlesh = "combat.impact.flesh";
        public const string CombatImpactKnifeFoliage = "combat.impact.knife.foliage";
        public const string CombatImpactKnifeStone = "combat.impact.knife.stone";
        public const string CombatImpactAxeWood = "combat.impact.axe.wood";
        public const string CombatImpactPickaxeStone = "combat.impact.pickaxe.stone";
        public const string FoodEat = "food.eat";
        public const string FoodCrunch = "food.crunch";
        public const string FoodDrink = "food.drink";
    }
}
