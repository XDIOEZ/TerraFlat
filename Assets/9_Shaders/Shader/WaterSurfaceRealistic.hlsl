// 写实水面：连续色散波、解析法线与柔和微表面高光。
#ifndef FLATWORLD_WATERSURFACEREALISTIC_HLSL
#define FLATWORLD_WATERSURFACEREALISTIC_HLSL

/// <summary>累加波高及其解析斜率，短波按像素覆盖范围衰减，避免缩远后的闪烁。</summary>
void AccumulateWaterWave(
    float2 position, float2 direction, float frequency, float steepness,
    float time, float phaseOffset, inout float height, inout float2 slope)
{
    float phase = dot(position, direction) * frequency
        - time * sqrt(9.81 * frequency) + phaseOffset;
    float footprint = fwidth(dot(position, direction)) * frequency;
    float visibility = 1.0 - smoothstep(0.8, 2.8, footprint);
    float waveSin;
    float waveCos;
    sincos(phase, waveSin, waveCos);
    height += waveSin * (steepness / frequency) * visibility;
    slope += direction * waveCos * steepness * visibility;
}

/// <summary>用错相正弦波生成连续波光，同时返回解析斜率供法线复用。</summary>
float2 SampleWaterShimmerWave(
    float2 position,
    float2 direction,
    float frequency,
    float time,
    float speed,
    float phaseOffset)
{
    float phase = dot(position, direction) * frequency + time * speed + phaseOffset;
    float footprint = fwidth(phase);
    float visibility = 1.0 - smoothstep(0.9, 2.8, footprint);
    float waveSin;
    float waveCos;
    sincos(phase, waveSin, waveCos);
    return float2((waveSin * 0.5 + 0.5) * visibility, waveCos * visibility);
}

/// <summary>六组色散波与细波法线共同驱动反射，潮流平移和风浪传播分别跟随游戏时间。</summary>
WaterSurfaceData CalculateWaterSurface(
    float2 positionWS,
    float2 screenUV,
    half liquidDepth,
    float2 direction)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float2 lateral = float2(-direction.y, direction.x);
    float waveSpeed = max(_WaveSpeed, 0.0);
    float time = _OceanWaveTime * waveSpeed;
    float tide = ResolveTideFlowPhase(waveSpeed);
    float2 waterPosition = positionWS - direction * tide * 0.045;
    float2 drift = direction * time * 0.08;
    float macroA = WaterNoise(waterPosition * 0.075 - drift * 0.1);
    float macroB = WaterNoise(waterPosition * 0.13 + drift * 0.07 + float2(17.31, 9.17));
    float2 warpedPosition = waterPosition
        + (direction * (macroA - 0.5) + lateral * (macroB - 0.5)) * _WaveDistortion;

    // 波长不同，传播速度也不同；固定方向叠加，不让整片水面旋转或在潮汐换向时停住。
    float height = 0.0;
    float2 slope = float2(0.0, 0.0);
    float swellScale = max(_SwellScale, 0.05);
    float detailScale = max(_DetailScale, 0.5);
    AccumulateWaterWave(warpedPosition, direction,
        swellScale, 0.34, time, 0.4, height, slope);
    AccumulateWaterWave(warpedPosition, direction * 0.8 + lateral * 0.6,
        swellScale * 1.71, 0.22, time, 2.1, height, slope);
    AccumulateWaterWave(warpedPosition, direction * 0.6 - lateral * 0.8,
        swellScale * 2.37, 0.17, time, 4.6, height, slope);
    AccumulateWaterWave(warpedPosition, direction * 0.96 + lateral * 0.28,
        detailScale, 0.19, time, 1.3, height, slope);
    AccumulateWaterWave(warpedPosition, direction * 0.28 - lateral * 0.96,
        detailScale * 1.73, 0.13, time, 3.8, height, slope);
    AccumulateWaterWave(warpedPosition, direction * 0.8 - lateral * 0.6,
        detailScale * 2.61, 0.09, time, 5.2, height, slope);

    // 参考 2D 水面波光的做法：多组不同方向、速度的正弦波错相叠加，形成连续碎光而非随机噪点。
    float rippleScale = max(_RippleScale, 0.25);
    float2 shimmerPosition = waterPosition * rippleScale - drift * 0.45;
    float2 shimmerDirectionA = direction * 0.86 + lateral * 0.51;
    shimmerDirectionA *= rsqrt(max(dot(shimmerDirectionA, shimmerDirectionA), 0.001));
    float2 shimmerDirectionB = direction * 0.34 - lateral * 0.94;
    shimmerDirectionB *= rsqrt(max(dot(shimmerDirectionB, shimmerDirectionB), 0.001));
    float2 shimmerDirectionC = direction * 0.62 + lateral * 0.78;
    shimmerDirectionC *= rsqrt(max(dot(shimmerDirectionC, shimmerDirectionC), 0.001));
    float2 shimmerA = SampleWaterShimmerWave(
        shimmerPosition, shimmerDirectionA, 0.92, time, -1.18, 0.4);
    float2 shimmerB = SampleWaterShimmerWave(
        shimmerPosition, shimmerDirectionB, 1.37, time, 0.96, 2.1);
    float2 shimmerC = SampleWaterShimmerWave(
        shimmerPosition, shimmerDirectionC, 2.05, time, -0.72, 4.7);
    float2 fineSlope =
        shimmerDirectionA * shimmerA.y * 0.52 +
        shimmerDirectionB * shimmerB.y * 0.31 +
        shimmerDirectionC * shimmerC.y * 0.17;
    slope += fineSlope * 0.12 * rippleScale;
    height *= _OceanWaveFactors.y;
    slope *= _OceanWaveFactors.y;
    float3 normalWS = normalize(float3(-slope * _NormalStrength, 1.0));

    // 水面水深已离散为十档；使用等距颜色权重，避免深水段被指数吸收压缩后相邻档位难以分辨。
    surface.liquidDepth = saturate(liquidDepth);
    surface.depthBlend = 1.0h - surface.liquidDepth;

    float3 sunDirection = normalize(_SunDirection.xyz);
    float3 viewDirection = float3(0.0, 0.0, 1.0);
    float3 halfDirection = normalize(sunDirection + viewDirection);
    float ndv = saturate(normalWS.z);
    float ndl = saturate(dot(normalWS, sunDirection));
    float ndh = saturate(dot(normalWS, halfDirection));
    float vdh = saturate(dot(viewDirection, halfDirection));
    float fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);

    // 天空只提供柔和反射；太阳使用 GGX 微表面高光，波光由法线自然切碎。
    float3 reflectedView = reflect(-viewDirection, normalWS);
    float mirrorAlignment = saturate(dot(reflectedView, normalize(_ReflectionDirection.xyz)));
    float skyLobe = pow(mirrorAlignment, lerp(4.0, 18.0, _ReflectionSmoothness));
    surface.reflection = saturate(_ReflectionStrength) * (fresnel + skyLobe * 0.075)
        * _ReflectionColor.a;

    float roughness = lerp(0.38, 0.16, saturate(_ReflectionSmoothness))
        * pow(48.0 / max(_SpecularPower, 4.0), 0.25);
    float3 normalDx = ddx(normalWS);
    float3 normalDy = ddy(normalWS);
    float normalVariance = dot(normalDx, normalDx) + dot(normalDy, normalDy);
    float alphaSquared = max(pow(roughness, 4.0) + normalVariance * 0.25, 0.0025);
    float denominator = ndh * ndh * (alphaSquared - 1.0) + 1.0;
    float distribution = alphaSquared / max(3.14159265 * denominator * denominator, 0.00001);
    float geometryK = (roughness + 1.0) * (roughness + 1.0) * 0.125;
    float geometry = ndl / max(ndl * (1.0 - geometryK) + geometryK, 0.001);
    float sunFresnel = 0.02 + 0.98 * pow(1.0 - vdh, 5.0);
    float sunRadiance = distribution * geometry * sunFresnel / max(ndv, 0.001);
    // 平滑压缩强高光，避免硬截断把连续波光变成等亮色块。
    surface.specular = sunRadiance / (1.0 + sunRadiance)
        * saturate(_SpecularStrength) * _SpecularColor.a;

    // 大浪负责体积起伏，错相细浪只负责连续波光；二者共用同一水面运动时钟。
    float crest = smoothstep(0.08, 0.8 - saturate(_RippleWidth) * 0.6, height);
    float backLight = saturate(dot(-normalWS.xy, sunDirection.xy) + 0.18);
    float glintA = smoothstep(0.76, 0.96, shimmerA.x)
        * smoothstep(0.24, 0.82, shimmerB.x);
    float glintB = smoothstep(0.8, 0.975, shimmerB.x)
        * smoothstep(0.28, 0.86, shimmerC.x);
    float glintBreakup = saturate(
        0.72 + (macroA - 0.5) * 0.46 + (macroB - 0.5) * 0.34);
    float waveGlint = saturate(max(glintA, glintB * 0.72) * glintBreakup);
    surface.ripple = saturate(crest * backLight * 0.42 + waveGlint)
        * saturate(_RippleStrength) * _RippleColor.a;
    surface.rippleShadow = (1.0 - ndl) * saturate(_RippleShadowStrength) * 0.35;

    float detailA = shimmerA.x;
    float detailB = shimmerB.x;
    float detailC = shimmerC.x;
    float causticFocus = pow(saturate(1.0 - abs(detailA - detailB) * 3.0), 12.0);
    surface.caustic = causticFocus * pow(surface.depthBlend, 4.0)
        * saturate(_CausticStrength) * _CausticColor.a;

    // 白沫只在足够陡的波峰上零星出现，避免每道细浪都变成白线。
    float steepCrest = crest * smoothstep(0.12, 0.46, dot(slope, slope));
    float foamBreakup = smoothstep(
        0.56,
        0.82,
        detailA * 0.46 + detailB * 0.34 + detailC * 0.2);
    surface.whitecap = steepCrest * foamBreakup
        * saturate(_WhitecapStrength) * _FoamColor.a * _OceanWaveFactors.z;
    surface.moonReflection = ComputeMoonReflection(
        screenUV,
        height,
        macroA,
        macroB,
        detailA * 2.0 - 1.0,
        detailB * 2.0 - 1.0,
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
        surface.liquidDepth);
    sourceColor = lerp(sourceColor, waterTint, tintStrength);
    sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
    sourceColor += _CausticColor.rgb * surface.caustic;
    sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
    // 波光采用提亮混合，不把原水色直接染成一条条固定颜色的线。
    sourceColor += _RippleColor.rgb * surface.ripple * saturate(1.0h - sourceColor);
    sourceColor += _SpecularColor.rgb * surface.specular;
    sourceColor = lerp(sourceColor, _FoamColor.rgb, surface.whitecap);
    return saturate(sourceColor);
}

/// <summary>岸边只留断续的薄泡沫，沿游戏时间起落，避免描出连续的方格亮边。</summary>
half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS, float2 direction)
{
    float tide = ResolveTideFlowPhase(_FoamSpeed);
    float time = _GlobalGameDay * 180.0 * _FoamSpeed;
    float2 foamPosition = positionWS - direction * tide * 0.04;
    float foamNoise = WaterNoise(foamPosition * 1.1 + float2(11.3, 27.1));
    float foamDetail = WaterNoise(foamPosition * 3.7 - direction * time * 0.12);
    float wash = 0.5 + 0.5 * sin(time + foamNoise * 4.0);
    float shoreBand = pow(saturate(recess * (1.0 - recess) * 4.0), 1.5);
    float foamCoverage = smoothstep(0.48, 0.82,
        foamNoise * 0.5 + foamDetail * 0.35 + wash * 0.25);
    float foam = shoreBand * foamCoverage * saturate(_ShoreFoamStrength) * _FoamColor.a;
    sourceColor = lerp(sourceColor, _ShoreColor.rgb,
        shoreBand * foamNoise * saturate(_ShoreStrength) * _ShoreColor.a);
    return lerp(sourceColor, _FoamColor.rgb, foam);
}

#endif
