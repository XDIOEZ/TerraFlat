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
            #include "Assets/9_Shaders/Shader/UniverseStarfieldCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudViewportOrigin;
                float4 _CloudViewportRight;
                float4 _CloudViewportUp;
                float4 _CloudWorldCenter;
                float4 _CloudDriftOffset;
                float4 _CloudWindow;
                float4 _CloudFeather;
                float4 _CloudColor;
                float4 _CloudShape;
                float4 _CloudScreenSize;
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
                float2 distanceFromWindow = abs(input.playerOffset - _CloudWindow.xy) - _CloudWindow.zw;
                float2 edgeProgress = distanceFromWindow / max(_CloudFeather.xy, 0.0001);
                float edgeFade = smoothstep(0.0, 1.0, max(edgeProgress.x, edgeProgress.y));
                [branch]
                if (_CloudShape.z > 0.5)
                {
                    // 真空星球用同一个柔边窗口遮住未加载区，不绘制白色云纹。
                    half3 stars = SampleUniverseStarfield(input.positionCS.xy - _CloudScreenSize.xy * 0.5);
                    return half4(stars, edgeFade);
                }
                float2 worldPosition = input.playerOffset + _CloudWorldCenter.xy;
                float3 cloudCoords = float3((worldPosition - _CloudDriftOffset.xy) * _CloudShape.x, 0.71) + 137.0;
                float clouds = PlanetFractal(cloudCoords);
                float incoming = saturate(edgeFade + (clouds - 0.5) * 0.3 * edgeFade * (1.0 - edgeFade));
                // 云纹只扰动方形窗口的柔边带，已加载清晰区和不透明外边界只随加载范围变化。
                float veilAlpha = edgeFade;
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
