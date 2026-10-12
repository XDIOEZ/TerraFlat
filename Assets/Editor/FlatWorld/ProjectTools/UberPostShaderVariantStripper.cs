using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.EditorTools
{
    /// <summary>只在打包时剔除 TerraFlat 没启用的 UberPost 效果，保留 Shader 和运行时泛光、调色。</summary>
    internal sealed class UberPostShaderVariantStripper : IPreprocessShaders
    {
        #region 后处理变体剔除

        private const string UberPostShaderName = "Hidden/Universal Render Pipeline/UberPost";

        // 当前 WorldPostProcessQuality 不创建这些效果；DefaultVolumeProfile 对应强度与镜头脏污均为零。
        // 日后启用镜头畸变、色差、胶片颗粒或 Bloom 镜头脏污时，先从此列表移除对应关键字。
        private static readonly string[] UnusedEffectKeywords =
        {
            "_DISTORTION",
            "_CHROMATIC_ABERRATION",
            "_FILM_GRAIN",
            "_BLOOM_LQ_DIRT",
            "_BLOOM_HQ_DIRT"
        };

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
        {
            if (shader == null || shader.name != UberPostShaderName || snippet.passName != "UberPost" || variants.Count == 0)
                return;

            ShaderKeyword[] keywords = new ShaderKeyword[UnusedEffectKeywords.Length];
            for (int i = 0; i < keywords.Length; i++)
                keywords[i] = new ShaderKeyword(shader, UnusedEffectKeywords[i]);

            for (int i = variants.Count - 1; i >= 0; i--)
            {
                ShaderKeywordSet keywordSet = variants[i].shaderKeywordSet;
                for (int j = 0; j < keywords.Length; j++)
                {
                    if (!keywordSet.IsEnabled(keywords[j]))
                        continue;

                    variants.RemoveAt(i);
                    break;
                }
            }
        }

        #endregion
    }
}
