#ifndef FLATWORLD_UNIVERSE_STARFIELD_COMMON_INCLUDED
#define FLATWORLD_UNIVERSE_STARFIELD_COMMON_INCLUDED

float UniverseStarHash(float2 cell)
{
    float3 value = frac(float3(cell.xyx) * 0.1031);
    value += dot(value, value.yzx + 33.33);
    return frac((value.x + value.y) * value.z);
}

half3 SampleUniverseStarLayer(float2 pixelPosition, float spacing, float seed)
{
    float2 cell = floor(pixelPosition / spacing);
    float random = UniverseStarHash(cell + seed);
    float2 center = (cell + 0.12 + float2(UniverseStarHash(cell + seed + 7.3),
        UniverseStarHash(cell + seed + 19.1)) * 0.76) * spacing;
    float coverage = 1.0 - smoothstep(0.45, 1.6, length(pixelPosition - center));
    coverage *= step(0.78, random) * (0.65 + sin(_Time.y * 0.55 + random * 41.0) * 0.2);
    half3 color = random > 0.97 ? half3(1.0, 0.55, 0.4) :
        random > 0.93 ? half3(0.52, 0.7, 1.0) : half3(0.84, 0.86, 0.9);
    return color * coverage;
}

half3 SampleUniverseStarfield(float2 centeredPixels)
{
    // 地表未加载区和宇宙远景共用固定像素星点，镜头缩放不改变星点大小。
    return SampleUniverseStarLayer(centeredPixels, 48.0, 11.7) +
        SampleUniverseStarLayer(centeredPixels, 79.0, 43.3) + half3(0.001, 0.002, 0.006);
}

#endif
