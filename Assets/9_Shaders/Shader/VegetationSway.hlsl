#ifndef FLATWORLD_VEGETATION_SWAY_INCLUDED
#define FLATWORLD_VEGETATION_SWAY_INCLUDED

#ifndef FLATWORLD_VEGETATION_SWAY_MATERIAL_DECLARED
float _GrassSwayEnabled;
float _GrassSwayAmplitude;
float _GrassSwaySpeed;
float _GrassSwayFrequency;
float _GrassBendPower;
float _GrassSecondaryStrength;
float _GrassSpriteHeight;
float _GrassBendStart;
float _GrassTileAnchor;
float _GrassUseObjectRoot;
float4 _GrassDirection;
#endif
float _GlobalWindStrength;

// Tilemap 按单元锚点取局部高度；独立 Sprite 可在树干上方才开始弯曲。
float GrassBendWeight(float3 positionOS)
{
    if (_GrassUseObjectRoot > 0.5)
    {
        float objectHeight = saturate(
            (positionOS.y - _GrassBendStart)
            / max(_GrassSpriteHeight - _GrassBendStart, 0.001));
        return pow(objectHeight, max(_GrassBendPower, 0.01));
    }

    float localY = frac(positionOS.y) - _GrassTileAnchor;
    if (localY > 0.5)
        localY -= 1.0;
    else if (localY < -0.5)
        localY += 1.0;

    float normalizedY = saturate(
        (localY + _GrassSpriteHeight * 0.5) / max(_GrassSpriteHeight, 0.001));
    return pow(normalizedY, max(_GrassBendPower, 0.01));
}

// 低风力保持原摆幅，高风力平滑增强；满风时视觉摆幅为原实现的 300%。
float ResolveVegetationVisualWindStrength(float windStrength)
{
    float highWindBlend = smoothstep(0.5, 1.0, windStrength);
    return windStrength * lerp(1.0, 3.0, highWindBlend);
}

// 共享天气风力只在 GPU 顶点阶段计算，材质分别标定摆动幅度和根部高度。
float3 ApplyGrassSway(float3 positionOS)
{
    float windStrength = saturate(_GlobalWindStrength);
    if (_GrassSwayEnabled < 0.5 || _GrassSwayAmplitude <= 0.0001 || windStrength <= 0.0001)
        return positionOS;

    float visualWindStrength = ResolveVegetationVisualWindStrength(windStrength);

    float2 direction = _GrassDirection.xy;
    direction /= max(length(direction), 0.001);

    float2 worldPosition = TransformObjectToWorld(positionOS).xy;
    float phase = sin(dot(worldPosition, float2(12.9898, 78.233))) * 1.7;
    float time = _Time.y * _GrassSwaySpeed;
    float primary = sin(time + phase + dot(worldPosition, direction) * _GrassSwayFrequency);
    float secondary = sin(
        time * 0.63 + phase * 1.71 + worldPosition.y * _GrassSwayFrequency * 0.73);
    float sway = (primary + secondary * _GrassSecondaryStrength)
        * _GrassSwayAmplitude
        * visualWindStrength
        * GrassBendWeight(positionOS);

    positionOS.xy += direction * sway;
    return positionOS;
}

// BRG 精灵网格以 Pivot 为局部原点；按中心 Pivot 还原草根高度，再直接偏移世界顶点。
float3 ApplyChunkGrassSway(float3 positionOS, float3 positionWS)
{
    float windStrength = saturate(_GlobalWindStrength);
    if (_GrassSwayEnabled < 0.5 || _GrassSwayAmplitude <= 0.0001 || windStrength <= 0.0001)
        return positionWS;

    float normalizedHeight = saturate(
        (positionOS.y + _GrassSpriteHeight * 0.5 - _GrassBendStart)
        / max(_GrassSpriteHeight - _GrassBendStart, 0.001));
    float bend = pow(normalizedHeight, max(_GrassBendPower, 0.01));
    float visualWindStrength = ResolveVegetationVisualWindStrength(windStrength);
    float2 direction = _GrassDirection.xy;
    direction /= max(length(direction), 0.001);

    float2 worldPosition = positionWS.xy;
    float phase = sin(dot(worldPosition, float2(12.9898, 78.233))) * 1.7;
    float time = _Time.y * _GrassSwaySpeed;
    float primary = sin(time + phase + dot(worldPosition, direction) * _GrassSwayFrequency);
    float secondary = sin(
        time * 0.63 + phase * 1.71 + worldPosition.y * _GrassSwayFrequency * 0.73);
    float sway = (primary + secondary * _GrassSecondaryStrength)
        * _GrassSwayAmplitude
        * visualWindStrength
        * bend;

    positionWS.xy += direction * sway;
    return positionWS;
}

#endif
