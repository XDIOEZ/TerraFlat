// 风格化水面：保留独立浪脊、亮纹、焦散和较鲜明的泡沫。
#ifndef FLATWORLD_WATERSURFACESTYLIZED_HLSL
#define FLATWORLD_WATERSURFACESTYLIZED_HLSL

float _PixelDensity;

/// <summary>将世界坐标锁定到细像素格，保持像素画风并避免波纹随镜头抖动。</summary>
float2 QuantizeWaterPosition(float2 positionWS)
{
    float density = max(_PixelDensity, 1.0);
    return floor(positionWS * density + 0.5) / density;
}

/// <summary>叠加大涌浪和多方向细浪，并汇总海面各层光学信息。</summary>
WaterSurfaceData CalculateWaterSurface(
    float2 positionWS,
    float2 screenUV,
    half waterDepth)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float2 direction = ResolveWaterFlowAxis();
    float2 lateral = float2(-direction.y, direction.x);
    float2 pixelPosition = QuantizeWaterPosition(positionWS);
    float time = ResolveTideFlowPhase(_WaveSpeed);

    float2 drift = direction * time * 0.018
        - lateral * time * 0.006;
    float macroA = WaterNoise(pixelPosition * 0.065 + drift);
    float macroB = WaterNoise(
        pixelPosition * 0.11
        + float2(17.31, 9.17)
        + float2(-drift.y, drift.x));
    float2 warpedPosition = pixelPosition
        + direction * (macroA - 0.5) * _WaveDistortion
        + lateral * (macroB - 0.5) * _WaveDistortion * 1.25;

    float2 swellDirection = direction * 0.42 + lateral * 0.91;
    swellDirection *= rsqrt(max(dot(swellDirection, swellDirection), 0.001));
    float2 detailDirectionA = direction * 0.8 - lateral * 0.6;
    detailDirectionA *= rsqrt(max(dot(detailDirectionA, detailDirectionA), 0.001));
    float2 detailDirectionB = -direction * 0.18 + lateral * 0.98;
    detailDirectionB *= rsqrt(max(dot(detailDirectionB, detailDirectionB), 0.001));

    float swellPhaseA = dot(warpedPosition, direction) * _SwellScale + time * 0.55;
    float swellPhaseB = dot(warpedPosition, swellDirection) * _SwellScale * 1.72
        - time * 0.38
        + macroB * 1.4;
    float detailPhaseA = dot(warpedPosition, detailDirectionA) * _DetailScale
        + time * 1.25
        + macroA * 0.9;
    float detailPhaseB = dot(warpedPosition, detailDirectionB) * _DetailScale * 1.83
        - time * 1.55
        + macroB * 1.1;

    float swellA = sin(swellPhaseA);
    float swellB = sin(swellPhaseB);
    float detailA = sin(detailPhaseA);
    float detailB = sin(detailPhaseB);
    float height = swellA * 0.48 + swellB * 0.27 + detailA * 0.17 + detailB * 0.08;

    float2 gradient = direction * cos(swellPhaseA) * _SwellScale * 0.48;
    gradient += swellDirection * cos(swellPhaseB) * _SwellScale * 1.72 * 0.27;
    gradient += detailDirectionA * cos(detailPhaseA) * _DetailScale * 0.075;
    gradient += detailDirectionB * cos(detailPhaseB) * _DetailScale * 1.83 * 0.035;
    float3 normalWS = normalize(float3(
        -gradient.x * _NormalStrength,
        -gradient.y * _NormalStrength,
        1.0));

    // 权威水深决定整体明暗，噪声只在中间深度保留轻微的自然过渡。
    surface.waterDepth = smoothstep(0.0h, 1.0h, saturate(waterDepth));
    half depthVariation = (
        (macroA - 0.5) * 0.12
        + (macroB - 0.5) * 0.05
        + height * 0.025)
        * (surface.waterDepth * (1.0h - surface.waterDepth) * 4.0h);
    surface.depthBlend = saturate(1.0h - surface.waterDepth + depthVariation);

    // 中尺度浪脊与大涌浪共用扭曲坐标，再用低频噪声切成自然短段。
    float rippleWarp = WaterNoise(
        pixelPosition * 0.19
        + float2(-time * 0.045, time * 0.032)
        + float2(16.8, 7.4)) - 0.5;
    float ripplePhaseA = dot(warpedPosition, direction) * _RippleScale
        + time * 0.82
        + (macroB - 0.5) * 2.6
        + (macroA - 0.5) * 0.9
        + rippleWarp * 3.2;
    float ripplePhaseB = dot(warpedPosition, swellDirection) * _RippleScale * 1.43
        - time * 0.57
        + (macroA - 0.5) * 2.9
        + (macroB - 0.5) * 0.7
        - rippleWarp * 2.15;
    float rippleWaveA = sin(ripplePhaseA);
    float rippleWaveB = sin(ripplePhaseB);
    float crestExponent = 4.0 / max(_RippleWidth, 0.01);
    float crestA = pow(saturate(rippleWaveA * 0.5 + 0.5), crestExponent);
    float crestB = pow(saturate(rippleWaveB * 0.5 + 0.5), crestExponent * 1.12);
    float2 rippleLateralB = float2(-swellDirection.y, swellDirection.x);

    float segmentA = WaterNoise(
        float2(
            dot(pixelPosition, lateral) * 0.58 + time * 0.055,
            dot(pixelPosition, direction) * 0.13 - time * 0.018)
        + float2(5.7, 19.3));
    float segmentB = WaterNoise(
        float2(
            dot(pixelPosition, rippleLateralB) * 0.64 - time * 0.046,
            dot(pixelPosition, swellDirection) * 0.15 + time * 0.016)
        + float2(27.4, 3.8));
    float segmentDetailA = WaterNoise(
        float2(
            dot(pixelPosition, lateral) * 1.21 - time * 0.085,
            dot(pixelPosition, direction) * 0.22 + time * 0.021)
        + float2(34.1, 11.6));
    float segmentDetailB = WaterNoise(
        float2(
            dot(pixelPosition, rippleLateralB) * 1.34 + time * 0.074,
            dot(pixelPosition, swellDirection) * 0.25 - time * 0.018)
        + float2(8.9, 36.2));
    float segmentGateA = smoothstep(0.28, 0.5, segmentA * segmentDetailA);
    float segmentGateB = smoothstep(0.31, 0.53, segmentB * segmentDetailB);
    crestA *= segmentGateA;
    crestB *= segmentGateB * 0.28;
    surface.ripple = max(crestA, crestB)
        * saturate(_RippleStrength)
        * _RippleColor.a;

    // 浪脊后方的窄暗带强化起伏，不把整片水面压暗。
    float shadowA = pow(
        saturate(sin(ripplePhaseA - 0.3) * 0.5 + 0.5),
        crestExponent * 0.82)
        * segmentGateA;
    float shadowB = pow(
        saturate(sin(ripplePhaseB - 0.25) * 0.5 + 0.5),
        crestExponent * 0.9)
        * segmentGateB
        * 0.2;
    surface.rippleShadow = max(shadowA, shadowB)
        * (1.0 - max(crestA, crestB))
        * saturate(_RippleShadowStrength);

    // 以俯视相机入射方向反射虚拟环境方向，让镜面亮块真实跟随波面法线移动。
    float3 mirrorDirection = _ReflectionDirection.xyz;
    mirrorDirection *= rsqrt(max(dot(mirrorDirection, mirrorDirection), 0.001));
    float3 reflectedView = reflect(float3(0.0, 0.0, -1.0), normalWS);
    float mirrorAlignment = saturate(dot(reflectedView, mirrorDirection));
    float mirrorExponent = exp2(lerp(1.5, 6.0, saturate(_ReflectionSmoothness)));
    float mirrorLobe = pow(mirrorAlignment, mirrorExponent);
    float mirrorBreakup = saturate(
        0.64
        + (macroA - 0.5) * 0.5
        + (macroB - 0.5) * 0.32
        + height * 0.07);
    float coherentMirror = mirrorLobe
        * lerp(mirrorBreakup, 1.0, saturate(_ReflectionSmoothness));
    float fresnel = saturate((1.0 - normalWS.z) * 3.5);
    surface.reflection = saturate(coherentMirror + fresnel * fresnel * 0.22)
        * saturate(_ReflectionStrength)
        * _ReflectionColor.a;

    float3 sunDirection = _SunDirection.xyz;
    sunDirection *= rsqrt(max(dot(sunDirection, sunDirection), 0.001));
    float3 halfDirection = sunDirection + float3(0.0, 0.0, 1.0);
    halfDirection *= rsqrt(max(dot(halfDirection, halfDirection), 0.001));
    float2 sparkleDrift = float2(time * 0.11, -time * 0.08);
    float sparkleFine = WaterNoise(
        pixelPosition * 1.65
        + sparkleDrift
        + float2(7.23, 14.81));
    float sparkleBreakup = WaterNoise(
        pixelPosition * 3.15
        - sparkleDrift * 1.7
        + float2(21.47, 5.39));
    float sparkleGate = smoothstep(
        0.5,
        0.72,
        sparkleFine * sparkleBreakup + max(detailA, detailB) * 0.025);
    surface.specular = pow(
        saturate(dot(normalWS, halfDirection)),
        max(_SpecularPower, 1.0))
        * sparkleGate
        * saturate(_SpecularStrength)
        * _SpecularColor.a;

    float2 causticPosition = warpedPosition * (_DetailScale * 0.42);
    float2 causticDrift = direction * time * 0.09 + lateral * time * 0.035;
    float causticWarp = WaterNoise(
        causticPosition * 0.37 + causticDrift + float2(13.2, 6.7)) - 0.5;
    float causticFieldA = WaterNoise(
        causticPosition
        + direction * causticWarp * 1.45
        + causticDrift
        + float2(3.4, 18.6));
    float causticFieldB = WaterNoise(
        causticPosition * 1.67
        + lateral * causticWarp * 1.2
        - causticDrift * 1.35
        + float2(24.8, 2.9));
    float causticRidgeA = smoothstep(
        0.92,
        0.985,
        1.0 - abs(causticFieldA * 2.0 - 1.0));
    float causticRidgeB = smoothstep(
        0.94,
        0.992,
        1.0 - abs(causticFieldB * 2.0 - 1.0));
    float causticBreakup = WaterNoise(
        pixelPosition * 0.31
        + float2(-time * 0.018, time * 0.012)
        + float2(9.6, 32.1));
    float causticNetwork = max(causticRidgeA, causticRidgeB * 0.62)
        * smoothstep(0.3, 0.72, causticBreakup);
    surface.caustic = causticNetwork
        * pow(saturate(surface.depthBlend), 1.65)
        * saturate(_CausticStrength)
        * _CausticColor.a;

    float whitecapNoiseA = WaterNoise(
        pixelPosition * 0.47
        + float2(-time * 0.038, time * 0.026)
        + float2(31.7, 4.9));
    float whitecapNoiseB = WaterNoise(
        pixelPosition * 0.93
        + float2(time * 0.052, -time * 0.033)
        + float2(4.6, 26.3));
    float crestHeight = height + max(detailA, detailB) * 0.12;
    float foamBreakup = smoothstep(0.36, 0.66, whitecapNoiseA * whitecapNoiseB);
    float rippleFoam = max(crestA, crestB * 0.65)
        * lerp(0.28, 1.0, foamBreakup);
    float swellFoam = smoothstep(0.7, 0.92, crestHeight)
        * smoothstep(0.44, 0.68, whitecapNoiseA * whitecapNoiseB);
    surface.whitecap = saturate(max(rippleFoam, swellFoam * 0.72))
        * saturate(_WhitecapStrength)
        * _FoamColor.a;
    surface.moonReflection = ComputeMoonReflection(
        screenUV,
        height,
        macroA,
        macroB,
        detailA,
        detailB,
        time);
    return surface;
}

/// <summary>按深浅水、焦散、天空反光、太阳高光和白沫的层级合成海面。</summary>
half3 ApplyWaterSurface(half3 sourceColor, WaterSurfaceData surface)
{
    half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, surface.depthBlend);
    half tintStrength = lerp(
        saturate(_SurfaceTint),
        1.0h,
        surface.waterDepth);
    sourceColor = lerp(sourceColor, waterTint, tintStrength);
    sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
    sourceColor += _CausticColor.rgb * surface.caustic;
    sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
    sourceColor = lerp(sourceColor, _RippleColor.rgb, surface.ripple);
    sourceColor = lerp(sourceColor, _SpecularColor.rgb, surface.specular);
    sourceColor = lerp(sourceColor, _FoamColor.rgb, surface.whitecap);
    return saturate(sourceColor);
}

/// <summary>在岸线内侧叠加亮边与流动泡沫。</summary>
half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS)
{
    half shoreBand = saturate(recess * (1.0h - recess) * 4.0h);
    float foamTime = ResolveTideFlowPhase(_FoamSpeed);
    float2 flowDirection = ResolveWaterFlowAxis();
    float2 flowLateral = float2(-flowDirection.y, flowDirection.x);
    float foamNoise = WaterNoise(
        positionWS * 0.58
        + flowDirection * foamTime * 0.04
        - flowLateral * foamTime * 0.012
        + float2(11.3, 27.1));
    half foamPulse = 0.7h + 0.3h * sin(
        dot(positionWS, flowDirection) * 1.25
        + foamTime
        + foamNoise * 2.4);
    half foam = saturate(shoreBand * (0.45h + foamNoise * 0.75h) * foamPulse)
        * saturate(_ShoreFoamStrength)
        * _FoamColor.a;
    sourceColor = lerp(
        sourceColor,
        _ShoreColor.rgb,
        shoreBand * saturate(_ShoreStrength) * _ShoreColor.a);
    sourceColor = lerp(sourceColor, _FoamColor.rgb, foam);
    return sourceColor;
}

#endif
