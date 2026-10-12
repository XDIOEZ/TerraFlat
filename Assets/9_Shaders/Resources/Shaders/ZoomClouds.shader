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
                float2 coarsePosition = (worldPosition - _CloudDriftOffset.xy) * _CloudShape.x;
                float2 detailPosition = (worldPosition - _CloudDriftOffset.xy * 0.63) * _CloudShape.x * 2.17 + float2(7.1, 19.4);
                // 两层不同尺度与速度的噪声都沿当地风向移动，云团和细雾形成流动层次。
                float coarse = CloudNoise(coarsePosition) * 0.57 +
                    CloudNoise(coarsePosition * 2.03 + 11.7) * 0.28 +
                    CloudNoise(coarsePosition * 4.09 + 23.1) * 0.15;
                float detail = CloudNoise(detailPosition) * 0.65 + CloudNoise(detailPosition * 2.07 + 3.9) * 0.35;
                float cloudShape = smoothstep(0.3, 0.7, coarse * 0.78 + detail * 0.22);
                float strength = saturate(_CloudRadiiStrength.z);
                // 高视野最浓时远处不留透明孔，继续拉远也只能看清玩家附近。
                float density = lerp(cloudShape, 1.0, strength * strength * radial);
                float alpha = strength * radial * density;
                half3 cloudColor = _CloudColor.rgb * (1.0 - (1.0 - cloudShape) * _CloudShape.y);
                return half4(cloudColor, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
