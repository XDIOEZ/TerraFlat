using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.EditorTools
{
    internal sealed class FlatWorldSingleLightBlendStyleShaderVariantStripper : IPreprocessShaders
    {
        #region 单混合通道构建变体过滤

        // 正式渲染只使用 0 号 Multiply；没有灯的变体也必须保留。
        private static readonly string[] UnusedBlendStyleKeywords =
        {
            "USE_SHAPE_LIGHT_TYPE_1",
            "USE_SHAPE_LIGHT_TYPE_2",
            "USE_SHAPE_LIGHT_TYPE_3"
        };

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
        {
            if (shader == null || variants.Count == 0)
                return;

            var unusedKeywords = new ShaderKeyword[UnusedBlendStyleKeywords.Length];
            for (int i = 0; i < unusedKeywords.Length; i++)
                unusedKeywords[i] = new ShaderKeyword(shader, UnusedBlendStyleKeywords[i]);

            for (int i = variants.Count - 1; i >= 0; i--)
            {
                ShaderKeywordSet keywords = variants[i].shaderKeywordSet;
                for (int j = 0; j < unusedKeywords.Length; j++)
                {
                    if (!keywords.IsEnabled(unusedKeywords[j]))
                        continue;

                    variants.RemoveAt(i);
                    break;
                }
            }
        }

        #endregion
    }
}
