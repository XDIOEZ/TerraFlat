// 两种水体风格共用水深、岸线、潮汐时钟及月光合成契约。
#ifndef FLATWORLD_WATERSURFACECOMMON_HLSL
#define FLATWORLD_WATERSURFACECOMMON_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
TEXTURE2D(_MaskTex);
SAMPLER(sampler_MaskTex);
TEXTURE2D(_WaterDepthTexture);
SAMPLER(sampler_WaterDepthTexture);

half4 _Color;
half4 _RendererColor;
half4 _DeepColor;
half4 _ShallowColor;
half4 _ReflectionColor;
half4 _SpecularColor;
half4 _CausticColor;
half4 _FoamColor;
half4 _RippleColor;
half4 _MoonReflectionColor;
half4 _ShoreColor;
float4 _FlowDirection;
float _TideCyclesPerDay;
float _GlobalGameDay;
float4 _ReflectionDirection;
float4 _SunDirection;
float4 _MoonReflectionPosition;
float4 _WaterDepthUvScaleOffset;
half _SurfaceTint;
float _SwellScale;
float _DetailScale;
float _WaveSpeed;
half _WaveDistortion;
half _NormalStrength;
half _RippleStrength;
float _RippleScale;
half _RippleWidth;
half _RippleShadowStrength;
half _ReflectionStrength;
half _ReflectionSmoothness;
half _SpecularStrength;
float _SpecularPower;
half _CausticStrength;
half _WhitecapStrength;
half _MoonReflectionStrength;
float _MoonDiscRadius;
float _MoonTrailLength;
float _MoonTrailWidth;
half _GlobalMoonlightIntensity;
half _GlobalMoonAppearance;
half _EdgeWidth;
half _CornerStrength;
half _ShoreStrength;
half _ShoreFoamStrength;
float _FoamSpeed;

/// <summary>读取材质定义的潮流轴；潮汐只沿该轴往返，不再让整片水面持续绕圈。</summary>
float2 ResolveWaterFlowAxis()
{
    float2 baseDirection = _FlowDirection.xy;
    float baseLengthSq = dot(baseDirection, baseDirection);
    return baseLengthSq > 0.0001
        ? baseDirection * rsqrt(baseLengthSq)
        : float2(1.0, 0.0);
}

/// <summary>用半日潮的积分相位驱动往返流动；相位到达峰谷时流速自然降为零后反向。</summary>
float ResolveTideFlowPhase(float flowSpeed)
{
    const float fullTurn = 6.28318530718;
    const float tideTravel = 96.0;
    float cyclesPerDay = max(_TideCyclesPerDay, 0.01);
    float tideAngle = _GlobalGameDay * cyclesPerDay * fullTurn;
    return sin(tideAngle) * flowSpeed * tideTravel;
}

/// <summary>生成稳定的二维随机值，避免程序波纹形成规则重复图案。</summary>
float WaterHash(float2 cell)
{
    float3 value = frac(float3(cell.xyx) * float3(0.1031, 0.103, 0.0973));
    value += dot(value, value.yzx + 33.33);
    return frac((value.x + value.y) * value.z);
}

/// <summary>在世界空间插值随机值，为流向弯曲和波峰断续提供连续噪声。</summary>
float WaterNoise(float2 position)
{
    float2 cell = floor(position);
    float2 local = frac(position);
    float2 blend = local * local * (3.0 - 2.0 * local);
    float bottom = lerp(WaterHash(cell), WaterHash(cell + float2(1.0, 0.0)), blend.x);
    float top = lerp(
        WaterHash(cell + float2(0.0, 1.0)),
        WaterHash(cell + float2(1.0, 1.0)),
        blend.x);
    return lerp(bottom, top, blend.y);
}

/// <summary>汇总海面各层光学信息，供水色、反光、焦散与白沫统一混合。</summary>
struct WaterSurfaceData
{
    half waterDepth;
    half depthBlend;
    half ripple;
    half rippleShadow;
    half reflection;
    half caustic;
    half specular;
    half whitecap;
    half moonReflection;
};

/// <summary>Tile Color RGBA 只表示左、右、下、上四向岸线。</summary>
half4 DecodeWaterShoreMask(half4 encodedData)
{
    return step(0.5h, encodedData);
}

/// <summary>通过显式世界坐标映射采样 Chunk 水深，避免 Tilemap 合批改变局部坐标。</summary>
half SampleWaterDepth(float2 positionWS)
{
    float2 depthUV = positionWS * _WaterDepthUvScaleOffset.xy
        + _WaterDepthUvScaleOffset.zw;
    return SAMPLE_TEXTURE2D(_WaterDepthTexture, sampler_WaterDepthTexture, depthUV).r;
}

/// <summary>利用屏幕位置和既有波形生成圆形月面及向下延伸的碎光带。</summary>
half ComputeMoonReflection(
    float2 screenUV,
    float height,
    float macroA,
    float macroB,
    float detailA,
    float detailB,
    float time)
{
    half moonAppearance = saturate(_GlobalMoonAppearance);
    half appearanceEase = moonAppearance * moonAppearance * (3.0h - 2.0h * moonAppearance);
    half brightnessEase = appearanceEase * appearanceEase;
    half moonStrength = saturate(
        _GlobalMoonlightIntensity * max(_MoonReflectionStrength, 0.0h))
        * _MoonReflectionColor.a
        * brightnessEase;
    UNITY_BRANCH
    if (moonStrength <= 0.0001h)
        return 0.0h;

    float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.001);
    float2 moonDelta = screenUV - _MoonReflectionPosition.xy;
    float correctedX = moonDelta.x * aspect;
    float sizeEase = lerp(0.2, 1.0, appearanceEase);
    float discRadius = max(_MoonDiscRadius * sizeEase, 0.001);
    float discWaveShift = (height * 0.08 + macroA - macroB) * discRadius * 0.16;
    float discDistance = length(float2(correctedX + discWaveShift, moonDelta.y));
    half disc = 1.0h - smoothstep(discRadius * 0.72, discRadius, discDistance);
    half halo = (1.0h - smoothstep(discRadius, discRadius * 1.85, discDistance)) * 0.2h;
    half discBreakup = lerp(
        0.62h,
        1.0h,
        smoothstep(-0.55, 0.65, height + (macroA - macroB) * 0.7));

    float belowMoon = -moonDelta.y;
    float trailLength = max(_MoonTrailLength * lerp(0.12, 1.0, appearanceEase), 0.001);
    float trailProgress = saturate(belowMoon / trailLength);
    half trailRange = step(0.0, belowMoon)
        * (1.0h - smoothstep(trailLength * 0.72, trailLength, belowMoon));
    float trailWidth = max(_MoonTrailWidth * lerp(0.3, 1.0, appearanceEase), 0.001)
        * lerp(0.6, 1.55, trailProgress);
    float trailWaveShift = (
        height * 0.28
        + (macroA - macroB) * 0.32
        + sin(screenUV.y * 58.0 - time * 1.7) * 0.16)
        * trailWidth;
    half trailCenter = 1.0h - smoothstep(
        trailWidth * 0.2,
        trailWidth,
        abs(correctedX + trailWaveShift));
    float stripeWave = sin(screenUV.y * 220.0 + time * 2.3 + macroA * 4.0);
    half stripeBreakup = lerp(
        0.18h,
        1.0h,
        smoothstep(-0.42, 0.62, stripeWave + detailA * 0.24 + detailB * 0.14));
    half trail = trailRange
        * trailCenter
        * stripeBreakup
        * lerp(1.0h, 0.42h, trailProgress);

    return saturate(disc * discBreakup + halo + trail) * moonStrength;
}

/// <summary>在场景 2D 光照之后叠加月光，避免夜间亮度被重复相乘。</summary>
half3 ApplyMoonReflection(half3 sourceColor, half moonReflection)
{
    half3 reflectedLight = _MoonReflectionColor.rgb * saturate(moonReflection);
    return saturate(sourceColor + reflectedLight * (1.0h - sourceColor));
}

/// <summary>RGBA 分别读取左、右、下、上岸线，计算水格内侧的渐变遮罩。</summary>
half ComputeShoreRecess(float2 positionWS, half4 shoreMask)
{
    float2 cellUV = frac(positionWS + 0.0001);
    half width = max(_EdgeWidth, 0.001h);
    half left = shoreMask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
    half right = shoreMask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
    half bottom = shoreMask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
    half top = shoreMask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
    half strongest = max(max(left, right), max(bottom, top));
    half overlap = saturate(left + right + bottom + top - strongest);
    return saturate(strongest + overlap * _CornerStrength);
}

#endif
