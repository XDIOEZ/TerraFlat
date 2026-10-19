// 低配水面仅用线性水色和三角波亮纹，不计算噪声、法线、反射、焦散或月光。
#ifndef FLATWORLD_WATERSURFACELOWSPEC_HLSL
#define FLATWORLD_WATERSURFACELOWSPEC_HLSL

WaterSurfaceData CalculateLowSpecWaterSurface(float2 positionWS, half depth, float2 current, float waterKind)
{
    WaterSurfaceData surface = (WaterSurfaceData)0;
    surface.liquidDepth = saturate(depth);
    surface.depthBlend = surface.liquidDepth;
    float ocean = 1.0 - step(0.25, abs(waterKind - 2.0));
    float2 velocity = current * lerp(step(0.5, waterKind), 0.08, ocean) * max(_FlowSpeed, 0.0);
    float2 waveUV = positionWS - velocity * _Time.y;
    float phase = dot(waveUV, float2(0.22, 0.31)) - _Time.y * max(_WaveSpeed, 0.0) * 0.12;
    half crest = saturate((1.0h - abs(frac(phase) * 2.0h - 1.0h) - 0.82h) * 5.55h);
    half breakup = saturate((1.0h - abs(frac(dot(waveUV, float2(-0.13, 0.17))) * 2.0h - 1.0h)) * 2.0h);
    surface.ripple = crest * breakup;
    return surface;
}

// Tilemap 回退路径复用同一水色，运动输入在该分支不参与复杂求解。
WaterSurfaceData CalculateWaterSurface(float2 positionWS, float2 screenUV, half depth, WaterMotionData motion)
{
    return CalculateLowSpecWaterSurface(positionWS, depth, _FlowDirection.xy, 2.0);
}

half3 ApplyWaterSurface(half3 sourceColor, WaterSurfaceData surface)
{
    half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, surface.depthBlend);
    half3 color = lerp(sourceColor, waterColor, _SurfaceTint);
    return saturate(color + _RippleColor.rgb * surface.ripple * _RippleStrength);
}

half3 ApplyLowSpecWaterShore(half3 sourceColor, half recess)
{
    return lerp(sourceColor, _ShoreColor.rgb, saturate(recess * _ShoreStrength));
}

half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS, WaterMotionData motion)
{
    return ApplyLowSpecWaterShore(sourceColor, recess);
}

#endif
