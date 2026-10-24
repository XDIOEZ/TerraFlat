#ifndef FLATWORLD_SPRITE_SNOW_COMMON_INCLUDED
#define FLATWORLD_SPRITE_SNOW_COMMON_INCLUDED

// 按精灵仿射包围盒的世界高度覆雪，旋转、翻面和缩放都不倾斜雪线。
float FlatWorldSpriteSnowHeight(float worldY, float4 localBounds, float2 worldYBasis, float originY)
{
    float centerY = originY + dot(localBounds.xy, worldYBasis);
    float halfHeight = dot(localBounds.zw, abs(worldYBasis));
    return (worldY - centerY) / max(1e-5, halfHeight * 2.0) + 0.5;
}

half3 FlatWorldApplySpriteSnow(half3 color, float snowHeight, float coverage)
{
    float snowMask = smoothstep(0.8 - coverage * 0.3, 0.9 - coverage * 0.3, snowHeight);
    return lerp(color, half3(0.88, 0.94, 1.0), snowMask * saturate(coverage));
}

#endif
