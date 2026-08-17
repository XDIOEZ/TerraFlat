using FlatWorld.Audio;
using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>食物音频配置；与 Mod_Food 主逻辑分开，保留原有 ConsumeAudio 字段格式。</summary>
public partial class Mod_Food
{
    [Serializable]
    public sealed class ConsumeAudioSettings
    {
        [LabelText("启用进食音效")]
        public bool Enabled = true;

        [LabelText("音效 Cue ID")]
        [Tooltip("默认 food.eat；也可以配置 food.crunch、food.drink 等 AudioCatalog Cue ID。")]
        public string CueId = AudioEventIds.FoodEat;

        [LabelText("音量")]
        [Range(0f, 2f)]
        public float VolumeScale = 0.78f;

        [LabelText("音高最小值")]
        [MinValue(0.01f)]
        public float PitchMin = 0.96f;

        [LabelText("音高最大值")]
        [MinValue(0.01f)]
        public float PitchMax = 1.04f;

        public string ResolveCueId()
        {
            return string.IsNullOrWhiteSpace(CueId) ? AudioEventIds.FoodEat : CueId.Trim();
        }

        public float SamplePitch()
        {
            float min = Mathf.Max(0.01f, Mathf.Min(PitchMin, PitchMax));
            float max = Mathf.Max(min, Mathf.Max(PitchMin, PitchMax));
            return Mathf.Approximately(min, max) ? min : UnityEngine.Random.Range(min, max);
        }
    }

    [FoldoutGroup("音频")]
    [LabelText("进食音效")]
    [Tooltip("每种食物可以单独配置 Cue、音量和音高范围。")]
    public ConsumeAudioSettings ConsumeAudio = new ConsumeAudioSettings();
}
