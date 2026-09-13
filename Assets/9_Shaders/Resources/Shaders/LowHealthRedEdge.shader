Shader "Hidden/FlatWorld/LowHealthRedEdge"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "LowHealthRedEdge"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _EdgeColor;
            float _EdgeStrength;
            float _EdgeSmoothness;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centered = abs(input.uv * 2.0 - 1.0);
                float edgeDistance = max(centered.x, centered.y);

                // smoothness 越高，红边向屏幕中心延伸得越柔和。
                float innerEdge = lerp(0.80, 0.48, saturate(_EdgeSmoothness));
                float edgeMask = smoothstep(innerEdge, 1.0, edgeDistance);
                float alpha = saturate(edgeMask * _EdgeStrength * 0.9);

                return half4(_EdgeColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
