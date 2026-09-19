Shader "FlatWorld/2D/AIECS Blob Shadow"
{
    Properties
    {
        _Softness("边缘柔和度", Range(0.01, 1)) = 0.65
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "BlobShadow"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Softness;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half alpha : COLOR; float2 uv : TEXCOORD0; };
            // 昼夜、水深和死亡透明度由批量表现入口提供，不采样光照纹理或阴影图。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.alpha = input.color.a;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radiusSquared = dot(input.uv, input.uv);
                half alpha = 1.0h - smoothstep(1.0h - max(0.01h, _Softness), 1.0h, radiusSquared);
                return half4(0, 0, 0, alpha * input.alpha);
            }
            ENDHLSL
        }
    }
}
