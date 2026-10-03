// 岩浆共用低成本世界空间流动、高光与冷却壳计算。
#ifndef FLATWORLD_LAVASURFACECOMMON_HLSL
#define FLATWORLD_LAVASURFACECOMMON_HLSL

struct LavaSurfaceData
{
    half3 albedo;
    half3 emission;
    half alpha;
};

float2 ResolveLavaFlowAxis(float2 direction)
{
    return direction * rsqrt(max(dot(direction, direction), 0.000001));
}

LavaSurfaceData CalculateLavaSurface(
    float2 positionWS,
    half liquidDepth,
    half shoreRecess,
    half textureAlpha)
{
    LavaSurfaceData surface = (LavaSurfaceData)0;
    half depth = saturate(liquidDepth);
    float scale = max(_LavaScale, 0.001);
    float detailScale = max(_LavaDetailScale, 1.0);
    float2 axis = ResolveLavaFlowAxis(_LavaFlowDirection.xy);
    float2 crossAxis = float2(-axis.y, axis.x);
    float flowTime = _Time.y * _LavaFlowSpeed;
    float2 basePosition = positionWS * scale;

    float detail = WaterNoise(
        basePosition * detailScale
        + axis * flowTime * 1.37
        + crossAxis * 7.13);
    float warp = (detail - 0.5) * _LavaDistortion;
    float macro = WaterNoise(
        basePosition
        - axis * flowTime
        + crossAxis * warp);
    float breakup = WaterNoise(
        basePosition * 0.57
        + axis * flowTime * 0.31
        - crossAxis * flowTime * 0.19
        + 19.37);

    float field = saturate(macro * 0.68 + detail * 0.24 + breakup * 0.08);
    float ridge = saturate(1.0 - abs(field * 2.0 - 1.0) + (detail - 0.5) * 0.16);
    float hotSoftness = max(_LavaHotSoftness, 0.001);
    float coreSoftness = max(_LavaCoreSoftness, 0.001);
    half molten = smoothstep(
        _LavaHotThreshold - hotSoftness,
        _LavaHotThreshold + hotSoftness,
        ridge);
    half core = smoothstep(
        _LavaCoreThreshold - coreSoftness,
        _LavaCoreThreshold + coreSoftness,
        ridge);

    half shallow = 1.0h - depth;
    half crust = saturate(
        (1.0h - molten) * _LavaCrustStrength
        + shoreRecess * _LavaShoreCrust
        + shallow * _LavaShallowCrust);
    molten *= 1.0h - crust * 0.62h;
    core *= 1.0h - crust * 0.84h;

    half3 color = lerp(_LavaCrustColor.rgb, _LavaMoltenColor.rgb, molten);
    color = lerp(color, _LavaHotColor.rgb, core);
    color *= lerp(0.88h, 1.06h, (half)breakup);

    half pulse = 1.0h + sin(_Time.y * _LavaPulseSpeed + breakup * 6.2831853)
        * _LavaPulseStrength;
    half shoreEmission = 1.0h - shoreRecess * 0.42h;
    half3 emission = (
        _LavaMoltenColor.rgb * molten * 0.62h
        + _LavaHotColor.rgb * core * 1.28h)
        * _LavaEmissionStrength
        * pulse
        * shoreEmission;

    surface.albedo = color;
    surface.emission = emission;
    surface.alpha = textureAlpha * lerp(_LavaShallowAlpha, _LavaDeepAlpha, depth);
    return surface;
}

#endif
