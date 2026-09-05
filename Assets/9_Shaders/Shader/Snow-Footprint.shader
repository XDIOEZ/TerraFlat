Shader "FlatWorld/Environment/Snow Footprint"
{
    Properties
    {
        _CenterColor("Center Color", Color) = (0.34, 0.39, 0.42, 0.24)
        _EdgeColor("Edge Color", Color) = (0.12, 0.16, 0.18, 0.52)
        _HighlightColor("Inner Highlight", Color) = (0.72, 0.80, 0.84, 1)
        _EdgeStart("Dark Edge Start", Range(0.1, 0.95)) = 0.58
        _EdgeSoftness("Outer Softness", Range(0.01, 0.3)) = 0.08
        _DepthContrast("Inner Shadow", Range(0, 0.5)) = 0.14
        _HighlightStrength("Inner Highlight Strength", Range(0, 0.4)) = 0.10
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _CenterColor;
            half4 _EdgeColor;
            half4 _HighlightColor;
            float _EdgeStart;
            float _EdgeSoftness;
            float _DepthContrast;
            float _HighlightStrength;
        CBUFFER_END

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
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            half2 lightingUV : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings FootprintVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.color = input.color;
            output.uv = input.uv;
            output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
            return output;
        }

        half4 EvaluateFootprint(half4 vertexColor, float2 uv)
        {
            float2 centered = abs(uv * 2.0 - 1.0);
            float shapeMetric = pow(centered.x, 4.0) + pow(centered.y, 4.0);
            float shapeAlpha = 1.0 - smoothstep(
                1.0 - _EdgeSoftness,
                1.0 + _EdgeSoftness,
                shapeMetric);

            float edgeMetric = saturate(max(centered.x, centered.y));
            float edgeBand = smoothstep(_EdgeStart, 1.0, edgeMetric);
            half4 footprint = lerp(_CenterColor, _EdgeColor, edgeBand);

            float signedDepth = dot(uv - 0.5, float2(-0.45, 0.90));
            float interior = 1.0 - edgeBand;
            float innerShadow = smoothstep(0.0, 0.45, -signedDepth) * interior;
            float innerHighlight = smoothstep(0.0, 0.45, signedDepth) * interior;

            footprint.rgb *= 1.0 - innerShadow * _DepthContrast;
            footprint.rgb = lerp(
                footprint.rgb,
                _HighlightColor.rgb,
                innerHighlight * _HighlightStrength);

            footprint.rgb *= vertexColor.rgb;
            footprint.a *= shapeAlpha * vertexColor.a;
            clip(footprint.a - 0.001h);
            return footprint;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex FootprintVertex
            #pragma fragment FootprintLitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

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

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 FootprintLitFragment(Varyings input) : SV_Target
            {
                half4 footprint = EvaluateFootprint(input.color, input.uv);

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(footprint.rgb, footprint.a, half4(1, 1, 1, 1), surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex FootprintVertex
            #pragma fragment FootprintForwardFragment
            #pragma multi_compile_instancing

            half4 FootprintForwardFragment(Varyings input) : SV_Target
            {
                return EvaluateFootprint(input.color, input.uv);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
