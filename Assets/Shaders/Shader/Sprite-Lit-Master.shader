Shader "Game/2D/Sprite-Lit-Master"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}

        // 额外效果属性
        _WaterY ("Water Surface Y (World)", Float) = 0
        _BodyClip ("Body Bottom Clip (0-1)", Range(0,1)) = 0
        _BodyMinV ("Body UV Bottom", Range(0,1)) = 0
        _BodyMaxV ("Body UV Top", Range(0,1)) = 1

        // 通用角色水体表现：水下染色、透明度和水线
        _WaterEnabled ("Water Immersion Blend", Range(0,1)) = 0
        _WaterSurfaceV ("Water Surface V", Range(0,1)) = 0
        _WaterFeather ("Water Surface Feather", Range(0,0.2)) = 0.035
        _WaterTint ("Water Underwater Tint", Color) = (0.18,0.42,0.78,1)
        _WaterTintStrength ("Water Tint Strength", Range(0,1)) = 0
        _WaterAlpha ("Water Underwater Alpha", Range(0,1)) = 1
        _WaterLineColor ("Water Line Color", Color) = (0.65,0.9,1,1)
        _WaterLineStrength ("Water Line Strength", Range(0,1)) = 0
        _WaterLineWidth ("Water Line Width", Range(0,0.2)) = 0.035
        _WaterWaveAmplitude ("Water Wave Amplitude", Range(0,0.1)) = 0.018
        _WaterWaveFrequency ("Water Wave Frequency", Range(0,30)) = 8
        _WaterWaveSpeed ("Water Wave Speed", Range(0,10)) = 2.4

        _ActorTint ("Actor Status Tint", Color) = (1,1,1,1)
        _ActorTintStrength ("Actor Status Tint Strength", Range(0,1)) = 0
        _HitFlash ("Hit Flash", Range(0,1)) = 0
        _HitFlashColor ("Hit Flash Color", Color) = (1,0.08,0.08,1)

        _Dissolve ("Dissolve", Range(0,1)) = 0
        _DissolveTex ("Dissolve Noise", 2D) = "white" {}

        // Legacy properties，保持与官方 Sprite-Lit-Default 一致，方便管线处理
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // ===== 2D 光照 Pass（在官方 Sprite-Lit-Default 的基础上加上 BodyClip / HitFlash / Dissolve） =====
        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex CombinedShapeLightVertex
            #pragma fragment CombinedShapeLightFragment

			// GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            // 自定义功能开关
            #pragma shader_feature _ DISSOLVE_ON

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2  uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS  : SV_POSITION;
                half4   color       : COLOR;
                float4  uv          : TEXCOORD0; // xy is uv, z is localY, w is localX
                half2   lightingUV  : TEXCOORD1;
                #if defined(DEBUG_DISPLAY)
                float3  positionWS  : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_DissolveTex);
            SAMPLER(sampler_DissolveTex);

            half4 _MainTex_ST;
            float4 _Color;
            half4 _RendererColor;

            // 自定义效果参数
            float _WaterY;
            float _BodyClip;
            float _BodyMinV;
            float _BodyMaxV;
            float _WaterEnabled;
            float _WaterSurfaceV;
            float _WaterFeather;
            float4 _WaterTint;
            float _WaterTintStrength;
            float _WaterAlpha;
            float4 _WaterLineColor;
            float _WaterLineStrength;
            float _WaterLineWidth;
            float _WaterWaveAmplitude;
            float _WaterWaveFrequency;
            float _WaterWaveSpeed;
            float4 _ActorTint;
            float _ActorTintStrength;
            float _HitFlash;
            float4 _HitFlashColor;
            float _Dissolve;

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
                o.positionCS = TransformObjectToHClip(v.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(v.positionOS);
                #endif
                o.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv.z = v.positionOS.y;
                o.uv.w = v.positionOS.x;
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
                half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv.xy);

                // === 下半身剔除：根据 _BodyMinV/_BodyMaxV (实际传入 Local Y) 和 _BodyClip 控制 ===
                // 因为改成了本地坐标 Y (i.uv.z) 替代 uv.y，这能完美免疫贴图旋转切片影响！
                float bodyRange = max(1e-5, _BodyMaxV - _BodyMinV);
                float bodyV = saturate((i.uv.z - _BodyMinV) / bodyRange);
                if (bodyV < _BodyClip)
                    discard;

                // === 水下表现：保留身体轮廓，用染色和透明度表达浸没程度 ===
                float waterBlend = saturate(_WaterEnabled);
                if (waterBlend > 0.0001)
                {
                    float surfaceV = saturate(_WaterSurfaceV);
                    float feather = max(1e-5, _WaterFeather);
                    float wavePhase = i.uv.w * _WaterWaveFrequency + _Time.y * _WaterWaveSpeed;
                    float waveOffset = sin(wavePhase) * _WaterWaveAmplitude;
                    waveOffset += sin(wavePhase * 1.7 - _Time.y * _WaterWaveSpeed * 0.75) * _WaterWaveAmplitude * 0.35;
                    float waveSurfaceV = saturate(surfaceV + waveOffset);
                    float submergedMask = 1.0 - smoothstep(
                        waveSurfaceV - feather,
                        waveSurfaceV + feather,
                        bodyV);
                    submergedMask *= waterBlend;

                    main.rgb = lerp(
                        main.rgb,
                        _WaterTint.rgb,
                        saturate(_WaterTintStrength) * submergedMask);
                    main.a *= lerp(1.0, saturate(_WaterAlpha), submergedMask);

                    // 水线使用柔和带状渐变，避免硬裁剪造成的平直断面。
                    float lineRadius = max(1e-5, _WaterLineWidth);
                    float lineMask = 1.0 - smoothstep(
                        lineRadius,
                        lineRadius + feather,
                        abs(bodyV - waveSurfaceV));
                    lineMask *= waterBlend * saturate(_WaterLineStrength);
                    main.rgb = lerp(
                        main.rgb,
                        _WaterLineColor.rgb,
                        lineMask * saturate(_WaterLineColor.a));
                }

                // === 角色状态染色与受击闪红 ===
                main.rgb = lerp(main.rgb, _ActorTint.rgb, saturate(_ActorTintStrength));
                main.rgb = lerp(main.rgb, _HitFlashColor.rgb, saturate(_HitFlash));

                // === 溶解效果（可选） ===
                #ifdef DISSOLVE_ON
                half noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, i.uv.xy).r;
                if (noise < _Dissolve)
                    discard;
                #endif

                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(i.uv.xy, i.lightingUV, inputData);

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // ===== 法线 Pass（保持官方实现，供法线贴图和调试使用） =====
        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            // GPU Instancing
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                float4 tangent      : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS      : SV_POSITION;
                half4   color           : COLOR;
                float2  uv              : TEXCOORD0;
                half3   normalWS        : TEXCOORD1;
                half3   tangentWS       : TEXCOORD2;
                half3   bitangentWS     : TEXCOORD3;
                float   localY          : TEXCOORD4;
                float   localX          : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            half4 _NormalMap_ST;
            float _BodyMinV;
            float _BodyMaxV;
            float _WaterEnabled;
            float _WaterSurfaceV;
            float _WaterFeather;
            float _WaterAlpha;
            float _WaterWaveAmplitude;
            float _WaterWaveFrequency;
            float _WaterWaveSpeed;
            float4 _ActorTint;
            float _ActorTintStrength;
            float _HitFlash;
            float4 _HitFlashColor;

            Varyings NormalsRenderingVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#ifdef UNITY_INSTANCING_ENABLED
                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteFlip);
#endif
                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                o.uv = TRANSFORM_TEX(attributes.uv, _NormalMap);
                o.color = attributes.color;
                o.normalWS = -GetViewForwardDir();
                o.tangentWS = TransformObjectToWorldDir(attributes.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * attributes.tangent.w;
                o.localY = attributes.positionOS.y;
                o.localX = attributes.positionOS.x;
#ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
#endif
                return o;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"

            half4 NormalsRenderingFragment(Varyings i) : SV_Target
            {
                half4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv));

                float bodyRange = max(1e-5, _BodyMaxV - _BodyMinV);
                float bodyV = saturate((i.localY - _BodyMinV) / bodyRange);
                float feather = max(1e-5, _WaterFeather);
                float wavePhase = i.localX * _WaterWaveFrequency + _Time.y * _WaterWaveSpeed;
                float waveOffset = sin(wavePhase) * _WaterWaveAmplitude;
                waveOffset += sin(wavePhase * 1.7 - _Time.y * _WaterWaveSpeed * 0.75) * _WaterWaveAmplitude * 0.35;
                float waveSurfaceV = saturate(_WaterSurfaceV + waveOffset);
                float submergedMask = 1.0 - smoothstep(
                    waveSurfaceV - feather,
                    waveSurfaceV + feather,
                    bodyV);
                submergedMask *= saturate(_WaterEnabled);
                mainTex.a *= lerp(1.0, saturate(_WaterAlpha), submergedMask);

                return NormalsRenderingShared(mainTex, normalTS, i.tangentWS.xyz, i.bitangentWS.xyz, i.normalWS.xyz);
            }
            ENDHLSL
        }

        // ===== 调试 / 非 2D Renderer 回退用的 Unlit Pass =====
        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            // GPU Instancing
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS      : SV_POSITION;
                float4  color           : COLOR;
                float2  uv              : TEXCOORD0;
                float   localY          : TEXCOORD1;
                float   localX          : TEXCOORD3;
                #if defined(DEBUG_DISPLAY)
                float3  positionWS  : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            half4 _RendererColor;
            float _BodyMinV;
            float _BodyMaxV;
            float _WaterEnabled;
            float _WaterSurfaceV;
            float _WaterFeather;
            float4 _WaterTint;
            float _WaterTintStrength;
            float _WaterAlpha;
            float4 _WaterLineColor;
            float _WaterLineStrength;
            float _WaterLineWidth;
            float _WaterWaveAmplitude;
            float _WaterWaveFrequency;
            float _WaterWaveSpeed;
            float4 _ActorTint;
            float _ActorTintStrength;
            float _HitFlash;
            float4 _HitFlashColor;

            Varyings UnlitVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#ifdef UNITY_INSTANCING_ENABLED
                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteFlip);
#endif
                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(attributes.positionOS);
                #endif
                o.uv = TRANSFORM_TEX(attributes.uv, _MainTex);
                o.localY = attributes.positionOS.y;
                o.localX = attributes.positionOS.x;
                o.color = attributes.color * _Color * _RendererColor;
#ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
#endif
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                float4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                float bodyRange = max(1e-5, _BodyMaxV - _BodyMinV);
                float bodyV = saturate((i.localY - _BodyMinV) / bodyRange);
                float waterBlend = saturate(_WaterEnabled);
                if (waterBlend > 0.0001)
                {
                    float surfaceV = saturate(_WaterSurfaceV);
                    float feather = max(1e-5, _WaterFeather);
                    float wavePhase = i.localX * _WaterWaveFrequency + _Time.y * _WaterWaveSpeed;
                    float waveOffset = sin(wavePhase) * _WaterWaveAmplitude;
                    waveOffset += sin(wavePhase * 1.7 - _Time.y * _WaterWaveSpeed * 0.75) * _WaterWaveAmplitude * 0.35;
                    float waveSurfaceV = saturate(surfaceV + waveOffset);
                    float submergedMask = 1.0 - smoothstep(
                        waveSurfaceV - feather,
                        waveSurfaceV + feather,
                        bodyV);
                    submergedMask *= waterBlend;
                    mainTex.rgb = lerp(
                        mainTex.rgb,
                        _WaterTint.rgb,
                        saturate(_WaterTintStrength) * submergedMask);
                    mainTex.a *= lerp(1.0, saturate(_WaterAlpha), submergedMask);

                    float lineRadius = max(1e-5, _WaterLineWidth);
                    float lineMask = 1.0 - smoothstep(
                        lineRadius,
                        lineRadius + feather,
                        abs(bodyV - waveSurfaceV));
                    lineMask *= waterBlend * saturate(_WaterLineStrength);
                    mainTex.rgb = lerp(
                        mainTex.rgb,
                        _WaterLineColor.rgb,
                        lineMask * saturate(_WaterLineColor.a));
                }

                // === 角色状态染色与受击闪红 ===
                mainTex.rgb = lerp(mainTex.rgb, _ActorTint.rgb, saturate(_ActorTintStrength));
                mainTex.rgb = lerp(mainTex.rgb, _HitFlashColor.rgb, saturate(_HitFlash));

                #if defined(DEBUG_DISPLAY)
                SurfaceData2D surfaceData;
                InputData2D inputData;
                half4 debugColor = 0;

                InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
                InitializeInputData(i.uv, inputData);
                SETUP_DEBUG_DATA_2D(inputData, i.positionWS);

                if(CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                {
                    return debugColor;
                }
                #endif

                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
