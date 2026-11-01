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
        [HideInInspector] _FrozenIcePatternOrigin("Frozen Ice Pattern Origin", Vector) = (0,0,0,0)
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
            float4 _FrozenIcePatternOrigin;
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

        #if defined(FLATWORLD_FROZEN_ICE)
        float FrozenIceCellHash(float2 cell)
        {
            float3 value = frac(cell.xyx * 0.1031);
            value += dot(value, value.yzx + 33.33);
            return frac((value.x + value.y) * value.z);
        }

        // 使用规范区块坐标生成静止的像素冰裂与少量霜色，不改变透明度。
        half3 ShadeFrozenIceSurface(half3 color, float2 positionOS)
        {
            float2 pixel = (floor(positionOS * 16.0) + 0.5) / 16.0 + _FrozenIcePatternOrigin.xy;
            float2 grid = pixel * 0.375;
            float seed = FrozenIceCellHash(floor(grid));
            float4 variation = frac(seed * float4(7.13, 17.43, 31.87, 13.73));
            float2 local = frac(grid);
            local = seed > 0.5 ? local.yx : local;

            float start = 0.1 + 0.25 * variation.x;
            float crackLength = 0.28 + 0.4 * variation.y;
            float along = local.x - start;
            float slope = (variation.w - 0.5) * 1.5;
            float bend = crackLength * (0.35 + 0.3 * variation.x);
            float center = 0.15 + 0.7 * variation.z;
            float crackY = center + slope * min(along, bend) +
                (slope + variation.y - 0.5) * max(along - bend, 0.0);
            float crackExtent = step(0.0, along) * step(along, crackLength) * step(0.1, seed);
            float crack = step(abs(local.y - crackY), 0.014) * crackExtent;
            float crackRim = step(abs(local.y - crackY - 0.028), 0.012) * crackExtent;
            float branchAlong = along - bend;
            float branchSlope = slope + (variation.x > 0.5 ? 0.75 : -0.75);
            float branch = step(abs(local.y - center - slope * bend - branchAlong * branchSlope), 0.012) *
                step(0.0, branchAlong) * step(branchAlong, 0.12) * step(0.65, variation.z);
            float glint = step(abs(local.y - 0.2 - 0.55 * variation.y - along * 0.25), 0.018) *
                step(0.06, along) * step(along, 0.22) * step(0.5, variation.x);
            float2 frostCenter = 0.3 + 0.4 * variation.xy;
            float2 frostOffset = local - frostCenter;
            float2 frostDistance = abs(float2(frostOffset.x + frostOffset.y * (variation.y - 0.5) * 0.5,
                frostOffset.y));
            float frost = step(frostDistance.x + frostDistance.y * 0.7, 0.14 + 0.08 * variation.z) *
                step(0.45, variation.w);
            color = lerp(color, half3(0.40, 0.60, 0.70), crackRim * 0.2);
            half strength = saturate(0.025 + frost * 0.10 + crack * 0.5 + branch * 0.3 + glint * 0.28);
            return lerp(color, half3(0.78, 0.93, 1.0), strength);
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
            half4 surface = main * tint * _Color * _RendererColor;
            #if defined(FLATWORLD_FROZEN_ICE)
                surface.rgb = ShadeFrozenIceSurface(surface.rgb, input.positionOS);
            #endif
            return surface;
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
            #pragma multi_compile_local_fragment _ FLATWORLD_FROZEN_ICE
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
            #pragma multi_compile_local_fragment _ FLATWORLD_FROZEN_ICE
            half4 Frag(Varyings input) : SV_Target
            {
                float2 sampleUV;
                return SampleGround(input, sampleUV);
            }
            ENDHLSL
        }
    }
}
