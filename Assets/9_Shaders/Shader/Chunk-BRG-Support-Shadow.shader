// 水上平台使用扩大后的同一 Sprite 网格，只在原格子外侧绘制接触水面的阴影。
Shader "FlatWorld/2D/Chunk BRG Support Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _ShadowColor("Shadow Color", Color) = (0.02, 0.025, 0.03, 0.58)
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "ChunkBRGInstance.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

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
            float2 positionWS : TEXCOORD1;
            nointerpolation half4 edgeMask : TEXCOORD2;
            nointerpolation float3 shadowData : TEXCOORD3;
            half4 tint : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _RendererColor;
            float4 _ShadowColor;
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
            ChunkBRGInstanceData data = LoadChunkBRGInstanceData();
            float3 positionWS = TransformChunkBRGVertex(input.positionOS, data);
            Varyings output = (Varyings)0;
            output.positionCS = TransformWorldToHClip(positionWS);
            output.uv = input.uv;
            output.positionWS = positionWS.xy;
            output.edgeMask = data.data0;
            output.shadowData = data.data1.xyz;
            output.tint = input.color * data.tint;
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            return output;
        }

        half4 ShadeShadow(Varyings input)
        {
            float2 delta = input.positionWS - input.shadowData.xy;
            float2 outside = abs(delta) - 0.5;
            float outsideDistance = max(outside.x, outside.y);
            clip(outsideDistance - 0.0001);

            half sideMask = 0.0h;
            if (outside.x > 0.0)
                sideMask = max(sideMask, delta.x < 0.0 ? input.edgeMask.r : input.edgeMask.g);
            if (outside.y > 0.0)
                sideMask = max(sideMask, delta.y < 0.0 ? input.edgeMask.b : input.edgeMask.a);
            clip(sideMask - 0.001h);

            half sourceAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
            float extent = max(input.shadowData.z, 0.001);
            half fade = 1.0h - smoothstep(0.0, extent, outsideDistance);
            half alpha = sourceAlpha * input.tint.a * _Color.a * _RendererColor.a *
                _ShadowColor.a * sideMask * fade;
            return half4(_ShadowColor.rgb, alpha);
        }
        ENDHLSL

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return ShadeShadow(input);
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
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return ShadeShadow(input);
            }
            ENDHLSL
        }
    }
}
