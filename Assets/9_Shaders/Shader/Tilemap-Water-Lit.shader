Shader "FlatWorld/2D/Tilemap Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}
        [PerRendererData] _WaterDepthTexture("水深场", 2D) = "black" {}
        [HideInInspector] _WaterDepthUvScaleOffset("水深纹理坐标", Vector) = (1,1,0,0)

        [Header(Ocean Surface)]
        _DeepColor("深海颜色", Color) = (0.035, 0.13, 0.19, 1)
        _ShallowColor("浅海颜色", Color) = (0.19, 0.42, 0.4, 1)
        _SurfaceTint("海水染色强度", Range(0, 1)) = 1
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.74
        _DetailScale("细浪尺度", Range(0.5, 12)) = 4.6
        _WaveSpeed("海流速度", Range(-3, 3)) = 0.42
        _WaveDistortion("海流扭曲", Range(0, 4)) = 0.8
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.44
        _PixelDensity("风格化采样密度", Range(1, 128)) = 64
        _FlowDirection("流动方向", Vector) = (1, 0.35, 0, 0)
        _TideCyclesPerDay("每日潮汐循环次数", Range(1, 4)) = 2.0
        _RippleColor("浪脊颜色", Color) = (0.3, 0.53, 0.53, 1)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.12
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 2.8
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.18
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.06

        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (0.52, 0.65, 0.72, 1)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.65
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.68
        _ReflectionDirection("镜面环境方向", Vector) = (-0.35, 0.18, 0.92, 0)
        _SpecularColor("太阳高光", Color) = (1, 0.97, 0.88, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.34
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 72
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (0.42, 0.66, 0.58, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.025
        _FoamColor("泡沫颜色", Color) = (0.81, 0.87, 0.85, 1)
        _WhitecapStrength("浪峰白沫", Range(0, 1)) = 0.12

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
        _ShoreColor("岸线亮部", Color) = (0.39, 0.57, 0.53, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.035
        _ShoreFoamStrength("岸边泡沫强度", Range(0, 1)) = 0.3
        _FoamSpeed("岸边泡沫速度", Range(0, 3)) = 0.52

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
        #include "WaterSurfaceCommon.hlsl"
        #if defined(FLATWORLD_WATER_STYLIZED)
            #include "WaterSurfaceStylized.hlsl"
        #else
            #include "WaterSurfaceRealistic.hlsl"
        #endif
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
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ FLATWORLD_WATER_STYLIZED
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
                half4 waterTileData : TEXCOORD2;
                float2 positionWS : TEXCOORD3;
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

            /// <summary>准备 2D 光照、岸线、世界位置与屏幕位置数据。</summary>
            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.waterTileData = input.color;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            /// <summary>合成受 2D 灯光影响的水面，并在末尾叠加月光倒影。</summary>
            half4 WaterFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                half4 shoreMask = DecodeWaterShoreMask(input.waterTileData);
                half waterDepth = SampleWaterDepth(input.positionWS);
                WaterSurfaceData waterSurface = CalculateWaterSurface(
                    input.positionWS,
                    input.lightingUV,
                    waterDepth);
                main.rgb = ApplyWaterSurface(main.rgb, waterSurface);
                half recess = ComputeShoreRecess(input.positionWS, shoreMask);
                main.rgb = ApplyShore(main.rgb, recess, input.positionWS);

                half4 lightMask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, lightMask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                lit.rgb = ApplyMoonReflection(lit.rgb, waterSurface.moonReflection);
                return lit;
            }
            ENDHLSL
        }

        // 非 2D Renderer 下保留同样的水面表现，方便 Scene 视图检查。
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ FLATWORLD_WATER_STYLIZED

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
                half4 waterTileData : TEXCOORD1;
                float2 positionWS : TEXCOORD2;
                float2 screenUV : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            /// <summary>准备非 2D Renderer 下的水面与屏幕位置数据。</summary>
            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.waterTileData = input.color;
                float4 screenPosition = ComputeScreenPos(output.positionCS);
                output.screenUV = screenPosition.xy / max(screenPosition.w, 0.0001);
                return output;
            }

            /// <summary>合成非 2D Renderer 下的水面及月光倒影。</summary>
            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                half4 shoreMask = DecodeWaterShoreMask(input.waterTileData);
                half waterDepth = SampleWaterDepth(input.positionWS);
                WaterSurfaceData waterSurface = CalculateWaterSurface(
                    input.positionWS,
                    input.screenUV,
                    waterDepth);
                main.rgb = ApplyWaterSurface(main.rgb, waterSurface);
                half recess = ComputeShoreRecess(input.positionWS, shoreMask);
                main.rgb = ApplyShore(main.rgb, recess, input.positionWS);
                main.rgb = ApplyMoonReflection(main.rgb, waterSurface.moonReflection);
                return main;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
