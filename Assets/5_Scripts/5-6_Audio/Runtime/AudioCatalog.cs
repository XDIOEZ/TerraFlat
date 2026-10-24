using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Audio
{
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "FlatWorld/Audio/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        [SerializeField] private List<AudioCue> cues = new List<AudioCue>();

        private readonly Dictionary<string, AudioCue> cueById =
            new Dictionary<string, AudioCue>(StringComparer.OrdinalIgnoreCase);

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
            for (int i = 0; i < cues.Count; i++)
            {
                AudioCue cue = cues[i];
                if (cue == null || string.IsNullOrWhiteSpace(cue.Id))
                    continue;

                if (!cueById.ContainsKey(cue.Id))
                    cueById.Add(cue.Id, cue);
            }
        }

        internal static AudioCatalog CreateRuntimeCatalogFromResources()
        {
            AudioCue[] resourceCues = Resources.LoadAll<AudioCue>("Audio/Cues");
            AudioCatalog catalog = CreateInstance<AudioCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
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
}
