Shader "FlatWorld/Snowflake"
{
    Properties
    {
        _SnowTint("雪花颜色", Color) = (0.88, 0.94, 1, 0.9)
        _SnowSize("雪花直径范围", Vector) = (0.05, 0.14, 0, 0)
        _SnowFallSpeed("下落速度范围", Vector) = (1.6, 3.1, 0, 0)
        _SnowSway("左右摆动幅度", Float) = 0.3
        [PerRendererData] _SnowField("覆盖宽高与密度", Vector) = (20, 12, 0, 0)
        [PerRendererData] _SnowDrift("整层风与镜头位移", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SnowTint;
                float4 _SnowSize;
                float4 _SnowFallSpeed;
                float _SnowSway;
                float4 _SnowField;
                float4 _SnowDrift;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 seed : TEXCOORD1;
                float2 motion : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half opacity : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float visibility = saturate((_SnowField.z - input.motion.y) * 100.0);
                float size = lerp(_SnowSize.x, _SnowSize.y, input.seed.z) * visibility;
                float speed = lerp(_SnowFallSpeed.x, _SnowFallSpeed.y, input.seed.w);
                float sway = sin(_Time.y * lerp(0.7, 1.3, input.seed.w) + input.motion.x) * _SnowSway;
                float2 fieldSize = max(_SnowField.xy, float2(0.01, 0.01));
                // 雪花只在视口外环绕，静止无风时也完全由 GPU 推进动画。
                float2 displacement = _SnowDrift.xy + float2(sway, -_Time.y * speed);
                float2 center = (frac(input.seed.xy + displacement / fieldSize) - 0.5) * fieldSize;
                float3 positionOS = float3(center + input.positionOS.xy * size, 0);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                output.opacity = visibility * lerp(0.6, 1.0, input.seed.z) * _SnowTint.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 offset = input.uv * 2.0 - 1.0;
                float radiusSquared = dot(offset, offset);
                float edge = max(fwidth(radiusSquared), 0.15);
                half mask = 1.0 - smoothstep(max(0.0, 1.0 - edge), 1.0, radiusSquared);
                return half4(_SnowTint.rgb, input.opacity * mask);
            }
            ENDHLSL
        }
    }
}
