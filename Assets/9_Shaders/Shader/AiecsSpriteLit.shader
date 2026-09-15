Shader "Game/2D/AIECS Sprite Lit"
{
    Properties
    {
        _MainTex("帧图集", 2D) = "white" {}
        _WaterTint("水下颜色", Color) = (0.18,0.42,0.78,1)
        _WaterAlpha("水下透明度", Range(0,1)) = 0.1
        _WaterLineColor("水线颜色", Color) = (0.65,0.9,1,1)
        _WaterLineStrength("水线强度", Range(0,1)) = 0.8
        _WaterFeather("水线柔化", Float) = 0.035
        _WaterLineWidth("水线宽度", Float) = 0.035
        _WaterWaveAmplitude("波幅", Float) = 0.018
        _WaterWaveFrequency("波频率", Float) = 8
        _WaterWaveSpeed("波速度", Float) = 2.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #include "ActorWaterCommon.hlsl"
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _WaterTint, _WaterLineColor;
            float _WaterAlpha, _WaterLineStrength, _WaterFeather, _WaterLineWidth;
            float _WaterWaveAmplitude, _WaterWaveFrequency, _WaterWaveSpeed;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 water : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 water : TEXCOORD1;
            float2 world : TEXCOORD2;
            half2 lightingUV : TEXCOORD3;
            half4 color : COLOR;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // 顶点已按透明顺序装入网格；世界坐标水线不依赖图集 UV 或网格根节点。
        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.world = TransformObjectToWorld(input.positionOS).xy;
            output.uv = input.uv;
            output.water = input.water;
            output.color = input.color;
            output.lightingUV = ComputeScreenPos(output.positionCS / output.positionCS.w).xy;
            return output;
        }

        // 颜色与法线 Pass 使用同一透明轮廓，水下仍使用 Alpha Blend。
        half4 SampleBody(Varyings input)
        {
            half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
            return FlatWorldApplyActorWater(main, 0, 0, input.world,
                float4(input.water.x, 1, input.water.y, input.water.z),
                float4(0, _WaterFeather, _WaterLineWidth, _WaterWaveAmplitude),
                float4(_WaterWaveFrequency, _WaterWaveSpeed, input.water.w, _WaterLineStrength),
                _WaterTint, _WaterLineColor, _WaterAlpha, _Time.y);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
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

            // 在当前原生 layer batch 绑定的 Shape Light 纹理中完成光照。
            half4 Fragment(Varyings input) : SV_Target
            {
                half4 main = SampleBody(input);
                SurfaceData2D surface;
                InputData2D lighting;
                InitializeSurfaceData(main.rgb, main.a, half4(1,1,1,1), surface);
                InitializeInputData(input.uv, input.lightingUV, lighting);
                return CombinedShapeLightShared(surface, lighting);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="NormalsRendering" }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Normals
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            // 当前导出的普通帧采用平面法线；自定义法线贴图必须由导出器明确登记。
            half4 Normals(Varyings input) : SV_Target
            {
                return NormalsRenderingShared(SampleBody(input), half3(0,0,1),
                    half3(1,0,0), half3(0,1,0), -GetViewForwardDir());
            }
            ENDHLSL
        }
    }
}
