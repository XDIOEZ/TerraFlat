Shader "FlatWorld/2D/Chunk BRG Sprite Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __

            #include "ChunkBRGInstance.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
                half4 tint : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RendererColor;
            CBUFFER_END
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _Color)
                UNITY_DOTS_INSTANCED_PROP(float4, _RendererColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
            #define _RendererColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RendererColor)
            #endif

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ChunkBRGInstanceData instanceData = LoadChunkBRGInstanceData();
                float3 positionWS = TransformChunkBRGVertex(input.positionOS, instanceData);
                Varyings output = (Varyings)0;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.tint = input.color * instanceData.tint;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= input.tint * _Color * _RendererColor;
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "ChunkBRGInstance.hlsl"
            struct Attributes { float3 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 tint:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RendererColor;
            CBUFFER_END
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _Color)
                UNITY_DOTS_INSTANCED_PROP(float4, _RendererColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
            #define _RendererColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RendererColor)
            #endif
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ChunkBRGInstanceData d = LoadChunkBRGInstanceData();
                Varyings o = (Varyings)0;
                o.positionCS = TransformWorldToHClip(TransformChunkBRGVertex(input.positionOS, d));
                o.uv = input.uv;
                o.tint = input.color * d.tint;
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.tint * _Color * _RendererColor;
            }
            ENDHLSL
        }
    }
}
