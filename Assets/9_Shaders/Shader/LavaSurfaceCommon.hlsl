// 岩浆共用低成本世界空间流动、高光与冷却壳计算。
#ifndef FLATWORLD_LAVASURFACECOMMON_HLSL
#define FLATWORLD_LAVASURFACECOMMON_HLSL

TEXTURE2D(_LavaPatternTex);
SAMPLER(sampler_LavaPatternTex);

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
    float flowTime = _Time.y * _LavaFlowSpeed;
    float2 baseUV = positionWS * scale;

    // RGBA 打包纹理：R=主体，G=扭曲，B/A=两套交替高光，三次采样覆盖完整岩浆层次。
    half4 basePattern = SAMPLE_TEXTURE2D(_LavaPatternTex, sampler_LavaPatternTex, baseUV);
    half2 distortMap = half2(basePattern.g, 1.0h - basePattern.g);
    half2 distortion = (distortMap - 0.5h) * (2.0h * _LavaDistortion);
    float2 warpUV = baseUV - distortion + axis * flowTime;
    half4 flowPattern = SAMPLE_TEXTURE2D(_LavaPatternTex, sampler_LavaPatternTex, warpUV);
    half4 subPattern = SAMPLE_TEXTURE2D(
        _LavaPatternTex,
        sampler_LavaPatternTex,
        warpUV * detailScale + float2(0.173, 0.391));

    half ramp = flowPattern.r * flowPattern.r;
    half switchMask = saturate(0.5h + 0.5h * sin(_Time.y * max(_LavaPulseSpeed, 0.001)));
    half alternatingNoise = saturate(
        flowPattern.g * 0.5h
        + flowPattern.b * switchMask
        + flowPattern.a * (1.0h - switchMask));
    half highlightField = saturate(ramp * (0.65h + alternatingNoise));
    float hotSoftness = max(_LavaHotSoftness, 0.001);
    float coreSoftness = max(_LavaCoreSoftness, 0.001);
    half molten = smoothstep(
        _LavaHotThreshold - hotSoftness,
        _LavaHotThreshold + hotSoftness,
        highlightField);
    half core = smoothstep(
        _LavaCoreThreshold - coreSoftness,
        _LavaCoreThreshold + coreSoftness,
        highlightField);

    half shallow = 1.0h - depth;
    half subLava = 1.0h - smoothstep(
        _LavaHotThreshold - hotSoftness,
        _LavaHotThreshold + hotSoftness,
        subPattern.r);
    half cooling = saturate(
        subLava * _LavaCrustStrength
        + shoreRecess * _LavaShoreCrust
        + shallow * _LavaShallowCrust);

    half3 color = lerp(_LavaMoltenColor.rgb, _LavaCrustColor.rgb, cooling * 0.72h);
    color = lerp(color, _LavaHotColor.rgb, molten * 0.58h + core * 0.42h);
    color *= lerp(0.9h, 1.06h, flowPattern.g);

    half pulse = 1.0h + sin(
        _Time.y * _LavaPulseSpeed + flowPattern.a * 6.2831853h)
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
