Shader "Hidden/FlatWorld/TemperatureScreenEffect"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float _TemperatureSeverity;
        float _TemperatureTime;
        float4 _TemperatureScreenSize;
        float4 _TemperatureSourceTexelSize;

        float EdgeDistance(float2 uv, float aspect)
        {
            float2 nearest = min(uv, 1.0 - uv) * float2(aspect, 1.0);
            return min(nearest.x, nearest.y);
        }

        float FrostHash(float2 cell)
        {
            float3 seed = frac(float3(cell.xyx) * 0.1031);
            seed += dot(seed, seed.yzx + 33.33);
            return frac((seed.x + seed.y) * seed.z);
        }
        ENDHLSL

        Pass
        {
            Name "FrostBlocks"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex FrostVert
            #pragma fragment FrostFrag

            struct FrostAttributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FrostVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            FrostVaryings FrostVert(FrostAttributes input)
            {
                FrostVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 FrostFrag(FrostVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float aspect = _TemperatureScreenSize.x / _TemperatureScreenSize.y;
                float distanceFromEdge = EdgeDistance(input.uv, aspect);
                // 中间完全不绘制，方块和噪声只在最多18%屏幕高度的边缘内计算。
                if (distanceFromEdge >= 0.18)
                    return half4(0, 0, 0, 0);

                float2 grid = float2(aspect, 1.0) * 180.0;
                float2 cell = floor(input.uv * grid);
                float2 cellCenter = (cell + 0.5) / grid;
                float cellDistance = EdgeDistance(cellCenter, aspect);
                float coarse = FrostHash(floor(cell / 3.0));
                float detail = FrostHash(cell);
                float front = 0.18 * _TemperatureSeverity * (0.60 + coarse * 0.28 + detail * 0.12);
                float ice = smoothstep(cellDistance - 0.008, cellDistance + 0.008, front);
                float2 cellLocal = frac(input.uv * grid);
                float bevel = 1.0 - step(0.16, min(cellLocal.x, cellLocal.y));
                float facet = saturate(0.30 + detail * 0.55 + bevel * 0.15);
                half3 iceColor = lerp(half3(0.24, 0.53, 0.80), half3(0.84, 0.97, 1.0), facet);
                float alpha = ice * _TemperatureSeverity * (0.40 + detail * 0.34 + bevel * 0.08);
                alpha *= 1.0 - smoothstep(0.17, 0.18, distanceFromEdge);
                return half4(iceColor, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HeatDistortion"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment HeatFrag
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 HeatFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = (input.texcoord - _BlitScaleBias.zw) / _BlitScaleBias.xy;
                float aspect = _TemperatureScreenSize.x / _TemperatureScreenSize.y;
                float distanceFromEdge = EdgeDistance(screenUV, aspect);
                // 清晰中央只读原图，不计算热浪波形，也不叠加热色。
                if (distanceFromEdge >= 0.16)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                float edgeMask = 1.0 - smoothstep(0.018, 0.16, distanceFromEdge);
                float2 nearest = min(screenUV, 1.0 - screenUV) * float2(aspect, 1.0);
                float sideWeight = 1.0 - smoothstep(-0.025, 0.025, nearest.x - nearest.y);
                float phaseVertical = screenUV.y * 54.0 - _TemperatureTime * 2.8;
                float phaseHorizontal = screenUV.x * aspect * 54.0 - _TemperatureTime * 2.8;
                float curl = sin(distanceFromEdge * 72.0 - _TemperatureTime * 2.0) * 2.2;
                float verticalSnake = sin(phaseVertical + curl);
                float horizontalSnake = sin(phaseHorizontal + curl);
                float2 verticalWarp = float2(cos(phaseVertical * 0.72 + curl), verticalSnake);
                float2 horizontalWarp = float2(horizontalSnake, cos(phaseHorizontal * 0.72 + curl));
                float2 warp = lerp(horizontalWarp, verticalWarp, sideWeight);
                float strength = _TemperatureSeverity * edgeMask;
                float2 offset = warp * float2(0.0045 / aspect, 0.0045) * strength;
                float2 sampleUV = input.texcoord + offset * _BlitScaleBias.xy;
                float2 uvMinimum = min(_BlitScaleBias.zw, _BlitScaleBias.zw + _BlitScaleBias.xy)
                    + _TemperatureSourceTexelSize.xy * 0.5;
                float2 uvMaximum = max(_BlitScaleBias.zw, _BlitScaleBias.zw + _BlitScaleBias.xy)
                    - _TemperatureSourceTexelSize.xy * 0.5;
                sampleUV = clamp(sampleUV, uvMinimum, uvMaximum);
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV);
                float ribbon = saturate(lerp(horizontalSnake, verticalSnake, sideWeight) * 0.5 + 0.5);
                ribbon = ribbon * ribbon * ribbon;
                float tint = strength * (0.035 + ribbon * 0.065);
                sceneColor.rgb = lerp(sceneColor.rgb, half3(1.0, 0.30, 0.055), tint);
                return sceneColor;
            }
            ENDHLSL
        }
    }
}
