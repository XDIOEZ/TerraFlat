Shader "FlatWorld/Debug/TemperatureHeatmap"
{
    Properties
    {
        _MainTex ("Temperature Cells", 2D) = "white" {}
        _Opacity ("Overlay Opacity", Range(0, 1)) = 0.58
        _NavigationMode ("Navigation Arrows", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            Name "TemperatureOverlay"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                half _Opacity;
                half _NavigationMode;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            // 单格内的线段距离，箭身和两个箭翼共用；不依赖箭头贴图或逐格 Renderer。
            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                return length(p - a - ab * saturate(dot(p - a, ab) / dot(ab, ab)));
            }

            half4 NavigationGlyph(float2 cellPosition, half4 data, float antialias)
            {
                float distance;
                float lineWidth = 0.024;
                half3 tint;
                if (data.b < 0.375)
                {
                    float2 direction = normalize((float2)data.rg * 2.0 - 1.0);
                    float2 p = float2(dot(cellPosition, direction), dot(cellPosition, float2(-direction.y, direction.x)));
                    distance = SegmentDistance(p, float2(-0.29, 0), float2(0.29, 0));
                    distance = min(distance, SegmentDistance(p, float2(0.08, 0.18), float2(0.29, 0)));
                    distance = min(distance, SegmentDistance(p, float2(0.08, -0.18), float2(0.29, 0)));
                    tint = half3(1.0, 0.92, 0.59);
                }
                else if (data.b < 0.625)
                {
                    distance = length(cellPosition);
                    lineWidth = 0.12;
                    tint = half3(0.35, 0.8, 1.0);
                }
                else
                {
                    distance = min(SegmentDistance(cellPosition, float2(-0.15, -0.15), float2(0.15, 0.15)),
                        SegmentDistance(cellPosition, float2(-0.15, 0.15), float2(0.15, -0.15)));
                    tint = half3(1.0, 0.35, 0.3);
                }
                // 深色细描边让箭头在草地、雪地和水面上都能辨认；格子其余部分完全透明。
                float outer = 1.0 - smoothstep(lineWidth + 0.025 - antialias, lineWidth + 0.025 + antialias, distance);
                float inner = 1.0 - smoothstep(lineWidth - antialias, lineWidth + antialias, distance);
                return half4(lerp(half3(0.06, 0.06, 0.06), tint, inner), outer * data.a * _Opacity);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (_NavigationMode > 0.5)
                {
                    float2 cellUv = input.uv * _MainTex_TexelSize.zw;
                    float antialias = clamp(max(fwidth(cellUv.x), fwidth(cellUv.y)), 0.002, 0.08);
                    if (color.a < 0.5)
                        return half4(0, 0, 0, 0);
                    return NavigationGlyph(frac(cellUv) - 0.5, color, antialias);
                }
                // 保留未加载格的透明遮罩，整体透明度无需重新上传温度纹理。
                color.a *= _Opacity;
                return color;
            }
            ENDHLSL
        }
    }
}
