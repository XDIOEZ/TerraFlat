Shader "Game/2D/Tilemap-Contact-Shadow-Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        _EdgeColor("Contact Shadow", Color) = (0.035, 0.022, 0.015, 1)
        _EdgeWidth("Shadow Width", Range(0.03, 0.45)) = 0.2
        _EdgeStrength("Shadow Strength", Range(0, 1)) = 0.72
        _CornerStrength("Corner Strength", Range(0, 1)) = 0.18

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex CaveWaterVertex
            #pragma fragment CaveWaterFragment
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
                float2 localPosition : TEXCOORD2;
                half4 shoreMask : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            half4 _Color;
            half4 _RendererColor;
            half4 _EdgeColor;
            half _EdgeWidth;
            half _EdgeStrength;
            half _CornerStrength;

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

            Varyings CaveWaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.shoreMask = input.color;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            /// <summary>按四向接触遮罩生成柔和暗边，表现墙脚或下凹水岸。</summary>
            half ComputeRecessShadow(float2 localPosition, half4 shoreMask)
            {
                float2 cellUV = frac(localPosition + 0.0001);
                half width = max(_EdgeWidth, 0.001);
                half left = shoreMask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
                half right = shoreMask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
                half bottom = shoreMask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
                half top = shoreMask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
                half strongest = max(max(left, right), max(bottom, top));
                half overlap = saturate(left + right + bottom + top - strongest);
                return saturate(strongest + overlap * _CornerStrength);
            }

            half4 CaveWaterFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                half recess = ComputeRecessShadow(input.localPosition, input.shoreMask);
                main.rgb = lerp(main.rgb, _EdgeColor.rgb,
                    saturate(recess * _EdgeStrength * _EdgeColor.a));

                half4 lightMask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, lightMask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // 无 2D Renderer 时仍保留同样的岸线效果，方便 Scene 视图检查。
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                half4 shoreMask : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half4 _Color;
            half4 _RendererColor;
            half4 _EdgeColor;
            half _EdgeWidth;
            half _EdgeStrength;
            half _CornerStrength;

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.shoreMask = input.color;
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                float2 cellUV = frac(input.localPosition + 0.0001);
                half width = max(_EdgeWidth, 0.001);
                half left = input.shoreMask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
                half right = input.shoreMask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
                half bottom = input.shoreMask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
                half top = input.shoreMask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
                half strongest = max(max(left, right), max(bottom, top));
                half overlap = saturate(left + right + bottom + top - strongest);
                half recess = saturate(strongest + overlap * _CornerStrength);

                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                main.rgb = lerp(main.rgb, _EdgeColor.rgb,
                    saturate(recess * _EdgeStrength * _EdgeColor.a));
                return main;
            }
            ENDHLSL
        }
    }
}
