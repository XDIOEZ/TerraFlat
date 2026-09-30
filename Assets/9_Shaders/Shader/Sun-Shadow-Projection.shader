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
        [HideInInspector] _SunShadowBatched("Geometry (0 Sprite / 1 Batch / 2 Mesh Proxy)", Float) = 0
        [HideInInspector] _SunShadowTexelSize("Source Texel Size / Blur Scale", Vector) = (0.015625,0.015625,1,0)
        [HideInInspector] _SunShadowUvBounds("Source UV Bounds", Vector) = (0,0,1,1)
    }
    SubShader
    {
        // Shadow/0 队列 2990：地表排序层由 Renderer 决定，队列只确定太阳投影早于接触阴影。
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "SunShadow"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
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
            CBUFFER_START(UnityPerMaterial)
                float4 _SunShadowCaster;
                half4 _Color;
                half4 _RendererColor;
                float _SunShadowBatched;
                float _EnableExternalAlpha;
                float4 _SunShadowTexelSize;
                float4 _SunShadowUvBounds;
            CBUFFER_END
            #define FLATWORLD_SUN_SHADOW_EXTERNAL_ALPHA
            #include "SunShadowSampling.hlsl"

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
                float4 uvBounds : TEXCOORD2;
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
                bool batched = _SunShadowBatched > 0.5 && _SunShadowBatched < 1.5;
                float4 caster = batched ? input.caster : _SunShadowCaster;
                float3 world = TransformObjectToWorld(input.positionOS);
                // 脚点以下保留有符号高度，让根部投影自然伸进本体下方。
                float h = world.y - caster.x;
                float2 displacement = _WorldSunShadow.xy * max(0, caster.y);
                displacement *= min(1, _WorldSunShadow.z / max(0.0001, length(displacement) * caster.z));
                world.xy = float2(world.x, caster.x) + displacement * h;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.uvBounds = batched ? input.uvBounds : _SunShadowUvBounds;
                output.alpha = alpha * caster.w * _WorldSunShadow.w * _WorldSunShadowColor.a;
                return output;
            }

            // 普通物品与批量资源读取同一柔化核和本机设置。
            half4 Frag(Varyings input) : SV_Target
            {
                float texelScale = _SunShadowTexelSize.z > 0.001 ? _SunShadowTexelSize.z : 1.0;
                half coverage = SampleSunShadowCoverage(input.uv, _SunShadowTexelSize.xy,
                    input.uvBounds, 0, texelScale);
                return half4(_WorldSunShadowColor.rgb, coverage * input.alpha);
            }
            ENDHLSL
        }
    }
}
