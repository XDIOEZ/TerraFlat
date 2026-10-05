Shader "Hidden/FlatWorld/DenseFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }

        Pass
        {
            Name "DenseFog"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _FogViewportOrigin;
            float4 _FogViewportRight;
            float4 _FogViewportUp;
            float4 _FogWorldCenter;
            float4 _FogRadiiStrength;
            float4 _FogColor;
            float4 _FogNoise;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 playerOffset : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                // 与 CPU 的 GPU 裁剪坐标使用同一基底，不依赖贴图 UV 的平台翻转。
                float2 viewport = output.positionCS.xy * 0.5 + 0.5;
                output.playerOffset = _FogViewportOrigin.xy +
                    viewport.x * _FogViewportRight.xy + viewport.y * _FogViewportUp.xy;
                return output;
            }

            float Hash(float2 point)
            {
                float3 value = frac(float3(point.xyx) * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float CloudNoise(float2 point)
            {
                float2 cell = floor(point);
                float2 local = frac(point);
                local = local * local * (3.0 - 2.0 * local);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1.0, 0.0)), local.x),
                    lerp(Hash(cell + float2(0.0, 1.0)), Hash(cell + 1.0), local.x), local.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float distanceFromPlayer = length(input.playerOffset);
                float radial = saturate((distanceFromPlayer - _FogRadiiStrength.x) /
                    max(0.001, _FogRadiiStrength.y - _FogRadiiStrength.x));
                if (radial <= 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float2 worldPoint = input.playerOffset + _FogWorldCenter.xy;
                float clouds = CloudNoise(worldPoint * _FogNoise.x +
                    _Time.y * _FogNoise.y * float2(1.0, 0.37));
                radial += (clouds - 0.5) * 0.35 * radial * (1.0 - radial);
                float alpha = smoothstep(0.0, 1.0, radial) * _FogRadiiStrength.z;
                // 远处仅改变白雾颜色，不打透明孔，人物、名字与光源不会从雾里漏出来。
                half3 fogColor = _FogColor.rgb * (1.0 - clouds * _FogNoise.z);
                return half4(fogColor, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
