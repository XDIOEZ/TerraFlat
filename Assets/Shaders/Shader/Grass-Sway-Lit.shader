Shader "FlatWorld/2D/Grass Sway Lit"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}

        // 兼容 TilemapRenderer 与 SpriteRenderer 的标准属性。
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0

        [Header(Grass Sway)]
        [Toggle] _GrassSwayEnabled("启用草地摆动", Float) = 1
        _GrassSwayAmplitude("摆动幅度", Range(0, 0.2)) = 0.035
        _GrassSwaySpeed("摆动速度", Range(0, 5)) = 1.2
        _GrassSwayFrequency("风场频率", Range(0, 10)) = 1.5
        _GrassBendPower("弯曲曲线", Range(0.5, 4)) = 1.8
        _GrassSecondaryStrength("次级摆动", Range(0, 1)) = 0.35
        _GrassSpriteHeight("草地精灵高度", Range(0.01, 2)) = 0.5
        _GrassTileAnchor("Tile 锚点 Y", Range(0, 1)) = 0.5
        [Toggle] _GrassUseObjectRoot("使用对象根部弯曲", Float) = 0
        _GrassDirection("风向", Vector) = (1, 0, 0, 0)
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        TEXTURE2D(_NormalMap);
        SAMPLER(sampler_NormalMap);

        half4 _MainTex_ST;
        half4 _NormalMap_ST;
        float4 _Color;
        half4 _RendererColor;
        float _GrassSwayEnabled;
        float _GrassSwayAmplitude;
        float _GrassSwaySpeed;
        float _GrassSwayFrequency;
        float _GrassBendPower;
        float _GrassSecondaryStrength;
        float _GrassSpriteHeight;
        float _GrassTileAnchor;
        float _GrassUseObjectRoot;
        float4 _GrassDirection;
        float _GlobalWindStrength;

        // Tilemap 按单元锚点取局部高度，独立 Sprite 则直接以对象原点固定根部。
        float GrassBendWeight(float3 positionOS)
        {
            if (_GrassUseObjectRoot > 0.5)
            {
                float objectHeight = saturate(positionOS.y / max(_GrassSpriteHeight, 0.001));
                return pow(objectHeight, max(_GrassBendPower, 0.01));
            }

            float localY = frac(positionOS.y) - _GrassTileAnchor;
            if (localY > 0.5)
                localY -= 1.0;
            else if (localY < -0.5)
                localY += 1.0;

            float normalizedY = saturate(
                (localY + _GrassSpriteHeight * 0.5) / max(_GrassSpriteHeight, 0.001));
            return pow(normalizedY, max(_GrassBendPower, 0.01));
        }

        // 在 GPU 顶点阶段计算连续风场，所有草地 Tilemap 共用一份材质参数。
        float3 ApplyGrassSway(float3 positionOS)
        {
            float windStrength = saturate(_GlobalWindStrength);
            if (_GrassSwayEnabled < 0.5 || _GrassSwayAmplitude <= 0.0001 || windStrength <= 0.0001)
                return positionOS;

            float2 direction = _GrassDirection.xy;
            direction /= max(length(direction), 0.001);

            float2 worldPosition = TransformObjectToWorld(positionOS).xy;
            float phase = sin(dot(worldPosition, float2(12.9898, 78.233))) * 1.7;
            float time = _Time.y * _GrassSwaySpeed;
            float primary = sin(
                time + phase + dot(worldPosition, direction) * _GrassSwayFrequency);
            float secondary = sin(
                time * 0.63 + phase * 1.71 + worldPosition.y * _GrassSwayFrequency * 0.73);
            float sway = (primary + secondary * _GrassSecondaryStrength)
                * _GrassSwayAmplitude
                * windStrength
                * GrassBendWeight(positionOS);

            positionOS.xy += direction * sway;
            return positionOS;
        }
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex CombinedShapeLightVertex
            #pragma fragment CombinedShapeLightFragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
                #if defined(DEBUG_DISPLAY)
                float3 positionWS : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

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

            Varyings CombinedShapeLightVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                #ifdef UNITY_INSTANCING_ENABLED
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteFlip);
                #endif
                v.positionOS = ApplyGrassSway(v.positionOS);

                o.positionCS = TransformObjectToHClip(v.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(v.positionOS);
                #endif
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                o.color = v.color * _Color * _RendererColor;
                #ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
                #endif
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 CombinedShapeLightFragment(Varyings i) : SV_Target
            {
                const half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(i.uv, i.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 tangent : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 tangentWS : TEXCOORD2;
                half3 bitangentWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings NormalsRenderingVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                #ifdef UNITY_INSTANCING_ENABLED
                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteFlip);
                #endif
                attributes.positionOS = ApplyGrassSway(attributes.positionOS);

                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                o.uv = TRANSFORM_TEX(attributes.uv, _NormalMap);
                o.color = attributes.color;
                o.normalWS = -GetViewForwardDir();
                o.tangentWS = TransformObjectToWorldDir(attributes.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * attributes.tangent.w;
                #ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
                #endif
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            half4 NormalsRenderingFragment(Varyings i) : SV_Target
            {
                const half4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                const half3 normalTS = UnpackNormal(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv));
                return NormalsRenderingShared(
                    mainTex, normalTS, i.tangentWS.xyz, i.bitangentWS.xyz, i.normalWS.xyz);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue" = "Transparent" "RenderType" = "Transparent" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                #if defined(DEBUG_DISPLAY)
                float3 positionWS : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings UnlitVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                #ifdef UNITY_INSTANCING_ENABLED
                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteFlip);
                #endif
                attributes.positionOS = ApplyGrassSway(attributes.positionOS);

                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(attributes.positionOS);
                #endif
                o.uv = TRANSFORM_TEX(attributes.uv, _MainTex);
                o.color = attributes.color * _Color * _RendererColor;
                #ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
                #endif
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                float4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                #if defined(DEBUG_DISPLAY)
                SurfaceData2D surfaceData;
                InputData2D inputData;
                half4 debugColor = 0;
                InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
                InitializeInputData(i.uv, inputData);
                SETUP_DEBUG_DATA_2D(inputData, i.positionWS);
                if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                    return debugColor;
                #endif

                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
