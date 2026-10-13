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
            #include "Assets/9_Shaders/Shader/PlanetCloudCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudViewportOrigin;
                float4 _CloudViewportRight;
                float4 _CloudViewportUp;
                float4 _CloudWorldCenter;
                float4 _CloudDriftOffset;
                float4 _CloudRadiiStrength;
                float4 _CloudProtection;
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float distanceFromPlayer = length(input.playerOffset);
                float2 direction = input.playerOffset / max(distanceFromPlayer, 0.001);
                // 拉远时把外侧云形向玩家推进，拉近时同一偏移反向退回，中心保持连续。
                float travel = _CloudShape.z * smoothstep(_CloudProtection.x, _CloudProtection.y, distanceFromPlayer);
                float2 worldPosition = input.playerOffset + direction * travel + _CloudWorldCenter.xy;
                float3 cloudCoords = float3((worldPosition - _CloudDriftOffset.xy) * _CloudShape.x, 0.71) + 137.0;
                float clouds = PlanetFractal(cloudCoords);
                float feather = _CloudRadiiStrength.y - _CloudRadiiStrength.x;
                float edgeOffset = (clouds - 0.5) * feather * 0.3 * _CloudRadiiStrength.w;
                float incoming = smoothstep(_CloudRadiiStrength.x, _CloudRadiiStrength.y,
                    distanceFromPlayer + edgeOffset);
                float protection = smoothstep(_CloudProtection.x, _CloudProtection.y, distanceFromPlayer);
                float strength = saturate(_CloudRadiiStrength.z);
                // 第一版外圈浓云填住空隙，星球共用柔边云团叠在上面，满屏时连中心也完全遮挡。
                float veilAlpha = saturate(max(protection * _CloudProtection.z, incoming * strength));
                float cloudAlpha = PlanetCloudOpacity(clouds) * incoming * strength;
                half3 veilColor = _CloudColor.rgb * (1.0 - (1.0 - clouds) * _CloudShape.y);
                float alpha = cloudAlpha + veilAlpha * (1.0 - cloudAlpha);
                half3 premultiplied = _CloudColor.rgb * cloudAlpha + veilColor * veilAlpha * (1.0 - cloudAlpha);
                return half4(premultiplied / max(alpha, 0.0001), alpha);
            }
            ENDHLSL
        }
    }
}
