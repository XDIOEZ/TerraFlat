// 四向波纹与碎光改编自 Daniel Shervheim 的 unity-stylized-water，BSD-3-Clause 许可见 ThirdPartyNotices。
#ifndef FLATWORLD_UNITY_STYLIZED_WATER_SURFACE_HLSL
#define FLATWORLD_UNITY_STYLIZED_WATER_SURFACE_HLSL

TEXTURE2D(_UnityWaterPatternTex);
SAMPLER(sampler_UnityWaterPatternTex);
TEXTURE2D(_UnityWaterDetailTex);
SAMPLER(sampler_UnityWaterDetailTex);
#if !defined(FLATWORLD_WATER_MATERIAL_CBUFFER_DEFINED)
float _UnityNormalScale;
float _UnityFoamScale;
float _UnitySparkleScale;
float _UnitySparkleStrength;
float _UnitySparkleExponent;
float _UnityDepthDensity;
half4 _UnitySparkleColor;
#endif

// RG 是线性法线 XY，B 是泡沫，A 是浅水亮度，不使用第三方演示贴图。
float3 DecodeUnityWaterNormal(float2 encoded)
{
    float2 normalXY = encoded * 2.0 - 1.0;
    return normalize(float3(normalXY, sqrt(max(1.0 - dot(normalXY, normalXY), 0.01))));
}

// 对应原 MotionFourWayChaos 的四组偏移和滚动方向，显式梯度保持世界采样连续。
float4 UnityMotionFourWayChaos(float2 positionWS, float scale, float time,
    float2 positionDx, float2 positionDy, bool normalField)
{
    float inverseScale = rcp(max(scale, 0.05));
    float2 uv = positionWS * inverseScale;
    float2 uvDx = positionDx * inverseScale;
    float2 uvDy = positionDy * inverseScale;
    float travel = frac(time * 0.70710678);
    float4 sampleA = SAMPLE_TEXTURE2D_GRAD(_UnityWaterPatternTex, sampler_UnityWaterPatternTex,
        uv + float2(travel, travel), uvDx, uvDy);
    float4 sampleB = SAMPLE_TEXTURE2D_GRAD(_UnityWaterPatternTex, sampler_UnityWaterPatternTex,
        uv + float2(0.418, 0.355) - float2(travel, travel), uvDx, uvDy);
    float4 sampleC = SAMPLE_TEXTURE2D_GRAD(_UnityWaterPatternTex, sampler_UnityWaterPatternTex,
        uv + float2(0.865, 0.148) + float2(-travel, travel), uvDx, uvDy);
    float4 sampleD = SAMPLE_TEXTURE2D_GRAD(_UnityWaterPatternTex, sampler_UnityWaterPatternTex,
        uv + float2(0.651, 0.752) + float2(travel, -travel), uvDx, uvDy);
    float4 result = (sampleA + sampleB + sampleC + sampleD) * 0.25;
    if (normalField)
    {
        float3 normalTS = normalize(DecodeUnityWaterNormal(sampleA.rg) + DecodeUnityWaterNormal(sampleB.rg)
            + DecodeUnityWaterNormal(sampleC.rg) + DecodeUnityWaterNormal(sampleD.rg));
        result.xy = normalTS.xy;
    }
    return result;
}

// 对应原 MotionFourWaySparkle 的不同密度和 XY 混合，细节只改变碎光法线。
float3 UnityMotionFourWaySparkle(float2 positionWS, float4 coordinateScale, float time,
    float2 positionDx, float2 positionDy)
{
    float inverseScale = rcp(max(_UnitySparkleScale, 0.05));
    float2 uv = positionWS * inverseScale;
    float2 uvDx = positionDx * inverseScale;
    float2 uvDy = positionDy * inverseScale;
    float travel = frac(time * 0.70710678);
    float3 sampleA = DecodeUnityWaterNormal(SAMPLE_TEXTURE2D_GRAD(_UnityWaterDetailTex, sampler_UnityWaterDetailTex,
        uv * coordinateScale.x + float2(travel, travel), uvDx * coordinateScale.x, uvDy * coordinateScale.x).rg);
    float3 sampleB = DecodeUnityWaterNormal(SAMPLE_TEXTURE2D_GRAD(_UnityWaterDetailTex, sampler_UnityWaterDetailTex,
        uv * coordinateScale.y - float2(travel, travel), uvDx * coordinateScale.y, uvDy * coordinateScale.y).rg);
    float3 sampleC = DecodeUnityWaterNormal(SAMPLE_TEXTURE2D_GRAD(_UnityWaterDetailTex, sampler_UnityWaterDetailTex,
        uv * coordinateScale.z + float2(-travel, travel), uvDx * coordinateScale.z, uvDy * coordinateScale.z).rg);
    float3 sampleD = DecodeUnityWaterNormal(SAMPLE_TEXTURE2D_GRAD(_UnityWaterDetailTex, sampler_UnityWaterDetailTex,
        uv * coordinateScale.w + float2(travel, -travel), uvDx * coordinateScale.w, uvDy * coordinateScale.w).rg);
    float3 normalA = float3(sampleA.x, sampleB.y, 1.0);
    float3 normalB = float3(sampleC.x, sampleD.y, 1.0);
    return normalize(float3((normalA + normalB).xy, (normalA * normalB).z));
}

WaterSurfaceData CalculateWaterSurface(float2 positionWS, float2 screenUV, half liquidDepth, WaterMotionData motion)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    float2 positionDx = ddx(positionWS);
    float2 positionDy = ddy(positionWS);
    float2 firstPosition;
    float2 secondPosition;
    float firstWeight;
    // 实际水流平移与四向扰动分开，湖泊保持零漂移，河流保留真实速度大小。
    ResolveWaterFlowPhases(positionWS, motion.velocity, firstPosition, secondPosition, firstWeight);
    float waveTime = motion.waveTime * 0.065;
    float4 normalA = UnityMotionFourWayChaos(firstPosition - motion.tideOffset, _UnityNormalScale,
        waveTime, positionDx, positionDy, true);
    float4 normalB = UnityMotionFourWayChaos(secondPosition - motion.tideOffset, _UnityNormalScale,
        waveTime, positionDx, positionDy, true);
    float4 normalPattern = lerp(normalB, normalA, firstWeight);
    float wavePhase = dot(positionWS, motion.waveDirection) * max(_SwellScale, 0.05) - motion.waveTime;
    float waveHeight = sin(wavePhase) * 0.16 * motion.waveHeight;
    float2 slope = normalPattern.xy * max(_NormalStrength, 0.0) * motion.waveHeight;
    slope += motion.waveDirection * cos(wavePhase) * _NormalStrength * 0.055;
    float3 normalWS = normalize(float3(-slope, 1.0));

    surface.liquidDepth = saturate(liquidDepth);
    surface.depthBlend = exp(-max(_UnityDepthDensity, 0.01) * surface.liquidDepth);
    float3 mirrorDirection = _ReflectionDirection.xyz
        * rsqrt(max(dot(_ReflectionDirection.xyz, _ReflectionDirection.xyz), 0.0001));
    float3 sunDirection = _SunDirection.xyz
        * rsqrt(max(dot(_SunDirection.xyz, _SunDirection.xyz), 0.0001));
    float3 reflectedView = reflect(float3(0.0, 0.0, -1.0), normalWS);
    float fresnel = pow(saturate(1.0 - normalWS.z), 5.0);
    surface.reflection = (fresnel + pow(saturate(dot(reflectedView, mirrorDirection)),
        lerp(3.0, 24.0, saturate(_ReflectionSmoothness))) * 0.22) * _ReflectionStrength * _ReflectionColor.a;
    surface.specular = pow(saturate(dot(reflectedView, sunDirection)), max(_SpecularPower, 4.0) * 4.0)
        * _SpecularStrength * _SpecularColor.a;
    surface.ripple = saturate(normalPattern.w * 0.4 + waveHeight * 0.45)
        * _RippleStrength * _RippleColor.a;
    surface.rippleShadow = saturate(-waveHeight) * _RippleShadowStrength;
    surface.caustic = normalPattern.w * normalPattern.w * surface.depthBlend
        * _CausticStrength * _CausticColor.a;

    float sparkleTime = motion.waveTime * 0.09;
    float3 sparkleA = lerp(UnityMotionFourWaySparkle(secondPosition, float4(1.0, 2.0, 3.0, 4.0),
        sparkleTime, positionDx, positionDy), UnityMotionFourWaySparkle(firstPosition, float4(1.0, 2.0, 3.0, 4.0),
        sparkleTime, positionDx, positionDy), firstWeight);
    float3 sparkleB = lerp(UnityMotionFourWaySparkle(secondPosition, float4(1.0, 0.5, 2.5, 2.0),
        sparkleTime, positionDx, positionDy), UnityMotionFourWaySparkle(firstPosition, float4(1.0, 0.5, 2.5, 2.0),
        sparkleTime, positionDx, positionDy), firstWeight);
    sparkleA = normalize(sparkleA);
    sparkleB = normalize(sparkleB);
    float sparkleMask = pow(saturate(dot(sparkleA, sparkleB)), max(_UnitySparkleExponent, 4.0))
        * saturate(3.0 * sqrt(saturate(sparkleA.x * sparkleB.x)));
    // 屏幕覆盖不足时淡出碎光，避免硬 ceil 在远景产生像素闪烁。
    float sparkleFootprint = max(length(positionDx), length(positionDy)) / max(_UnitySparkleScale, 0.05);
    surface.sparkle = sparkleMask * _UnitySparkleStrength * _UnitySparkleColor.a
        * (1.0 - smoothstep(0.2, 0.65, sparkleFootprint));

    float2 foamShift = normalPattern.xy * min(max(_WaveDistortion, 0.0), 4.0) * 0.12;
    float foamTime = _Time.y * max(_FoamSpeed, 0.0) * 0.08;
    float foamA = UnityMotionFourWayChaos(firstPosition + foamShift, _UnityFoamScale,
        foamTime, positionDx, positionDy, false).b;
    float foamB = UnityMotionFourWayChaos(secondPosition + foamShift, _UnityFoamScale,
        foamTime, positionDx, positionDy, false).b;
    float foamField = lerp(foamB, foamA, firstWeight);
    float foamWidth = clamp(_RippleWidth, 0.02, 0.45);
    float foamCoverage = smoothstep(0.55 - foamWidth, 0.72, foamField);
    surface.whitecap = foamCoverage * _WhitecapStrength * _FoamColor.a
        * lerp(0.35, 1.0, saturate(motion.foamActivity));
    surface.moonReflection = ComputeMoonReflection(screenUV, waveHeight,
        normalPattern.z, normalPattern.w, normalWS.x, normalWS.y, motion.waveTime);
    return surface;
}

half3 ApplyWaterSurface(half3 sourceColor, WaterSurfaceData surface)
{
    half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, surface.depthBlend);
    sourceColor = lerp(sourceColor, waterTint, saturate(_SurfaceTint));
    sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
    sourceColor += _CausticColor.rgb * surface.caustic * (1.0h - sourceColor);
    sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
    sourceColor += _RippleColor.rgb * surface.ripple * (1.0h - sourceColor);
    sourceColor += _SpecularColor.rgb * surface.specular;
    sourceColor += _UnitySparkleColor.rgb * surface.sparkle;
    return saturate(lerp(sourceColor, _FoamColor.rgb, saturate(surface.whitecap)));
}

// 原 3D 光学岸深替换为现有真实岸线，破碎白沫沿水流平移。
half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS, WaterMotionData motion)
{
    float2 positionDx = ddx(positionWS);
    float2 positionDy = ddy(positionWS);
    UNITY_BRANCH
    if (recess > 0.0001h && (_ShoreStrength + _ShoreFoamStrength) > 0.0001h)
    {
        float2 firstPosition;
        float2 secondPosition;
        float firstWeight;
        ResolveWaterFlowPhases(positionWS, motion.velocity, firstPosition, secondPosition, firstWeight);
        float time = _Time.y * max(_FoamSpeed, 0.0) * 0.08;
        float foamA = UnityMotionFourWayChaos(firstPosition, _UnityFoamScale,
            time, positionDx, positionDy, false).b;
        float foamB = UnityMotionFourWayChaos(secondPosition, _UnityFoamScale,
            time, positionDx, positionDy, false).b;
        float foam = lerp(foamB, foamA, firstWeight);
        float edgeFoam = pow(saturate(recess), 0.65) * smoothstep(0.18, 0.5, foam + recess * 0.35);
        sourceColor = lerp(sourceColor, _ShoreColor.rgb, recess * _ShoreStrength * _ShoreColor.a);
        sourceColor = lerp(sourceColor, _FoamColor.rgb, saturate(edgeFoam * _ShoreFoamStrength * _FoamColor.a));
    }
    return sourceColor;
}

#endif
