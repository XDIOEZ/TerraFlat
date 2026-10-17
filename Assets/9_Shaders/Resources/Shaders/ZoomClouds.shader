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
                float4 _CloudRadii;
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
                float2 worldPosition = input.playerOffset + _CloudWorldCenter.xy;
                float3 cloudCoords = float3((worldPosition - _CloudDriftOffset.xy) * _CloudShape.x, 0.71) + 137.0;
                float clouds = PlanetFractal(cloudCoords);
                float radial = smoothstep(_CloudRadii.x, _CloudRadii.y, distanceFromPlayer);
                float incoming = saturate(radial + (clouds - 0.5) * 0.3 * radial * (1.0 - radial));
                // 云纹只扰动过渡带，固定清晰内圈和不透明外圈不随镜头缩放变化。
                float veilAlpha = radial;
                float cloudAlpha = PlanetCloudOpacity(clouds) * incoming;
                half3 veilColor = _CloudColor.rgb * (1.0 - (1.0 - clouds) * _CloudShape.y);
                float alpha = cloudAlpha + veilAlpha * (1.0 - cloudAlpha);
                half3 premultiplied = _CloudColor.rgb * cloudAlpha + veilColor * veilAlpha * (1.0 - cloudAlpha);
                return half4(premultiplied / max(alpha, 0.0001), alpha);
            }
            ENDHLSL
        }
    }
}
