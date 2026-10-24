Shader "FlatWorld/RainStreak"
{
    Properties
    {
        _RainTint("雨丝颜色", Color) = (0.7, 0.83, 0.96, 0.62)
        _RainLength("雨丝长度范围", Vector) = (0.22, 0.56, 0, 0)
        _RainWidth("雨丝宽度范围", Vector) = (0.015, 0.035, 0, 0)
        _RainFallSpeed("下落速度范围", Vector) = (15, 26, 0, 0)
        [PerRendererData] _RainField("覆盖宽高、密度与样本数", Vector) = (20, 12, 0, 2048)
        [PerRendererData] _RainDrift("整层风与镜头位移", Vector) = (0, 0, 0, 0)
        [PerRendererData] _RainWind("镜头平面风速", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RainTint;
            float4 _RainLength;
            float4 _RainWidth;
            float4 _RainFallSpeed;
            float4 _RainField;
            float4 _RainDrift;
            float4 _RainWind;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 seed : TEXCOORD1;
            float2 threshold : TEXCOORD2;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half opacity : TEXCOORD1;
        };

        Varyings RainVertex(Attributes input)
        {
            Varyings output;
            float visibility = saturate((_RainField.z - input.threshold.x) * _RainField.w);
            float depth = floor(input.seed.z * 3.0) * 0.5;
            float size = lerp(0.65, 1.2, depth);
            float speed = lerp(_RainFallSpeed.x, _RainFallSpeed.y, depth * 0.7 + input.seed.w * 0.3);
            float streakLength = lerp(_RainLength.x, _RainLength.y, input.seed.w) * size;
            float streakWidth = lerp(_RainWidth.x, _RainWidth.y, depth) * size;
            float2 velocity = _RainWind.xy + float2(0.0, -max(0.01, speed));
            float2 direction = velocity * rsqrt(max(dot(velocity, velocity), 0.0001));
            float2 across = float2(-direction.y, direction.x);
            float2 fieldSize = max(_RainField.xy, float2(0.01, 0.01));
            // 随机相位与三层尺寸消除整齐雨帘，环绕位置留在视口之外。
            float2 displacement = _RainDrift.xy + float2(0.0, -_Time.y * speed);
            float2 center = (frac(input.seed.xy + displacement / fieldSize) - 0.5) * fieldSize;
            float taper = lerp(0.3, 1.0, input.uv.y);
            float2 corner = across * input.positionOS.x * streakWidth * taper +
                direction * input.positionOS.y * streakLength;
            float3 positionOS = float3(center + corner * step(0.00001, visibility), 0.0);
            output.positionCS = TransformObjectToHClip(positionOS);
            output.uv = input.uv;
            output.opacity = visibility * lerp(0.28, 0.9, depth) * _RainTint.a;
            return output;
        }

        half4 RainFragment(Varyings input) : SV_Target
        {
            float side = abs(input.uv.x * 2.0 - 1.0);
            float edge = max(fwidth(side), 0.08);
            half mask = 1.0 - smoothstep(max(0.0, 1.0 - edge), 1.0, side);
            float tail = smoothstep(0.0, 0.24, input.uv.y);
            float head = 1.0 - smoothstep(0.86, 1.0, input.uv.y);
            half opacity = input.opacity * mask * tail * head * lerp(0.2, 1.0, input.uv.y);
            return half4(_RainTint.rgb, opacity);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RainVertex
            #pragma fragment RainFragment
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RainVertex
            #pragma fragment RainFragment
            ENDHLSL
        }
    }
}
