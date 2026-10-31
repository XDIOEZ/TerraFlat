#ifndef FLATWORLD_SHADER_FLAME_COMMON_INCLUDED
#define FLATWORLD_SHADER_FLAME_COMMON_INCLUDED

// 世界火场与独立火焰共用同一套GPU像素火舌和火星算法。
float FlatWorldFlameHash21(float2 p)
{
    float3 h = frac(float3(p.xyx) * 0.1031);
    h += dot(h, h.yzx + 33.33);
    return frac((h.x + h.y) * h.z);
}

float FlatWorldFlameNoise(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(FlatWorldFlameHash21(cell), FlatWorldFlameHash21(cell + float2(1, 0)), f.x),
        lerp(FlatWorldFlameHash21(cell + float2(0, 1)), FlatWorldFlameHash21(cell + 1.0), f.x), f.y);
}

float FlatWorldFlameTongue(float2 p, float offset, float height, float width, float bend, float t, float wind)
{
    float y = saturate(p.y / max(height, 0.01));
    float center = offset + (bend + wind * 0.13) * y * y
        + sin(y * 8.0 - t * 2.2) * 0.038 * y;
    float radius = width * pow(saturate(1.0 - y), 0.72) * smoothstep(-0.04, 0.13, p.y);
    return min(radius - abs(p.x - center), (height - p.y) * 0.5);
}

float FlatWorldFlameEmber(float2 cell, float2 pixelGrid, float t, float seed, float wind, float embers)
{
    float life = frac(t * 0.24 + seed);
    float2 center = float2(sin(seed * 31.0 + life * 5.0) * (0.06 + life * 0.12),
        0.24 + life * 0.72);
    center.x += wind * life * 0.12;
    float2 emberCell = floor(float2(center.x + 0.5, center.y) * pixelGrid);
    float2 delta = abs(cell - emberCell);
    return (1.0 - step(0.5, max(delta.x, delta.y)))
        * step(0.05, life) * (1.0 - step(0.25 + embers * 0.55, life));
}

half4 FlatWorldEvaluateProceduralFlame(
    float2 inputUv, float2 pixelGrid, float t, float phaseSeed, float intensity,
    float wind, float turbulence, float embers, float brightness,
    half3 edgeColor, half3 bodyColor, half3 innerColor, half3 coreColor)
{
    float2 cell = min(floor(inputUv * pixelGrid), pixelGrid - 1.0);
    float2 uv = (cell + 0.5) / pixelGrid;
    float2 p = float2(uv.x - 0.5, uv.y);
    float flow = FlatWorldFlameNoise(float2(p.x * 6.0 + phaseSeed, p.y * 4.5 - t * 1.4)) - 0.5;
    float detail = FlatWorldFlameNoise(float2(p.x * 15.0 + 7.3, p.y * 11.0 - t * 2.3)) - 0.5;
    p.x += flow * turbulence * 0.045 * p.y;
    float mainHeight = 0.80 + sin(t * 1.7) * 0.065;
    float field = max(FlatWorldFlameTongue(p, 0.0, mainHeight, 0.255, 0.035, t, wind),
        max(FlatWorldFlameTongue(p, -0.155, 0.56 + sin(t * 2.3 + 1.5) * 0.08, 0.12, -0.045, t + 2.0, wind),
            FlatWorldFlameTongue(p, 0.15, 0.60 + sin(t * 1.9 + 4.0) * 0.075, 0.125, 0.06, t + 4.0, wind)));
    field += (flow * 0.055 + detail * 0.025) * turbulence * (0.3 + p.y);
    float body = step(0.0, field);
    float orange = step(0.025, field);
    float gold = step(0.08, field - p.y * 0.035);
    float coreField = FlatWorldFlameTongue(p, 0.0, 0.40 + flow * 0.07, 0.105, -0.015, t + 0.7, wind);
    float core = step(0.0, coreField + detail * 0.014) * body;
    half3 color = lerp(edgeColor, bodyColor, orange);
    color = lerp(color, innerColor, gold);
    color = lerp(color, coreColor, core);

    float sparks = max(FlatWorldFlameEmber(cell, pixelGrid, t, 0.19, wind, embers),
        FlatWorldFlameEmber(cell, pixelGrid, t, 0.67, wind, embers)) * step(0.001, embers);
    float opacity = saturate(intensity);
    float alpha = max(body, sparks) * opacity;
    half3 rgb = color * body + innerColor * sparks * (1.0 - body);
    return half4(rgb * opacity * brightness * max(intensity, 1.0), alpha);
}

#endif
