Shader "FlatWorld/2D/Chunk Mesh Contact Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        _EdgeColor("Contact Shadow", Color) = (0.035, 0.022, 0.015, 1)
        _EdgeWidth("Shadow Width", Range(0.03, 0.45)) = 0.2
        _EdgeStrength("Shadow Strength", Range(0, 1)) = 0.72
        _CornerStrength("Corner Strength", Range(0, 1)) = 0.18
        _ElevationStrength("Elevation Strength", Range(0, 1)) = 1
        _ElevationLevelCount("Elevation Level Count", Range(2, 64)) = 20
        _ElevationToneStrength("Elevation Tone Strength", Range(0, 0.2)) = 0.04
        _ElevationShadowStrength("Elevation Shadow Strength", Range(0, 1)) = 0.72
        _ElevationShadowColor("Elevation Shadow Color", Color) = (0.12, 0.09, 0.045, 1)
        _ElevationHighlightStrength("Elevation Highlight Strength", Range(0, 1)) = 0.12
        _ElevationEdgeWidth("Elevation Edge Width", Range(0.01, 0.45)) = 0.2
        _ElevationDeltaForMaxStrength("Elevation Delta For Max Strength", Range(1, 10)) = 2
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
            nointerpolation half4 contact : TEXCOORD3;
            nointerpolation float4 elevationDelta : TEXCOORD4;
            nointerpolation float elevationLevel : TEXCOORD5;
            float2 positionOS : TEXCOORD6;
            half4 tint : COLOR;
        };

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _RendererColor;
            float4 _EdgeColor;
            float _EdgeWidth;
            float _EdgeStrength;
            float _CornerStrength;
            float _ElevationStrength;
            float _ElevationLevelCount;
            float _ElevationToneStrength;
            float _ElevationShadowStrength;
            float4 _ElevationShadowColor;
            float _ElevationHighlightStrength;
            float _ElevationEdgeWidth;
            float _ElevationDeltaForMaxStrength;
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
            output.contact = input.contact;
            output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
            output.elevationLevel = -1.0;
            if (input.height >= 0.0)
            {
                float levelCount = max(2.0, floor(_ElevationLevelCount));
                float level = min(floor(saturate(input.height) * levelCount), levelCount - 1.0);
                float4 neighbourLevels = min(floor(saturate(input.neighbourHeight) * levelCount), levelCount - 1.0);
                output.elevationDelta = neighbourLevels - level;
                output.elevationLevel = level / (levelCount - 1.0);
            }
            return output;
        }

        void ResolveElevation(float height, float4 neighbourHeight,
            out float4 elevationDelta, out float elevationLevel)
        {
            elevationDelta = 0.0;
            elevationLevel = -1.0;
            if (height < 0.0)
                return;
            float levelCount = max(2.0, floor(_ElevationLevelCount));
            float level = min(floor(saturate(height) * levelCount), levelCount - 1.0);
            float4 neighbourLevels = min(floor(saturate(neighbourHeight) * levelCount), levelCount - 1.0);
            elevationDelta = neighbourLevels - level;
            elevationLevel = level / (levelCount - 1.0);
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

        half3 ApplyGroundElevation(half3 color, float2 positionWS, float4 delta, float level)
        {
            if (level < 0.0 || _ElevationStrength <= 0.0) return color;
            float2 cellUV = frac(positionWS + 0.0001);
            float4 distances = float4(cellUV.x, 1.0 - cellUV.x, cellUV.y, 1.0 - cellUV.y);
            float4 edges = 1.0 - smoothstep(0.0, max(_ElevationEdgeWidth, 0.001), distances);
            float maxDelta = max(_ElevationDeltaForMaxStrength, 1.0);
            float4 shadows = edges * saturate(delta / maxDelta);
            float4 highlights = edges * saturate(-delta / maxDelta);
            half shadow = max(max(shadows.x, shadows.y), max(shadows.z, shadows.w));
            half highlight = max(max(highlights.x, highlights.y), max(highlights.z, highlights.w));
            half strength = saturate(_ElevationStrength);
            color *= 1.0h + (level - 0.5h) * _ElevationToneStrength * strength;
            color = lerp(color, _ElevationShadowColor.rgb,
                saturate(shadow * _ElevationShadowStrength * _ElevationShadowColor.a * strength));
            return lerp(color, half3(1, 1, 1),
                saturate(highlight * _ElevationHighlightStrength * strength) * (1.0h - shadow));
        }

        half ComputeContact(float2 positionWS, half4 mask)
        {
            float2 cellUV = frac(positionWS + 0.0001);
            half width = max(_EdgeWidth, 0.001);
            half left = mask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
            half right = mask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
            half bottom = mask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
            half top = mask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
            half strongest = max(max(left, right), max(bottom, top));
            half overlap = saturate(left + right + bottom + top - strongest);
            return saturate(strongest + overlap * _CornerStrength);
        }

        half4 SampleGround(Varyings input, out float2 sampleUV)
        {
            half4 tint = input.tint;
            half4 contactMask = input.contact;
            float4 elevationDelta = input.elevationDelta;
            float elevationLevel = input.elevationLevel;
            sampleUV = input.uv;
            #if defined(FLATWORLD_CHUNK_CELL_BUFFER)
                float2 spriteUV;
                ChunkGroundCellData data = LoadGroundCell(input.positionOS, spriteUV);
                sampleUV = ResolveAtlasUV(data, spriteUV);
                tint = data.tint;
                contactMask = data.contact;
                ResolveElevation(data.meta.x, data.neighbourHeight, elevationDelta, elevationLevel);
            #endif

            half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV) *
                tint * _Color * _RendererColor;
            main.rgb = ApplyGroundElevation(main.rgb, input.positionWS,
                elevationDelta, elevationLevel);
            half contact = ComputeContact(input.positionWS, contactMask);
            main.rgb = lerp(main.rgb, _EdgeColor.rgb,
                saturate(contact * _EdgeStrength * _EdgeColor.a));
            return main;
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
            #pragma shader_feature_local _ FLATWORLD_CHUNK_CELL_BUFFER
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
            #pragma shader_feature_local _ FLATWORLD_CHUNK_CELL_BUFFER
            half4 Frag(Varyings input) : SV_Target
            {
                float2 sampleUV;
                return SampleGround(input, sampleUV);
            }
            ENDHLSL
        }
    }
}
