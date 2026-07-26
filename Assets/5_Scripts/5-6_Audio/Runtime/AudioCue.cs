using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace FlatWorld.Audio
{
    /// <summary>
    /// 声音事件的声明式配置。业务只引用稳定 ID，不直接依赖具体 AudioClip。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCue", menuName = "FlatWorld/Audio/Audio Cue")]
    public sealed class AudioCue : ScriptableObject
    {
        [SerializeField, Tooltip("稳定事件 ID，例如 item.axe.swing")]
        private string id;

        [SerializeField] private AudioBus bus = AudioBus.Sfx;
        [SerializeField] private List<AudioClip> clips = new List<AudioClip>();
        [SerializeField] private AudioClipSelection selection = AudioClipSelection.RandomNoRepeat;
        [SerializeField] private bool loop;
        [SerializeField] private AudioFloatRange volume = new AudioFloatRange(1f);
        [SerializeField] private AudioFloatRange pitch = new AudioFloatRange(1f);
        [SerializeField, Range(0f, 1f)] private float spatialBlend;
        [SerializeField, Min(0.01f)] private float minDistance = 1f;
        [SerializeField, Min(0.02f)] private float maxDistance = 20f;
        [SerializeField, Range(0, 256)] private int priority = 128;
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField, Min(1)] private int maxInstances = 4;
        [SerializeField] private AudioConcurrencyPolicy concurrencyPolicy = AudioConcurrencyPolicy.StopOldest;
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
        [SerializeField] private AudioMixerGroup outputOverride;

        private int lastClipIndex = -1;
        private int sequentialIndex;

        public string Id => id;
        public AudioBus Bus => bus;
        public IReadOnlyList<AudioClip> Clips => clips;
        public bool Loop => loop;
        public AudioFloatRange Volume => volume;
        public AudioFloatRange Pitch => pitch;
        public float SpatialBlend => spatialBlend;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public int Priority => priority;
        public float Cooldown => cooldown;
        public int MaxInstances => maxInstances;
        public AudioConcurrencyPolicy ConcurrencyPolicy => concurrencyPolicy;
        public AudioRolloffMode RolloffMode => rolloffMode;
        public AudioMixerGroup OutputOverride => outputOverride;

        internal AudioClip SelectClip()
        {
            if (clips == null || clips.Count == 0)
                return null;

            int index;
            switch (selection)
            {
                case AudioClipSelection.Sequential:
                    index = sequentialIndex % clips.Count;
                    sequentialIndex = (sequentialIndex + 1) % clips.Count;
                    break;

                case AudioClipSelection.Random:
                    index = UnityEngine.Random.Range(0, clips.Count);
                    break;

                default:
                    if (clips.Count == 1)
                    {
                        index = 0;
                    }
                    else
                    {
                        index = UnityEngine.Random.Range(0, clips.Count - 1);
                        if (index >= lastClipIndex)
                            index++;
                    }
                    break;
            }

            lastClipIndex = index;
            return clips[index];
        }

        private void OnValidate()
        {
            id = id == null ? string.Empty : id.Trim();
            minDistance = Mathf.Max(0.01f, minDistance);
            maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);
            maxInstances = Mathf.Max(1, maxInstances);
            volume.Min = Mathf.Max(0f, volume.Min);
            volume.Max = Mathf.Max(0f, volume.Max);
            pitch.Min = Mathf.Clamp(pitch.Min, -3f, 3f);
            pitch.Max = Mathf.Clamp(pitch.Max, -3f, 3f);
        }
    }
}
