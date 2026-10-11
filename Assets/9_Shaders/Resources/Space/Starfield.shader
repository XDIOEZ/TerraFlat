Shader "FlatWorld/Space/Starfield"
{
    Properties
    {
        _TwinkleStrength("Twinkle Strength", Range(0,1)) = 0.4
        _Brightness("Brightness", Range(0,2)) = 1.15
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _ParallaxOffset;
            float _TwinkleStrength;
            float _Brightness;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float2 center : TEXCOORD1;
            float4 animation : TEXCOORD2;
            half4 color : COLOR;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };
        Varyings StarVertex(Attributes input)
        {
            Varyings output;
            // 三层星点缓慢反向移动，环绕和独立闪烁都在 GPU 上完成。
            float2 center = frac(input.center + 0.5 - _ParallaxOffset.xy * input.animation.x) - 0.5;
            float3 position = input.positionOS + float3(center, 0.0);
            output.positionCS = TransformObjectToHClip(position);
            output.uv = input.uv * 2.0 - 1.0;
            float wave = sin(_Time.y * input.animation.z + input.animation.y);
            wave = wave * 0.7 + sin(_Time.y * input.animation.z * 1.73 + input.animation.y * 2.0) * 0.3;
            output.color = input.color;
            output.color.rgb *= _Brightness * (1.0 + wave * _TwinkleStrength * input.animation.w);
            return output;
        }
        half4 StarFragment(Varyings input) : SV_Target
        {
            float radiusSquared = dot(input.uv, input.uv);
            clip(1.0 - radiusSquared);
            float softness = max(fwidth(radiusSquared), 0.02);
            float core = 1.0 - smoothstep(0.015, 0.13 + softness, radiusSquared);
            float halo = exp2(-radiusSquared * 7.0) * (1.0 - smoothstep(0.6, 1.0, radiusSquared));
            float alpha = saturate(core * 0.75 + halo * 0.45);
            return half4(input.color.rgb, alpha * input.color.a);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex StarVertex
            #pragma fragment StarFragment
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex StarVertex
            #pragma fragment StarFragment
            ENDHLSL
        }
    }
}
