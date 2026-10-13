// 风格化海洋复用河流自生成的短亮纹，保留海风和潮汐，不描噪声闭环。
#ifndef FLATWORLD_OCEAN_SURFACE_STYLIZED_HLSL
#define FLATWORLD_OCEAN_SURFACE_STYLIZED_HLSL

// 复用河流的格角局部坐标，风向变化不会围绕世界原点甩动整片水纹。
float4 SampleStylizedOceanPattern(float2 positionWS, WaterMotionData motion,
    float2 positionDx, float2 positionDy)
{
    float density = max(_PixelDensity, 1.0);
    float2 samplePosition = floor(positionWS * density + 0.5) / density - motion.tideOffset;
    float2 anchor = floor(positionWS);
    float2 cellUV = frac(positionWS);
    float2 blend = cellUV * cellUV * (3.0 - 2.0 * cellUV);
    // velocity 已在统一入口应用流速倍率，海洋不重复乘倍率，也不读取河流泡沫配置。
    float4 bottomLeft = SampleDirectionalWaterCorner(samplePosition, anchor,
        motion.velocity, motion.waveDirection, positionDx, positionDy, 0.12);
    float4 bottomRight = SampleDirectionalWaterCorner(samplePosition, anchor + float2(1.0, 0.0),
        motion.velocity, motion.waveDirection, positionDx, positionDy, 0.12);
    float4 topLeft = SampleDirectionalWaterCorner(samplePosition, anchor + float2(0.0, 1.0),
        motion.velocity, motion.waveDirection, positionDx, positionDy, 0.12);
    float4 topRight = SampleDirectionalWaterCorner(samplePosition, anchor + float2(1.0, 1.0),
        motion.velocity, motion.waveDirection, positionDx, positionDy, 0.12);
    float4 pattern = lerp(lerp(bottomLeft, bottomRight, blend.x), lerp(topLeft, topRight, blend.x), blend.y);
    float2 energy = sqrt(blend * blend + (1.0 - blend) * (1.0 - blend));
    pattern.xy /= max(energy.x * energy.y, 0.25);
    return pattern;
}

WaterSurfaceData CalculateStylizedOceanSurface(float2 positionWS, float2 screenUV, half depth,
    WaterMotionData motion, float2 positionDx, float2 positionDy)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float4 pattern = SampleStylizedOceanPattern(positionWS, motion, positionDx, positionDy);
    float2 waterPosition = positionWS - motion.tideOffset;
    float phase = dot(waterPosition, motion.waveDirection) * max(_SwellScale, 0.05)
        - motion.waveTime * 0.85;
    float finePhase = dot(waterPosition, motion.waveDirection) * max(_RippleScale, 0.25)
        - motion.waveTime * 1.25;
    float height = (sin(phase) * 0.07 + sin(finePhase) * 0.025) * motion.waveHeight;
    float2 slope = pattern.xy * lerp(0.35, 0.7, saturate(motion.waveHeight));
    slope += motion.waveDirection * (cos(phase) * 0.055 + cos(finePhase) * 0.025) * motion.waveHeight;
    float3 normalWS = normalize(float3(-slope * max(_NormalStrength, 0.0), 1.0));
    float3 sunDirection = _SunDirection.xyz
        * rsqrt(max(dot(_SunDirection.xyz, _SunDirection.xyz), 0.0001));
    float3 halfDirection = normalize(sunDirection + float3(0.0, 0.0, 1.001));
    float3 mirrorDirection = _ReflectionDirection.xyz
        * rsqrt(max(dot(_ReflectionDirection.xyz, _ReflectionDirection.xyz), 0.0001));
    float reflectionLobe = pow(saturate(dot(reflect(float3(0.0, 0.0, -1.0), normalWS), mirrorDirection)),
        lerp(3.0, 24.0, saturate(_ReflectionSmoothness)));
    float sparkle = smoothstep(0.52, 0.86, pattern.z);
    surface.liquidDepth = saturate(depth);
    surface.depthBlend = 1.0h - surface.liquidDepth;
    surface.reflection = reflectionLobe * _ReflectionStrength * _ReflectionColor.a * 0.35;
    surface.specular = pow(saturate(dot(normalWS, halfDirection)), max(_SpecularPower, 4.0))
        * sparkle * _SpecularStrength * _SpecularColor.a;
    float rippleWidth = clamp(_RippleWidth, 0.02, 0.45);
    surface.ripple = smoothstep(0.78 - rippleWidth, 0.92, pattern.z)
        * _RippleStrength * _RippleColor.a * lerp(0.35, 0.8, saturate(motion.waveHeight));
    surface.rippleShadow = (1.0 - pattern.z) * _RippleShadowStrength * 0.3;
    surface.whitecap = pattern.w * _WhitecapStrength * _FoamColor.a
        * saturate(motion.foamActivity) * smoothstep(0.05, 0.85, motion.waveHeight);
    surface.moonReflection = ComputeMoonReflection(screenUV, height,
        pattern.z, pattern.w, normalWS.x, normalWS.y, motion.waveTime);
    return surface;
}

#endif
