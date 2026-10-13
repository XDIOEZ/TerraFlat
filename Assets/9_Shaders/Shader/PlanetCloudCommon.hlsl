#ifndef FLATWORLD_PLANET_CLOUD_COMMON_INCLUDED
#define FLATWORLD_PLANET_CLOUD_COMMON_INCLUDED

// 星球表面与高视野云层共用原来的三层噪声和柔和透明度。
float PlanetHash(float3 p)
{
    float3 p3 = frac(p * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float PlanetNoise(float3 p)
{
    float3 cell = floor(p);
    float3 t = frac(p);
    t = t * t * (3.0 - 2.0 * t);
    float lower = lerp(lerp(PlanetHash(cell), PlanetHash(cell + float3(1,0,0)), t.x),
                       lerp(PlanetHash(cell + float3(0,1,0)), PlanetHash(cell + float3(1,1,0)), t.x), t.y);
    float upper = lerp(lerp(PlanetHash(cell + float3(0,0,1)), PlanetHash(cell + float3(1,0,1)), t.x),
                       lerp(PlanetHash(cell + float3(0,1,1)), PlanetHash(cell + float3(1,1,1)), t.x), t.y);
    return lerp(lower, upper, t.z);
}

float PlanetFilteredNoise(float3 p)
{
    // 远景自动淡出小于像素的细节，近景保留星球原来的柔和云形。
    float pixelWidth = max(length(ddx(p)), length(ddy(p)));
    if (pixelWidth >= 0.8) return 0.5;
    return lerp(PlanetNoise(p), 0.5, smoothstep(0.35, 0.8, pixelWidth));
}

float PlanetFractal(float3 p)
{
    return PlanetFilteredNoise(p) * 0.58 + PlanetFilteredNoise(p * 2.03 + 17.0) * 0.28 +
           PlanetFilteredNoise(p * 4.13 + 53.0) * 0.14;
}

float PlanetCloudOpacity(float cloudNoise)
{
    return smoothstep(0.56, 0.72, cloudNoise) * 0.82;
}

#endif
