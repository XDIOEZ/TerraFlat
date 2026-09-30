#ifndef FLATWORLD_SUN_SHADOW_SAMPLING_INCLUDED
#define FLATWORLD_SUN_SHADOW_SAMPLING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// 所有太阳投影共用设置值，阴影独立线性采样以保留本体的像素风格。
float _WorldSunShadowBlur;

half SampleSunShadowAlpha(float2 uv, float2 texel, float4 bounds, float4 clipPlane, bool filtered)
{
    float2 border = filtered ? texel * 0.5 : 0;
    if (any(uv < bounds.xy - border) || any(uv > bounds.zw + border)) return 0;
    float2 sampleUv = clamp(uv, bounds.xy + texel * 0.5, bounds.zw - texel * 0.5);
    half alpha;
    if (filtered) alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, sampleUv).a;
    else alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUv).a;
    #if defined(ETC1_EXTERNAL_ALPHA) && defined(FLATWORLD_SUN_SHADOW_EXTERNAL_ALPHA)
    half externalAlpha;
    if (filtered) externalAlpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_LinearClamp, sampleUv).r;
    else externalAlpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, sampleUv).r;
    alpha = lerp(alpha, externalAlpha, _EnableExternalAlpha);
    #endif
    if (filtered)
    {
        float2 fade = saturate((uv - bounds.xy) / texel + 0.5)
            * saturate((bounds.zw - uv) / texel + 0.5);
        alpha *= fade.x * fade.y;
    }
    // 半埋作物在各采样点裁切，模糊后的地面边缘不再被整片硬切。
    if (clipPlane.w > 0.5) alpha *= step(0, dot(uv, clipPlane.xy) + clipPlane.z);
    return alpha;
}

half SunShadowKernelWeight(int offset)
{
    return offset == 0 ? 6 : (abs(offset) == 1 ? 4 : 1);
}

half SampleSunShadowCoverage(float2 uv, float2 texel, float4 bounds, float4 clipPlane, float texelScale)
{
    if (_WorldSunShadowBlur <= 0.001)
        return SampleSunShadowAlpha(uv, texel, bounds, clipPlane, false);

    // 连续的五乘五加权采样避免稀疏点采样留下多层锐利轮廓。
    float2 spacing = texel * max(0.01, texelScale) * (saturate(_WorldSunShadowBlur) * 2.0);
    half coverage = 0;
    [unroll] for (int row = -2; row <= 2; row++)
    {
        [unroll] for (int column = -2; column <= 2; column++)
        {
            half weight = SunShadowKernelWeight(row) * SunShadowKernelWeight(column);
            coverage += SampleSunShadowAlpha(uv + float2(column, row) * spacing,
                texel, bounds, clipPlane, true) * weight;
        }
    }
    return saturate(coverage / 256.0);
}

#endif
