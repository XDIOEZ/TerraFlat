Shader "FlatWorld/2D/Tilemap Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}

        [Header(Water Surface)]
        _DeepColor("深水颜色", Color) = (0.08, 0.34, 0.56, 1)
        _ShallowColor("浅水颜色", Color) = (0.18, 0.68, 0.78, 1)
        _SurfaceTint("水面染色强度", Range(0, 1)) = 0.12
        _WaveColor("波纹高光", Color) = (0.72, 0.94, 0.98, 0.82)
        _WaveStrength("波纹强度", Range(0, 1)) = 0.2
        _WaveScale("波纹密度", Range(0.25, 16)) = 3.2
        _WaveSpeed("流动速度", Range(-3, 3)) = 0.45
        _WaveThreshold("高光阈值", Range(-1, 1)) = 0.58
        _WaveSoftness("高光柔和度", Range(0.01, 0.5)) = 0.18
        _PixelDensity("像素采样密度", Range(1, 64)) = 32
        _WaveDistortion("波纹弯曲程度", Range(0, 1.5)) = 0.42
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
        float _PixelDensity;
        half _WaveDistortion;
        half _EdgeWidth;
        half _EdgeStrength;
        half _CornerStrength;
        half _ShoreStrength;

        /// <summary>将世界坐标锁定到细像素格，保持像素画风并避免波纹随镜头抖动。</summary>
        float2 QuantizeWaterPosition(float2 positionWS)
        {
            float density = max(_PixelDensity, 1.0);
            return floor(positionWS * density + 0.5) / density;
        }

        /// <summary>用双向连续波与低频弯曲生成自然、断续且跨 Chunk 连贯的水纹。</summary>
        half2 CalculateWaterPattern(float2 positionWS)
        {
            float2 direction = _FlowDirection.xy;
            direction *= rsqrt(max(dot(direction, direction), 0.001));
            float2 lateral = float2(-direction.y, direction.x);
            float2 pixelPosition = QuantizeWaterPosition(positionWS);
            float time = _Time.y * _WaveSpeed;

            float longitudinal = dot(pixelPosition, direction) * _WaveScale;
            float transverse = dot(pixelPosition, lateral) * _WaveScale;
            half bend = sin(transverse * 0.34 - time * 0.42) * _WaveDistortion;
            float primaryPhase = longitudinal + time + bend;
            float secondaryPhase = transverse * 0.72 - time * 0.67
                + sin(longitudinal * 0.3 + time * 0.22) * _WaveDistortion * 0.75;
            half primary = sin(primaryPhase);
            half secondary = sin(secondaryPhase);
            half wave = primary * 0.68h + secondary * 0.32h;

            half depthBlend = saturate(0.5h + wave * 0.22h);
            half crest = smoothstep(
                _WaveThreshold,
                _WaveThreshold + max(_WaveSoftness, 0.001h),
                primary);
            half breakup = smoothstep(-0.35h, 0.62h, secondary);
            half highlight = crest
                * lerp(0.18h, 1.0h, breakup)
                * saturate(_WaveStrength)
                * _WaveColor.a;
            return half2(depthBlend, highlight);
        }

        /// <summary>以稳定底色承载轻微明暗变化，并叠加细窄、断续的波峰高光。</summary>
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
                float2 positionWS : TEXCOORD4;
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
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.shoreMask = input.color;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 WaterFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                main.rgb = ApplyWaterSurface(main.rgb, CalculateWaterPattern(input.positionWS));
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
                float2 positionWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.shoreMask = input.color;
                return output;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                main.rgb = ApplyWaterSurface(main.rgb, CalculateWaterPattern(input.positionWS));
                half recess = ComputeRecessShadow(input.localPosition, input.shoreMask);
                main.rgb = ApplyShore(main.rgb, recess);
                return main;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
