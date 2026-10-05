#ifndef FLATWORLD_INTERACTION_OUTLINE_COMMON_INCLUDED
#define FLATWORLD_INTERACTION_OUTLINE_COMMON_INCLUDED

float _WorldInteractionOutlinePixels;

// 以最终屏幕像素为标尺，并补偿 URP 的内部渲染分辨率缩放。
float2 InteractionOutlinePixelScale()
{
    return max(0.0, _WorldInteractionOutlinePixels) *
        _ScaledScreenParams.xy / max(float2(1.0, 1.0), _ScreenParams.xy);
}

half InteractionOutlineAlpha(float2 uv, float4 region)
{
    if (any(uv < region.xy) || any(uv > region.zw))
        return 0.0h;

    half alpha = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).a;
    if (_EnableExternalAlpha > 0.5)
        alpha = SAMPLE_TEXTURE2D_LOD(_AlphaTex, sampler_AlphaTex, uv, 0).r;
    return alpha;
}

half InteractionOutlineMask(float2 uv, float4 region, float2 pixelX, float2 pixelY)
{
    half neighbours = min(InteractionOutlineAlpha(uv + pixelX, region), InteractionOutlineAlpha(uv - pixelX, region));
    neighbours = min(neighbours, min(InteractionOutlineAlpha(uv + pixelY, region), InteractionOutlineAlpha(uv - pixelY, region)));
    float2 diagonalA = (pixelX + pixelY) * 0.70710678;
    float2 diagonalB = (pixelX - pixelY) * 0.70710678;
    neighbours = min(neighbours, min(InteractionOutlineAlpha(uv + diagonalA, region), InteractionOutlineAlpha(uv - diagonalA, region)));
    neighbours = min(neighbours, min(InteractionOutlineAlpha(uv + diagonalB, region), InteractionOutlineAlpha(uv - diagonalB, region)));
    return (1.0h - step(0.1h, neighbours)) * step(0.0001, _WorldInteractionOutlinePixels);
}

#endif
