Shader "FlatWorld/Particles/Rain Ground Splash"
{
    Properties
    {
        _Color("Tint", Color) = (1, 1, 1, 1)
        _RingRadius("Ring Radius", Range(0.1, 1)) = 0.72
        _RingWidth("Ring Width", Range(0.01, 0.5)) = 0.12
        _EdgeSoftness("Edge Softness", Range(0.001, 0.2)) = 0.04
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _RingRadius;
            float _RingWidth;
            float _EdgeSoftness;
        CBUFFER_END

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
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings SplashVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.color = input.color;
            output.uv = input.uv;
            return output;
        }

        half4 SplashFragment(Varyings input) : SV_Target
        {
            float radialDistance = length(input.uv * 2.0 - 1.0);
            float innerRadius = max(0.0, _RingRadius - _RingWidth);
            float inner = smoothstep(innerRadius - _EdgeSoftness, innerRadius, radialDistance);
            float outer = 1.0 - smoothstep(_RingRadius, _RingRadius + _EdgeSoftness, radialDistance);
            half4 color = input.color * _Color;
            color.a *= inner * outer;
            return color;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex SplashVertex
            #pragma fragment SplashFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex SplashVertex
            #pragma fragment SplashFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
