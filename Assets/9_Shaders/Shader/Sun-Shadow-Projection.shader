Shader "FlatWorld/2D/Sun Shadow Projection"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("External Alpha Enabled", Float) = 0
        [HideInInspector] _SunShadowCaster("Foot Y / Height Scale / Height / Alpha", Vector) = (0,1,1,1)
        [HideInInspector] _SunShadowBatched("Batched Geometry", Float) = 0
        [HideInInspector] _SunShadowTexelSize("Source Texel Size", Vector) = (0.015625,0.015625,0,0)
        [HideInInspector] _SunShadowUvBounds("Source UV Bounds", Vector) = (0,0,1,1)
    }
    SubShader
    {
        // Default/0 队列 2990：BRG 地形与水体之后，Blocking(2992)、草(2993)和实体之前。
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "SunShadow"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);
            // 全局太阳和柔化参数不能声明在 Properties 中，否则材质默认值会屏蔽 Shader.SetGlobalFloat/Vector。
            float4 _WorldSunShadow;
            half4 _WorldSunShadowColor;
            float _WorldSunShadowBlur;
            CBUFFER_START(UnityPerMaterial)
                float4 _SunShadowCaster;
                half4 _Color;
                half4 _RendererColor;
                float _SunShadowBatched;
                float _EnableExternalAlpha;
                float4 _SunShadowTexelSize;
                float4 _SunShadowUvBounds;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 caster : TEXCOORD1;
                float4 uvBounds : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half alpha : COLOR;
                float2 worldXY : TEXCOORD1;
                float4 uvBounds : TEXCOORD2;
                half distanceFromFoot : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 单实体由 MPB 提供脚底，ECS 批次把同一契约写入顶点；不为每只生物创建材质。
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                half alpha = input.color.a * _Color.a * _RendererColor.a;
                #ifdef UNITY_INSTANCING_ENABLED
                if (_SunShadowBatched < 0.5)
                {
                    input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteFlip);
                    alpha *= unity_SpriteColor.a;
                }
                #endif
                float4 caster = _SunShadowBatched > 0.5 ? input.caster : _SunShadowCaster;
                float3 world = TransformObjectToWorld(input.positionOS);
                float h = max(0, world.y - caster.x);
                float2 displacement = _WorldSunShadow.xy * max(0, caster.y);
                displacement *= min(1, _WorldSunShadow.z / max(0.0001, length(displacement) * caster.z));
                world.xy = float2(world.x, caster.x) + displacement * h;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.worldXY = world.xy;
                output.uvBounds = _SunShadowBatched > 0.5 ? input.uvBounds : _SunShadowUvBounds;
                output.distanceFromFoot = saturate(h / max(0.0001, caster.z));
                output.alpha = alpha * caster.w * _WorldSunShadow.w * _WorldSunShadowColor.a;
                return output;
            }

            // 图集内采样，防止邻近帧的颜色渗入当前阴影。
            half SampleShadowAlpha(float2 uv, float4 bounds)
            {
                if (any(uv < bounds.xy) || any(uv > bounds.zw)) return 0;
                float2 inset = _SunShadowTexelSize.xy * 0.5;
                uv = clamp(uv, bounds.xy + inset, bounds.zw - inset);
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
                #if ETC1_EXTERNAL_ALPHA
                alpha = lerp(alpha, SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, uv).r, _EnableExternalAlpha);
                #endif
                return alpha;
            }

            // 强度同时扩大轮廓采样半径和边缘渐隐，最大值会消解树叶细节成为柔和色块。
            half4 Frag(Varyings input) : SV_Target
            {
                half distanceFade = lerp(1.0, 0.78, input.distanceFromFoot);
                half center = SampleShadowAlpha(input.uv, input.uvBounds);
                if (_WorldSunShadowBlur <= 0.001)
                    return half4(_WorldSunShadowColor.rgb, center * distanceFade * input.alpha);

                float2 nearStep = _SunShadowTexelSize.xy * (_WorldSunShadowBlur * 5.0);
                float2 farStep = _SunShadowTexelSize.xy * (_WorldSunShadowBlur * 7.0);
                half coverage = center * 0.2;
                coverage += SampleShadowAlpha(input.uv + float2(nearStep.x, 0), input.uvBounds) * 0.12;
                coverage += SampleShadowAlpha(input.uv - float2(nearStep.x, 0), input.uvBounds) * 0.12;
                coverage += SampleShadowAlpha(input.uv + float2(0, nearStep.y), input.uvBounds) * 0.12;
                coverage += SampleShadowAlpha(input.uv - float2(0, nearStep.y), input.uvBounds) * 0.12;
                coverage += SampleShadowAlpha(input.uv + farStep, input.uvBounds) * 0.08;
                coverage += SampleShadowAlpha(input.uv - farStep, input.uvBounds) * 0.08;
                coverage += SampleShadowAlpha(input.uv + float2(farStep.x, -farStep.y), input.uvBounds) * 0.08;
                coverage += SampleShadowAlpha(input.uv + float2(-farStep.x, farStep.y), input.uvBounds) * 0.08;

                float2 edgePixels = min(input.uv - input.uvBounds.xy,
                    input.uvBounds.zw - input.uv) / _SunShadowTexelSize.xy;
                half edgeFade = saturate(min(edgePixels.x, edgePixels.y) /
                    max(0.5, _WorldSunShadowBlur * 4.0));
                float2 cell = floor(input.worldXY * 8.0);
                half grain = frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
                half grainAmount = lerp(0.14, 0.015, _WorldSunShadowBlur);
                half softCoverage = saturate((coverage - 0.025 + (grain - 0.5) * grainAmount) * 1.08);
                half interior = lerp(lerp(0.82, 1.0, grain), 1.0, _WorldSunShadowBlur);
                return half4(_WorldSunShadowColor.rgb,
                    softCoverage * edgeFade * interior * distanceFade * input.alpha);
            }
            ENDHLSL
        }
    }
}
