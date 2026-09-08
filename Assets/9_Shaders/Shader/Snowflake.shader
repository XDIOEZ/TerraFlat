Shader "FlatWorld/Snowflake"
{
    Properties { _MainTex("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            // 粒子仅使用自身位置与顶点色。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            // 程序化小雪点，无额外位图依赖。
            half4 Frag(Varyings input):SV_Target
            {
                float2 p = abs(input.uv - 0.5);
                float mask = 1 - smoothstep(0.28, 0.48, length(p));
                return half4(input.color.rgb, input.color.a * mask);
            }
            ENDHLSL
        }
    }
}
