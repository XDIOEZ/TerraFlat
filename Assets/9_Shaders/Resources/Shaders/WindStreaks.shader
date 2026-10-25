Shader "Hidden/FlatWorld/WindStreaks"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }

        Pass
        {
            Name "WindStreaks"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _WindViewportOrigin;
            float4 _WindViewportRight;
            float4 _WindViewportUp;
            float4 _WindStreakDirection;
            float4 _WindStreakPattern;
            float4 _WindStreakShape;
            half4 _WindStreakColor;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldOffset : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 viewport = output.positionCS.xy * 0.5 + 0.5;
                output.worldOffset = _WindViewportOrigin.xy +
                    viewport.x * _WindViewportRight.xy + viewport.y * _WindViewportUp.xy;
                return output;
            }

            float3 HashCell(float2 cell)
            {
                // 与 CPU 的 128 格相位回绕保持同一周期，边缘衔接不会换一批风纹。
                cell -= floor(cell / 128.0) * 128.0;
                float3 value = frac(float3(cell.xyx) * float3(0.1031, 0.1030, 0.0973));
                value += dot(value, value.yxz + 33.33);
                return frac((value.xxy + value.yzz) * value.zyx);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 direction = _WindStreakDirection.xy;
                float2 across = float2(-direction.y, direction.x);
                float2 pattern = float2(dot(input.worldOffset, direction), dot(input.worldOffset, across)) /
                    _WindStreakPattern.zw + _WindStreakPattern.xy;
                float3 random = HashCell(floor(pattern));
                float2 local = frac(pattern);
                float2 center = float2(lerp(0.28, 0.72, random.x), lerp(0.30, 0.70, random.y));
                float streakLength = _WindStreakShape.z * lerp(0.55, 1.0, random.y);
                float along = (local.x - center.x) * _WindStreakPattern.z;
                float progress = along / streakLength + 0.5;
                float curve = sin(saturate(progress) * PI) * _WindStreakPattern.w *
                    lerp(-0.07, 0.07, random.x);
                float lateral = (local.y - center.y) * _WindStreakPattern.w - curve;
                float halfWidth = _WindStreakShape.w * lerp(0.35, 0.58, random.z);
                float antialias = max(fwidth(dot(input.worldOffset, across)) * 0.5, 0.001);
                float core = 1.0 - smoothstep(halfWidth, halfWidth + antialias, abs(lateral));
                float shadow = 1.0 - smoothstep(halfWidth * 1.5, halfWidth * 1.5 + antialias,
                    abs(lateral + halfWidth));
                float taper = smoothstep(0.0, 0.22, progress) * (1.0 - smoothstep(0.86, 1.0, progress));
                float visible = saturate((_WindStreakShape.y - random.z) * 10.0);
                float alpha = max(core, shadow * 0.38) * taper * visible * _WindStreakShape.x;
                // 淡暗边让雪地也能看见风，亮色细线保留轻盈的空气感。
                half3 color = lerp(half3(0.18, 0.27, 0.34), _WindStreakColor.rgb, core);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
