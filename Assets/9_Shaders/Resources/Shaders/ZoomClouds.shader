Shader "Hidden/FlatWorld/ZoomClouds"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }

        Pass
        {
            Name "ZoomClouds"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudViewportOrigin;
                float4 _CloudViewportRight;
                float4 _CloudViewportUp;
                float4 _CloudWorldCenter;
                float4 _CloudDriftOffset;
                float4 _CloudRadiiStrength;
                float4 _CloudColor;
                float4 _CloudShape;
            CBUFFER_END

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
                float2 viewport = output.positionCS.xy * 0.5 + 0.5;
                output.playerOffset = _CloudViewportOrigin.xy +
                    viewport.x * _CloudViewportRight.xy + viewport.y * _CloudViewportUp.xy;
                return output;
            }

            float CloudHash(float2 noisePosition)
            {
                float3 value = frac(float3(noisePosition.xyx) * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float CloudNoise(float2 noisePosition)
            {
                float2 cell = floor(noisePosition);
                float2 local = frac(noisePosition);
                local = local * local * (3.0 - 2.0 * local);
                return lerp(lerp(CloudHash(cell), CloudHash(cell + float2(1.0, 0.0)), local.x),
                    lerp(CloudHash(cell + float2(0.0, 1.0)), CloudHash(cell + 1.0), local.x), local.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float radial = smoothstep(_CloudRadiiStrength.x, _CloudRadiiStrength.y, length(input.playerOffset));
                if (radial <= 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float2 worldPosition = input.playerOffset + _CloudWorldCenter.xy;
                float2 coarsePosition = (worldPosition - _CloudDriftOffset.xy) * _CloudShape.x * float2(0.42, 1.25);
                // 错行的扁长云团始终缩在各自区域内，云与云之间保留真正透明的空隙。
                coarsePosition.x += frac(floor(coarsePosition.y) * 0.5);
                float2 cell = floor(coarsePosition);
                float seed = CloudHash(cell + float2(2.3, 13.7));
                if (seed < 0.2)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float2 center = float2(
                    0.5 + (CloudHash(cell + float2(1.7, 7.3)) - 0.5) * 0.1,
                    0.5 + (CloudHash(cell + float2(5.2, 2.8)) - 0.5) * 0.3);
                float2 cloudSize = float2(
                    0.32 + CloudHash(cell + float2(8.1, 4.7)) * 0.09,
                    0.17 + seed * 0.07);
                float2 cloudLocal = (frac(coarsePosition) - center) / cloudSize;
                float shapeDistance = dot(cloudLocal, cloudLocal);
                if (shapeDistance >= 1.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float2 detailPosition = (worldPosition - _CloudDriftOffset.xy * 0.63) *
                    _CloudShape.x * float2(2.17, 4.09) + float2(7.1, 19.4);
                // 两层随风噪声只修饰云边和阴影，不会随着拉远把透明间隙填成白雾。
                float coarse = CloudNoise(coarsePosition * float2(4.1, 2.7) + 11.7);
                float detail = CloudNoise(detailPosition);
                float edgeNoise = coarse * 0.75 + detail * 0.25;
                float cloudShape = 1.0 - smoothstep(0.48 + edgeNoise * 0.14,
                    0.72 + edgeNoise * 0.28, shapeDistance);
                float strength = saturate(_CloudRadiiStrength.z);
                float alpha = strength * radial * cloudShape;
                half3 cloudColor = _CloudColor.rgb * (1.0 - (1.0 - coarse * 0.6 - detail * 0.4) * _CloudShape.y);
                return half4(cloudColor, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
