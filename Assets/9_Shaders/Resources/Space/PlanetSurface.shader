Shader "FlatWorld/Space/PlanetSurface"
{
    Properties
    {
        _Color("Planet Color", Color) = (1,1,1,1)
        _SurfaceAccentColor("Surface Accent", Color) = (0.5,0.5,0.5,1)
        [Enum(Rocky,0,Ocean,1,Banded,2,Stellar,3)] _SurfaceStyle("Surface Style", Float) = 0
        _SurfaceDetailStrength("Surface Detail Strength", Range(0,1)) = 0.2
        _SurfaceDetailScale("Surface Detail Scale", Float) = 12
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _SurfaceAccentColor;
            float _SurfaceStyle;
            float _SurfaceDetailStrength;
            float _SurfaceDetailScale;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half2 lightingUV : TEXCOORD1;
        };
        Varyings PlanetVertex(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
            return output;
        }

        float PlanetCoverage(float2 uv)
        {
            // 圆边只柔化一个屏幕像素，放大时不会继承贴图的模糊边。
            float radius = length(uv * 2.0 - 1.0);
            float pixelWidth = max(fwidth(radius), 0.0000001);
            return 1.0 - smoothstep(1.0 - pixelWidth, 1.0, radius);
        }
        float PlanetHash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }
        float PlanetNoise(float2 p)
        {
            float2 cell = floor(p);
            float2 t = frac(p);
            t = t * t * (3.0 - 2.0 * t);
            return lerp(lerp(PlanetHash(cell), PlanetHash(cell + float2(1,0)), t.x),
                        lerp(PlanetHash(cell + float2(0,1)), PlanetHash(cell + float2(1,1)), t.x), t.y);
        }
        float PlanetFilteredNoise(float2 p)
        {
            // 小于屏幕像素的细节自动淡出，远看不闪烁，也不分配高清纹理。
            float pixelWidth = max(length(ddx(p)), length(ddy(p)));
            return lerp(PlanetNoise(p), 0.5, smoothstep(0.35, 0.8, pixelWidth));
        }
        half4 PlanetColor(float2 uv)
        {
            float alpha = PlanetCoverage(uv) * _Color.a;
            clip(alpha - 0.0001);
            float2 p = uv * 2.0 - 1.0;
            float2 coords = p * max(1.0, _SurfaceDetailScale);
            float coarse = PlanetFilteredNoise(coords) * 0.6 +
                           PlanetFilteredNoise(coords * 2.1 + 17.0) * 0.3 +
                           PlanetFilteredNoise(coords * 4.3 + 53.0) * 0.1;
            float fine = PlanetFilteredNoise(coords * 64.0 + 91.0) - 0.5;
            half3 color = _Color.rgb;
            if (_SurfaceStyle < 0.5)
            {
                color *= 1.0 + _SurfaceDetailStrength * ((coarse - 0.5) * 0.75 + fine * 0.18);
            }
            else if (_SurfaceStyle < 1.5)
            {
                float coastlineWidth = max(fwidth(coarse), 0.003);
                float land = smoothstep(0.53 - coastlineWidth, 0.53 + coastlineWidth, coarse);
                color = lerp(color, _SurfaceAccentColor.rgb, land * _SurfaceDetailStrength);
                color *= 1.0 + fine * 0.08;
            }
            else if (_SurfaceStyle < 2.5)
            {
                float bandPhase = p.y * _SurfaceDetailScale * 3.0 + (coarse - 0.5) * 4.0;
                float bandVisibility = 1.0 - smoothstep(0.8, 3.0, fwidth(bandPhase));
                float band = 0.5 + 0.5 * sin(bandPhase) * bandVisibility;
                color = lerp(color, _SurfaceAccentColor.rgb, band * _SurfaceDetailStrength);
                color *= 1.0 + fine * 0.06;
            }
            else
            {
                color = lerp(color, _SurfaceAccentColor.rgb, coarse * _SurfaceDetailStrength);
                color *= 0.95 + coarse * 0.1 + fine * 0.05;
            }
            return half4(color, alpha);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PlanetVertex
            #pragma fragment PlanetLitFragment
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
            half4 PlanetLitFragment(Varyings input) : SV_Target
            {
                half4 color = PlanetColor(input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(color.rgb, color.a, half4(1,1,1,1), surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="NormalsRendering" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PlanetVertex
            #pragma fragment PlanetNormalsFragment
            half4 PlanetNormalsFragment(Varyings input) : SV_Target
            {
                half alpha = PlanetCoverage(input.uv) * _Color.a;
                clip(alpha - 0.0001);
                return half4(0.5 * (1.0 - GetViewForwardDir()), alpha);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PlanetVertex
            #pragma fragment PlanetUnlitFragment
            half4 PlanetUnlitFragment(Varyings input) : SV_Target { return PlanetColor(input.uv); }
            ENDHLSL
        }
    }
}
