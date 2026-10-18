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
            #include "Assets/9_Shaders/Shader/UniverseStarfieldCommon.hlsl"
            float4 _UniverseScreenSize;
            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // 星点保持固定屏幕像素大小，拉远不创建更多对象或重建星空贴图。
                float2 pixels = input.positionCS.xy - _UniverseScreenSize.xy * 0.5;
                return half4(SampleUniverseStarfield(pixels), 1.0);
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
