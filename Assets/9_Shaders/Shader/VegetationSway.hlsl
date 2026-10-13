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
float _GrassFollowWind;
float4 _GrassDirection;
#endif
float _GlobalWindStrength;
float4 _GlobalWindDirection;

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

float VegetationSwayWave(float2 worldPosition, float2 direction)
{
    float phase = sin(dot(worldPosition, float2(12.9898, 78.233))) * 1.7;
    float time = _Time.y * _GrassSwaySpeed;
    float primary = sin(time + phase + dot(worldPosition, direction) * _GrassSwayFrequency);
    float secondary = sin(
        time * 0.63 + phase * 1.71 + worldPosition.y * _GrassSwayFrequency * 0.73);
    return primary + secondary * _GrassSecondaryStrength;
}

// 五级以内沿用原摆动，五到六级连续转为顺风偏折，阵风回摆仍留在下风侧。
float2 ResolveVegetationSwayOffset(float2 worldPosition, float bendWeight, float2 basisX, float2 basisY)
{
    float windStrength = saturate(_GlobalWindStrength);
    if (_GrassSwayEnabled < 0.5 || _GrassSwayAmplitude <= 0.0001 || windStrength <= 0.0001)
        return float2(0, 0);

    float2 direction = _GrassDirection.xy / max(length(_GrassDirection.xy), 0.001);
    float amplitude = _GrassSwayAmplitude * ResolveVegetationVisualWindStrength(windStrength) * bendWeight;
    float2 legacyOffset = (basisX * direction.x + basisY * direction.y)
        * VegetationSwayWave(worldPosition, direction) * amplitude;
    float windLength = length(_GlobalWindDirection.xy);
    if (_GrassFollowWind < 0.5 || windStrength <= 0.5 || windLength <= 0.001)
        return legacyOffset;

    float2 windDirection = _GlobalWindDirection.xy / windLength;
    float wave = VegetationSwayWave(worldPosition, windDirection);
    float biasedWave = 0.75 * (1.0 + abs(_GrassSecondaryStrength)) + 0.25 * wave;
    float objectScale = max(length(basisX), length(basisY));
    float2 directionalOffset = windDirection * biasedWave * amplitude * objectScale;
    return lerp(legacyOffset, directionalOffset, smoothstep(0.5, 0.6, windStrength));
}

// 独立 Sprite 与合批网格共用世界方向，翻转或旋转对象不会翻转强风方向。
float3 ApplyGrassSway(float3 positionOS)
{
    if (_GrassSwayEnabled < 0.5 || _GrassSwayAmplitude <= 0.0001 || _GlobalWindStrength <= 0.0001)
        return positionOS;

    float3 positionWS = TransformObjectToWorld(positionOS);
    float2 basisX = TransformObjectToWorldDir(float3(1, 0, 0), false).xy;
    float2 basisY = TransformObjectToWorldDir(float3(0, 1, 0), false).xy;
    float2 offsetWS = ResolveVegetationSwayOffset(positionWS.xy, GrassBendWeight(positionOS), basisX, basisY);
    positionOS += TransformWorldToObjectDir(float3(offsetWS, 0), false);
    return positionOS;
}

// BRG 精灵网格以 Pivot 为局部原点；按中心 Pivot 还原草根高度，再直接偏移世界顶点。
float3 ApplyChunkGrassSway(float3 positionOS, float3 positionWS)
{
    float normalizedHeight = saturate(
        (positionOS.y + _GrassSpriteHeight * 0.5 - _GrassBendStart)
        / max(_GrassSpriteHeight - _GrassBendStart, 0.001));
    float bend = pow(normalizedHeight, max(_GrassBendPower, 0.01));
    positionWS.xy += ResolveVegetationSwayOffset(positionWS.xy, bend, float2(1, 0), float2(0, 1));
    return positionWS;
}

#endif
