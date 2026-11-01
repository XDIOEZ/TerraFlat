Shader "FlatWorld/2D/Actor Water Reflection"
{
    Properties
    {
        [PerRendererData] _MainTex("当前生物帧", 2D) = "white" {}
        _ReflectionData("水线Y / 倒影长度 / 强度", Vector) = (0, 1, 0, 0)
        _UseVertexReflection("批量顶点水线", Float) = 0
        _AiecsWaterUseSource("使用投影前水线", Float) = 0
        [HideInInspector] _Color("Sprite Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Tint", Color) = (1,1,1,1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        float4 _RendererColor;
        float4 _ReflectionData;
        float _UseVertexReflection;
        float _AiecsWaterUseSource;
    CBUFFER_END
    float4x4 _AiecsWaterWorldToSource;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        float4 reflection : TEXCOORD1;
        half4 color : COLOR;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 sourcePosition : TEXCOORD1;
        float4 reflection : TEXCOORD2;
        half4 color : COLOR;
    };

    Varyings ReflectionVertex(Attributes input)
    {
        Varyings output = (Varyings)0;
        float3 positionWS = TransformObjectToWorld(input.positionOS);
        float2 sourcePosition = _AiecsWaterUseSource > 0.5
            ? mul(_AiecsWaterWorldToSource, float4(positionWS, 1)).xy : positionWS.xy;
        float4 data = _UseVertexReflection > 0.5 ? input.reflection : _ReflectionData;
        float distanceToSurface = max(0.0, data.x - sourcePosition.y);
        // 只移动显示顶点，水线与格子数据始终取原世界坐标。
        positionWS.x += (sin(sourcePosition.y * 15.0 + _Time.y * 2.4)
            + sin(sourcePosition.y * 22.0 - _Time.y * 1.6) * 0.5)
            * 0.009 * saturate(distanceToSurface / max(0.01, data.y));
        output.positionCS = TransformWorldToHClip(positionWS);
        output.sourcePosition = sourcePosition;
        output.reflection = data;
        output.uv = input.uv;
        output.color = input.color;
        return output;
    }

    half4 ReflectionFragment(Varyings input) : SV_Target
    {
        float distanceToSurface = input.reflection.x - input.sourcePosition.y;
        clip(distanceToSurface - 0.002);
        float depth = saturate(distanceToSurface / max(0.01, input.reflection.y));
        clip(0.985 - depth);
        half4 body = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) *
            input.color * _Color * _RendererColor;
        float shimmer = 0.84 + 0.16 * sin(input.sourcePosition.y * 34.0
            + input.sourcePosition.x * 5.0 - _Time.y * 3.1);
        body.rgb = lerp(body.rgb, half3(0.14, 0.36, 0.45), 0.38);
        body.a *= saturate(input.reflection.z) * 0.38 * shimmer
            * pow(1.0 - depth, 1.55);
        return body;
    }
    ENDHLSL

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Stencil { Ref 64 ReadMask 64 Comp Equal Pass Keep }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex ReflectionVertex
            #pragma fragment ReflectionFragment
            ENDHLSL
        }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex ReflectionVertex
            #pragma fragment ReflectionFragment
            ENDHLSL
        }
    }
}
