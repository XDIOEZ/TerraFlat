// 所有地表高差只在较低一侧绘制，共享完整格子网格与四条直边、四个圆角。
Shader "FlatWorld/2D/Terrain Height Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _ShadowColor("Shadow Color", Color) = (0.035, 0.022, 0.015, 1)
        _ElevationStrength("Enabled", Range(0, 1)) = 1
        _ElevationEdgeWidth("Width", Range(0.01, 0.45)) = 0.2
        _ShadowStrength("Strength", Range(0, 1)) = 0.72
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        // 乘到已受光的地表颜色上，夜里不会把暗地面反而染亮。
        Blend DstColor Zero, Zero One
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
            nointerpolation half4 cornerMask : TEXCOORD3;
            half4 tint : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _RendererColor;
            float4 _ShadowColor;
            float _ElevationStrength;
            float _ElevationEdgeWidth;
            float _ShadowStrength;
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
            output.cornerMask = data.data1;
            output.tint = input.color * data.tint;
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            return output;
        }

        half4 ShadeShadow(Varyings input)
        {
            float4 distances = float4(input.uv.x, 1.0 - input.uv.x, input.uv.y, 1.0 - input.uv.y);
            float width = max(_ElevationEdgeWidth, 0.001);
            // 整数层差只决定该边是否投影，所有正高差共用相同深浅与宽度。
            float4 edges = (1.0 - smoothstep(0.0, width, distances)) *
                step(1.0, input.edgeMask);
            half shadow = max(max(edges.x, edges.y), max(edges.z, edges.w));
            if (any(input.cornerMask >= 1.0))
            {
                // 以格子四角为圆心补四分之一圆，与直边同宽同衰减，取最大值避免交汇处叠黑。
                float4 radii = sqrt(distances.xyxy * distances.xyxy + distances.zzww * distances.zzww);
                float4 corners = (1.0 - smoothstep(0.0, width, radii)) * step(1.0, input.cornerMask);
                shadow = max(shadow, max(max(corners.x, corners.y), max(corners.z, corners.w)));
            }
            half alpha = saturate(shadow * _ShadowStrength * _ElevationStrength) * input.tint.a *
                _Color.a * _RendererColor.a * _ShadowColor.a;
            return half4(lerp(half3(1, 1, 1), _ShadowColor.rgb, alpha), 0);
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
