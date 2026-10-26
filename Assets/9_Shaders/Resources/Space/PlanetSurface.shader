Shader "FlatWorld/Space/PlanetSurface"
{
    Properties
    {
        _Color("Planet Color", Color) = (1,1,1,1)
        _SurfaceAccentColor("Surface Accent", Color) = (0.5,0.5,0.5,1)
        [Enum(Rocky,0,Ocean,1,Banded,2,Stellar,3,Asteroid,4)] _SurfaceStyle("Surface Style", Float) = 0
        _SurfaceDetailStrength("Surface Detail Strength", Range(0,1)) = 0.2
        _SurfaceDetailScale("Surface Detail Scale", Float) = 12
        _SurfaceExtent("Surface Extent", Float) = 1.12
        _SurfaceLightDirection("Sun Direction", Vector) = (0.65,0.35,0.8,0)
        _SurfaceSeed("Surface Seed", Float) = 0
        _SurfaceRotation("Surface Rotation", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/9_Shaders/Shader/PlanetCloudCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _SurfaceAccentColor;
            float _SurfaceStyle;
            float _SurfaceDetailStrength;
            float _SurfaceDetailScale;
            float _SurfaceExtent;
            float4 _SurfaceLightDirection;
            float _SurfaceSeed;
            float _SurfaceRotation;
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
            float2 p = (uv * 2.0 - 1.0) * _SurfaceExtent;
            float radius = length(p);
            if (_SurfaceStyle > 3.5)
            {
                // 小行星轮廓保留大块不规则岩角，实际边界始终位于物理半径内。
                float angle = atan2(p.y, p.x + 0.0000001) + _SurfaceRotation;
                float boundary = 0.86 + sin(angle * 3.0 + _SurfaceSeed) * 0.065 +
                    sin(angle * 5.0 - _SurfaceSeed * 0.71) * 0.035 + sin(angle * 9.0 + _SurfaceSeed) * 0.02;
                radius /= boundary;
            }
            float pixelWidth = max(fwidth(radius), 0.0000001);
            return 1.0 - smoothstep(1.0 - pixelWidth, 1.0 + pixelWidth, radius);
        }
        float3 PlanetRotate(float3 p, float angle)
        {
            float sine, cosine;
            sincos(angle, sine, cosine);
            return float3(p.x * cosine + p.z * sine, p.y, p.z * cosine - p.x * sine);
        }
        float PlanetAtmosphere(float radius, float3 normal)
        {
            if (_SurfaceStyle < 0.5 || _SurfaceStyle > 2.5) return 0.0;
            float outer = 1.0 - smoothstep(1.0, 1.115, radius);
            float rim = exp2(-abs(radius - 1.0) * 65.0);
            float daylight = 0.4 + 0.6 * saturate(dot(normal, normalize(_SurfaceLightDirection.xyz)));
            return outer * rim * daylight * (_SurfaceStyle < 1.5 ? 0.42 : 0.24);
        }
        float PlanetCorona(float2 p, float radius)
        {
            float angle = atan2(p.y, p.x + 0.0000001);
            float pulse = 0.8 + sin(angle * 11.0 + _Time.y * 0.45) * 0.12 +
                          sin(angle * 23.0 - _Time.y * 0.3) * 0.08;
            float flare = pow(saturate(0.5 + 0.5 * sin(angle * 7.0 + _Time.y * 0.16)), 6.0);
            float falloff = exp2(-max(0.0, radius - 1.0) * (18.0 - flare * 9.0));
            return falloff * pulse * (1.0 - smoothstep(1.16, 1.295, radius)) * 0.65;
        }
        half4 PlanetColor(float2 uv)
        {
            float2 p = (uv * 2.0 - 1.0) * _SurfaceExtent;
            float radius = length(p);
            float coverage = PlanetCoverage(uv);
            float3 normal = normalize(float3(p, sqrt(saturate(1.0 - dot(p, p)))));
            float glow = _SurfaceStyle > 2.5 && _SurfaceStyle < 3.5 ? PlanetCorona(p, radius) : PlanetAtmosphere(radius, normal);
            float alpha = max(coverage, glow) * _Color.a;
            clip(alpha - 0.0001);
            float3 surfaceNormal = PlanetRotate(normal, _SurfaceRotation);
            float3 coords = surfaceNormal * max(1.0, _SurfaceDetailScale) + _SurfaceSeed;
            float coarse = _SurfaceStyle < 1.5 || _SurfaceStyle > 3.5 ? PlanetFractal(coords) : 0.5;
            float fine = PlanetFilteredNoise(coords * 7.3 + 91.0) - 0.5;
            float diffuse = saturate(dot(normal, normalize(_SurfaceLightDirection.xyz)));
            half3 color = _Color.rgb;
            if (_SurfaceStyle < 0.5)
            {
                float ridges = 1.0 - abs(coarse * 2.0 - 1.0);
                float basins = smoothstep(0.22, 0.5, coarse);
                color *= 1.0 + _SurfaceDetailStrength * ((ridges - 0.65) * 0.85 + fine * 0.5);
                color = lerp(color * 0.6, color, lerp(1.0, basins, _SurfaceDetailStrength * 0.6));
                color *= 0.24 + diffuse * 0.76;
            }
            else if (_SurfaceStyle < 1.5)
            {
                float coastlineWidth = max(fwidth(coarse), 0.007);
                float land = smoothstep(0.5 - coastlineWidth, 0.5 + coastlineWidth, coarse);
                float shallow = smoothstep(0.4, 0.5, coarse);
                half3 ocean = color * lerp(0.48, 0.85, shallow);
                half3 terrain = lerp(_SurfaceAccentColor.rgb * 0.6, _SurfaceAccentColor.rgb,
                    saturate((coarse - 0.48) * 6.0));
                terrain = lerp(terrain, half3(0.6,0.5,0.3), smoothstep(0.64, 0.8, coarse) * 0.45);
                color = lerp(ocean, terrain, land * _SurfaceDetailStrength);
                float ice = smoothstep(0.86 + (coarse - 0.5) * 0.08, 0.95, abs(normal.y));
                color = lerp(color, half3(0.88,0.94,1.0), ice);
                float3 cloudCoords = PlanetRotate(surfaceNormal, _Time.y * 0.012) * (_SurfaceDetailScale * 1.7) + _SurfaceSeed + 137.0;
                float clouds = PlanetFractal(cloudCoords);
                float cloudAlpha = PlanetCloudOpacity(clouds);
                color *= 1.0 - cloudAlpha * 0.14;
                color = lerp(color, half3(0.94,0.96,1.0), cloudAlpha);
                color *= 0.22 + diffuse * 0.78;
                float specular = pow(saturate(dot(normal, normalize(normalize(_SurfaceLightDirection.xyz) + float3(0,0,1)))), 48.0);
                color += specular * (1.0 - land) * (1.0 - cloudAlpha) * 0.22;
            }
            else if (_SurfaceStyle < 2.5)
            {
                float3 flowCoords = PlanetRotate(surfaceNormal, _Time.y * 0.018) * _SurfaceDetailScale + _SurfaceSeed;
                float flow = PlanetFractal(flowCoords);
                float bandPhase = normal.y * _SurfaceDetailScale * 3.0 + (flow - 0.5) * 6.0;
                float bandVisibility = 1.0 - smoothstep(0.8, 3.0, fwidth(bandPhase));
                float band = 0.5 + 0.5 * sin(bandPhase) * bandVisibility;
                color = lerp(color, _SurfaceAccentColor.rgb, band * _SurfaceDetailStrength);
                color *= 1.0 + _SurfaceDetailStrength * ((flow - 0.5) * 0.8 + fine * 0.12);
                color *= 0.28 + diffuse * 0.72;
            }
            else if (_SurfaceStyle < 3.5)
            {
                // 太阳粒状表面、热点和日冕只读 Shader 时间，不逐帧重绘 CPU 纹理。
                float3 flowCoords = coords + float3(_Time.y * 0.06, _Time.y * 0.035, 0.0);
                float granules = PlanetFractal(flowCoords);
                float cells = smoothstep(0.28, 0.68, granules + fine * 0.12);
                half3 hotColor = lerp(_Color.rgb, half3(1.0,0.96,0.72), 0.55);
                color = lerp(_SurfaceAccentColor.rgb * 0.65, hotColor, cells);
                color *= 0.7 + sqrt(saturate(1.0 - dot(p, p))) * 0.35;
                color = lerp(_Color.rgb, color, _SurfaceDetailStrength);
                color = lerp(_SurfaceAccentColor.rgb, color, coverage);
            }
            else
            {
                float fractures = 1.0 - abs(coarse * 2.0 - 1.0);
                float ore = smoothstep(0.74, 0.87, PlanetFilteredNoise(coords * 2.1 + 37.0));
                color *= 0.65 + fractures * 0.42 + fine * _SurfaceDetailStrength * 0.38;
                color = lerp(color, _SurfaceAccentColor.rgb, ore * _SurfaceDetailStrength);
                color *= 0.2 + diffuse * 0.8;
            }
            if (_SurfaceStyle < 2.5)
            {
                half3 atmosphereColor = _SurfaceStyle < 1.5 ? half3(0.28,0.57,1.0) : lerp(_Color.rgb, half3(0.8,0.9,1.0), 0.25);
                color += atmosphereColor * glow * coverage * 0.65;
                color = lerp(atmosphereColor, color, coverage);
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
                if (_SurfaceStyle > 2.5 && _SurfaceStyle < 3.5) return color;
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
                float2 p = (input.uv * 2.0 - 1.0) * _SurfaceExtent;
                float radius = length(p);
                float3 normal = normalize(float3(p, sqrt(saturate(1.0 - dot(p, p)))));
                float glow = _SurfaceStyle > 2.5 && _SurfaceStyle < 3.5 ? PlanetCorona(p, radius) : PlanetAtmosphere(radius, normal);
                half alpha = max(PlanetCoverage(input.uv), glow) * _Color.a;
                clip(alpha - 0.0001);
                return half4(0.5 * (1.0 - GetViewForwardDir()), alpha);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Name "PlanetUnlit"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PlanetVertex
            #pragma fragment PlanetUnlitFragment
            half4 PlanetUnlitFragment(Varyings input) : SV_Target { return PlanetColor(input.uv); }
            ENDHLSL
        }
    }
}
