Shader "Hidden/FlatWorld/UniverseOverview"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "UniverseBackground"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _UniverseScreenSize;
            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }
            float StarHash(float2 cell)
            {
                float3 value = frac(float3(cell.xyx) * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }
            half3 Stars(float2 pixelPosition, float spacing, float seed)
            {
                float2 cell = floor(pixelPosition / spacing);
                float random = StarHash(cell + seed);
                float2 center = (cell + 0.12 + float2(StarHash(cell + seed + 7.3),
                    StarHash(cell + seed + 19.1)) * 0.76) * spacing;
                float coverage = 1.0 - smoothstep(0.45, 1.6, length(pixelPosition - center));
                coverage *= step(0.78, random) * (0.65 + sin(_Time.y * 0.55 + random * 41.0) * 0.2);
                half3 color = random > 0.97 ? half3(1.0, 0.55, 0.4) :
                    random > 0.93 ? half3(0.52, 0.7, 1.0) : half3(0.84, 0.86, 0.9);
                return color * coverage;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // 星点保持固定屏幕像素大小，拉远不创建更多对象或重建星空贴图。
                float2 pixels = input.positionCS.xy - _UniverseScreenSize.xy * 0.5;
                half3 stars = Stars(pixels, 48.0, 11.7) + Stars(pixels, 79.0, 43.3);
                return half4(stars + half3(0.001, 0.002, 0.006), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "UniverseComposite"
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _UniverseBlend;
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                return half4(color, saturate(_UniverseBlend));
            }
            ENDHLSL
        }
    }
}
