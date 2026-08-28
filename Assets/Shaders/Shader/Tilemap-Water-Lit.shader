Shader "FlatWorld/2D/Tilemap Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}

        [Header(Water Surface)]
        _DeepColor("深水颜色", Color) = (0.08, 0.34, 0.56, 1)
        _ShallowColor("浅水颜色", Color) = (0.18, 0.68, 0.78, 1)
        _SurfaceTint("水面染色强度", Range(0, 1)) = 0.2
        _WaveColor("波纹高光", Color) = (0.65, 0.96, 1, 1)
        _WaveStrength("波纹强度", Range(0, 1)) = 0.28
        _WaveScale("波纹密度", Range(0.25, 16)) = 5.5
        _WaveSpeed("流动速度", Range(-3, 3)) = 0.75
        _WaveThreshold("高光阈值", Range(-1, 1)) = 0.48
        _WaveSoftness("高光柔和度", Range(0.01, 0.5)) = 0.14
        _FlowDirection("流动方向", Vector) = (1, 0.35, 0, 0)

        [Header(Shore)]
        _EdgeColor("岸线暗部", Color) = (0.035, 0.022, 0.015, 1)
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.2
        _EdgeStrength("岸线暗部强度", Range(0, 1)) = 0.72
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.18
        _ShoreColor("岸线亮部", Color) = (0.48, 0.85, 0.92, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.16

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);

        half4 _Color;
        half4 _RendererColor;
        half4 _DeepColor;
        half4 _ShallowColor;
        half4 _WaveColor;
        half4 _EdgeColor;
        half4 _ShoreColor;
        float4 _FlowDirection;
        half _SurfaceTint;
        half _WaveStrength;
        float _WaveScale;
        float _WaveSpeed;
        half _WaveThreshold;
        half _WaveSoftness;
        half _EdgeWidth;
        half _EdgeStrength;
        half _CornerStrength;
        half _ShoreStrength;

        /// <summary>用折线波近似正弦波，避免移动端顶点阶段的三角函数开销。</summary>
        half TriangleWave(float phase)
        {
            const float inverseTwoPi = 0.159154943;
            return abs(frac(phase * inverseTwoPi - 0.25) - 0.5) * 4.0h - 1.0h;
        }

        /// <summary>只在 Tile 顶点生成水纹参数，交给 GPU 插值后供像素阶段直接混色。</summary>
        half2 CalculateWaterPattern(float2 positionWS)
        {
            float2 direction = _FlowDirection.xy;
            direction *= rsqrt(max(dot(direction, direction), 0.001));
            float2 lateral = float2(-direction.y, direction.x);
            float time = _Time.y * _WaveSpeed;

            half primary = TriangleWave(dot(positionWS, direction) * _WaveScale + time);
            half secondary = TriangleWave(
                dot(positionWS, lateral) * _WaveScale * 0.73 - time * 0.67);
            half modulation = TriangleWave(
                dot(positionWS, float2(0.37, 1.0)) * _WaveScale * 0.43 + time * 0.27);
            half wave = primary * 0.62h + secondary * 0.28h + modulation * 0.1h;

            half depthBlend = saturate(wave * 0.5 + 0.5);
            half crest = smoothstep(
                _WaveThreshold,
                _WaveThreshold + max(_WaveSoftness, 0.001h),
                wave);
            half dash = smoothstep(
                -0.15h,
                0.45h,
                TriangleWave(dot(positionWS, lateral) * _WaveScale * 1.85 + time * 0.35));
            half highlight = crest * dash * saturate(_WaveStrength) * _WaveColor.a;
            return half2(depthBlend, highlight);
        }

        /// <summary>像素阶段只执行两次颜色混合，避免大面积水域产生高昂片元开销。</summary>
        half3 ApplyWaterSurface(half3 sourceColor, half2 waterPattern)
        {
            half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, waterPattern.x);
            sourceColor = lerp(sourceColor, waterTint, saturate(_SurfaceTint));
            return lerp(sourceColor, _WaveColor.rgb, waterPattern.y);
        }

        /// <summary>RGBA 分别读取左、右、下、上岸线，计算水格内侧的渐变遮罩。</summary>
        half ComputeRecessShadow(float2 localPosition, half4 shoreMask)
        {
            float2 cellUV = frac(localPosition + 0.0001);
            half width = max(_EdgeWidth, 0.001h);
            half left = shoreMask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
            half right = shoreMask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
            half bottom = shoreMask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
            half top = shoreMask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
            half strongest = max(max(left, right), max(bottom, top));
            half overlap = saturate(left + right + bottom + top - strongest);
            return saturate(strongest + overlap * _CornerStrength);
        }

        /// <summary>在暗岸线内侧补一条柔和反光，使水体边界更清楚。</summary>
        half3 ApplyShore(half3 sourceColor, half recess)
        {
            half shoreBand = saturate(recess * (1.0h - recess) * 4.0h);
            sourceColor = lerp(
                sourceColor,
                _ShoreColor.rgb,
                shoreBand * saturate(_ShoreStrength) * _ShoreColor.a);
            return lerp(
                sourceColor,
                _EdgeColor.rgb,
                saturate(recess * _EdgeStrength * _EdgeColor.a));
        }
    ENDHLSL

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
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

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
                float2 localPosition : TEXCOORD2;
                half4 shoreMask : TEXCOORD3;
                half2 waterPattern : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

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

            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                float2 positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.shoreMask = input.color;
                output.waterPattern = CalculateWaterPattern(positionWS);
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 WaterFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                main.rgb = ApplyWaterSurface(main.rgb, input.waterPattern);
                half recess = ComputeRecessShadow(input.localPosition, input.shoreMask);
                main.rgb = ApplyShore(main.rgb, recess);

                half4 lightMask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, lightMask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // 非 2D Renderer 下保留同样的水面表现，方便 Scene 视图检查。
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing

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
                float2 localPosition : TEXCOORD1;
                half4 shoreMask : TEXCOORD2;
                half2 waterPattern : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                float2 positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.shoreMask = input.color;
                output.waterPattern = CalculateWaterPattern(positionWS);
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                main.rgb = ApplyWaterSurface(main.rgb, input.waterPattern);
                half recess = ComputeRecessShadow(input.localPosition, input.shoreMask);
                main.rgb = ApplyShore(main.rgb, recess);
                return main;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
