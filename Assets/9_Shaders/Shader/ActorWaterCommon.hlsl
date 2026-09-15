#ifndef FLATWORLD_ACTOR_WATER_COMMON_INCLUDED
#define FLATWORLD_ACTOR_WATER_COMMON_INCLUDED

// 旧 Sprite 与 ECS 批量网格共用浸没公式；surface=混合/世界模式/世界水线/身体高度。
// shape=归一化水线/柔化/线宽/波幅；wave=频率/速度/染色强度/水线强度。
half4 FlatWorldApplyActorWater(half4 main, float bodyV, float localX, float2 positionWS,
    float4 surface, float4 shape, float4 wave, float4 tint, float4 lineColor, float alpha, float time)
{
    float waterBlend = saturate(surface.x);
    if (waterBlend > 0.0001)
    {
        float surfaceV = saturate(shape.x);
        float feather = max(1e-5, shape.y);
        float worldMode = saturate(surface.y);
        float referenceHeight = max(1e-5, surface.w);
        float horizontalPosition = lerp(localX, positionWS.x, worldMode);
        float wavePhase = horizontalPosition * wave.x + time * wave.y;
        float waveOffset = sin(wavePhase) * shape.w;
        waveOffset += sin(wavePhase * 1.7 - time * wave.y * 0.75) * shape.w * 0.35;
        float waterPosition = lerp(bodyV, positionWS.y, worldMode);
        float waveSurface = lerp(saturate(surfaceV + waveOffset), surface.z + waveOffset * referenceHeight, worldMode);
        float effectiveFeather = lerp(feather, feather * referenceHeight, worldMode);
        float submergedMask = 1.0 - smoothstep(waveSurface - effectiveFeather, waveSurface + effectiveFeather, waterPosition);
        submergedMask *= waterBlend;
        main.rgb = lerp(main.rgb, tint.rgb, saturate(wave.z) * submergedMask);
        main.a *= lerp(1.0, saturate(alpha), submergedMask);
        float lineRadius = max(1e-5, shape.z) * lerp(1.0, referenceHeight, worldMode);
        float lineMask = 1.0 - smoothstep(lineRadius, lineRadius + effectiveFeather, abs(waterPosition - waveSurface));
        lineMask *= waterBlend * saturate(wave.w);
        main.rgb = lerp(main.rgb, lineColor.rgb, lineMask * saturate(lineColor.a));
    }
    return main;
}
#endif
