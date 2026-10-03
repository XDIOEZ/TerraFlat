Shader "FlatWorld/2D/Tilemap Lava Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("岩浆遮罩", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}
        [PerRendererData] _LiquidDepthTexture("液深场", 2D) = "black" {}
        [HideInInspector] _LiquidDepthUvScaleOffset("液深UV", Vector) = (1,1,0,0)

        [Header(Lava Color)]
        [HDR] _LavaCrustColor("冷却壳颜色", Color) = (0.07, 0.008, 0.003, 1)
        [HDR] _LavaMoltenColor("熔融颜色", Color) = (1.0, 0.11, 0.008, 1)
        [HDR] _LavaHotColor("高温高光", Color) = (1.0, 0.68, 0.055, 1)
        _LavaEmissionStrength("自发光强度", Range(0, 4)) = 1.7

        [Header(Lava Flow)]
        _LavaFlowDirection("流动方向", Vector) = (0.82, 0.57, 0, 0)
        _LavaScale("大纹理尺度", Range(0.2, 6)) = 1.35
        _LavaDetailScale("细节尺度", Range(1, 8)) = 2.8
        _LavaFlowSpeed("流动速度", Range(0, 1.5)) = 0.18
        _LavaDistortion("流动扭曲", Range(0, 2)) = 0.58
        _LavaHotThreshold("熔融阈值", Range(0, 1)) = 0.55
        _LavaHotSoftness("熔融过渡", Range(0.01, 0.4)) = 0.12
        _LavaCoreThreshold("高光阈值", Range(0, 1)) = 0.78
        _LavaCoreSoftness("高光过渡", Range(0.01, 0.3)) = 0.085
        _LavaPulseSpeed("高光脉动速度", Range(0, 4)) = 1.1
        _LavaPulseStrength("高光脉动幅度", Range(0, 0.3)) = 0.07

        [Header(Lava Crust)]
        _LavaCrustStrength("冷却壳强度", Range(0, 1.5)) = 0.76
        _LavaShoreCrust("岸边冷却", Range(0, 1.5)) = 0.9
        _LavaShallowCrust("浅层冷却", Range(0, 1)) = 0.24
        _LavaShallowAlpha("浅层透明度", Range(0, 1)) = 0.94
        _LavaDeepAlpha("深层透明度", Range(0, 1)) = 0.99
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.22
        _CornerStrength("岸角叠加", Range(0, 1)) = 0.18

        [HideInInspector] _LavaMode("Lava Mode", Float) = 1
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    HLSLINCLUDE
        half4 _LavaCrustColor;
        half4 _LavaMoltenColor;
        half4 _LavaHotColor;
        float4 _LavaFlowDirection;
        float _LavaScale;
        float _LavaDetailScale;
        float _LavaFlowSpeed;
        float _LavaDistortion;
        half _LavaHotThreshold;
        half _LavaHotSoftness;
        half _LavaCoreThreshold;
        half _LavaCoreSoftness;
        half _LavaCrustStrength;
        half _LavaShoreCrust;
        half _LavaShallowCrust;
        half _LavaEmissionStrength;
        float _LavaPulseSpeed;
        half _LavaPulseStrength;
        half _LavaShallowAlpha;
        half _LavaDeepAlpha;
        float _LavaMode;

        #include "WaterSurfaceCommon.hlsl"
        #include "LavaSurfaceCommon.hlsl"
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
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __

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
                half4 shore : TEXCOORD2;
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

            Varyings LavaVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.shore = input.color;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 LavaFragment(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                source *= _Color * _RendererColor;
                half liquidDepth = SampleLiquidDepth(input.positionWS);
                half shoreRecess = ComputeShoreRecess(
                    input.positionWS,
                    DecodeWaterShoreMask(input.shore));
                LavaSurfaceData lava = CalculateLavaSurface(
                    input.positionWS,
                    liquidDepth,
                    shoreRecess,
                    source.a);

                half4 lightMask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(lava.albedo, lava.alpha, lightMask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                lit.rgb += lava.emission;
                lit.a = lava.alpha;
                return lit;
            }
            ENDHLSL
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
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
                half4 shore : TEXCOORD1;
                float2 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LavaVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.shore = input.color;
                return output;
            }

            half4 LavaFragment(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                source *= _Color * _RendererColor;
                half liquidDepth = SampleLiquidDepth(input.positionWS);
                half shoreRecess = ComputeShoreRecess(
                    input.positionWS,
                    DecodeWaterShoreMask(input.shore));
                LavaSurfaceData lava = CalculateLavaSurface(
                    input.positionWS,
                    liquidDepth,
                    shoreRecess,
                    source.a);
                return half4(lava.albedo + lava.emission, lava.alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
