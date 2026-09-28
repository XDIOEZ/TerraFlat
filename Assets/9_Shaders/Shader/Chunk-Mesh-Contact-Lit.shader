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
        CBUFFER_END

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.positionWS = positionWS.xy;
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

        half4 SampleGround(Varyings input)
        {
            half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) *
                input.tint * _Color * _RendererColor;
            main.rgb = ApplyGroundElevation(main.rgb, input.positionWS,
                input.elevationDelta, input.elevationLevel);
            half contact = ComputeContact(input.positionWS, input.contact);
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
                half4 main = SampleGround(input);
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData; InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
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
            half4 Frag(Varyings input) : SV_Target { return SampleGround(input); }
            ENDHLSL
        }
    }
}
