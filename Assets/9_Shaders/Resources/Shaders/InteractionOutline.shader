Shader "Game/2D/Interaction-Outline"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1, 1, 1, 1)
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        [HideInInspector] PixelSnap("Pixel snap", Float) = 0
        [HideInInspector] _Flip("Flip", Vector) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
        [HideInInspector] _BodyClip("Body Clip", Float) = 0
        [HideInInspector] _BodyMinV("Body Min V", Float) = 0
        [HideInInspector] _BodyMaxV("Body Max V", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
            // 与主体保持同一本地坐标裁剪约定，禁止合批预变换代理 Sprite 的顶点。
            "DisableBatching" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct OutlineAttributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float localY : TEXCOORD1;
                half2 lightingUV : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _OutlineColor;
                float _BodyClip;
                float _BodyMinV;
                float _BodyMaxV;
            CBUFFER_END

            float4 _RendererColor;

            OutlineVaryings OutlineVertex(OutlineAttributes input)
            {
                OutlineVaryings output = (OutlineVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                #ifdef UNITY_INSTANCING_ENABLED
                    input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteFlip);
                #endif
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.localY = input.positionOS.y;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                output.color = input.color * _Color * _RendererColor * _OutlineColor;
                #ifdef UNITY_INSTANCING_ENABLED
                    output.color *= unity_SpriteColor;
                #endif
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 OutlineFragment(OutlineVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float bodyRange = max(1e-5, _BodyMaxV - _BodyMinV);
                float bodyV = saturate((input.localY - _BodyMinV) / bodyRange);
                clip(bodyV - _BodyClip);

                // 交互描边也是世界 Sprite；仅声明 Universal2D Pass 不会自动接入 Light2D。
                // 用与 Sprite-Lit-Default 相同的 Shape Light 合成，使描边随火把/昼夜光一起明暗变化。
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(input.color.rgb, input.color.a * texel.a, half4(1, 1, 1, 1), surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
