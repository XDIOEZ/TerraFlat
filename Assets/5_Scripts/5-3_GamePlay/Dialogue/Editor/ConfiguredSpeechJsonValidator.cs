using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.Dialogue.Editor
{
    /// <summary>
    /// 开发者主动校验 Resources 中全部角色自言自语 JSON 的菜单入口。
    /// </summary>
    public static class ConfiguredSpeechJsonValidator
    {
        private const string ConfigFolder = "Assets/Resources/Dialogue/Soliloquy";

        #region 菜单入口

        [MenuItem("FlatWorld/自言自语/校验配置 JSON")]
        [MenuItem("FlatWorld/Dialogue/Validate Soliloquy JSON")]
        public static void ValidateAll()
        {
            CharacterSpeechConfigLoadResult result =
                CharacterSpeechConfigLoader.LoadSources(CollectSources(), logIssues: false);

            for (int i = 0; i < result.Issues.Count; i++)
                Debug.LogError(result.Issues[i].ToString());

            if (result.HasErrors)
            {
                Debug.LogError(
                    $"[自言自语配置] 校验失败：{result.Issues.Count} 个错误，" +
                    $"其余 {result.Entries.Count} 个有效条目仍可加载。");
                return;
            }

            Debug.Log($"[自言自语配置] 校验通过，共 {result.Entries.Count} 个有效条目。");
        }

        #endregion

        #region 配置收集

        private static List<CharacterSpeechConfigSource> CollectSources()
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { ConfigFolder });
            List<string> paths = new(guids.Length);
            for (int i = 0; i < guids.Length; i++)
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            paths.Sort(StringComparer.Ordinal);

            List<CharacterSpeechConfigSource> sources = new(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                sources.Add(new CharacterSpeechConfigSource(
                    Path.GetFileName(path),
                    File.ReadAllText(path)));
            }

            return sources;
        }

        #endregion
    }
}
