Shader "FlatWorld/2D/Tilemap Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}

        [Header(Water Surface)]
        _DeepColor("深水颜色", Color) = (0.08, 0.34, 0.56, 1)
        _ShallowColor("浅水颜色", Color) = (0.18, 0.68, 0.78, 1)
        _SurfaceTint("水面染色强度", Range(0, 1)) = 0.06
        _WaveColor("波纹高光", Color) = (0.72, 0.94, 0.98, 0.68)
        _WaveStrength("波纹强度", Range(0, 1)) = 0.18
        _WaveScale("波纹密度", Range(0.25, 16)) = 2.45
        _WaveSpeed("流动速度", Range(-3, 3)) = 0.32
        _WaveThreshold("高光阈值", Range(-1, 1)) = 0.78
        _WaveSoftness("高光柔和度", Range(0.01, 0.5)) = 0.12
        _PixelDensity("像素采样密度", Range(1, 64)) = 32
        _WaveDistortion("波纹弯曲程度", Range(0, 3)) = 1.65
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

        /// <summary>生成稳定的二维随机值，避免程序波纹形成规则重复图案。</summary>
        float WaterHash(float2 cell)
        {
            float3 value = frac(float3(cell.xyx) * float3(0.1031, 0.103, 0.0973));
            value += dot(value, value.yzx + 33.33);
            return frac((value.x + value.y) * value.z);
        }

        /// <summary>在世界空间插值随机值，为流向弯曲和波峰断续提供连续噪声。</summary>
        float WaterNoise(float2 position)
        {
            float2 cell = floor(position);
            float2 local = frac(position);
            float2 blend = local * local * (3.0 - 2.0 * local);
            float bottom = lerp(WaterHash(cell), WaterHash(cell + float2(1.0, 0.0)), blend.x);
            float top = lerp(
                WaterHash(cell + float2(0.0, 1.0)),
                WaterHash(cell + float2(1.0, 1.0)),
                blend.x);
            return lerp(bottom, top, blend.y);
        }

        /// <summary>用噪声扭曲两种方向的波峰，并把长条波纹裁成不规则短段。</summary>
        half2 CalculateWaterPattern(float2 positionWS)
        {
            float2 direction = _FlowDirection.xy;
            direction *= rsqrt(max(dot(direction, direction), 0.001));
            float2 lateral = float2(-direction.y, direction.x);
            float2 pixelPosition = QuantizeWaterPosition(positionWS);
            float time = _Time.y * _WaveSpeed;

            float2 drift = float2(time * 0.035, -time * 0.023);
            float warpA = WaterNoise(pixelPosition * 0.11 + drift);
            float warpB = WaterNoise(
                pixelPosition * 0.145
                + float2(19.37, 7.91)
                + float2(-drift.y, drift.x));
            float2 warpedPosition = pixelPosition
                + direction * (warpA - 0.5) * _WaveDistortion
                + lateral * (warpB - 0.5) * _WaveDistortion * 1.35;

            float2 secondaryDirection = direction * 0.62 + lateral * 0.78;
            secondaryDirection *= rsqrt(max(dot(secondaryDirection, secondaryDirection), 0.001));
            float primaryPhase = dot(warpedPosition, direction) * _WaveScale
                + time
                + (warpB - 0.5) * 1.4;
            float secondaryPhase = dot(warpedPosition, secondaryDirection) * _WaveScale * 0.78
                - time * 0.63
                + (warpA - 0.5) * 1.8;
            half primary = sin(primaryPhase);
            half secondary = sin(secondaryPhase);

            float2 secondaryLateral = float2(-secondaryDirection.y, secondaryDirection.x);
            float primarySegmentNoise = WaterNoise(
                float2(
                    dot(pixelPosition, lateral) * 0.28 + time * 0.018,
                    dot(pixelPosition, direction) * 0.075 - time * 0.006)
                + float2(5.23, 17.61));
            float secondarySegmentNoise = WaterNoise(
                float2(
                    dot(pixelPosition, secondaryLateral) * 0.24 - time * 0.013,
                    dot(pixelPosition, secondaryDirection) * 0.065 + time * 0.005)
                + float2(23.47, 3.83));
            half primarySegment = smoothstep(0.52h, 0.72h, primarySegmentNoise);
            half secondarySegment = smoothstep(0.62h, 0.8h, secondarySegmentNoise);
            half primaryCrest = smoothstep(
                _WaveThreshold,
                _WaveThreshold + max(_WaveSoftness, 0.001h),
                primary);
            half secondaryCrest = smoothstep(
                _WaveThreshold + 0.1h,
                _WaveThreshold + 0.1h + max(_WaveSoftness, 0.001h),
                secondary);
            half crest = max(
                primaryCrest * primarySegment,
                secondaryCrest * secondarySegment * 0.3h);

            half broadVariation = (warpA - 0.5h) * 0.65h + (warpB - 0.5h) * 0.35h;
            half depthBlend = saturate(0.5h + broadVariation * 0.14h);
            half highlight = crest
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
