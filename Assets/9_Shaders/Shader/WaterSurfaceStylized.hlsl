// 风格化水面：海洋与河流共用短亮纹美术，静水保留原细波和浅水焦散。
#ifndef FLATWORLD_WATERSURFACESTYLIZED_HLSL
#define FLATWORLD_WATERSURFACESTYLIZED_HLSL

#if !defined(FLATWORLD_WATER_MATERIAL_CBUFFER_DEFINED)
float _PixelDensity;
#endif

#include "RiverSurface.hlsl"
#include "OceanSurfaceStylized.hlsl"

// 世界像素格跟随地图固定，镜头移动不会拖着水面噪声走。
float2 QuantizeWaterPosition(float2 positionWS)
{
    float density = max(_PixelDensity, 1.0);
    return floor(positionWS * density + 0.5) / density;
}

WaterSurfaceData CalculateStillStylizedWaterSurface(float2 positionWS, float2 screenUV,
    half liquidDepth, WaterMotionData motion)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float2 direction = motion.waveDirection;
    float2 lateral = float2(-direction.y, direction.x);
    float2 pixelPosition = QuantizeWaterPosition(positionWS);
    float2 waterPosition = pixelPosition - motion.tideOffset;
    float time = motion.waveTime;
    float macroA = SampleAdvectedWaterNoise(pixelPosition, motion.velocity, 0.16, float2(3.1, 8.2));
    float macroB = SampleAdvectedWaterNoise(pixelPosition, motion.velocity, 0.29, float2(17.3, 9.2));
    float3 currentDetail = SampleWaterCurrentDetail(pixelPosition, motion);
    float2 warped = waterPosition + (direction * (macroA - 0.5)
        + lateral * (macroB - 0.5)) * _WaveDistortion;

    // 大涌浪决定起伏，小波错开方向和相位，避免整片水面只有平行直线。
    float swellScale = max(_SwellScale, 0.05);
    float detailScale = max(_DetailScale, 0.5);
    float2 crossDirection = direction * 0.8 + lateral * 0.6;
    float swellPhase = dot(warped, direction) * swellScale - time * 0.9;
    float crossPhase = dot(warped, crossDirection) * swellScale * 1.7 - time * 0.64 + 2.1;
    float detailPhase = dot(warped, direction * 0.6 - lateral * 0.8) * detailScale
        - time * 1.35 + macroB * 2.2;
    float swellA = sin(swellPhase);
    float swellB = sin(crossPhase);
    float detail = sin(detailPhase);
    float height = (swellA * 0.55 + swellB * 0.3 + detail * 0.15) * motion.waveHeight;
    float2 slope = direction * cos(swellPhase) * swellScale * 0.55
        + crossDirection * cos(crossPhase) * swellScale * 0.51
        + (direction * 0.6 - lateral * 0.8) * cos(detailPhase) * detailScale * 0.15
        + currentDetail.yz * motion.currentStrength * 0.24;
    float3 normalWS = normalize(float3(-slope * _NormalStrength * motion.waveHeight, 1.0));

    surface.liquidDepth = saturate(liquidDepth);
    surface.depthBlend = 1.0h - surface.liquidDepth;
    float ripplePhase = dot(warped, direction) * max(_RippleScale, 0.25)
        - time * 1.15 + macroB * 2.8;
    float rippleWave = 0.5 + 0.5 * sin(ripplePhase);
    float width = max(_RippleWidth, 0.02);
    float aa = max(fwidth(rippleWave), 0.01);
    float crest = smoothstep(1.0 - width - aa, 1.0 - width + aa, rippleWave);
    float segments = smoothstep(0.26, 0.72, macroA * 0.55 + currentDetail.x * 0.45);
    surface.ripple = crest * segments * _RippleStrength * _RippleColor.a
        * lerp(0.45, 1.0, saturate(motion.waveHeight));
    float backWave = 0.5 + 0.5 * sin(ripplePhase - 0.35);
    surface.rippleShadow = smoothstep(1.0 - width * 1.5, 1.0, backWave)
        * (1.0 - crest) * segments * _RippleShadowStrength;

    // 同一法线驱动天空倒影和太阳碎光，参数在河流、湖泊、海面全部生效。
    float3 reflected = reflect(float3(0.0, 0.0, -1.0), normalWS);
    float3 mirrorDirection = _ReflectionDirection.xyz
        * rsqrt(max(dot(_ReflectionDirection.xyz, _ReflectionDirection.xyz), 0.0001));
    float alignment = saturate(dot(reflected, mirrorDirection));
    float mirrorLobe = pow(alignment, exp2(lerp(1.5, 5.0, saturate(_ReflectionSmoothness))));
    float fresnel = saturate((1.0 - normalWS.z) * 3.5);
    surface.reflection = saturate(mirrorLobe * (0.62 + macroB * 0.38) + fresnel * fresnel * 0.18)
        * _ReflectionStrength * _ReflectionColor.a;
    float3 sunDirection = _SunDirection.xyz
        * rsqrt(max(dot(_SunDirection.xyz, _SunDirection.xyz), 0.0001));
    float3 halfDirection = normalize(sunDirection + float3(0.0, 0.0, 1.001));
    float sparkle = smoothstep(0.52, 0.86, currentDetail.x + detail * 0.12);
    surface.specular = pow(saturate(dot(normalWS, halfDirection)), max(_SpecularPower, 4.0))
        * sparkle * _SpecularStrength * _SpecularColor.a;

    // 焦散使用流动场的细窄网纹，只在浅水明显；十档基础水色仍由真实水深决定。
    float causticDistance = abs(currentDetail.x - 0.5);
    float caustic = 1.0 - smoothstep(0.018, 0.085, causticDistance);
    surface.caustic = caustic * pow(surface.depthBlend, 1.65)
        * _CausticStrength * _CausticColor.a;
    float steepCrest = smoothstep(0.35, 0.86, height) * smoothstep(0.25, 1.2, dot(slope, slope));
    surface.whitecap = steepCrest * smoothstep(0.38, 0.72, currentDetail.x)
        * _WhitecapStrength * _FoamColor.a * motion.foamActivity;
    ApplyWaterCurrentDetail(surface, currentDetail, motion);
    surface.moonReflection = ComputeMoonReflection(screenUV, height,
        macroA, macroB, detail, swellB, time);
    return surface;
}

// 风格化海洋使用河流同款短亮纹，水深分档及材质切换仍由原入口负责。
WaterSurfaceData CalculateWaterSurface(float2 positionWS, float2 screenUV,
    half liquidDepth, WaterMotionData motion)
{
    float2 positionDx = ddx(positionWS);
    float2 positionDy = ddy(positionWS);
    WaterSurfaceData surface = (WaterSurfaceData)0;
    UNITY_BRANCH
    if (motion.ocean > 0.5)
        surface = CalculateStylizedOceanSurface(positionWS, screenUV, liquidDepth, motion, positionDx, positionDy);
    else
        surface = CalculateStillStylizedWaterSurface(positionWS, screenUV, liquidDepth, motion);
    return surface;
}

// 波光以提亮方式叠加，保留水深色阶，避免浪纹盖成固定颜色的块。
half3 ApplyWaterSurface(half3 sourceColor, WaterSurfaceData surface)
{
    half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, surface.depthBlend);
    sourceColor = lerp(sourceColor, waterTint, lerp(saturate(_SurfaceTint), 1.0h, surface.liquidDepth));
    sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
    sourceColor += _CausticColor.rgb * surface.caustic * (1.0h - sourceColor);
    sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
    sourceColor += _RippleColor.rgb * surface.ripple * (1.0h - sourceColor);
    sourceColor += _SpecularColor.rgb * surface.specular * (1.0h - sourceColor);
    return saturate(lerp(sourceColor, _FoamColor.rgb, surface.whitecap));
}

half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS, WaterMotionData motion)
{
    return ApplyFlowingWaterShore(sourceColor, recess, QuantizeWaterPosition(positionWS), motion);
}

#endif
