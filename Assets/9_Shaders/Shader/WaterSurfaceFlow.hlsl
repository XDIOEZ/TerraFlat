// 双相循环移植自 Cass Bunting 的 unity-voronoi-water（MIT-0），来源与许可见 ThirdPartyNotices。
#ifndef FLATWORLD_WATERSURFACEFLOW_HLSL
#define FLATWORLD_WATERSURFACEFLOW_HLSL

// 水面运动输入
struct WaterMotionData
{
    float2 velocity;
    float2 waveDirection;
    float2 tideOffset;
    float waveTime;
    float waveHeight;
    float foamActivity;
    float currentStrength;
};

// 海面输入是风向，河流及实验液流输入是实际速度；波浪传播与带动物品的水流分开。
WaterMotionData ResolveWaterMotion(float2 current, float waterKind)
{
    WaterMotionData motion = (WaterMotionData)0;
    float ocean = 1.0 - step(0.25, abs(waterKind - 2.0));
    float stillWater = 1.0 - step(0.5, waterKind);
    float2 fallback = ResolveWaterFlowAxis(_FlowDirection.xy);
    if (dot(fallback, fallback) < 0.5)
        fallback = float2(1.0, 0.0);
    float2 currentAxis = dot(current, current) > 0.000001
        ? ResolveWaterFlowAxis(current) : fallback;
    motion.waveDirection = dot(_WaveDirection.xy, _WaveDirection.xy) > 0.000001
        ? ResolveWaterFlowAxis(_WaveDirection.xy) : currentAxis;
    // 湖口邻河只提供波浪扰动，静水不会画出单向漂移；实验液流 kind=3 始终顺流。
    motion.velocity = lerp(current * (1.0 - stillWater), ResolveWaterFlowAxis(current) * 0.15, ocean)
        * max(_FlowSpeed, 0.0);
    motion.currentStrength = saturate(length(motion.velocity) / 0.45);
    motion.waveTime = lerp(_Time.y, _OceanWaveTime, ocean) * max(_WaveSpeed, 0.0);
    motion.waveHeight = lerp(lerp(0.35, 0.85, motion.currentStrength),
        max(_OceanWaveFactors.y, 0.05), ocean);
    motion.foamActivity = lerp(lerp(0.15, 1.15, motion.currentStrength),
        max(_OceanWaveFactors.z, 0.0), ocean);
    motion.tideOffset = ResolveWaterFlowAxis(current)
        * ResolveTideFlowPhase(0.15) * ocean * 0.045;
    return motion;
}

// 四个共享格角保留速度大小，Mesh 与 BRG 使用完全相同的插值。
float2 InterpolateWaterCurrent(float2 positionWS, float4 flowX, float4 flowY)
{
    float2 cellUV = frac(positionWS);
    return float2(
        lerp(lerp(flowX.x, flowX.y, cellUV.x), lerp(flowX.z, flowX.w, cellUV.x), cellUV.y),
        lerp(lerp(flowY.x, flowY.y, cellUV.x), lerp(flowY.z, flowY.w, cellUV.x), cellUV.y));
}
// 双相流动采样
// 重置的一相权重为零，两相相隔半周期，避免接力闪跳和弯道无限拉伸。
void ResolveWaterFlowPhases(float2 positionWS, float2 velocity,
    out float2 firstUV, out float2 secondUV, out float firstWeight)
{
    float cycle = max(_FlowCycleSeconds, 0.5);
    float firstPhase = frac(_Time.y / cycle);
    float secondPhase = frac(firstPhase + 0.5);
    firstWeight = 1.0 - abs(1.0 - 2.0 * firstPhase);
    firstUV = positionWS - velocity * firstPhase * cycle;
    secondUV = positionWS - velocity * secondPhase * cycle;
}

// 复用项目的二维噪声及解析导数，替代原实现昂贵的四维 Voronoi 邻点搜索。
float3 WaterNoiseGradient(float2 position)
{
    float2 cell = floor(position);
    float2 local = frac(position);
    float2 blend = local * local * (3.0 - 2.0 * local);
    float2 derivative = 6.0 * local * (1.0 - local);
    float a = WaterHash(cell);
    float b = WaterHash(cell + float2(1.0, 0.0));
    float c = WaterHash(cell + float2(0.0, 1.0));
    float d = WaterHash(cell + float2(1.0, 1.0));
    float bottom = lerp(a, b, blend.x);
    float top = lerp(c, d, blend.x);
    return float3(lerp(bottom, top, blend.y),
        lerp(b - a, d - c, blend.y) * derivative.x,
        (top - bottom) * derivative.y);
}

float3 SampleAdvectedWaterGradient(float2 positionWS, float2 velocity, float scale, float2 offset)
{
    float2 firstUV;
    float2 secondUV;
    float firstWeight;
    ResolveWaterFlowPhases(positionWS, velocity, firstUV, secondUV, firstWeight);
    float3 first = WaterNoiseGradient(firstUV * scale + offset);
    float3 second = WaterNoiseGradient(secondUV * scale + offset);
    float3 field = lerp(second, first, firstWeight);
    field.yz *= scale;
    return field;
}

float SampleAdvectedWaterNoise(float2 positionWS, float2 velocity, float scale, float2 offset)
{
    return SampleAdvectedWaterGradient(positionWS, velocity, scale, offset).x;
}

// 水流本身没有固有波速，纹理只能由 velocity 带动；静水只保留独立风浪。
float3 SampleWaterCurrentDetail(float2 positionWS, WaterMotionData motion)
{
    return SampleAdvectedWaterGradient(positionWS, motion.velocity,
        max(_DetailScale, 0.5) * 0.65, float2(23.7, 6.2));
}

// 快水的断续亮纹与泡沫使用同一平流场，宽度和密度复用既有材质参数。
void ApplyWaterCurrentDetail(inout WaterSurfaceData surface, float3 field, WaterMotionData motion)
{
    float width = max(_RippleWidth * 0.45, 0.018);
    float distanceToCrest = abs(field.x - 0.54);
    float crest = 1.0 - smoothstep(width * 0.35, width, distanceToCrest);
    float shadow = 1.0 - smoothstep(width, width * 2.1, abs(field.x - 0.54 + width));
    float breakup = smoothstep(0.14, 0.75, length(field.yz));
    float activity = motion.currentStrength;
    surface.ripple = saturate(surface.ripple + crest * breakup * activity
        * _RippleStrength * _RippleColor.a);
    surface.rippleShadow = saturate(surface.rippleShadow + shadow * (1.0 - crest)
        * activity * _RippleShadowStrength);
    surface.whitecap = saturate(surface.whitecap + crest * breakup * activity
        * _WhitecapStrength * _FoamColor.a * 0.6);
}

// 岸边泡沫沿真实水流平移，洗岸起伏单独使用泡沫速度。
half3 ApplyFlowingWaterShore(half3 sourceColor, half recess, float2 positionWS, WaterMotionData motion)
{
    UNITY_BRANCH
    if (recess > 0.0001h && (_ShoreStrength + _ShoreFoamStrength) > 0.0001h)
    {
        float foamNoise = SampleAdvectedWaterNoise(positionWS, motion.velocity, 2.7, float2(11.3, 27.1));
        float washTime = _Time.y * max(_FoamSpeed, 0.0);
        float wash = 0.5 + 0.5 * sin(washTime * 1.7 + foamNoise * 5.0);
        float shoreBand = saturate(recess * (1.0 - recess) * 4.0);
        float coverage = smoothstep(0.32, 0.78, foamNoise + wash * 0.24);
        float foam = shoreBand * coverage * _ShoreFoamStrength * _FoamColor.a
            * lerp(0.5, 1.0, saturate(motion.foamActivity));
        sourceColor = lerp(sourceColor, _ShoreColor.rgb,
            shoreBand * _ShoreStrength * _ShoreColor.a);
        sourceColor = lerp(sourceColor, _FoamColor.rgb, saturate(foam));
    }
    return sourceColor;
}
#endif
