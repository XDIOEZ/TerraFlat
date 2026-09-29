// 机械轴芯在片元阶段折返 UV，避免跨贴图边界时被顶点插值拉伸。
Shader "FlatWorld/2D/Chunk BRG Sprite Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        _GrassSwayEnabled("启用植被摆动", Float) = 0
        _GrassSwayAmplitude("摆动幅度", Float) = 0
        _GrassSwaySpeed("摆动速度", Float) = 0
        _GrassSwayFrequency("风场频率", Float) = 0
        _GrassBendPower("弯曲曲线", Float) = 1
        _GrassSecondaryStrength("次级摆动", Float) = 0
        _GrassSpriteHeight("精灵高度", Float) = 0.5
        _GrassBendStart("起摆高度", Float) = 0
        _GrassTileAnchor("Tile 锚点", Float) = 0.5
        _GrassUseObjectRoot("使用对象根部弯曲", Float) = 0
        _GrassDirection("风向", Vector) = (1, 0, 0, 0)
    }

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
            #pragma multi_compile_local _ _CHUNK_GRASS_SWAY
            #pragma multi_compile_local _ _CHUNK_MECHANICAL
            #pragma multi_compile_local _ _CHUNK_RESOURCE
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __

            #include "ChunkBRGInstance.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
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
                half4 tint : COLOR;
                #if defined(_CHUNK_RESOURCE)
                float resourceLocalY : TEXCOORD4;
                #endif
                #if defined(_CHUNK_MECHANICAL)
                float4 mechanicalAnimation : TEXCOORD2;
                float4 mechanicalRegion : TEXCOORD3;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            float4 _WorldSunShadow;
            float4 _WorldSunShadowColor;
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RendererColor;
                float _GrassSwayEnabled;
                float _GrassSwayAmplitude;
                float _GrassSwaySpeed;
                float _GrassSwayFrequency;
                float _GrassBendPower;
                float _GrassSecondaryStrength;
                float _GrassSpriteHeight;
                float _GrassBendStart;
                float _GrassTileAnchor;
                float _GrassUseObjectRoot;
                float4 _GrassDirection;
            CBUFFER_END
            #define FLATWORLD_VEGETATION_SWAY_MATERIAL_DECLARED
            #include "VegetationSway.hlsl"
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _Color)
                UNITY_DOTS_INSTANCED_PROP(float4, _RendererColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
            #define _RendererColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RendererColor)
            #endif

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
                ChunkBRGInstanceData instanceData = LoadChunkBRGInstanceData();
                float3 positionOS = input.positionOS;
                #if defined(_CHUNK_MECHANICAL)
                positionOS = AnimateChunkMechanicalVertex(positionOS, instanceData);
                #endif
                float3 positionWS = TransformChunkBRGVertex(positionOS, instanceData);
                #if defined(_CHUNK_GRASS_SWAY)
                positionWS = ApplyChunkGrassSway(input.positionOS, positionWS);
                #endif
                #if defined(_CHUNK_RESOURCE)
                if (instanceData.data0.z > 0.5)
                {
                    float height = max(0, positionWS.y - instanceData.flowX.y) * instanceData.flowX.z;
                    height = min(height, _WorldSunShadow.z / max(0.0001, length(_WorldSunShadow.xy)));
                    positionWS.xy = float2(positionWS.x, instanceData.flowX.y) + _WorldSunShadow.xy * height;
                }
                #endif
                Varyings output = (Varyings)0;
                #if defined(_CHUNK_RESOURCE)
                output.resourceLocalY = input.positionOS.y;
                #endif
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                #if defined(_CHUNK_MECHANICAL)
                output.mechanicalAnimation = instanceData.data0;
                output.mechanicalRegion = instanceData.data1;
                #endif
                output.tint = input.color * instanceData.tint;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 uv = input.uv;
                #if defined(_CHUNK_MECHANICAL)
                uv = AnimateChunkMechanicalUV(uv, input.mechanicalAnimation, input.mechanicalRegion);
                #endif
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                #if defined(_CHUNK_RESOURCE)
                ChunkBRGInstanceData resource = LoadChunkBRGInstanceData();
                if (resource.data0.w > 0.5) clip(input.resourceLocalY - resource.data0.x);
                if (resource.data0.z > 0.5)
                    return half4(_WorldSunShadowColor.rgb,
                        main.a * input.tint.a * _WorldSunShadowColor.a * _WorldSunShadow.w);
                #endif
                main *= input.tint * _Color * _RendererColor;
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
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
            #pragma multi_compile_local _ _CHUNK_GRASS_SWAY
            #pragma multi_compile_local _ _CHUNK_MECHANICAL
            #pragma multi_compile_local _ _CHUNK_RESOURCE
            #include "ChunkBRGInstance.hlsl"
            struct Attributes { float3 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float2 uv:TEXCOORD0;
                half4 tint:COLOR;
                #if defined(_CHUNK_RESOURCE)
                float resourceLocalY:TEXCOORD4;
                #endif
                #if defined(_CHUNK_MECHANICAL)
                float4 mechanicalAnimation:TEXCOORD2;
                float4 mechanicalRegion:TEXCOORD3;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _WorldSunShadow;
            float4 _WorldSunShadowColor;
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RendererColor;
                float _GrassSwayEnabled;
                float _GrassSwayAmplitude;
                float _GrassSwaySpeed;
                float _GrassSwayFrequency;
                float _GrassBendPower;
                float _GrassSecondaryStrength;
                float _GrassSpriteHeight;
                float _GrassBendStart;
                float _GrassTileAnchor;
                float _GrassUseObjectRoot;
                float4 _GrassDirection;
            CBUFFER_END
            #define FLATWORLD_VEGETATION_SWAY_MATERIAL_DECLARED
            #include "VegetationSway.hlsl"
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _Color)
                UNITY_DOTS_INSTANCED_PROP(float4, _RendererColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
            #define _RendererColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RendererColor)
            #endif
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ChunkBRGInstanceData d = LoadChunkBRGInstanceData();
                float3 positionOS = input.positionOS;
                #if defined(_CHUNK_MECHANICAL)
                positionOS = AnimateChunkMechanicalVertex(positionOS, d);
                #endif
                float3 positionWS = TransformChunkBRGVertex(positionOS, d);
                #if defined(_CHUNK_GRASS_SWAY)
                positionWS = ApplyChunkGrassSway(input.positionOS, positionWS);
                #endif
                #if defined(_CHUNK_RESOURCE)
                if (d.data0.z > 0.5)
                {
                    float height = max(0, positionWS.y - d.flowX.y) * d.flowX.z;
                    height = min(height, _WorldSunShadow.z / max(0.0001, length(_WorldSunShadow.xy)));
                    positionWS.xy = float2(positionWS.x, d.flowX.y) + _WorldSunShadow.xy * height;
                }
                #endif
                Varyings o = (Varyings)0;
                #if defined(_CHUNK_RESOURCE)
                o.resourceLocalY = input.positionOS.y;
                #endif
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = input.uv;
                #if defined(_CHUNK_MECHANICAL)
                o.mechanicalAnimation = d.data0;
                o.mechanicalRegion = d.data1;
                #endif
                o.tint = input.color * d.tint;
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 uv = input.uv;
                #if defined(_CHUNK_MECHANICAL)
                uv = AnimateChunkMechanicalUV(uv, input.mechanicalAnimation, input.mechanicalRegion);
                #endif
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                #if defined(_CHUNK_RESOURCE)
                ChunkBRGInstanceData resource = LoadChunkBRGInstanceData();
                if (resource.data0.w > 0.5) clip(input.resourceLocalY - resource.data0.x);
                if (resource.data0.z > 0.5)
                    return half4(_WorldSunShadowColor.rgb,
                        main.a * input.tint.a * _WorldSunShadowColor.a * _WorldSunShadow.w);
                #endif
                return main * input.tint * _Color * _RendererColor;
            }
            ENDHLSL
        }
    }
}
