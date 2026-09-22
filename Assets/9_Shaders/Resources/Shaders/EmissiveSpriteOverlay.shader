Shader "Game/2D/Emissive-Sprite-Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1, 1, 1, 1)
        _ClipLocalY("Clip Local Y", Float) = 0.38
        [HideInInspector] PixelSnap("Pixel snap", Float) = 0
        [HideInInspector] _Flip("Flip", Vector) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
            // 裁剪依据 Sprite 本地坐标，不能让批处理预变换顶点。
            "DisableBatching" = "True"
        }

        // 只给被 2D 阴影压暗的像素提供自发光下限；原 Lit Sprite 如果被其他光照得更亮，继续保留更亮结果。
        Blend One One
        BlendOp Max
        Cull Off
        ZWrite Off

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

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
                float localY : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _ClipLocalY;
            CBUFFER_END

            float4 _RendererColor;

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                #ifdef UNITY_INSTANCING_ENABLED
                    input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteFlip);
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.localY = input.positionOS.y;
                output.color = input.color * _Color * _RendererColor;
                #ifdef UNITY_INSTANCING_ENABLED
                    output.color *= unity_SpriteColor;
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(input.localY - _ClipLocalY);
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                // Max 混合不应让透明像素携带的 RGB 污染背景，因此先预乘 Alpha。
                sprite.rgb *= sprite.a;
                return sprite;
            }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
