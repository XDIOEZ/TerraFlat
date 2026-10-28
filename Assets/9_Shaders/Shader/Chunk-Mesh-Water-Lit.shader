// 水面从 Chunk Mesh 顶点读取液深、岸线和流向。
Shader "FlatWorld/2D/Chunk Mesh Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}
        [NoScaleOffset] _LavaPatternTex("岩浆RGBA图(R主体/G扰动/B-A高光)", 2D) = "gray" {}
        [Header(Ocean Surface)]
        _DeepColor("深海颜色", Color) = (0.075, 0.22, 0.255, 1)
        _ShallowColor("浅海颜色", Color) = (0.32, 0.58, 0.57, 1)
        _SurfaceTint("海水染色强度", Range(0, 1)) = 1
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.74
        _DetailScale("细浪尺度", Range(0.5, 12)) = 3.2
        _WaveSpeed("波浪传播速度", Range(0, 3)) = 0.28
        _WaveDistortion("海流扭曲", Range(0, 4)) = 0.35
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.3
        _PixelDensity("风格化采样密度", Range(1, 128)) = 64
        _WaveDirection("波浪传播方向（零为跟随水流/风向）", Vector) = (0.18, 1, 0, 0)
        _FlowSpeed("水流平移倍率", Range(0, 4)) = 1
        _FlowCycleSeconds("流动交接周期（秒）", Range(2, 16)) = 8
        _FlowDirection("静水默认波向", Vector) = (1, 0.35, 0, 0)
        _TideCyclesPerDay("每日潮汐循环次数", Range(1, 4)) = 2.0
        _RippleColor("浪脊颜色", Color) = (0.58, 0.72, 0.65, 1)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.16
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 2.1
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.08
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.04
        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (0.43, 0.61, 0.6, 1)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.24
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.5
        _ReflectionDirection("镜面环境方向", Vector) = (-0.35, 0.18, 0.92, 0)
        _SpecularColor("太阳高光", Color) = (0.92, 0.9, 0.78, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.18
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 52
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (0.5, 0.67, 0.58, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.025
        _FoamColor("泡沫颜色", Color) = (0.83, 0.86, 0.78, 1)
        _WhitecapStrength("浪峰白沫", Range(0, 1)) = 0.09
        [Header(River Current)]
        [NoScaleOffset] _RiverPatternTex("河流RGBA图(RG斜率/B碎光/A泡沫)", 2D) = "gray" {}
        _RiverFoamCoverage("河流泡沫覆盖率", Range(0, 1)) = 0.12
        [Header(Unity Stylized Water)]
        [NoScaleOffset] _UnityWaterPatternTex("四向波纹RGBA图(RG法线/B泡沫/A浅水亮度)", 2D) = "gray" {}
        [NoScaleOffset] _UnityWaterDetailTex("碎光法线图", 2D) = "gray" {}
        _UnityNormalScale("四向波纹尺度", Range(0.1, 16)) = 4
        _UnityFoamScale("白沫尺度", Range(0.1, 16)) = 5.2
        _UnitySparkleScale("碎光尺度", Range(0.1, 4)) = 0.7
        _UnitySparkleStrength("碎光强度", Range(0, 2)) = 0.8
        _UnitySparkleExponent("碎光集中度", Range(4, 1024)) = 256
        _UnityDepthDensity("深浅颜色密度", Range(0.1, 8)) = 2.6
        _UnitySparkleColor("碎光颜色", Color) = (0.65, 1, 0.88, 1)
        [Header(Moon Reflection)]
        _MoonReflectionColor("月光倒影颜色", Color) = (0.72, 0.86, 1, 1)
        _MoonReflectionStrength("月光倒影强度", Range(0, 8)) = 4.2
        _MoonReflectionPosition("月光倒影屏幕位置", Vector) = (0.68, 0.62, 0, 0)
        _MoonDiscRadius("月面倒影半径", Range(0.01, 0.2)) = 0.055
        _MoonTrailLength("月光带长度", Range(0.01, 0.7)) = 0.34
        _MoonTrailWidth("月光带宽度", Range(0.005, 0.2)) = 0.065
        [Header(Shore)]
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.18
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.18
        _ShoreColor("岸线亮部", Color) = (0.5, 0.67, 0.58, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.05
        _ShoreFoamStrength("岸边泡沫强度", Range(0, 1)) = 0.18
        _FoamSpeed("岸边泡沫速度", Range(0, 3)) = 0.52
        [Header(Lava)]
        [HDR] _LavaCrustColor("冷却壳颜色", Color) = (0.12, 0.012, 0.004, 1)
        [HDR] _LavaMoltenColor("熔融颜色", Color) = (0.92, 0.085, 0.006, 1)
        [HDR] _LavaHotColor("高温高光", Color) = (1.25, 0.72, 0.055, 1)
        _LavaFlowDirection("流动方向", Vector) = (0.82, 0.57, 0, 0)
        _LavaScale("大纹理尺度", Range(0.02, 1)) = 0.11
        _LavaDetailScale("细节尺度", Range(1, 8)) = 1.85
        _LavaFlowSpeed("流动速度", Range(0, 1.5)) = 0.025
        _LavaDistortion("流动扭曲", Range(0, 2)) = 0.12
        _LavaHotThreshold("熔融阈值", Range(0, 1)) = 0.5
        _LavaHotSoftness("熔融过渡", Range(0.01, 0.4)) = 0.1
        _LavaCoreThreshold("高光阈值", Range(0, 1)) = 0.78
        _LavaCoreSoftness("高光过渡", Range(0.01, 0.3)) = 0.085
        _LavaCrustStrength("次级深红强度", Range(0, 1.5)) = 0.52
        _LavaShoreCrust("岸边冷却", Range(0, 1.5)) = 0.72
        _LavaShallowCrust("浅层冷却", Range(0, 1)) = 0.18
        _LavaEmissionStrength("自发光强度", Range(0, 4)) = 1.55
        _LavaPulseSpeed("高光交替速度", Range(0, 4)) = 0.35
        _LavaPulseStrength("高光脉动幅度", Range(0, 0.3)) = 0.045
        _LavaShallowAlpha("浅层透明度", Range(0, 1)) = 0.94
        _LavaDeepAlpha("深层透明度", Range(0, 1)) = 0.99
        [HideInInspector] _LavaMode("Lava Mode", Float) = 0
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #define FLATWORLD_WATER_MATERIAL_CBUFFER_DEFINED 1
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _RendererColor;
            float4 _DeepColor;
            float4 _ShallowColor;
            float4 _ReflectionColor;
            float4 _SpecularColor;
            float4 _CausticColor;
            float4 _FoamColor;
            float4 _RippleColor;
            float4 _MoonReflectionColor;
            float4 _ShoreColor;
            float4 _ReflectionDirection;
            float4 _SunDirection;
            float4 _MoonReflectionPosition;
            float4 _FlowDirection;
            float4 _WaveDirection;
            float _FlowSpeed;
            float _FlowCycleSeconds;
            float _RiverFoamCoverage;
            float4 _LavaCrustColor;
            float4 _LavaMoltenColor;
            float4 _LavaHotColor;
            float4 _LavaFlowDirection;
            float _TideCyclesPerDay;
            float _SurfaceTint;
            float _SwellScale;
            float _DetailScale;
            float _WaveSpeed;
            float _WaveDistortion;
            float _NormalStrength;
            float _PixelDensity;
            float _RippleStrength;
            float _RippleScale;
            float _RippleWidth;
            float _RippleShadowStrength;
            float _ReflectionStrength;
            float _ReflectionSmoothness;
            float _SpecularStrength;
            float _SpecularPower;
            float _CausticStrength;
            float _WhitecapStrength;
            float _MoonReflectionStrength;
            float _MoonDiscRadius;
            float _MoonTrailLength;
            float _MoonTrailWidth;
            float _EdgeWidth;
            float _CornerStrength;
            float _ShoreStrength;
            float _ShoreFoamStrength;
            float _FoamSpeed;
            float _LavaScale;
            float _LavaDetailScale;
            float _LavaFlowSpeed;
            float _LavaDistortion;
            float _LavaHotThreshold;
            float _LavaHotSoftness;
            float _LavaCoreThreshold;
            float _LavaCoreSoftness;
            float _LavaCrustStrength;
            float _LavaShoreCrust;
            float _LavaShallowCrust;
            float _LavaEmissionStrength;
            float _LavaPulseSpeed;
            float _LavaPulseStrength;
            float _LavaShallowAlpha;
            float _LavaDeepAlpha;
            float _LavaMode;
            float _UnityNormalScale;
            float _UnityFoamScale;
            float _UnitySparkleScale;
            float _UnitySparkleStrength;
            float _UnitySparkleExponent;
            float _UnityDepthDensity;
            float4 _UnitySparkleColor;
        CBUFFER_END
        struct ChunkMeshWaterData
        {
            float waterKind;
            float4 data0;
            float4 data1;
            float4 flowX;
            float4 flowY;
        };
        #include "WaterSurfaceCommon.hlsl"
        #if !defined(FLATWORLD_WATER_LOW_SPEC)
        #include "LavaSurfaceCommon.hlsl"
        #endif
        #if defined(FLATWORLD_WATER_LOW_SPEC)
            #include "WaterSurfaceLowSpec.hlsl"
        #elif defined(FLATWORLD_WATER_UNITY_STYLIZED)
            #include "UnityStylizedWaterSurface.hlsl"
        #elif defined(FLATWORLD_WATER_STYLIZED)
            #include "WaterSurfaceStylized.hlsl"
        #else
            #include "WaterSurfaceRealistic.hlsl"
        #endif

        #if !defined(FLATWORLD_WATER_UNITY_STYLIZED) && !defined(FLATWORLD_WATER_LOW_SPEC)
        #include "RiverSurface.hlsl"
        #endif

        // 两种批次共用外观分支，轻量档直接跳过完整波光与岸线噪声求解。
        WaterSurfaceData CalculateChunkWaterSurface(float2 positionWS, float2 screenUV, half depth, ChunkMeshWaterData data)
        {
            #if defined(FLATWORLD_WATER_LOW_SPEC)
            return CalculateLowSpecWaterSurface(positionWS, depth,
                InterpolateWaterCurrent(positionWS, data.flowX, data.flowY), data.waterKind);
            #else
            WaterMotionData motion = ResolveWaterMotion(
                InterpolateWaterCurrent(positionWS, data.flowX, data.flowY), data.waterKind);
            WaterSurfaceData surface = (WaterSurfaceData)0;
            #if !defined(FLATWORLD_WATER_UNITY_STYLIZED) && !defined(FLATWORLD_WATER_LOW_SPEC)
            float2 positionDx = ddx(positionWS);
            float2 positionDy = ddy(positionWS);
            UNITY_BRANCH
            if (abs(data.waterKind - 1.0) < 0.25 || abs(data.waterKind - 3.0) < 0.25)
                surface = CalculateRiverSurface(positionWS, screenUV, depth, data.flowX, data.flowY, motion, positionDx, positionDy);
            else
            #endif
                surface = CalculateWaterSurface(positionWS, screenUV, depth, motion);
            return surface;
            #endif
        }

        half3 ApplyChunkWaterShore(half3 sourceColor, half recess, float2 positionWS, ChunkMeshWaterData data)
        {
            #if defined(FLATWORLD_WATER_LOW_SPEC)
            return ApplyLowSpecWaterShore(sourceColor, recess);
            #else
            WaterMotionData motion = ResolveWaterMotion(
                InterpolateWaterCurrent(positionWS, data.flowX, data.flowY), data.waterKind);
            return ApplyShore(sourceColor, recess, positionWS, motion);
            #endif
        }
    ENDHLSL

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
            #pragma multi_compile_local_fragment _ FLATWORLD_WATER_STYLIZED FLATWORLD_WATER_UNITY_STYLIZED FLATWORLD_WATER_LOW_SPEC
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            struct Attributes { float3 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0;
                float4 shore:TEXCOORD1; float4 depth:TEXCOORD2; float4 flowX:TEXCOORD4;
                float4 flowY:TEXCOORD5; float kind:TEXCOORD6; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0;
                half2 lightingUV:TEXCOORD1; nointerpolation half4 shore:TEXCOORD2;
                nointerpolation half4 depth:TEXCOORD3; float2 positionWS:TEXCOORD4;
                nointerpolation float4 flowX:TEXCOORD5; nointerpolation float4 flowY:TEXCOORD6;
                nointerpolation float kind:TEXCOORD7; half4 tint:COLOR; };
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
                Varyings o = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS.xy;
                o.uv = input.uv;
                o.shore = input.shore;
                o.depth = input.depth;
                o.flowX = input.flowX;
                o.flowY = input.flowY;
                o.kind = input.kind;
                o.tint = input.color;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 Frag(Varyings input):SV_Target
            {
                ChunkMeshWaterData data;
                data.waterKind = input.kind;
                data.data0 = input.shore;
                data.data1 = input.depth;
                data.flowX = input.flowX;
                data.flowY = input.flowY;
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.tint * _Color * _RendererColor;
                half continuousDepth = SampleContinuousLiquidDepthCorners(input.positionWS, input.depth);
                #if defined(FLATWORLD_WATER_STYLIZED)
                half liquidDepth = QuantizeWaterVisualDepth(continuousDepth);
                #else
                half liquidDepth = continuousDepth;
                #endif
                half recess = ComputeShoreRecess(input.positionWS, DecodeWaterShoreMask(input.shore));
                #if !defined(FLATWORLD_WATER_LOW_SPEC)
                UNITY_BRANCH
                if (_LavaMode > 0.5)
                {
                    LavaSurfaceData lava = CalculateLavaSurface(
                        input.positionWS,
                        liquidDepth,
                        recess,
                        main.a);
                    half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                    SurfaceData2D surfaceData; InputData2D inputData;
                    InitializeSurfaceData(lava.albedo, lava.alpha, mask, surfaceData);
                    InitializeInputData(input.uv, input.lightingUV, inputData);
                    half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                    lit.rgb += lava.emission;
                    lit.a = lava.alpha;
                    return lit;
                }
                #endif
                // 保留原透明度，同时让透明度跟随连续水深跨格平滑变化。
                main.a = ResolveWaterSurfaceAlpha(main.a, continuousDepth);
                WaterSurfaceData surface = CalculateChunkWaterSurface(input.positionWS, input.lightingUV, liquidDepth, data);
                main.rgb = ApplyWaterSurface(main.rgb, surface);
                main.rgb = ApplyChunkWaterShore(main.rgb, recess, input.positionWS, data);
                #if defined(FLATWORLD_WATER_LOW_SPEC)
                half4 mask = half4(1, 1, 1, 1);
                #else
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                #endif
                SurfaceData2D surfaceData; InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                lit.rgb = ApplyMoonReflection(lit.rgb, surface.moonReflection);
                return lit;
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
            #pragma multi_compile_local_fragment _ FLATWORLD_WATER_STYLIZED FLATWORLD_WATER_UNITY_STYLIZED FLATWORLD_WATER_LOW_SPEC
            struct Attributes { float3 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0;
                float4 shore:TEXCOORD1; float4 depth:TEXCOORD2; float4 flowX:TEXCOORD4;
                float4 flowY:TEXCOORD5; float kind:TEXCOORD6; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0;
                float2 screenUV:TEXCOORD1; nointerpolation half4 shore:TEXCOORD2;
                nointerpolation half4 depth:TEXCOORD3; float2 positionWS:TEXCOORD4;
                nointerpolation float4 flowX:TEXCOORD5; nointerpolation float4 flowY:TEXCOORD6;
                nointerpolation float kind:TEXCOORD7; half4 tint:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS.xy;
                o.uv = input.uv;
                o.shore = input.shore;
                o.depth = input.depth;
                o.flowX = input.flowX;
                o.flowY = input.flowY;
                o.kind = input.kind;
                o.tint = input.color;
                float4 screenPosition = ComputeScreenPos(o.positionCS);
                o.screenUV = screenPosition.xy / max(screenPosition.w, 0.0001);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                ChunkMeshWaterData data;
                data.waterKind = input.kind;
                data.data0 = input.shore;
                data.data1 = input.depth;
                data.flowX = input.flowX;
                data.flowY = input.flowY;
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.tint * _Color * _RendererColor;
                half continuousDepth = SampleContinuousLiquidDepthCorners(input.positionWS, input.depth);
                #if defined(FLATWORLD_WATER_STYLIZED)
                half liquidDepth = QuantizeWaterVisualDepth(continuousDepth);
                #else
                half liquidDepth = continuousDepth;
                #endif
                half recess = ComputeShoreRecess(input.positionWS, DecodeWaterShoreMask(input.shore));
                #if !defined(FLATWORLD_WATER_LOW_SPEC)
                UNITY_BRANCH
                if (_LavaMode > 0.5)
                {
                    LavaSurfaceData lava = CalculateLavaSurface(
                        input.positionWS,
                        liquidDepth,
                        recess,
                        main.a);
                    return half4(lava.albedo + lava.emission, lava.alpha);
                }
                #endif
                // 保留原透明度，同时让透明度跟随连续水深跨格平滑变化。
                main.a = ResolveWaterSurfaceAlpha(main.a, continuousDepth);
                WaterSurfaceData surface = CalculateChunkWaterSurface(input.positionWS, input.screenUV, liquidDepth, data);
                main.rgb = ApplyWaterSurface(main.rgb, surface);
                main.rgb = ApplyChunkWaterShore(main.rgb, recess, input.positionWS, data);
                main.rgb = ApplyMoonReflection(main.rgb, surface.moonReflection);
                return main;
            }
            ENDHLSL
        }
    }
}
