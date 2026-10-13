Shader "Hidden/FlatWorld/ZoomClouds"
{
    Properties
    {
        [HideInInspector] _CloudDensityTex("Cloud Density Data", 2D) = "black" {}
    }
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

            TEXTURE2D(_CloudDensityTex);
            SAMPLER(sampler_CloudDensityTex);

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

            float CloudDensity(float4 cloudData, float detail)
            {
                // 噪声场决定整片云的厚度，细节侵蚀薄边，空域始终保持零密度。
                float body = saturate((cloudData.r - 0.49) * 4.2);
                float erosion = max(0.0, detail - 0.38) * 0.17 * (1.0 - body);
                return max(0.0, body - erosion);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float radial = smoothstep(_CloudRadiiStrength.x, _CloudRadiiStrength.y, length(input.playerOffset));
                float2 worldPosition = input.playerOffset + _CloudWorldCenter.xy;
                float2 densityUV = (worldPosition - _CloudDriftOffset.xy) * _CloudShape.x * float2(0.07, 0.085);
                float2 detailUV = (worldPosition - _CloudDriftOffset.xy * 0.73) *
                    _CloudShape.x * float2(0.157, 0.183) + float2(0.173, 0.619);
                float2 gradientX = ddx(densityUV), gradientY = ddy(densityUV);
                float2 detailGradientX = ddx(detailUV), detailGradientY = ddy(detailUV);
                if (radial <= 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float4 cloudData = SAMPLE_TEXTURE2D_GRAD(_CloudDensityTex, sampler_CloudDensityTex,
                    densityUV, gradientX, gradientY);
                float detail = SAMPLE_TEXTURE2D_GRAD(_CloudDensityTex, sampler_CloudDensityTex,
                    detailUV, detailGradientX, detailGradientY).b;
                float density = CloudDensity(cloudData, detail);
                if (density <= 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // 沿光线读取邻近厚度形成自遮挡，云顶亮、厚处灰，薄边自然透光。
                float2 lightOffset = float2(-0.006, 0.009);
                float4 nearCloud = SAMPLE_TEXTURE2D_GRAD(_CloudDensityTex, sampler_CloudDensityTex,
                    densityUV + lightOffset, gradientX, gradientY);
                float4 farCloud = SAMPLE_TEXTURE2D_GRAD(_CloudDensityTex, sampler_CloudDensityTex,
                    densityUV + lightOffset * 2.7, gradientX, gradientY);
                float nearDensity = CloudDensity(nearCloud, detail);
                float farDensity = CloudDensity(farCloud, detail);
                float opticalDepth = density * lerp(1.5, 3.5, cloudData.g);
                float lightDepth = max(0.0, nearDensity - density * 0.55) * 2.2 +
                    farDensity * 0.65 + density * 0.25;
                float illumination = 0.28 + 0.72 * exp2(-lightDepth * _CloudShape.y * 4.5);
                float relief = saturate(0.72 + (cloudData.g - nearCloud.g) * 1.4);
                half3 shadowColor = _CloudColor.rgb * half3(0.55, 0.64, 0.75);
                half3 cloudColor = lerp(shadowColor, _CloudColor.rgb, illumination) *
                    (0.91 + relief * 0.09);
                cloudColor += pow(1.0 - density, 2.0) * smoothstep(0.0, 0.12, density) * 0.06;
                float strength = saturate(_CloudRadiiStrength.z);
                float alpha = strength * radial * _CloudColor.a * (1.0 - exp2(-opticalDepth * 2.0));
                return half4(cloudColor, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
