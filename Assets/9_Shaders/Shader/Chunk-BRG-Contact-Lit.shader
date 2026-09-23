Shader "FlatWorld/2D/Chunk BRG Contact Lit"
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

        // 两个 Pass 共用材质布局、顶点数据与高度公式，保持 SRP Batcher / BRG 兼容。
        HLSLINCLUDE
        #include "ChunkBRGInstance.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        struct Attributes
        {
            float3 positionOS : POSITION;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
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
            UNITY_VERTEX_INPUT_INSTANCE_ID
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

        #if defined(UNITY_DOTS_INSTANCING_ENABLED)
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _Color)
            UNITY_DOTS_INSTANCED_PROP(float4, _RendererColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _EdgeColor)
            UNITY_DOTS_INSTANCED_PROP(float, _EdgeWidth)
            UNITY_DOTS_INSTANCED_PROP(float, _EdgeStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _CornerStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationLevelCount)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationToneStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationShadowStrength)
            UNITY_DOTS_INSTANCED_PROP(float4, _ElevationShadowColor)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationHighlightStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationEdgeWidth)
            UNITY_DOTS_INSTANCED_PROP(float, _ElevationDeltaForMaxStrength)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
        #define _RendererColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _RendererColor)
        #define _EdgeColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EdgeColor)
        #define _EdgeWidth UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _EdgeWidth)
        #define _EdgeStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _EdgeStrength)
        #define _CornerStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _CornerStrength)
        #define _ElevationStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationStrength)
        #define _ElevationLevelCount UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationLevelCount)
        #define _ElevationToneStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationToneStrength)
        #define _ElevationShadowStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationShadowStrength)
        #define _ElevationShadowColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _ElevationShadowColor)
        #define _ElevationHighlightStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationHighlightStrength)
        #define _ElevationEdgeWidth UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationEdgeWidth)
        #define _ElevationDeltaForMaxStrength UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ElevationDeltaForMaxStrength)
        #endif

        // 高度只改变着色；顶点位置仍使用原 BRG 变换，世界坐标边界不受 Sprite UV/旋转影响。
        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            ChunkBRGInstanceData d = LoadChunkBRGInstanceData();
            float3 positionWS = TransformChunkBRGVertex(input.positionOS, d);
            Varyings o = (Varyings)0;
            o.positionCS = TransformWorldToHClip(positionWS);
            o.positionWS = positionWS.xy;
            o.uv = input.uv;
            o.contact = d.data0;
            o.tint = input.color * d.tint;
            o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
            o.elevationLevel = -1.0;
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            if (d.transform0.w >= 0.0)
            {
                float levelCount = max(2.0, floor(_ElevationLevelCount));
                float level = min(floor(saturate(d.transform0.w) * levelCount), levelCount - 1.0);
                float4 neighbourLevels = min(floor(saturate(d.data1) * levelCount), levelCount - 1.0);
                o.elevationDelta = neighbourLevels - level;
                o.elevationLevel = level / (levelCount - 1.0);
            }
            #endif
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            return o;
        }

        // 每个方向只在存在层差时出边；同层格之间不会画线，拐角取最大值避免叠黑。
        half3 ApplyGroundElevation(half3 color, float2 positionWS, float4 delta, float level)
        {
            if (level < 0.0 || _ElevationStrength <= 0.0)
                return color;

            float2 cellUV = frac(positionWS + 0.0001);
            float4 distances = float4(cellUV.x, 1.0 - cellUV.x, cellUV.y, 1.0 - cellUV.y);
            float4 edges = 1.0 - smoothstep(0.0, max(_ElevationEdgeWidth, 0.001), distances);
            float maxDelta = max(_ElevationDeltaForMaxStrength, 1.0);
            float4 shadows = edges * saturate(delta / maxDelta);
            float4 highlights = edges * saturate(-delta / maxDelta);
            half shadow = max(max(shadows.x, shadows.y), max(shadows.z, shadows.w));
            half highlight = max(max(highlights.x, highlights.y), max(highlights.z, highlights.w));
            half strength = saturate(_ElevationStrength);

            // 默认整体明暗最多偏移 2%，保留雪地接近纯白；暗边优先于弱亮边。
            color *= 1.0h + (level - 0.5h) * _ElevationToneStrength * strength;
            color = lerp(color, _ElevationShadowColor.rgb,
                saturate(shadow * _ElevationShadowStrength * _ElevationShadowColor.a * strength));
            return lerp(color, half3(1, 1, 1),
                saturate(highlight * _ElevationHighlightStrength * strength) * (1.0h - shadow));
        }

        // 保留既有墙脚/石地接触阴影，不让高度参数修改 Contact 行为。
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

        // 两个 Pass 在光照前使用完全相同的地形颜色处理。
        half4 SampleGround(Varyings input)
        {
            half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.tint * _Color * _RendererColor;
            main.rgb = ApplyGroundElevation(main.rgb, input.positionWS, input.elevationDelta, input.elevationLevel);
            half contact = ComputeContact(input.positionWS, input.contact);
            main.rgb = lerp(main.rgb, _EdgeColor.rgb, saturate(contact * _EdgeStrength * _EdgeColor.a));
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
            #pragma multi_compile _ DOTS_INSTANCING_ON
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
                UNITY_SETUP_INSTANCE_ID(input);
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
            #pragma multi_compile _ DOTS_INSTANCING_ON

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return SampleGround(input);
            }
            ENDHLSL
        }
    }
}
