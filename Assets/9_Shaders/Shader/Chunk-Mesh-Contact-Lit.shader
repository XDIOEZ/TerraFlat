Shader "FlatWorld/2D/Chunk Mesh Contact Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        _FarmlandDryColor("Farmland Dry Soil Color", Color) = (0.67,0.50,0.30,1)
        _FarmlandDryBlend("Farmland Dry Color Strength", Range(0,1)) = 0.55
        _FarmlandWetTint("Farmland Wet Soil Tint", Color) = (0.70,0.76,0.80,1)

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        struct Attributes
        {
            float3 positionOS : POSITION;
            half4 tint : COLOR;
            float2 uv : TEXCOORD0;
            float4 contact : TEXCOORD1;
            float4 neighbourHeight : TEXCOORD2;
            float height : TEXCOORD3;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half2 lightingUV : TEXCOORD1;
            float2 positionWS : TEXCOORD2;
            half2 soilMoisture : TEXCOORD3;

            float2 positionOS : TEXCOORD6;
            half4 tint : COLOR;
        };

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _RendererColor;
            float4 _FarmlandDryColor;
            float4 _FarmlandWetTint;
            float _FarmlandDryBlend;

            float4 _ChunkSize;
        CBUFFER_END

        #if defined(FLATWORLD_CHUNK_CELL_BUFFER)
        struct ChunkGroundCellData
        {
            float4 uv01;
            float4 uv23;
            float4 tint;
            float4 contact;
            float4 neighbourHeight;
            float4 meta;
            float4 inverse0;
            float4 inverse1;
            float4 spriteBounds;
        };
        StructuredBuffer<ChunkGroundCellData> _ChunkCellData;
        #endif

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.positionWS = positionWS.xy;
            output.positionOS = input.positionOS.xy;
            output.uv = input.uv;
            output.tint = input.tint;
            output.soilMoisture = input.contact.xy;
            output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
            return output;
        }

        #if defined(FLATWORLD_CHUNK_CELL_BUFFER)
        ChunkGroundCellData LoadGroundCell(float2 positionOS, out float2 spriteUV)
        {
            float2 chunkSize = max(_ChunkSize.xy, 1.0);
            float2 local = clamp(positionOS, 0.0, chunkSize - 0.0001);
            uint2 cell = (uint2)floor(local);
            uint width = (uint)max(1.0, floor(chunkSize.x + 0.5));
            ChunkGroundCellData data = _ChunkCellData[cell.y * width + cell.x];
            clip(data.meta.y - 0.5);

            float2 target = frac(local) - 0.5;
            float2 source = float2(
                dot(data.inverse0.xyz, float3(target, 1.0)),
                dot(data.inverse1.xyz, float3(target, 1.0)));
            spriteUV = (source - data.spriteBounds.xy) * data.spriteBounds.zw;
            clip(spriteUV.x);
            clip(spriteUV.y);
            clip(1.0 - spriteUV.x);
            clip(1.0 - spriteUV.y);
            return data;
        }

        float2 ResolveAtlasUV(ChunkGroundCellData data, float2 spriteUV)
        {
            float2 bottom = lerp(data.uv01.xy, data.uv01.zw, spriteUV.x);
            float2 top = lerp(data.uv23.xy, data.uv23.zw, spriteUV.x);
            return lerp(bottom, top, spriteUV.y);
        }
        #endif

        half4 SampleGround(Varyings input, out float2 sampleUV)
        {
            half4 tint = input.tint;
            half2 soilMoisture = input.soilMoisture;
            sampleUV = input.uv;
            #if defined(FLATWORLD_CHUNK_CELL_BUFFER)
                float2 spriteUV;
                ChunkGroundCellData data = LoadGroundCell(input.positionOS, spriteUV);
                sampleUV = ResolveAtlasUV(data, spriteUV);
                tint = data.tint;
                soilMoisture = data.contact.xy;
            #endif

            half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV);
            // 同一张耕地图按真实水量从浅色干土过渡到深色湿土，保留原像素纹理与透明度。
            if (soilMoisture.y > 0.5h)
            {
                half3 dry = lerp(main.rgb, _FarmlandDryColor.rgb, saturate(_FarmlandDryBlend));
                half3 wet = main.rgb * _FarmlandWetTint.rgb;
                main.rgb = lerp(dry, wet, saturate(soilMoisture.x));
            }
            return main * tint * _Color * _RendererColor;
        }
        ENDHLSL

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            // 单 Quad 的逐格数据仅在运行时启用，打包必须保留两种网格路径。
            #pragma multi_compile_local _ FLATWORLD_CHUNK_CELL_BUFFER
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 sampleUV;
                half4 main = SampleGround(input, sampleUV);
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, sampleUV);
                SurfaceData2D surfaceData; InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(sampleUV, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ FLATWORLD_CHUNK_CELL_BUFFER
            half4 Frag(Varyings input) : SV_Target
            {
                float2 sampleUV;
                return SampleGround(input, sampleUV);
            }
            ENDHLSL
        }
    }
}
