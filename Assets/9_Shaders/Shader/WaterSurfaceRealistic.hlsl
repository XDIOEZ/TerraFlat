// 写实水面：宽缓涌浪、断续波光和低对比的天空反射。
#ifndef FLATWORLD_WATERSURFACEREALISTIC_HLSL
#define FLATWORLD_WATERSURFACEREALISTIC_HLSL

// 同一次正余弦求波高、斜率与亮纹，缩远时滤掉不足一像素的细波。
float AccumulateWaterWave(
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
    return waveSin * visibility;
}

// 四组错向波共用解析法线，水流只平移断续遮罩，不把整片水面扭成网纹。
WaterSurfaceData CalculateWaterSurface(
    float2 positionWS, float2 screenUV, half liquidDepth, WaterMotionData motion)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float2 direction = motion.waveDirection;
    float2 lateral = float2(-direction.y, direction.x);
    float time = motion.waveTime;
    float2 waterPosition = positionWS - motion.tideOffset;
    float macroA = SampleAdvectedWaterNoise(positionWS, motion.velocity, 0.8,
        float2(3.1, 8.2));
    float macroB = SampleAdvectedWaterNoise(positionWS, motion.velocity, 0.35,
        float2(17.3, 9.2));

    float height = 0.0;
    float2 slope = float2(0.0, 0.0);
    float swellScale = max(_SwellScale, 0.05);
    float primaryWave = AccumulateWaterWave(waterPosition, direction,
        swellScale, 0.24, time, 0.4, height, slope);
    float crossWave = AccumulateWaterWave(waterPosition, direction * 0.8 + lateral * 0.6,
        swellScale * 1.71, 0.14, time, 2.1, height, slope);
    AccumulateWaterWave(waterPosition, direction * 0.6 - lateral * 0.8,
        max(_DetailScale, 0.5) * 0.65, 0.075, time, 4.6, height, slope);
    float fineWave = AccumulateWaterWave(waterPosition, direction * 0.96 + lateral * 0.28,
        max(_RippleScale, 0.25), 0.055, time, 1.3, height, slope);
    height *= motion.waveHeight;
    slope *= motion.waveHeight * (1.0 + _WaveDistortion * 0.08);
    float3 normalWS = normalize(float3(-slope * _NormalStrength, 1.0));

    surface.liquidDepth = saturate(liquidDepth);
    surface.depthBlend = 1.0h - smoothstep(0.0h, 1.0h, surface.liquidDepth);

    float3 sunDirection = normalize(_SunDirection.xyz);
    float3 halfDirection = normalize(sunDirection + float3(0.0, 0.0, 1.001));
    float3 mirrorDirection = normalize(_ReflectionDirection.xyz);
    float mirrorAlignment = saturate(dot(reflect(float3(0.0, 0.0, -1.0), normalWS), mirrorDirection));
    float skyLobe = pow(mirrorAlignment, lerp(3.0, 14.0, _ReflectionSmoothness));
    surface.reflection = skyLobe * _ReflectionStrength * _ReflectionColor.a * 0.12;

    // 短亮纹只在部分波峰出现，细波和镜面高光不再各画一层密集亮线。
    float segments = smoothstep(0.5, 0.72, macroA * 0.7 + macroB * 0.3);
    float crestSignal = fineWave * 0.425 + crossWave * 0.075 + 0.5;
    float crestWidth = max(_RippleWidth, 0.04);
    float aa = max(fwidth(crestSignal), 0.015);
    float crest = smoothstep(1.0 - crestWidth - aa, 1.0 - aa, crestSignal);
    float lightPatch = crest * segments;
    surface.ripple = lightPatch * _RippleStrength * _RippleColor.a
        * lerp(0.35, 0.85, saturate(motion.waveHeight));
    surface.rippleShadow = saturate(-primaryWave) * (1.0 - segments * 0.5)
        * _RippleShadowStrength * 0.22;
    float3 normalDx = ddx(normalWS);
    float3 normalDy = ddy(normalWS);
    float normalVariance = dot(normalDx, normalDx) + dot(normalDy, normalDy);
    float specularPower = _SpecularPower / (1.0 + normalVariance * _SpecularPower);
    surface.specular = pow(saturate(dot(normalWS, halfDirection)), specularPower)
        * lightPatch * _SpecularStrength * _SpecularColor.a;

    // 浅水只露出柔和亮片，白沫随真实风浪增强，静水不铺满白线。
    surface.caustic = lightPatch * pow(surface.depthBlend, 3.0)
        * _CausticStrength * _CausticColor.a * 0.12 * (1.0 - motion.ocean);
    surface.whitecap = lightPatch * smoothstep(0.035, 0.16, dot(slope, slope))
        * _WhitecapStrength * _FoamColor.a * motion.foamActivity;
    surface.moonReflection = ComputeMoonReflection(screenUV, height,
        macroA, macroB, primaryWave, crossWave, time);
    return surface;
}

// 水深决定主色，波光只轻提亮原水色，保留清楚的水底与岸边层次。
half3 ApplyWaterSurface(half3 sourceColor, WaterSurfaceData surface)
{
    half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, surface.depthBlend);
    sourceColor = lerp(sourceColor, waterTint, lerp(_SurfaceTint, 1.0h, surface.liquidDepth));
    sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
    sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
    sourceColor += (_RippleColor.rgb * surface.ripple + _CausticColor.rgb * surface.caustic
        + _SpecularColor.rgb * surface.specular) * (1.0h - sourceColor);
    return saturate(lerp(sourceColor, _FoamColor.rgb, surface.whitecap));
}

half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS, WaterMotionData motion)
{
    return ApplyFlowingWaterShore(sourceColor, recess, positionWS, motion);
}

#endif
