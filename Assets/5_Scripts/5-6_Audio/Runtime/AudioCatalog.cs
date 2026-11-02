using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FlatWorld.Audio
{
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "FlatWorld/Audio/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        private const string PropagationResourcePath = "Audio/PropagationRanges";

        [SerializeField] private List<AudioCue> cues = new List<AudioCue>();
        [SerializeField] private TextAsset propagationRanges;
        [NonSerialized] private bool loadBuiltInPropagationRanges;

        private readonly Dictionary<string, AudioCue> cueById =
            new Dictionary<string, AudioCue>(StringComparer.OrdinalIgnoreCase);

        #region JSON 传播档位

        private readonly Dictionary<string, float> propagationMaxDistanceByCueId =
            new(StringComparer.OrdinalIgnoreCase);

        private void RebuildPropagationDistanceIndex()
        {
            propagationMaxDistanceByCueId.Clear();
            if (propagationRanges == null && !loadBuiltInPropagationRanges) return;
            Dictionary<string, float> distances = BuildPropagationDistanceIndex(ReadPropagationConfigurationForCatalog());
            foreach (KeyValuePair<string, float> distance in distances)
                propagationMaxDistanceByCueId.Add(distance.Key, distance.Value);
        }

        private AudioPropagationConfiguration ReadPropagationConfigurationForCatalog()
        {
#if UNITY_EDITOR
            if (propagationRanges != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(propagationRanges).Replace('\\', '/');
                if (!assetPath.EndsWith("/Resources/Audio/PropagationRanges.json", StringComparison.OrdinalIgnoreCase))
                    return JsonUtility.FromJson<AudioPropagationConfiguration>(propagationRanges.text);
            }
#endif
            return ReadBuiltInPropagationConfiguration();
        }

        private static AudioPropagationConfiguration ReadBuiltInPropagationConfiguration()
        {
            return BuiltInContentBinaryCatalog.ReadResource(
                PropagationResourcePath,
                json => JsonUtility.FromJson<AudioPropagationConfiguration>(json));
        }

        private static Dictionary<string, float> BuildPropagationDistanceIndex(AudioPropagationConfiguration configuration)
        {
            Dictionary<string, float> distances = new(StringComparer.OrdinalIgnoreCase);
            if (configuration == null)
                return distances;
            // 档位从 1 开始，事件仅声明档位，距离统一由 JSON 配置持有。
            foreach (AudioCuePropagationLevel cue in configuration.cues)
                distances.Add(cue.id.Trim(), configuration.levels[cue.level - 1]);
            return distances;
        }

        [BuiltInContentBake]
        public static void BakeBuiltInConfiguration()
        {
            _ = BuildPropagationDistanceIndex(ReadBuiltInPropagationConfiguration());
        }

        private void ConfigureCuePropagationDistance(AudioCue cue)
        {
            propagationMaxDistanceByCueId.TryGetValue(cue.Id, out float distance);
            cue.ConfigurePropagationMaxDistance(distance);
        }

        #endregion

        public IReadOnlyList<AudioCue> Cues => cues;

        public bool TryGet(string id, out AudioCue cue)
        {
            cue = null;
            if (cueById.Count == 0 && cues.Count > 0)
                RebuildIndex();

            if (string.IsNullOrWhiteSpace(id))
                return false;

            string normalizedId = id.Trim();
            if (cueById.TryGetValue(normalizedId, out cue))
                return true;

            // 允许新 Cue 在 Catalog 尚未重建时立即可用；编辑器构建器之后仍会
            // 将它写回正式列表，因此不会把业务代码绑死在手工 Catalog 更新上。
            cue = Resources.Load<AudioCue>($"Audio/Cues/{normalizedId}");
            if (cue == null)
                return false;

            ConfigureCuePropagationDistance(cue);
            cueById[normalizedId] = cue;
            return true;
        }

        #region 声音事件目录校验

        public List<string> GetValidationErrors()
        {
            List<string> errors = new List<string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < cues.Count; i++)
            {
                AudioCue cue = cues[i];
                if (cue == null)
                {
                    errors.Add($"索引 {i} 的 AudioCue 为空");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cue.Id))
                {
                    errors.Add($"{cue.name} 缺少事件 ID");
                    continue;
                }

                if (!ids.Add(cue.Id))
                    errors.Add($"事件 ID 重复：{cue.Id}");

                // 停用事件可以暂不配置录音，启用事件则必须保留完整可播放引用。
                if (!cue.PlaybackEnabled)
                    continue;
                if (cue.Clips == null || cue.Clips.Count == 0)
                {
                    errors.Add($"声音事件 {cue.Id} 没有可播放的 AudioClip");
                    continue;
                }
                for (int clipIndex = 0; clipIndex < cue.Clips.Count; clipIndex++)
                    if (cue.Clips[clipIndex] == null)
                        errors.Add($"声音事件 {cue.Id} 的 AudioClip 索引 {clipIndex} 为空");
            }

            return errors;
        }

        #endregion

        public void RebuildIndex()
        {
            cueById.Clear();
            RebuildPropagationDistanceIndex();
            for (int i = 0; i < cues.Count; i++)
            {
                AudioCue cue = cues[i];
                if (cue == null || string.IsNullOrWhiteSpace(cue.Id))
                    continue;

                ConfigureCuePropagationDistance(cue);
                if (!cueById.ContainsKey(cue.Id))
                    cueById.Add(cue.Id, cue);
            }
        }

        internal static AudioCatalog CreateRuntimeCatalogFromResources()
        {
            AudioCue[] resourceCues = Resources.LoadAll<AudioCue>("Audio/Cues");
            AudioCatalog catalog = CreateInstance<AudioCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            catalog.loadBuiltInPropagationRanges = true;
            catalog.cues.AddRange(resourceCues);
            catalog.RebuildIndex();
            return catalog;
        }

        private void OnEnable()
        {
            RebuildIndex();
        }

        private void OnValidate()
        {
            RebuildIndex();
        }
    }

    [MemoryPack.MemoryPackable]
    [Serializable]
    internal sealed partial class AudioPropagationConfiguration
    {
        public float[] levels;
        public AudioCuePropagationLevel[] cues;
    }

    [MemoryPack.MemoryPackable]
    [Serializable]
    internal sealed partial class AudioCuePropagationLevel
    {
        public string id;
        public int level;
    }
}
