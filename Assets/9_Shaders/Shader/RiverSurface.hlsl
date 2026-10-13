// 流动纹理改编自 MochiesCode，四角方向混合参考 Scrawk/Frans van Hoesel，许可见 ThirdPartyNotices。
#ifndef FLATWORLD_RIVERSURFACE_HLSL
#define FLATWORLD_RIVERSURFACE_HLSL

TEXTURE2D(_RiverPatternTex);
SAMPLER(sampler_RiverPatternTex);
#if !defined(FLATWORLD_WATER_MATERIAL_CBUFFER_DEFINED)
float _RiverFoamCoverage;
#endif

// Mochie FlowUV 的半周期交接，删除固定 jump，使水纹只按真实水流前进。
float3 RiverFlowUV(float2 uv, float2 velocityUV, float time, float phase)
{
    float progress = frac(time + phase);
    return float3(uv - velocityUV * progress, 1.0 - abs(1.0 - 2.0 * progress));
}

// 先在共享格角的局部坐标旋转，再把斜率转回世界方向，避免大世界坐标产生扭曲。
float4 SampleRiverCorner(float2 positionWS, float2 anchor, float2 velocity, float2 fallback,
    float2 positionDx, float2 positionDy)
{
    float speed = length(velocity);
    float2 axis = speed > 0.00001 ? velocity / speed : fallback;
    float2 lateral = float2(-axis.y, axis.x);
    float2 localPosition = positionWS - anchor;
    float scale = max(_DetailScale, 0.5) * 0.075;
    float2 localUV = float2(dot(localPosition, axis) * 0.75, dot(localPosition, lateral)) * scale;
    localUV += float2(WaterHash(anchor + 7.3), WaterHash(anchor + 19.1));
    float cycle = max(_FlowCycleSeconds, 0.5);
    float2 velocityUV = float2(speed * scale * cycle * 0.75, 0.0);
    float time = _Time.y / cycle + WaterHash(anchor + 31.7);
    float3 firstUV = RiverFlowUV(localUV, velocityUV, time, 0.0);
    float3 secondUV = RiverFlowUV(localUV, velocityUV, time, 0.5);
    // 显式世界梯度不包含锚点切换和像素量化的跳变，避免格边误选 Mipmap。
    float2 uvDx = float2(dot(positionDx, axis) * 0.75, dot(positionDx, lateral)) * scale;
    float2 uvDy = float2(dot(positionDy, axis) * 0.75, dot(positionDy, lateral)) * scale;
    float4 first = SAMPLE_TEXTURE2D_GRAD(_RiverPatternTex, sampler_RiverPatternTex, firstUV.xy, uvDx, uvDy);
    float4 second = SAMPLE_TEXTURE2D_GRAD(_RiverPatternTex, sampler_RiverPatternTex, secondUV.xy, uvDx, uvDy);
    // 覆盖率在各源纹理上处理，交接只混合透明度，不让泡沫在格心被二次阈值吞掉。
    float coverage = saturate(_RiverFoamCoverage);
    float lower = lerp(0.45, 0.12, coverage);
    float upper = lerp(0.75, 0.32, coverage);
    first.w = smoothstep(lower, upper, first.w) * step(0.0001, coverage);
    second.w = smoothstep(lower, upper, second.w) * step(0.0001, coverage);
    float4 pattern = first * firstUV.z + second * secondUV.z;
    float2 slope = pattern.rg * 2.0 - 1.0;
    pattern.xy = axis * slope.x * 0.75 + lateral * slope.y;
    return pattern;
}

// 相邻水格共用同一锚点和角速度，四块方向纹理平滑交接，不对逐像素噪声描等值线。
float4 SampleRiverSurfacePattern(float2 positionWS, float4 flowX, float4 flowY, WaterMotionData motion,
    float2 positionDx, float2 positionDy)
{
    float2 anchor = floor(positionWS);
    float2 cellUV = frac(positionWS);
    float2 blend = cellUV * cellUV * (3.0 - 2.0 * cellUV);
    float2 samplePosition = positionWS;
    #if defined(FLATWORLD_WATER_STYLIZED)
        float density = max(_PixelDensity, 1.0);
        samplePosition = floor(positionWS * density + 0.5) / density;
    #endif
    float speedScale = max(_FlowSpeed, 0.0);
    float4 bottomLeft = SampleRiverCorner(samplePosition, anchor,
        float2(flowX.x, flowY.x) * speedScale, motion.waveDirection, positionDx, positionDy);
    float4 bottomRight = SampleRiverCorner(samplePosition, anchor + float2(1.0, 0.0),
        float2(flowX.y, flowY.y) * speedScale, motion.waveDirection, positionDx, positionDy);
    float4 topLeft = SampleRiverCorner(samplePosition, anchor + float2(0.0, 1.0),
        float2(flowX.z, flowY.z) * speedScale, motion.waveDirection, positionDx, positionDy);
    float4 topRight = SampleRiverCorner(samplePosition, anchor + float2(1.0, 1.0),
        float2(flowX.w, flowY.w) * speedScale, motion.waveDirection, positionDx, positionDy);
    float4 pattern = lerp(lerp(bottomLeft, bottomRight, blend.x),
        lerp(topLeft, topRight, blend.x), blend.y);
    // Scrawk 的法线能量补偿只作用于斜率，不放大亮度和泡沫覆盖率。
    float2 energy = sqrt(blend * blend + (1.0 - blend) * (1.0 - blend));
    pattern.xy /= max(energy.x * energy.y, 0.25);
    return pattern;
}

// 河流用柔和法线、断续碎光与稀疏实心泡沫，海洋继续调用原效果。
WaterSurfaceData CalculateRiverSurface(float2 positionWS, float2 screenUV, half depth,
    float4 flowX, float4 flowY, WaterMotionData motion, float2 positionDx, float2 positionDy)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float4 pattern = SampleRiverSurfacePattern(positionWS, flowX, flowY, motion, positionDx, positionDy);
    // 独立小波保留波浪参数，幅度有界，不再把水纹弯成整片闭合轮廓。
    float phase = dot(positionWS, motion.waveDirection) * max(_SwellScale, 0.05)
        - motion.waveTime * 0.85;
    float finePhase = dot(positionWS, motion.waveDirection) * max(_RippleScale, 0.25)
        - motion.waveTime * 1.25;
    float gentleWave = sin(phase) * 0.07 + sin(finePhase) * 0.025;
    float2 slope = pattern.xy * lerp(0.45, 1.0, motion.currentStrength);
    slope += motion.waveDirection * gentleWave * (1.0 + min(max(_WaveDistortion, 0.0), 4.0) * 0.08);
    float3 normalWS = normalize(float3(-slope * max(_NormalStrength, 0.0), 1.0));
    float3 sunDirection = _SunDirection.xyz
        * rsqrt(max(dot(_SunDirection.xyz, _SunDirection.xyz), 0.0001));
    float3 halfDirection = sunDirection + float3(0.0, 0.0, 1.0);
    halfDirection *= rsqrt(max(dot(halfDirection, halfDirection), 0.0001));
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
        * _RippleStrength * _RippleColor.a * lerp(0.35, 0.8, motion.currentStrength);
    surface.rippleShadow = (1.0 - pattern.z) * _RippleShadowStrength * 0.3;
    // 浅水只保留低对比亮片，取消噪声中值的闭环焦散线。
    surface.caustic = pattern.z * pattern.z * pow(surface.depthBlend, 2.0)
        * _CausticStrength * _CausticColor.a * 0.18;
    surface.whitecap = pattern.w * _WhitecapStrength * _FoamColor.a
        * smoothstep(0.05, 0.85, motion.currentStrength);
    surface.moonReflection = ComputeMoonReflection(screenUV, gentleWave,
        pattern.z, pattern.w, normalWS.x, normalWS.y, motion.waveTime);
    return surface;
}

#endif
