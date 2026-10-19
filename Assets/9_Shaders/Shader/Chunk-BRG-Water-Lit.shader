// 与机械 Sprite 共用 ChunkBRGInstance.hlsl 的实例布局。
Shader "FlatWorld/2D/Chunk BRG Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}
        [Header(Ocean Surface)]
        _DeepColor("深海颜色", Color) = (0.02, 0.09, 0.36, 1)
        _ShallowColor("浅海颜色", Color) = (0.42, 0.72, 0.98, 1)
        _SurfaceTint("海水染色强度", Range(0, 1)) = 1
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.74
        _DetailScale("细浪尺度", Range(0.5, 12)) = 4.6
        _WaveSpeed("波浪传播速度", Range(0, 3)) = 0.42
        _WaveDistortion("海流扭曲", Range(0, 4)) = 0.8
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.44
        _PixelDensity("风格化采样密度", Range(1, 128)) = 64
        _WaveDirection("波浪传播方向（零为跟随水流/风向）", Vector) = (0, 0, 0, 0)
        _FlowSpeed("水流平移倍率", Range(0, 4)) = 1
        _FlowCycleSeconds("流动交接周期（秒）", Range(2, 16)) = 8
        _FlowDirection("静水默认波向", Vector) = (1, 0.35, 0, 0)
        _TideCyclesPerDay("每日潮汐循环次数", Range(1, 4)) = 2.0
        _RippleColor("浪脊颜色", Color) = (0.3, 0.57, 0.98, 1)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.12
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 2.8
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.18
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.06
        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (0.38, 0.57, 0.9, 1)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.65
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.68
        _ReflectionDirection("镜面环境方向", Vector) = (-0.35, 0.18, 0.92, 0)
        _SpecularColor("太阳高光", Color) = (1, 0.97, 0.88, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.34
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 72
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (0.35, 0.58, 0.96, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.025
        _FoamColor("泡沫颜色", Color) = (0.75, 0.87, 1, 1)
        _WhitecapStrength("浪峰白沫", Range(0, 1)) = 0.12
        [Header(River Current)]
        [NoScaleOffset] _RiverPatternTex("河流RGBA图(RG斜率/B碎光/A泡沫)", 2D) = "gray" {}
        _RiverFoamCoverage("河流泡沫覆盖率", Range(0, 1)) = 0.18
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
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.22
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.18
        _ShoreColor("岸线亮部", Color) = (0.32, 0.56, 0.92, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.035
        _ShoreFoamStrength("岸边泡沫强度", Range(0, 1)) = 0.3
        _FoamSpeed("岸边泡沫速度", Range(0, 3)) = 0.52
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    HLSLINCLUDE
        #include "ChunkBRGInstance.hlsl"
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
            float _UnityNormalScale;
            float _UnityFoamScale;
            float _UnitySparkleScale;
            float _UnitySparkleStrength;
            float _UnitySparkleExponent;
            float _UnityDepthDensity;
            float4 _UnitySparkleColor;
        CBUFFER_END
        #if defined(UNITY_DOTS_INSTANCING_ENABLED)
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _Color)
            UNITY_DOTS_INSTANCED_PROP(float4, _RendererColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _DeepColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _ShallowColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _ReflectionColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _SpecularColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _CausticColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _FoamColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _RippleColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _MoonReflectionColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _ShoreColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _ReflectionDirection)
            UNITY_DOTS_INSTANCED_PROP(float4, _SunDirection)
            UNITY_DOTS_INSTANCED_PROP(float4, _MoonReflectionPosition)
            UNITY_DOTS_INSTANCED_PROP(float4, _FlowDirection)
            UNITY_DOTS_INSTANCED_PROP(float4, _WaveDirection)
            UNITY_DOTS_INSTANCED_PROP(float, _FlowSpeed)
            UNITY_DOTS_INSTANCED_PROP(float, _FlowCycleSeconds)
            UNITY_DOTS_INSTANCED_PROP(float, _RiverFoamCoverage)
            UNITY_DOTS_INSTANCED_PROP(float, _TideCyclesPerDay)
            UNITY_DOTS_INSTANCED_PROP(float, _SurfaceTint)
            UNITY_DOTS_INSTANCED_PROP(float, _SwellScale)
            UNITY_DOTS_INSTANCED_PROP(float, _DetailScale)
            UNITY_DOTS_INSTANCED_PROP(float, _WaveSpeed)
            UNITY_DOTS_INSTANCED_PROP(float, _WaveDistortion)
            UNITY_DOTS_INSTANCED_PROP(float, _NormalStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _PixelDensity)
            UNITY_DOTS_INSTANCED_PROP(float, _RippleStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _RippleScale)
            UNITY_DOTS_INSTANCED_PROP(float, _RippleWidth)
            UNITY_DOTS_INSTANCED_PROP(float, _RippleShadowStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ReflectionStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ReflectionSmoothness)
            UNITY_DOTS_INSTANCED_PROP(float, _SpecularStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _SpecularPower)
            UNITY_DOTS_INSTANCED_PROP(float, _CausticStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _WhitecapStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _MoonReflectionStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _MoonDiscRadius)
            UNITY_DOTS_INSTANCED_PROP(float, _MoonTrailLength)
            UNITY_DOTS_INSTANCED_PROP(float, _MoonTrailWidth)
            UNITY_DOTS_INSTANCED_PROP(float, _EdgeWidth)
            UNITY_DOTS_INSTANCED_PROP(float, _CornerStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ShoreStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ShoreFoamStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _FoamSpeed)
            UNITY_DOTS_INSTANCED_PROP(float, _UnityNormalScale)
            UNITY_DOTS_INSTANCED_PROP(float, _UnityFoamScale)
            UNITY_DOTS_INSTANCED_PROP(float, _UnitySparkleScale)
            UNITY_DOTS_INSTANCED_PROP(float, _UnitySparkleStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _UnitySparkleExponent)
            UNITY_DOTS_INSTANCED_PROP(float, _UnityDepthDensity)
            UNITY_DOTS_INSTANCED_PROP(float4, _UnitySparkleColor)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
        #define _RendererColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RendererColor)
        #define _DeepColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _DeepColor)
        #define _ShallowColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _ShallowColor)
        #define _ReflectionColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _ReflectionColor)
        #define _SpecularColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecularColor)
        #define _CausticColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _CausticColor)
        #define _FoamColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _FoamColor)
        #define _RippleColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RippleColor)
        #define _MoonReflectionColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _MoonReflectionColor)
        #define _ShoreColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _ShoreColor)
        #define _ReflectionDirection UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _ReflectionDirection)
        #define _SunDirection UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SunDirection)
        #define _MoonReflectionPosition UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _MoonReflectionPosition)
        #define _FlowDirection UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _FlowDirection)
        #define _WaveDirection UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _WaveDirection)
        #define _FlowSpeed UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _FlowSpeed)
        #define _FlowCycleSeconds UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _FlowCycleSeconds)
        #define _RiverFoamCoverage UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _RiverFoamCoverage)
        #define _TideCyclesPerDay UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _TideCyclesPerDay)
        #define _SurfaceTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SurfaceTint)
        #define _SwellScale UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SwellScale)
        #define _DetailScale UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _DetailScale)
        #define _WaveSpeed UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _WaveSpeed)
        #define _WaveDistortion UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _WaveDistortion)
        #define _NormalStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _NormalStrength)
        #define _PixelDensity UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _PixelDensity)
        #define _RippleStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _RippleStrength)
        #define _RippleScale UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _RippleScale)
        #define _RippleWidth UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _RippleWidth)
        #define _RippleShadowStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _RippleShadowStrength)
        #define _ReflectionStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ReflectionStrength)
        #define _ReflectionSmoothness UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ReflectionSmoothness)
        #define _SpecularStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SpecularStrength)
        #define _SpecularPower UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SpecularPower)
        #define _CausticStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _CausticStrength)
        #define _WhitecapStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _WhitecapStrength)
        #define _MoonReflectionStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _MoonReflectionStrength)
        #define _MoonDiscRadius UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _MoonDiscRadius)
        #define _MoonTrailLength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _MoonTrailLength)
        #define _MoonTrailWidth UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _MoonTrailWidth)
        #define _EdgeWidth UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _EdgeWidth)
        #define _CornerStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _CornerStrength)
        #define _ShoreStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ShoreStrength)
        #define _ShoreFoamStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ShoreFoamStrength)
        #define _FoamSpeed UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _FoamSpeed)
        #define _UnityNormalScale UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _UnityNormalScale)
        #define _UnityFoamScale UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _UnityFoamScale)
        #define _UnitySparkleScale UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _UnitySparkleScale)
        #define _UnitySparkleStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _UnitySparkleStrength)
        #define _UnitySparkleExponent UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _UnitySparkleExponent)
        #define _UnityDepthDensity UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _UnityDepthDensity)
        #define _UnitySparkleColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _UnitySparkleColor)
        #endif
        #include "WaterSurfaceCommon.hlsl"
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
        WaterSurfaceData CalculateChunkWaterSurface(float2 positionWS, float2 screenUV, half depth)
        {
            #if defined(FLATWORLD_WATER_LOW_SPEC)
            ChunkBRGInstanceData data = LoadChunkBRGInstanceData();
            return CalculateLowSpecWaterSurface(positionWS, depth,
                InterpolateWaterCurrent(positionWS, data.flowX, data.flowY), data.transform0.w);
            #else
            ChunkBRGInstanceData data = LoadChunkBRGInstanceData();
            WaterMotionData motion = ResolveWaterMotion(
                InterpolateWaterCurrent(positionWS, data.flowX, data.flowY), data.transform0.w);
            WaterSurfaceData surface = (WaterSurfaceData)0;
            #if !defined(FLATWORLD_WATER_UNITY_STYLIZED) && !defined(FLATWORLD_WATER_LOW_SPEC)
            float2 positionDx = ddx(positionWS);
            float2 positionDy = ddy(positionWS);
            UNITY_BRANCH
            if (abs(data.transform0.w - 1.0) < 0.25 || abs(data.transform0.w - 3.0) < 0.25)
                surface = CalculateRiverSurface(positionWS, screenUV, depth, data.flowX, data.flowY, motion, positionDx, positionDy);
            else
            #endif
                surface = CalculateWaterSurface(positionWS, screenUV, depth, motion);
            return surface;
            #endif
        }

        half3 ApplyChunkWaterShore(half3 sourceColor, half recess, float2 positionWS)
        {
            #if defined(FLATWORLD_WATER_LOW_SPEC)
            return ApplyLowSpecWaterShore(sourceColor, recess);
            #else
            ChunkBRGInstanceData data = LoadChunkBRGInstanceData();
            WaterMotionData motion = ResolveWaterMotion(
                InterpolateWaterCurrent(positionWS, data.flowX, data.flowY), data.transform0.w);
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
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_local_fragment _ FLATWORLD_WATER_STYLIZED FLATWORLD_WATER_UNITY_STYLIZED FLATWORLD_WATER_LOW_SPEC
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            struct Attributes { float3 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half2 lightingUV:TEXCOORD1; half4 shore:TEXCOORD2; half4 depth:TEXCOORD3; float2 positionWS:TEXCOORD4; half4 tint:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
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
                ChunkBRGInstanceData d = LoadChunkBRGInstanceData();
                float3 positionWS = TransformChunkBRGVertex(input.positionOS, d);
                Varyings o = (Varyings)0;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS.xy;
                o.uv = input.uv;
                o.shore = d.data0;
                o.depth = d.data1;
                o.tint = input.color * d.tint;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.tint * _Color * _RendererColor;
                half continuousDepth = SampleContinuousLiquidDepthCorners(input.positionWS, input.depth);
                #if defined(FLATWORLD_WATER_STYLIZED)
                half liquidDepth = QuantizeWaterVisualDepth(continuousDepth);
                #else
                half liquidDepth = continuousDepth;
                #endif
                // 保留原透明度，同时让透明度跟随连续水深跨格平滑变化。
                main.a = ResolveWaterSurfaceAlpha(main.a, continuousDepth);
                WaterSurfaceData surface = CalculateChunkWaterSurface(input.positionWS, input.lightingUV, liquidDepth);
                main.rgb = ApplyWaterSurface(main.rgb, surface);
                half recess = ComputeShoreRecess(input.positionWS, DecodeWaterShoreMask(input.shore));
                main.rgb = ApplyChunkWaterShore(main.rgb, recess, input.positionWS);
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
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_local_fragment _ FLATWORLD_WATER_STYLIZED FLATWORLD_WATER_UNITY_STYLIZED FLATWORLD_WATER_LOW_SPEC
            struct Attributes { float3 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 shore:TEXCOORD1; half4 depth:TEXCOORD2; float2 positionWS:TEXCOORD3; float2 screenUV:TEXCOORD4; half4 tint:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ChunkBRGInstanceData d = LoadChunkBRGInstanceData();
                float3 positionWS = TransformChunkBRGVertex(input.positionOS, d);
                Varyings o = (Varyings)0;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS.xy;
                o.uv = input.uv;
                o.shore = d.data0;
                o.depth = d.data1;
                o.tint = input.color * d.tint;
                float4 screenPosition = ComputeScreenPos(o.positionCS);
                o.screenUV = screenPosition.xy / max(screenPosition.w, 0.0001);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.tint * _Color * _RendererColor;
                half continuousDepth = SampleContinuousLiquidDepthCorners(input.positionWS, input.depth);
                #if defined(FLATWORLD_WATER_STYLIZED)
                half liquidDepth = QuantizeWaterVisualDepth(continuousDepth);
                #else
                half liquidDepth = continuousDepth;
                #endif
                // 保留原透明度，同时让透明度跟随连续水深跨格平滑变化。
                main.a = ResolveWaterSurfaceAlpha(main.a, continuousDepth);
                WaterSurfaceData surface = CalculateChunkWaterSurface(input.positionWS, input.screenUV, liquidDepth);
                main.rgb = ApplyWaterSurface(main.rgb, surface);
                half recess = ComputeShoreRecess(input.positionWS, DecodeWaterShoreMask(input.shore));
                main.rgb = ApplyChunkWaterShore(main.rgb, recess, input.positionWS);
                main.rgb = ApplyMoonReflection(main.rgb, surface.moonReflection);
                return main;
            }
            ENDHLSL
        }
    }
}
