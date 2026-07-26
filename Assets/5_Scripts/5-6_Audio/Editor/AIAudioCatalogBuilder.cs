using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Audio;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.Audio.Editor
{
    /// <summary>
    /// 将 AI 生成的 “事件ID__变体.wav” 自动整理为 AudioCue 和 AudioCatalog。
    /// </summary>
    public static class AIAudioCatalogBuilder
    {
        public const string GeneratedFolder = "Assets/Audio/Generated";
        public const string CueFolder = "Assets/Resources/Audio/Cues";
        public const string CatalogPath = "Assets/Resources/Audio/AudioCatalog.asset";
        public const string RuntimeConfigPath = "Assets/Resources/Audio/AudioRuntimeConfig.asset";

        [MenuItem("Tools/FlatWorld/Audio/Rebuild AI Audio Catalog")]
        public static void Rebuild()
        {
            EnsureFolder(GeneratedFolder);
            EnsureFolder(CueFolder);

            Dictionary<string, List<AudioClip>> clipsByEvent =
                new Dictionary<string, List<AudioClip>>(StringComparer.OrdinalIgnoreCase);

            string[] clipGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { GeneratedFolder });
            Array.Sort(clipGuids, StringComparer.Ordinal);

            for (int i = 0; i < clipGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(clipGuids[i]);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    continue;

                string eventId = ParseEventId(path);
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    Debug.LogWarning($"[AIAudioCatalogBuilder] 无法从文件名解析事件 ID：{path}");
                    continue;
                }

                if (!clipsByEvent.TryGetValue(eventId, out List<AudioClip> clips))
                {
                    clips = new List<AudioClip>();
                    clipsByEvent.Add(eventId, clips);
                }

                clips.Add(clip);
            }

            List<AudioCue> cues = new List<AudioCue>();
            foreach (KeyValuePair<string, List<AudioClip>> pair in clipsByEvent.OrderBy(p => p.Key))
            {
                string cuePath = $"{CueFolder}/{ToSafeFileName(pair.Key)}.asset";
                AudioCue cue = AssetDatabase.LoadAssetAtPath<AudioCue>(cuePath);
                if (cue == null)
                {
                    cue = ScriptableObject.CreateInstance<AudioCue>();
                    cue.name = pair.Key;
                    AssetDatabase.CreateAsset(cue, cuePath);
                }

                ConfigureCue(cue, pair.Key, pair.Value);
                cues.Add(cue);
            }

            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            SerializedObject catalogObject = new SerializedObject(catalog);
            SerializedProperty cueArray = catalogObject.FindProperty("cues");
            cueArray.arraySize = cues.Count;
            for (int i = 0; i < cues.Count; i++)
                cueArray.GetArrayElementAtIndex(i).objectReferenceValue = cues[i];
            catalogObject.ApplyModifiedPropertiesWithoutUndo();
            catalog.RebuildIndex();
            EditorUtility.SetDirty(catalog);

            AudioRuntimeConfig runtimeConfig =
                AssetDatabase.LoadAssetAtPath<AudioRuntimeConfig>(RuntimeConfigPath);
            if (runtimeConfig == null)
            {
                runtimeConfig = ScriptableObject.CreateInstance<AudioRuntimeConfig>();
                AssetDatabase.CreateAsset(runtimeConfig, RuntimeConfigPath);
            }

            SerializedObject configObject = new SerializedObject(runtimeConfig);
            configObject.FindProperty("catalog").objectReferenceValue = catalog;
            configObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtimeConfig);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = catalog;
            Debug.Log($"[AIAudioCatalogBuilder] 完成：{cues.Count} 个事件，{clipGuids.Length} 个音频变体。");
        }

        [MenuItem("Tools/FlatWorld/Audio/Validate Audio Catalog")]
        public static void ValidateCatalog()
        {
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning($"[AIAudioCatalogBuilder] 尚未创建 Catalog，请先执行 Rebuild：{CatalogPath}");
                return;
            }

            List<string> errors = catalog.GetValidationErrors();
            if (errors.Count == 0)
            {
                Debug.Log($"[AIAudioCatalogBuilder] Catalog 验证通过，共 {catalog.Cues.Count} 个事件。", catalog);
                return;
            }

            Debug.LogError("[AIAudioCatalogBuilder] Catalog 验证失败：\n- " + string.Join("\n- ", errors), catalog);
        }

        private static void ConfigureCue(AudioCue cue, string eventId, List<AudioClip> clips)
        {
            SerializedObject cueObject = new SerializedObject(cue);
            cueObject.FindProperty("id").stringValue = eventId;

            SerializedProperty clipArray = cueObject.FindProperty("clips");
            clipArray.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
                clipArray.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];

            cueObject.FindProperty("bus").enumValueIndex = (int)InferBus(eventId);
            ApplyPlaybackDefaults(cueObject, eventId);
            cueObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cue);
        }

        private static void ApplyPlaybackDefaults(SerializedObject cueObject, string eventId)
        {
            bool isUi = eventId.StartsWith("ui.", StringComparison.OrdinalIgnoreCase);
            bool isWorldSfx = eventId.StartsWith("door.", StringComparison.OrdinalIgnoreCase) ||
                              eventId.StartsWith("item.", StringComparison.OrdinalIgnoreCase) ||
                              eventId.StartsWith("combat.", StringComparison.OrdinalIgnoreCase);

            cueObject.FindProperty("spatialBlend").floatValue = isWorldSfx ? 0.72f : 0f;
            cueObject.FindProperty("minDistance").floatValue = isWorldSfx ? 1.5f : 1f;
            cueObject.FindProperty("maxDistance").floatValue = isWorldSfx ? 14f : 20f;
            cueObject.FindProperty("priority").intValue = isUi ? 48 : 128;
            cueObject.FindProperty("cooldown").floatValue = isUi ? 0.025f : 0.04f;
            cueObject.FindProperty("maxInstances").intValue = isUi ? 3 : 5;
        }

        private static AudioBus InferBus(string eventId)
        {
            if (eventId.StartsWith("ui.", StringComparison.OrdinalIgnoreCase))
                return AudioBus.UI;
            if (eventId.StartsWith("music.", StringComparison.OrdinalIgnoreCase))
                return AudioBus.Music;
            if (eventId.StartsWith("ambient.", StringComparison.OrdinalIgnoreCase))
                return AudioBus.Ambient;
            if (eventId.StartsWith("voice.", StringComparison.OrdinalIgnoreCase))
                return AudioBus.Voice;
            return AudioBus.Sfx;
        }

        private static string ParseEventId(string assetPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            int variantSeparator = fileName.IndexOf("__", StringComparison.Ordinal);
            string eventId = variantSeparator >= 0 ? fileName.Substring(0, variantSeparator) : fileName;
            return eventId.Trim().Replace(' ', '.').ToLowerInvariant();
        }

        private static string ToSafeFileName(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (Array.IndexOf(invalidChars, characters[i]) >= 0)
                    characters[i] = '_';
            }

            return new string(characters);
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = path.Substring(0, path.LastIndexOf('/'));
            string folderName = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
